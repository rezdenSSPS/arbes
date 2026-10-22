#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AirsoftFPS.Editor
{
    // Server-rule checks for the objective modes without networking: a detached CTF / Domination object
    // gets scripted players (positions, alive or not) and a fake clock, and ServerTick runs as on the host.
    //
    // Menu: Tools/Airsoft/Check Objective Rules
    // Batch: Unity -batchmode -projectPath <p> -executeMethod AirsoftFPS.Editor.ObjectiveLogicCheck.RunBatch
    public static class ObjectiveLogicCheck
    {
        static int fails, passes;
        static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

        static void Check(bool ok, string what)
        {
            if (ok) passes++; else fails++;
            log.AppendLine((ok ? "  PASS " : "  FAIL ") + what);
        }

        public static void RunBatch()
        {
            bool ok = Run();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        [MenuItem("Tools/Airsoft/Check Objective Rules")]
        public static bool Run()
        {
            fails = passes = 0;
            log.Clear();
            log.AppendLine("[Airsoft] Objective rule check");
            try { CheckCtf(); } catch (System.Exception e) { fails++; log.AppendLine("  EXCEPTION (CTF) " + e); }
            try { CheckDomination(); } catch (System.Exception e) { fails++; log.AppendLine("  EXCEPTION (Domination) " + e); }
            log.AppendLine($"  => {passes} passed, {fails} failed");
            if (fails == 0) Debug.Log(log.ToString()); else Debug.LogError(log.ToString());
            return fails == 0;
        }

        class Sim
        {
            public readonly List<ObjectiveMode.Participant> players = new List<ObjectiveMode.Participant>();
            public double now;
            public void Set(ulong id, int team, Vector3 pos, bool active = true)
            {
                for (int i = 0; i < players.Count; i++)
                    if (players[i].id == id) { players[i] = new ObjectiveMode.Participant { id = id, team = team, position = pos, active = active }; return; }
                players.Add(new ObjectiveMode.Participant { id = id, team = team, position = pos, active = active });
            }
        }

        static T Make<T>(MatchNet.GameModeType mode, Sim sim, out GameObject go) where T : ObjectiveMode
        {
            go = new GameObject("~ObjectiveCheck") { hideFlags = HideFlags.HideAndDontSave };
            var o = (T)ObjectiveMode.CreateDetached(mode, go);
            o.testParticipants = () => sim.players;
            o.testNow = 0;
            return o;
        }

        static void Step(ObjectiveMode o, Sim sim, float seconds, float dt = 0.1f)
        {
            for (float t = 0; t < seconds - 1e-4f; t += dt)
            {
                sim.now += dt;
                o.testNow = sim.now;
                o.ServerTick(dt);
            }
        }

        // ───────────────────────── Capture the Flag ─────────────────────────
        static void CheckCtf()
        {
            log.AppendLine(" Capture the Flag");
            var sim = new Sim();
            var ctf = Make<CaptureTheFlag>(MatchNet.GameModeType.CaptureTheFlag, sim, out var go);
            Vector3 redHome = new Vector3(6, 0, 33), blueHome = new Vector3(-6, 0, -33), mid = Vector3.zero;
            ctf.SetHomes(redHome, blueHome);
            const ulong R = 1, R2 = 3, B = 2;
            var red = ctf.FlagOf(1); var blue = ctf.FlagOf(2);

            sim.Set(R, 1, mid); sim.Set(R2, 1, mid + Vector3.right * 10); sim.Set(B, 2, mid + Vector3.left * 10);
            Step(ctf, sim, 0.2f);
            Check(red.state == CaptureTheFlag.FlagState.Home && blue.state == CaptureTheFlag.FlagState.Home, "both flags start home");

            sim.Set(R, 1, redHome + Vector3.right * 0.5f); Step(ctf, sim, 0.2f);
            Check(red.state == CaptureTheFlag.FlagState.Home, "touching your own home flag does nothing");

            sim.Set(R, 1, blueHome + Vector3.forward * 1f); Step(ctf, sim, 0.2f);
            Check(blue.state == CaptureTheFlag.FlagState.Carried && blue.carrier == R, "touching the enemy flag picks it up");
            Check(ctf.IsCarrying(R, out int ft) && ft == 2, "carrier is flagged (no sprint)");

            sim.Set(R, 1, mid); Step(ctf, sim, 0.2f);
            Check((blue.pos - mid).sqrMagnitude < 0.01f, "carried flag follows the carrier");

            sim.Set(R, 1, redHome + Vector3.left * 1.5f); Step(ctf, sim, 0.2f);
            Check(ctf.TeamScore(1) == 1 && blue.state == CaptureTheFlag.FlagState.Home, "bringing it to your stand scores and returns it");
            Check(ctf.PointsFor(R) == 1, "capturer gets a capture point");

            // drop on death -> owners return by touch
            sim.Set(R, 1, blueHome); Step(ctf, sim, 0.2f);
            Vector3 dropAt = new Vector3(0, 0, -15);
            sim.Set(R, 1, dropAt); Step(ctf, sim, 0.2f);
            ctf.ServerPlayerOut(R, B);
            Check(blue.state == CaptureTheFlag.FlagState.Dropped && (blue.pos - dropAt).sqrMagnitude < 0.01f, "carrier killed: flag dropped where they died");
            sim.Set(R, 1, dropAt, false);
            Step(ctf, sim, 0.2f);
            Check(blue.state == CaptureTheFlag.FlagState.Dropped, "a dead player can't take it back");
            sim.Set(B, 2, dropAt + Vector3.forward * 1f); Step(ctf, sim, 0.2f);
            Check(blue.state == CaptureTheFlag.FlagState.Home, "owning team returns a dropped flag by touch");

            // drop when the carrier becomes inactive (e.g. Milsim HIT) -> auto return after 20 s
            sim.Set(B, 2, mid + Vector3.left * 10);
            sim.Set(R, 1, blueHome); Step(ctf, sim, 0.2f);
            sim.Set(R, 1, dropAt, false); Step(ctf, sim, 0.2f);
            Check(blue.state == CaptureTheFlag.FlagState.Dropped, "carrier out (HIT / dead) drops the flag in the tick");
            sim.Set(R, 1, mid);
            Step(ctf, sim, 19f);
            Check(blue.state == CaptureTheFlag.FlagState.Dropped, "still dropped after 19 s");
            Step(ctf, sim, 1.5f);
            Check(blue.state == CaptureTheFlag.FlagState.Home, "auto-returns after 20 s");

            // can't score while your own flag is away
            sim.Set(B, 2, redHome); Step(ctf, sim, 0.2f);
            Check(red.state == CaptureTheFlag.FlagState.Carried && red.carrier == B, "blue takes the red flag");
            sim.Set(B, 2, mid + Vector3.left * 10);
            sim.Set(R, 1, blueHome); Step(ctf, sim, 0.2f);
            sim.Set(R, 1, redHome); Step(ctf, sim, 0.5f);
            Check(ctf.TeamScore(1) == 1 && blue.carrier == R, "no capture while your own flag isn't home");
            // teammate kills the blue carrier (drops red flag), R2 returns it, then R scores
            ctf.ServerPlayerOut(B, R2);
            sim.Set(B, 2, mid + Vector3.left * 10, false);
            sim.Set(R2, 1, red.pos); Step(ctf, sim, 0.2f);
            Check(red.state == CaptureTheFlag.FlagState.Home, "teammate returns your flag");
            Step(ctf, sim, 0.2f);
            Check(ctf.TeamScore(1) == 2, "then the waiting carrier scores");

            ctf.WinnerOnTimeout(out int w, out bool draw);
            Check(w == 1 && !draw, "time-out winner = more captures");

            // blue captures twice -> 2:2 -> draw on time
            sim.Set(B, 2, mid + Vector3.left * 10, true);
            for (int i = 0; i < 2; i++)
            {
                sim.Set(B, 2, redHome); Step(ctf, sim, 0.2f);
                sim.Set(B, 2, blueHome); Step(ctf, sim, 0.2f);
            }
            ctf.WinnerOnTimeout(out w, out draw);
            Check(ctf.TeamScore(2) == 2 && draw, "2 : 2 on time is a draw");
            Object.DestroyImmediate(go);
        }

        // ───────────────────────── Domination ─────────────────────────
        static void CheckDomination()
        {
            log.AppendLine(" Domination");
            var sim = new Sim();
            var dom = Make<Domination>(MatchNet.GameModeType.Domination, sim, out var go);
            Vector3 a = new Vector3(-20, 0, 22), b = new Vector3(-7.5f, 0, -7.5f), c = new Vector3(22, 0, -22);
            dom.SetPoints(new[] { a, b, c });
            var A = dom.points[0]; var Bp = dom.points[1]; var C = dom.points[2];
            Check(A.label == "A" && Bp.label == "B" && C.label == "C", "points are labelled A, B, C");
            const ulong R = 1, R2 = 3, Bl = 2, B2 = 4;
            Vector3 away = new Vector3(0, 0, 38);

            sim.Set(R, 1, a + Vector3.right * 2f); sim.Set(R2, 1, away); sim.Set(Bl, 2, -away); sim.Set(B2, 2, -away);
            Step(dom, sim, Domination.CAPTURE_SECONDS - 0.5f);
            Check(A.owner == 0 && A.capTeam == 1 && A.progress > 0.8f, "capture in progress");
            Step(dom, sim, 0.7f);
            Check(A.owner == 1, $"one player captures a neutral point in {Domination.CAPTURE_SECONDS} s");
            Check(dom.PointsFor(R) == 1, "capturer credited");

            sim.Set(R, 1, b); sim.Set(Bl, 2, b + Vector3.forward * 3f);
            Step(dom, sim, 3f);
            Check(Bp.contested && Bp.owner == 0 && Bp.progress <= 0.001f, "both teams inside: contested, no progress");

            sim.Set(Bl, 2, -away);
            Step(dom, sim, 2f);
            float half = Bp.progress;
            sim.Set(R, 1, away);
            Step(dom, sim, 2f);
            Check(Bp.progress < half && Bp.progress > 0f, "progress fades when nobody is inside");

            // two red players on C: 1.5x speed
            sim.Set(R, 1, c); sim.Set(R2, 1, c + Vector3.left);
            Step(dom, sim, Domination.CAPTURE_SECONDS / 1.5f + 0.2f);
            Check(C.owner == 1, "two players capture faster (1.5x)");

            // blue takes A: neutralise, then capture
            sim.Set(R, 1, away); sim.Set(R2, 1, away);
            sim.Set(Bl, 2, a);
            Step(dom, sim, Domination.CAPTURE_SECONDS + 0.2f);
            Check(A.owner == 0, "enemy point is neutralised first");
            Step(dom, sim, Domination.CAPTURE_SECONDS + 0.2f);
            Check(A.owner == 2, "then captured");

            // scoring: red owns C, blue owns A
            int r0 = dom.TeamScore(1), b0 = dom.TeamScore(2);
            sim.Set(Bl, 2, -away);
            Step(dom, sim, Domination.SCORE_INTERVAL * 5f);
            int dr = dom.TeamScore(1) - r0, db = dom.TeamScore(2) - b0;   // 5 intervals (+-1 for where the timer was)
            Check(dr == db && dr >= 4 && dr <= 6, $"each owned point scores 1 per interval ({dr} : {db} in 5 intervals)");

            // red takes B as well -> 2 points per interval, runs to the cap
            sim.Set(R, 1, b); Step(dom, sim, Domination.CAPTURE_SECONDS + 0.5f);
            Check(Bp.owner == 1, "red captures B");
            sim.Set(R, 1, away);
            Step(dom, sim, Domination.SCORE_INTERVAL * 120f, 0.5f);
            Check(dom.TeamScore(1) == Domination.SCORE_TARGET && dom.TeamScore(2) < Domination.SCORE_TARGET, $"score stops at {Domination.SCORE_TARGET} (match end)");
            dom.WinnerOnTimeout(out int w, out bool draw);
            Check(w == 1 && !draw, "time-out winner = higher score");
            Object.DestroyImmediate(go);
        }
    }
}
#endif
