#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace AirsoftFPS.Editor
{
    // Real game frames for the main-menu background (MenuShowcase): plays an actual match with bots
    // (host + bot fill 8) on one map / mode and, once the bots are fighting, takes three pictures:
    //   0  first person: the host stands behind a teammate, looking at the enemy that teammate fights,
    //      holding the chosen replica (viewmodel in frame)
    //   1  over the shoulder of a fighting bot
    //   2  a chase cam behind a fighting bot
    // Saved as Assets/Resources/MenuShots/m<map>_g<mode>_<n>.jpg (the menu reads map + mode from the name).
    //
    // Batch (one match per run): Unity -batchmode -projectPath <p> -executeMethod AirsoftFPS.Editor.MenuShotsCapture.RunBatch
    //     -shotMode <GameModeType int> -shotMap <MapCatalog index> [-shotWeapon <weapon index>] [-shotSeed n] [-shotDelay s]
    // All the menu pictures at once: tools/menu_shots.sh
    public static class MenuShotsCapture
    {
        const string DIR = "Assets/Resources/MenuShots";
        const int W = 1600, H = 900, SS = 2;   // output size, supersampling
        static readonly float[] AT = { 8f, 14f, 20f };   // seconds after the go

        static MatchNet.GameModeType mode = MatchNet.GameModeType.TeamDeathmatch;
        static int mapIndex, weaponIndex = 5, stage, shot;
        static double stageStart, runStart, liveAt;
        static int savedBotFill;
        static bool fpWeaponSet;
        static float delay;   // -shotDelay: extra seconds before the first picture (round modes: PREP first)
        static string fpPath;
        static Health fpTarget;
        static int fpFrame;
        static readonly Dictionary<BotBrain, int> lastFired = new Dictionary<BotBrain, int>();
        static readonly HashSet<BotBrain> firing = new HashSet<BotBrain>();

        public static void RunBatch()
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
            {
                if (a[i] == "-shotMode") mode = (MatchNet.GameModeType)int.Parse(a[i + 1]);
                if (a[i] == "-shotMap") mapIndex = int.Parse(a[i + 1]);
                if (a[i] == "-shotWeapon") weaponIndex = int.Parse(a[i + 1]);
                if (a[i] == "-shotSeed") UnityEngine.Random.InitState(int.Parse(a[i + 1]));
                if (a[i] == "-shotDelay") delay = float.Parse(a[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            }
            stage = 0; shot = 0; fpWeaponSet = false;
            savedBotFill = PlayerPrefs.GetInt("Airsoft.BotFill", 0);
            Directory.CreateDirectory(DIR);
            EditorSceneManager.OpenScene("Assets/Scenes/AirsoftLobby.unity", OpenSceneMode.Single);
            runStart = stageStart = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static double InStage => EditorApplication.timeSinceStartup - stageStart;
        static void Next() { stage++; stageStart = EditorApplication.timeSinceStartup; }

        static void Finish(string why)
        {
            Debug.Log("[Airsoft] Menu shots: " + why);
            EditorApplication.update -= Tick;
            PlayerPrefs.SetInt("Airsoft.BotFill", savedBotFill);
            PlayerPrefs.Save();
            EditorApplication.Exit(why.StartsWith("done") ? 0 : 1);
        }

        static Transform Me()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && nm.LocalClient != null && nm.LocalClient.PlayerObject != null ? nm.LocalClient.PlayerObject.transform : null;
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup - runStart > 300) { Finish("timed out in stage " + stage); return; }
            if (!EditorApplication.isPlaying) return;
            var nm = NetworkManager.Singleton;
            var mn = MatchNet.Instance;
            try
            {
                switch (stage)
                {
                    case 0:
                        if (nm == null || InStage < 1) return;
                        var utp = nm.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                        if (utp != null) utp.SetConnectionData("127.0.0.1", (ushort)UnityEngine.Random.Range(20000, 40000));
                        nm.StartHost();
                        Next(); break;
                    case 1:
                        if (mn == null || mn.Roster.Count == 0 || InStage < 1) return;
                        mn.HostLockMap(mapIndex);
                        mn.HostSetMode(mode);
                        mn.HostSetBotFill(8);
                        if (!mn.LocalReady) mn.ToggleReady();
                        Next(); break;
                    case 2:
                        if (!mn.AllReady()) { if (InStage > 10) Finish("never ready"); return; }
                        mn.HostStartMatch();
                        Next(); break;
                    case 3:
                        if (SceneManager.GetActiveScene().name != MatchNet.CurrentMapScene || Me() == null || MatchNet.IntroFrozen || InStage < 2)
                        { if (InStage > 60) Finish("arena never came up"); return; }
                        liveAt = EditorApplication.timeSinceStartup + delay;
                        // the photographer must stay alive (bots also leave a protected player alone)
                        Me().GetComponent<Health>().spawnProtectedUntil.Value = nm.ServerTime.Time + 600;
                        Next(); break;
                    default:
                        double t = EditorApplication.timeSinceStartup - liveAt;
                        if (!fpWeaponSet && t > AT[0] - 1.5f) { SetWeapon(); fpWeaponSet = true; }
                        if (shot >= AT.Length) { Finish("done " + shot + " shots"); return; }
                        if (fpPath != null)
                        {
                            if (Time.frameCount < fpFrame) return;
                            var pc = Me().GetComponent<FPSController>().playerCamera;
                            var wcv = Me().GetComponent<WeaponController>();
                            bool ok = !Me().GetComponent<Health>().isDead.Value && wcv != null && wcv.weaponHolder != null
                                      && wcv.weaponHolder.gameObject.activeInHierarchy && !MatchIntro.Flying
                                      && fpTarget != null && !fpTarget.isDead.Value && Sees(pc.transform.position, fpTarget)
                                      && !Physics.SphereCast(pc.transform.position, 0.4f, pc.transform.forward, out _, 5f, ~0, QueryTriggerInteraction.Ignore);
                            if (ok)
                            {
                                pc.fieldOfView = 80f;
                                Render(pc, fpPath);
                            }
                            else { shot--; Debug.Log("[Airsoft] first-person shot not ready, again"); }
                            fpPath = null;
                        }
                        UpdateFiring();
                        if (t < AT[shot]) return;
                        // wait (up to 3 s) for a bot pulling the trigger this very frame: muzzle flash, BBs in the air
                        if (TakeShot(mn, shot, t > AT[shot] + 3f)) shot++;
                        else if (t > AT[shot] + 12f) shot++;   // nothing worth a picture: skip it
                        break;
                }
            }
            catch (Exception e) { Finish("exception: " + e); }
        }

        // The host holds the chosen replica (visual only: the viewmodel shows it).
        static void SetWeapon()
        {
            var wc = Me() != null ? Me().GetComponent<WeaponController>() : null;
            if (wc == null || wc.weapons == null || weaponIndex < 0 || weaponIndex >= wc.weapons.Length) return;
            typeof(WeaponController).GetMethod("SwitchToWeapon", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(wc, new object[] { weaponIndex });
        }

        static void UpdateFiring()
        {
            firing.Clear();
            foreach (var b in UnityEngine.Object.FindObjectsByType<BotBrain>(FindObjectsSortMode.None))
            {
                if (lastFired.TryGetValue(b, out int n) && b.ShotsFired > n) firing.Add(b);
                lastFired[b] = b.ShotsFired;
            }
        }

        struct Fighter { public Health h; public BotBrain brain; public Vector3 eye; }

        static List<Fighter> Bots()
        {
            var list = new List<Fighter>();
            foreach (var b in UnityEngine.Object.FindObjectsByType<BotBrain>(FindObjectsSortMode.None))
            {
                var h = b.GetComponent<Health>();
                if (h == null || h.isDead.Value || h.IsOut) continue;
                list.Add(new Fighter { h = h, brain = b, eye = b.transform.position + Vector3.up * 1.6f });
            }
            return list;
        }

        static bool Enemies(MatchNet mn, Health a, Health b)
        {
            if (!mn.TeamMode) return true;
            return mn.TeamOf(a.MatchId) != mn.TeamOf(b.MatchId);
        }

        static bool Sees(Vector3 from, Health target)
        {
            Vector3 to = target.transform.position + Vector3.up * 1.3f;
            if (!Physics.Linecast(from, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider.GetComponentInParent<Health>() == target;
        }

        // A bot and the enemy it fights: engaged bots first, 8-35 m apart, in sight of each other.
        static bool FindFight(MatchNet mn, bool anyFight, out Fighter a, out Fighter b)
        {
            a = b = default;
            var bots = Bots();
            float best = -1f;
            foreach (var x in bots)
                foreach (var y in bots)
                {
                    if (x.h == y.h || !Enemies(mn, x.h, y.h)) continue;
                    float d = Vector3.Distance(x.eye, y.eye);
                    if (d < 6f || d > (anyFight ? 34f : 26f) || !Sees(x.eye, y.h)) continue;
                    if (!anyFight && !firing.Contains(x.brain)) continue;
                    float score = (firing.Contains(x.brain) ? 20f : 0f)
                                  + (x.brain.DebugState.StartsWith("engage") ? 10f : 0f) + (y.brain.DebugState.StartsWith("engage") ? 4f : 0f)
                                  + 5f - Mathf.Abs(d - 13f) * 0.3f + UnityEngine.Random.value;
                    if (score > best) { best = score; a = x; b = y; }
                }
            return best >= 0f;
        }

        static bool TakeShot(MatchNet mn, int n, bool anyFight)
        {
            if (!FindFight(mn, anyFight, out var a, out var b)) return false;
            Vector3 dir = b.eye - a.eye; dir.y = 0f; dir.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            Vector3 aim = b.h.transform.position + Vector3.up * 1.25f;
            string path = $"{DIR}/m{mapIndex}_g{(int)mode}_{n}.jpg";
            var me = Me();
            var fps = me.GetComponent<FPSController>();
            var pcam = fps.playerCamera;

            if (n == 0)
            {
                // first person, a step behind and to the left of the teammate
                var myH = me.GetComponent<Health>();
                if (myH == null || myH.isDead.Value) return false;
                Vector3 spot = a.h.transform.position - dir * 2.2f - right * 1.7f;
                if (NavMesh.SamplePosition(spot, out var hitN, 2f, NavMesh.AllAreas)) spot = hitN.position;
                var cc = me.GetComponent<CharacterController>();
                cc.enabled = false;
                me.position = spot;
                Vector3 eye = spot + Vector3.up * (cc.height - 0.15f);
                Vector3 look = aim - eye;
                me.rotation = Quaternion.LookRotation(new Vector3(look.x, 0f, look.z));
                cc.enabled = true;
                Physics.SyncTransforms();
                float pitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
                fps.cameraHolder.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                if (!Sees(eye, b.h)) return false;
                // let the game run a few frames at the new pose (viewmodel, bounds, shadows settle), then shoot
                typeof(FPSController).GetField("xRotation", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(fps, pitch);
                fpPath = path;
                fpTarget = b.h;
                fpFrame = Time.frameCount + 3;
                return true;
            }

            var go = new GameObject("MenuShotCam");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(pcam);
            var urp = go.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = true;
            urp.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            Vector3 target = n == 1 ? Vector3.Lerp(a.eye, aim, 0.8f) : Vector3.Lerp(a.eye, aim, 0.55f);
            cam.fieldOfView = n == 1 ? 42f : 55f;
            // over the shoulder: just behind and beside the bot, its enemy ahead;
            // chase cam: behind, beside and above it, looking past it at the fight. Either side, whichever is clear.
            Vector3 from = a.eye + Vector3.up * 0.15f, pos = default;
            bool clear = false;
            foreach (float side in new[] { 1f, -1f })
            {
                Vector3 p = n == 1 ? a.eye - dir * 1.6f + right * (0.6f * side) + Vector3.up * 0.12f
                                   : a.eye - dir * 3.2f + right * (1.2f * side) + Vector3.up * 1.1f;
                Vector3 off = p - from;
                if (Physics.SphereCast(from, 0.3f, off.normalized, out RaycastHit hit, off.magnitude, ~0, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<Health>() == null) continue;
                if (!Sees(p, b.h)) continue;
                pos = p; clear = true; break;
            }
            if (!clear) { UnityEngine.Object.Destroy(go); return false; }   // boxed in: try another fight
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
            Render(cam, path);
            UnityEngine.Object.Destroy(go);
            return true;
        }

        static void Render(Camera cam, string path)
        {
            var big = new RenderTexture(W * SS, H * SS, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var small = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            cam.targetTexture = big;
            cam.Render();
            cam.targetTexture = prev;
            Graphics.Blit(big, small);
            RenderTexture.active = small;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToJPG(88));
            UnityEngine.Object.DestroyImmediate(tex);
            big.Release(); small.Release();
            Debug.Log("[Airsoft] Menu shot " + path);
        }
    }
}
#endif
