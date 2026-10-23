using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using Unity.Netcode.Components;

namespace AirsoftFPS
{
    // Server-side brain of a PvP bot (an AI soldier from the Enemy prefab with Health.botId set,
    // spawned by MatchNet.Bots). Built on the enemy AI's NavMeshAgent: it roams the arena between
    // spawn points, hunts the nearest visible opponent (players and other bots, respecting teams),
    // fires real BBs from a WeaponData weapon (Gun Game: its level's weapon) with the same ballistics
    // as players, reloads, and in Milsim walks back to its team zone with its hand up when HIT.
    // Difficulty sets reaction time, aim spread and accuracy (and a bit of awareness).
    [DisallowMultipleComponent]
    public class BotBrain : MonoBehaviour
    {
        struct Skill
        {
            public float reaction;   // seconds from spotting someone to the first shot
            public float spread;     // extra aim error, degrees (shrinks while tracking a target)
            public float accuracy;   // 0..1: share of shots aimed at the body (the rest are near misses)
            public float settle;     // seconds of tracking until the aim error is at its minimum
            public float lead;       // 0..1: how much the bot leads a moving target
            public float fireMult;   // fire-rate multiplier (trigger discipline)
            public float sight;      // detection range, metres
            public float fov;        // field of view, degrees
            public bool strafe;      // sidesteps while fighting
        }

        static Skill For(MatchNet.BotDifficulty d)
        {
            switch (d)
            {
                case MatchNet.BotDifficulty.Easy:
                    return new Skill { reaction = 0.9f, spread = 5.5f, accuracy = 0.35f, settle = 1.4f, lead = 0f,
                                       fireMult = 0.6f, sight = 45f, fov = 110f, strafe = false };
                case MatchNet.BotDifficulty.Hard:
                    return new Skill { reaction = 0.22f, spread = 1.1f, accuracy = 0.85f, settle = 0.35f, lead = 0.9f,
                                       fireMult = 1f, sight = 80f, fov = 160f, strafe = true };
                default:
                    return new Skill { reaction = 0.45f, spread = 2.8f, accuracy = 0.6f, settle = 0.75f, lead = 0.5f,
                                       fireMult = 0.85f, sight = 60f, fov = 130f, strafe = true };
            }
        }

        const float WALK_SPEED = 4.5f, RUN_SPEED = 7f, FIGHT_SPEED = 3.2f;
        const float EYE_HEIGHT = 1.55f, CHEST_HEIGHT = 1.2f;

        public ulong BotId { get; private set; }
        public MatchNet.BotDifficulty Difficulty { get; private set; }
        public int ShotsFired { get; private set; }
        // One-line state for debugging (e.g. "engage Viper 18m AK AEG").
        public string DebugState => (health != null && health.isDead.Value ? "dead" : health != null && health.IsOut ? "hit"
            : target != null ? (targetVisible ? "engage " : "chase ") + (MatchNet.Instance != null ? MatchNet.Instance.NameFor(target.MatchId) : "?")
              + " " + Mathf.RoundToInt(Vector3.Distance(transform.position, target.transform.position)) + "m"
            : "roam") + " " + (Weapon != null ? Weapon.shortName : "-") + (agent != null && agent.isOnNavMesh ? "" : " OFFMESH");

        Skill skill;
        NavMeshAgent agent;
        Health health;
        NetworkObject netObj;
        NetworkTransform netTransform;
        EnemyAI fx;
        EnemyAnimator anim;

        static WeaponData[] s_weapons;
        WeaponData[] weapons;
        int weaponIdx = -1;
        int mag;
        float reloadUntil;
        float nextShot;
        int burstLeft;
        float burstPauseUntil;

        Health target;
        bool targetVisible;
        float acquiredAt, lastSeenTime, nextScan;
        Vector3 lastSeenPos, targetVel, prevTargetPos;
        float prevTargetTime;

