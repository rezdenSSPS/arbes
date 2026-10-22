using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

namespace AirsoftFPS
{
    // Lives on the NetworkManager (DontDestroyOnLoad). Players do NOT auto-spawn
    // on connect; instead they spawn only once the match scene has loaded, so the
    // lobby/waiting room has no avatars. Clients who join after the match started
    // are dropped straight in. GetSpawnFor / GetTeamZone pick where players (re)spawn.
    public class PlayerSpawner : MonoBehaviour
    {
        public GameObject playerPrefab;
        public string matchScene = "AirsoftArena";   // legacy; every MapCatalog map counts as a match scene

        bool matchLive;

        void Start()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            nm.OnServerStarted += OnServerStarted;
            nm.OnClientConnectedCallback += OnClientConnected;
        }

        void OnDestroy()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            nm.OnServerStarted -= OnServerStarted;
            nm.OnClientConnectedCallback -= OnClientConnected;
            if (nm.SceneManager != null)
                nm.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        }

        void OnServerStarted()
        {
            matchLive = false;
            var sm = NetworkManager.Singleton.SceneManager;
            if (sm != null)
            {
                sm.OnLoadEventCompleted -= OnLoadEventCompleted; // avoid double-subscribe on re-host
                sm.OnLoadEventCompleted += OnLoadEventCompleted;
            }
        }

        void OnLoadEventCompleted(string sceneName, UnityEngine.SceneManagement.LoadSceneMode mode,
            List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            if (sceneName != matchScene && !MapCatalog.IsMatchScene(sceneName))
            {
                // Back in the lobby. Avatars survive scene loads, so remove them: the next match (maybe on
                // another map) then spawns everyone fresh on that map's spawn points.
                matchLive = false;
                foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
                {
                    var po = NetworkManager.Singleton.ConnectedClients[id].PlayerObject;
                    if (po != null && po.IsSpawned) po.Despawn(true);
                }
                return;
            }

            matchLive = true;
            foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
                SpawnFor(id);
        }

        void OnClientConnected(ulong clientId)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            if (matchLive) SpawnFor(clientId); // late joiner drops straight into the match
        }

        void SpawnFor(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (playerPrefab == null) return;
            if (!nm.ConnectedClients.ContainsKey(clientId)) return;
            if (nm.ConnectedClients[clientId].PlayerObject != null) return; // already has an avatar

            var spawn = GetSpawnFor(clientId);
            var go = Instantiate(playerPrefab, spawn.position, spawn.rotation);
            go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
        }

        // ────────── spawn selection (server; also usable by Milsim walk-back and bots) ──────────

        // Where a player should (re)spawn right now. Team modes (TDM, Milsim): one of the player's own team's
        // points (Red north, Blue south). Every mode: the point farthest from living enemies, avoiding
        // points someone is already standing on. A little randomness spreads players over equal points.
        public static Pose GetSpawnFor(ulong clientId)
        {
            if (ObjectiveMode.TrySpawnFor(clientId, out Pose objPose)) return objPose;   // e.g. VIP escort: by role
            var mn = MatchNet.Instance;
            var mode = mn != null ? mn.Mode : MatchNet.GameModeType.Coop;
            int team = MatchNet.IsTeamMode(mode) && mn != null ? mn.TeamOf(clientId) : 0;

            var points = Candidates(team);
            if (points.Count == 0)
            {
                var p = new Vector3(Random.Range(-10f, 10f), 1f, Random.Range(-10f, 10f));
                return new Pose(p, FaceCentre(p));
            }

            // living players and bots, excluding the one being spawned
            var others = new List<(Vector3 pos, bool enemy)>();
            foreach (var h in Object.FindObjectsByType<Health>(FindObjectsSortMode.None))
            {
                if (!h.IsSpawned || h.isDead.Value) continue;
                if (h.IsCombatant && h.MatchId == clientId) continue;
                bool enemy;
                if (!h.IsCombatant) enemy = true;                                  // AI (co-op)
                else if (mode == MatchNet.GameModeType.Coop) enemy = false;        // co-op teammates
                else if (team != 0) enemy = mn.TeamOf(h.MatchId) != team;         // TDM (players and bots)
                else enemy = true;                                                 // FFA / Gun Game
                others.Add((h.transform.position, enemy));
            }

            float jitter = team != 0 ? 12f : 2f;   // teams: spread over their side; FFA: really the farthest
            Transform best = null;
            float bestScore = float.NegativeInfinity;
            foreach (var t in points)
            {
                float nearestEnemy = 80f, nearestAny = float.PositiveInfinity;
                foreach (var o in others)
                {
                    Vector3 d = o.pos - t.position; d.y = 0;
                    float dist = d.magnitude;
                    nearestAny = Mathf.Min(nearestAny, dist);
                    if (o.enemy) nearestEnemy = Mathf.Min(nearestEnemy, dist);
                }
                float score = nearestEnemy + Random.Range(0f, jitter);
                if (nearestAny < 2f) score -= 100f;   // occupied
                if (score > bestScore) { bestScore = score; best = t; }
            }
            return new Pose(best.position, best.rotation);
        }

        // The area a team spawns in (bounds around its spawn points, a few metres of margin).
        // Team 0 (or a map without team points) = all player spawns.
        public static Bounds GetTeamZone(int team)
        {
            var points = Candidates(team);
            if (points.Count == 0)
            {
                // no spawn points at all: that team's half of the 80 x 80 m arena
                float z = team == 1 ? 20f : team == 2 ? -20f : 0f;
                return new Bounds(new Vector3(0, 1f, z), new Vector3(80f, 4f, team == 0 ? 80f : 40f));
            }
            var b = new Bounds(points[0].position, Vector3.zero);
            foreach (var t in points) b.Encapsulate(t.position);
            b.Expand(new Vector3(6f, 4f, 6f));
            return b;
        }

        // The team's spawn point closest to a position (e.g. where a "dead" Milsim player walks back to).
        public static Pose NearestTeamSpawn(int team, Vector3 from)
        {
            var points = Candidates(team);
            if (points.Count == 0) return new Pose(GetTeamZone(team).center, Quaternion.identity);
            Transform best = points[0];
            foreach (var t in points)
                if ((t.position - from).sqrMagnitude < (best.position - from).sqrMagnitude) best = t;
            return new Pose(best.position, best.rotation);
        }

        // Spawn points usable by a team (0 = all). Falls back to every point if the team has none,
        // and to the old "SpawnPoint"-tagged objects if the scene has no SpawnPoint components.
        static List<Transform> Candidates(int team)
        {
            var list = new List<Transform>();
            foreach (var sp in SpawnPoint.All)
                if (sp != null && (team == 0 || sp.team == team)) list.Add(sp.transform);
            if (list.Count == 0 && team != 0)
                foreach (var sp in SpawnPoint.All)
                    if (sp != null) list.Add(sp.transform);
            if (list.Count == 0)
                foreach (var go in GameObject.FindGameObjectsWithTag("SpawnPoint"))
                    list.Add(go.transform);
            return list;
        }

        static Quaternion FaceCentre(Vector3 p)
        {
            Vector3 d = -p; d.y = 0;
            return d.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d) : Quaternion.identity;
        }
    }
}