        float alertUntil;          // just got shot: look all around
        int lastHp = -1;
        float nextRoam, strafeUntil, stuckFor, nextWalkBack;
        Vector3 lastPos;
        bool wasDead, wasHit, configured;

        static readonly List<Health> s_combatants = new List<Health>();
        static float s_combatantsAt = -1f;
        static readonly RaycastHit[] s_hits = new RaycastHit[16];

        // Called by MatchNet before the soldier is spawned.
        public void Setup(ulong botId, MatchNet.BotDifficulty difficulty)
        {
            BotId = botId;
            Difficulty = difficulty;
            skill = For(difficulty);
            configured = true;
        }

        void Start()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || !configured) { Destroy(this); return; }
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();
            netObj = GetComponent<NetworkObject>();
            netTransform = GetComponent<NetworkTransform>();
            fx = GetComponent<EnemyAI>();
            anim = GetComponent<EnemyAnimator>();
            s_weapons ??= WeaponData.GetAllWeapons();
            weapons = s_weapons;
            lastPos = transform.position;

            if (agent != null)
            {
                agent.stoppingDistance = 0.5f;
                agent.autoBraking = true;
                if (agent.enabled && !agent.isOnNavMesh
                    && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 12f, NavMesh.AllAreas))
                    agent.Warp(hit.position);
            }
            UpdateWeapon(MatchNet.Instance, true);
        }

        // Server: respawn move (Health.Respawn). The soldier's NetworkTransform is server-authoritative.
        public void Teleport(Vector3 pos, Quaternion rot)
        {
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 6f, NavMesh.AllAreas)) pos = hit.position;
            if (netTransform != null && netTransform.CanCommitToTransform) netTransform.Teleport(pos, rot, transform.localScale);
            else transform.SetPositionAndRotation(pos, rot);
            if (agent != null && agent.enabled) { agent.Warp(pos); if (agent.isOnNavMesh) agent.ResetPath(); }
            transform.rotation = rot;
            lastPos = pos;
        }

        void Update()
        {
            if (health == null || netObj == null || !netObj.IsSpawned) return;
            var mn = MatchNet.Instance;
            bool live = mn != null && mn.CurrentPhase == MatchNet.Phase.InMatch;

            if (health.isDead.Value)
            {
                if (!wasDead) { wasDead = true; target = null; Halt(); }
                return;
            }
            if (wasDead) { wasDead = false; OnRespawned(mn); }

            int hp = health.currentHealth.Value;
            if (lastHp >= 0 && hp < lastHp) alertUntil = Time.time + 2.5f;
            lastHp = hp;

            UpdateWeapon(mn, false);
            if (!live) { Halt(); return; }

            if (health.IsOut)   // Milsim HIT: hand up (EnemyAnimator), no shooting, walk back to base
            {
                wasHit = true;
                target = null;
                WalkBack(mn);
                return;
            }
            if (wasHit) { wasHit = false; nextRoam = 0f; lastHp = -1; }

            // Round modes (Bomb / VIP) freeze everyone during the prep countdown (MoveFactor 0): stand still.
            if (ObjectiveMode.SpeedFactor(BotId) <= 0.01f) { Halt(); return; }

            if (Time.time >= nextScan) { nextScan = Time.time + 0.2f; Scan(mn); }
            TrackTargetVelocity();

            // Objective modes give the bot a goal (flag, control point, …). "Deliver" (carrying the flag home)
            // overrides fighting; otherwise the bot fights when it sees someone and pursues the goal when it doesn't.
            bool hasObj = ObjectiveMode.BotObjective(BotId, out Vector3 objPos, out var objTask);
            bool priority = hasObj && (objTask == ObjectiveMode.BotTask.Deliver || objTask == ObjectiveMode.BotTask.Plant
                                    || objTask == ObjectiveMode.BotTask.Defuse || objTask == ObjectiveMode.BotTask.Revive);

            if (priority) PursueObjective(objPos, objTask);          // carry / plant / defuse / revive: get it done
            else if (target != null && targetVisible) Engage(mn);
            else if (target != null && Time.time - lastSeenTime < 4f && !hasObj) Chase();
            else if (hasObj) PursueObjective(objPos, objTask);
            else { target = null; Roam(mn); }

            CheckStuck();
        }

        void OnRespawned(MatchNet mn)
        {
            target = null;
            lastHp = -1;
            nextRoam = 0f;
            if (agent != null && agent.isOnNavMesh) agent.isStopped = false;
            // a fresh loadout now and then (Gun Game keeps the level weapon)
            if (mn != null && mn.Mode != MatchNet.GameModeType.GunGame && Random.value < 0.4f) Equip(PickWeapon());
            else if (weaponIdx >= 0) Equip(weaponIdx);
        }

        // ───────── weapon ─────────

        WeaponData Weapon => weaponIdx >= 0 && weaponIdx < weapons.Length ? weapons[weaponIdx] : null;

        void UpdateWeapon(MatchNet mn, bool first)
        {
            if (weapons == null || weapons.Length == 0) return;
            int want = weaponIdx;
            if (mn != null && mn.Mode == MatchNet.GameModeType.GunGame)
                want = Mathf.Clamp(mn.GunLevelOf(BotId), 0, weapons.Length - 1);
            else if (weaponIdx < 0 || first) want = weaponIdx < 0 ? PickWeapon() : weaponIdx;
            // Objective roles may restrict weapons (VIP escort: the VIP carries sidearms only).
            if (want >= 0 && want < weapons.Length && !ObjectiveMode.WeaponAllowed(BotId, weapons[want]))
                want = FirstAllowedWeapon(want);
            if (want != weaponIdx) Equip(want);
        }

        int PickWeapon() => Random.Range(0, weapons.Length);

        int FirstAllowedWeapon(int preferred)
        {
            if (ObjectiveMode.WeaponAllowed(BotId, weapons[preferred])) return preferred;
            for (int i = 0; i < weapons.Length; i++)
                if (ObjectiveMode.WeaponAllowed(BotId, weapons[i])) return i;
            return preferred;
        }

        void Equip(int idx)
        {
            weaponIdx = Mathf.Clamp(idx, 0, weapons.Length - 1);
            var w = weapons[weaponIdx];
            mag = w.magSize;
            reloadUntil = 0f;
            nextShot = Time.time + 0.4f;
            burstLeft = 0;
            if (fx != null && fx.IsSpawned) fx.botWeapon.Value = weaponIdx;
        }

        // ───────── perception ─────────

        Vector3 Eye => transform.position + Vector3.up * EYE_HEIGHT;
        static Vector3 Chest(Health h) => h.transform.position + Vector3.up * CHEST_HEIGHT;

        static List<Health> Combatants()
        {
            if (Time.time - s_combatantsAt > 1f || s_combatantsAt < 0f)
            {
                s_combatantsAt = Time.time;
                s_combatants.Clear();
                foreach (var h in FindObjectsByType<Health>())
                    if (h.IsSpawned && h.IsCombatant) s_combatants.Add(h);
            }
            return s_combatants;
        }

        bool IsOpponent(MatchNet mn, Health h)
        {
            if (h == null || h == health || !h.IsSpawned || !h.IsCombatant) return false;
            if (mn == null || !mn.TeamMode) return true;   // FFA / Gun Game: everyone
            int mine = mn.TeamOf(BotId), theirs = mn.TeamOf(h.MatchId);
            return mine == 0 || theirs == 0 || mine != theirs;
        }

        void Scan(MatchNet mn)
        {
            bool alert = Time.time < alertUntil;
            Health best = null;
            float bestScore = float.MaxValue;
            foreach (var h in Combatants())
            {
                if (h == null || h.isDead.Value || h.IsOut || !IsOpponent(mn, h)) continue;
                Vector3 to = Chest(h) - Eye;
                float d = to.magnitude;
                if (d > skill.sight) continue;
                bool current = h == target && Time.time - lastSeenTime < 1.5f;
                Vector3 flat = to; flat.y = 0;
                bool inView = alert || current || d < 6f || Vector3.Angle(transform.forward, flat) < skill.fov * 0.5f;
                if (!inView || !LineOfSight(h)) continue;
                float score = d;
                if (h == target) score *= 0.7f;          // stick with the current target
                if (h.IsSpawnProtected) score += 30f;    // can't be hurt right now
                if (score < bestScore) { bestScore = score; best = h; }
            }

            if (best != null)
            {
                if (best != target)
                {
                    target = best;
                    acquiredAt = Time.time;
                    prevTargetPos = best.transform.position;
                    prevTargetTime = Time.time;
                    targetVel = Vector3.zero;
                    burstLeft = 0;
                }
                targetVisible = true;
                lastSeenTime = Time.time;
                lastSeenPos = best.transform.position;
            }
            else targetVisible = false;

            if (target != null && (target.isDead.Value || target.IsOut || !target.IsSpawned)) { target = null; targetVisible = false; }
        }

        // Clear line from the bot's eyes to the target's chest (HIT Milsim players don't block).
        bool LineOfSight(Health h)
        {
            Vector3 a = Eye, b = Chest(h);
            Vector3 d = b - a;
            float dist = d.magnitude;
            if (dist < 0.01f) return true;
            int n = Physics.RaycastNonAlloc(a, d / dist, s_hits, dist, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            Health first = null;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                var hit = s_hits[i];
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (Health.PassesBBs(hit.collider)) continue;
                if (hit.distance >= nearest) continue;
                nearest = hit.distance;
                first = hit.collider.GetComponentInParent<Health>();
                any = true;
            }
            return !any || first == h;
        }

        void TrackTargetVelocity()
        {
            if (target == null) return;
            float dt = Time.time - prevTargetTime;
            if (dt < 0.1f) return;
            Vector3 v = (target.transform.position - prevTargetPos) / dt;
            if (v.sqrMagnitude > 400f) v = Vector3.zero;   // teleported (respawn)
            targetVel = Vector3.Lerp(targetVel, v, 0.5f);
            prevTargetPos = target.transform.position;
            prevTargetTime = Time.time;
        }

        // ───────── behaviour ─────────

        void Engage(MatchNet mn)
        {
            var w = Weapon;
            if (w == null) return;
            Vector3 to = target.transform.position - transform.position;
            float d = new Vector3(to.x, 0, to.z).magnitude;

            // face the target
            if (agent != null) agent.updateRotation = false;
            Vector3 flat = to; flat.y = 0;
            if (flat.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), 540f * Time.deltaTime);

            // move: close in when out of comfortable range, otherwise hold or sidestep
            float comfy = Mathf.Clamp(w.range * 0.5f, 8f, 35f);
            if (d > comfy * 1.4f) Go(target.transform.position, FIGHT_SPEED * 1.3f * w.moveSpeedMult);
            else if (skill.strafe && Time.time >= strafeUntil)
            {
                strafeUntil = Time.time + Random.Range(0.9f, 2.2f);
                Vector3 side = Vector3.Cross(Vector3.up, flat.normalized) * (Random.value < 0.5f ? -1f : 1f);
                Vector3 p = transform.position + side * Random.Range(1.5f, 4.5f);
                if (d < comfy * 0.4f) p -= flat.normalized * 2.5f;   // too close: back off a bit
                if (NavMesh.SamplePosition(p, out NavMeshHit hit, 2.5f, NavMesh.AllAreas)) Go(hit.position, FIGHT_SPEED * w.moveSpeedMult);
            }
            else if (!skill.strafe && agent != null && agent.isOnNavMesh && d <= comfy * 1.4f) agent.ResetPath();

            if (Time.time - acquiredAt >= skill.reaction && Vector3.Angle(transform.forward, flat) < 25f)
                TryFire(w, d);
        }

        void Chase()
        {
            if (agent != null) agent.updateRotation = true;
            var w = Weapon;
            Go(lastSeenPos, RUN_SPEED * (w != null ? w.moveSpeedMult : 1f));
            if (agent != null && agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance < 1.2f)
                target = null;   // got there, nobody: go back to roaming
        }

        void Roam(MatchNet mn)
        {
            if (agent == null || !agent.isOnNavMesh) return;
            agent.updateRotation = true;
            bool arrived = !agent.pathPending && (!agent.hasPath || agent.remainingDistance < 1.5f);
            if (!arrived && Time.time < nextRoam) return;
            nextRoam = Time.time + Random.Range(14f, 24f);

            // Head for a spawn point (the opponents' side more often in team modes), a bit off it.
            int myTeam = mn != null && mn.TeamMode ? mn.TeamOf(BotId) : 0;
            Vector3 goal = transform.position + Random.insideUnitSphere * 25f;
            var points = SpawnPoint.All;
            if (points.Count > 0)
            {
                SpawnPoint pick = null;
                for (int tries = 0; tries < 6 && pick == null; tries++)
                {
                    var sp = points[Random.Range(0, points.Count)];
                    if (sp == null) continue;
                    if (myTeam != 0 && sp.team == myTeam && Random.value < 0.7f) continue;   // push forward
                    pick = sp;
                }
                if (pick != null) goal = pick.transform.position + Random.insideUnitSphere * 8f;
            }
            goal.y = transform.position.y;
            var w = Weapon;
            if (NavMesh.SamplePosition(goal, out NavMeshHit hit, 10f, NavMesh.AllAreas))
                Go(hit.position, WALK_SPEED * (w != null ? w.moveSpeedMult : 1f));
        }

        void WalkBack(MatchNet mn)
        {
            if (agent != null) agent.updateRotation = true;
            if (Time.time < nextWalkBack) return;
            nextWalkBack = Time.time + 1f;
            int team = mn != null ? mn.TeamOf(BotId) : 0;
            float outside = MilsimZone.DistanceOutside(team, transform.position, out Vector3 zone);
            if (outside <= 0f)
            {
                if (agent != null && agent.isOnNavMesh) agent.ResetPath();   // in the zone: wait for the respawn
                return;
            }
            if (NavMesh.SamplePosition(zone, out NavMeshHit hit, 6f, NavMesh.AllAreas)) zone = hit.position;
            Go(zone, WALK_SPEED);
        }

        // Objective modes: walk to the goal the mode handed us. Deliver (carrying a flag home) runs; the rest
        // walk. Plant / Defuse / Revive actions when close are added with their modes (later phases).
        void PursueObjective(Vector3 pos, ObjectiveMode.BotTask task)
        {
            if (agent != null) agent.updateRotation = true;
            var w = Weapon;
            float mult = w != null ? w.moveSpeedMult : 1f;
            float speed = (task == ObjectiveMode.BotTask.Deliver ? RUN_SPEED : WALK_SPEED) * mult;
            Go(pos, speed);
        }

        void Go(Vector3 p, float speed)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
            agent.speed = speed * Mathf.Clamp(ObjectiveMode.SpeedFactor(BotId), 0.1f, 1f);   // VIP walks slower
            agent.isStopped = false;
            agent.SetDestination(p);
        }

        void Halt()
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh) { agent.isStopped = true; agent.ResetPath(); }
        }

        void CheckStuck()
        {
            Vector3 d = transform.position - lastPos; d.y = 0;
            lastPos = transform.position;
            bool moving = agent != null && agent.isOnNavMesh && agent.hasPath && !agent.isStopped && agent.remainingDistance > 1.5f;
            if (moving && d.magnitude < 0.3f * Time.deltaTime) stuckFor += Time.deltaTime;
            else stuckFor = 0f;
            if (stuckFor > 2.5f) { stuckFor = 0f; nextRoam = 0f; strafeUntil = 0f; if (agent.isOnNavMesh) agent.ResetPath(); }
        }

        // ───────── shooting ─────────

        void TryFire(WeaponData w, float dist)
        {
            float now = Time.time;
            if (reloadUntil > 0f)
            {
                if (now < reloadUntil) return;
                reloadUntil = 0f;
                mag = w.magSize;
            }
            if (mag <= 0) { reloadUntil = now + w.reloadTime; return; }
            if (now < nextShot || now < burstPauseUntil) return;
            if (dist > w.range * 0.9f) return;

            float interval = 1f / Mathf.Max(0.1f, w.fireRate * skill.fireMult);
            if (!w.isAutomatic) interval = Mathf.Max(interval, 0.12f + Random.Range(0f, skill.reaction * 0.6f));
            nextShot = now + interval;
            mag--;
            if (w.isAutomatic)
            {
                if (burstLeft <= 0) burstLeft = Random.Range(3, 10);
                if (--burstLeft <= 0) burstPauseUntil = now + Random.Range(0.25f, 0.6f) + skill.reaction * 0.5f;
            }
            if (mag <= 0) reloadUntil = now + w.reloadTime;
            ShotsFired++;
            FireShot(w, dist);
        }

        void FireShot(WeaponData w, float dist)
        {
            Vector3 origin = Eye;
            Vector3 aim = Chest(target);

            // lead a moving target by the BB's flight time
            float tof = dist / Mathf.Max(10f, w.muzzleVelocity) * 1.15f;
            aim += targetVel * tof * skill.lead;

            // accuracy: some shots are near misses
            if (Random.value > skill.accuracy)
            {
                Vector2 off = Random.insideUnitCircle.normalized * Random.Range(0.55f, 1.3f);
                aim += transform.right * off.x + Vector3.up * off.y * 0.6f;
            }

            // aim spread, shrinking while the bot tracks the same target
            float settle = Mathf.Clamp01((Time.time - acquiredAt) / Mathf.Max(0.05f, skill.settle));
            float err = skill.spread * Mathf.Lerp(2.5f, 1f, settle);
            Vector3 dir = (aim - origin).normalized;
            dir = Quaternion.LookRotation(dir) * Quaternion.Euler(Random.Range(-err, err) * 0.7f, Random.Range(-err, err), 0f) * Vector3.forward;

            uint seed = (uint)Random.Range(int.MinValue, int.MaxValue);
            health.EndSpawnProtection();   // firing ends spawn protection, like for players

            Vector3 muzzle = anim != null ? anim.MuzzlePosition : origin + transform.forward * 0.6f;
            if ((muzzle - origin).sqrMagnitude > 2.25f) muzzle = origin;
            int pellets = Mathf.Max(1, w.pelletsPerShot);
            for (int p = 0; p < pellets; p++)
                BBProjectile.Fire(w, origin, BBBallistics.PelletDirection(w, dir, seed, p, false), muzzle, ~0, transform,
                                  hit => OnBBHit(hit, w));
            if (fx != null) fx.BotShotRpc(weaponIdx, origin, dir, seed);
        }

        // The server flies the bot's BBs itself, so a hit here is authoritative.
        void OnBBHit(RaycastHit hit, WeaponData w)
        {
            if (this == null || netObj == null || !netObj.IsSpawned) return;
            var mn = MatchNet.Instance;
            if (mn == null || mn.CurrentPhase != MatchNet.Phase.InMatch) return;
            var h = hit.collider != null ? hit.collider.GetComponentInParent<Health>() : null;
            if (h == null || h == health || !h.IsSpawned || h.isDead.Value || h.IsOut) return;
            if (mn.BlocksDamage(BotId, h.MatchId)) return;   // friendly fire is off in team modes
            h.TakeDamage(w.damage, BotId, w.weaponName, netObj);
        }
    }
}
