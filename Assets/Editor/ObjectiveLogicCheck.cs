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
            try { CheckBomb(); } catch (System.Exception e) { fails++; log.AppendLine("  EXCEPTION (Bomb) " + e); }
            try { CheckVip(); } catch (System.Exception e) { fails++; log.AppendLine("  EXCEPTION (VIP) " + e); }
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

        // ───────────────────────── round modes ─────────────────────────
        static readonly HashSet<ulong> held = new HashSet<ulong>();

        static void ToLive(RoundMode o, Sim sim)
        {
            Step(o, sim, 0.2f);
            int guard = 0;
            while (o.RoundStage != RoundMode.Stage.Live && guard++ < 400) Step(o, sim, 0.1f);
        }

        // Everyone is back in at a round start (the real game respawns them).
        static void ReviveAll(Sim sim)
        {
            for (int i = 0; i < sim.players.Count; i++) { var p = sim.players[i]; p.active = true; sim.players[i] = p; }
        }

        static void NextRound(RoundMode o, Sim sim)
        {
            int r = o.Round;
            int guard = 0;
            while (o.Round == r && guard++ < 200) Step(o, sim, 0.1f);
            ReviveAll(sim);
            ToLive(o, sim);
        }

        // ───────────────────────── Bomb ─────────────────────────
        static void CheckBomb()
        {
            log.AppendLine(" Bomb");
            var sim = new Sim();
            var bm = Make<BombMode>(MatchNet.GameModeType.Bomb, sim, out var go);
            bm.testUse = id => held.Contains(id);
            held.Clear();
            Vector3 siteA = new Vector3(23, 0, 15), siteB = new Vector3(-23, 0, -15);
            bm.SetSites(new[] { siteA, siteB });
            const ulong R1 = 1, R2 = 3, B1 = 2, B2 = 4;
            Vector3 north = new Vector3(0, 0, 30), south = new Vector3(0, 0, -30);
            sim.Set(R1, 1, north); sim.Set(R2, 1, north + Vector3.right * 3); sim.Set(B1, 2, south); sim.Set(B2, 2, south + Vector3.right * 3);

            Step(bm, sim, 1f);
            Check(bm.RoundStage == RoundMode.Stage.Waiting && !bm.TestHoldsRespawn(R1), "waits a moment before round 1 (normal respawns)");
            Step(bm, sim, 1.2f);
            Check(bm.Round == 1 && bm.RoundStage == RoundMode.Stage.Prep, "round 1 starts (prep)");
            Check(bm.AttackTeam == 1 && bm.DefendTeam == 2, "Red attacks first");
            Check(bm.testRespawned.Count == 4, "everyone respawned at the round start");
            Check(bm.State == BombMode.BombState.Carried && (bm.Carrier == R1 || bm.Carrier == R2), "a random attacker carries the bomb");
            Check(bm.TestMoveFactor(B1) == 0f, "frozen during prep");
            Check(bm.TestHoldsRespawn(B1), "no respawns during a round");
            ToLive(bm, sim);
            Check(bm.RoundStage == RoundMode.Stage.Live && bm.TestMoveFactor(B1) == 1f, $"live after {RoundMode.PREP_SECONDS} s prep, moving again");

            ulong carrier = bm.Carrier, other = carrier == R1 ? R2 : R1;
            // carrier goes down -> dropped -> teammate picks it up
            sim.Set(carrier, 1, new Vector3(5, 0, 5)); Step(bm, sim, 0.2f);
            bm.ServerPlayerOut(carrier, B1);
            sim.Set(carrier, 1, new Vector3(5, 0, 5), false);
            Check(bm.State == BombMode.BombState.Dropped && (bm.BombPos - new Vector3(5, 0, 5)).sqrMagnitude < 0.01f, "carrier down: bomb dropped there");
            sim.Set(B1, 2, new Vector3(5, 0, 5.5f)); Step(bm, sim, 0.2f);
            Check(bm.State == BombMode.BombState.Dropped, "defenders can't pick it up");
            sim.Set(B1, 2, south);
            sim.Set(other, 1, new Vector3(5.5f, 0, 5)); Step(bm, sim, 0.2f);
            Check(bm.State == BombMode.BombState.Carried && bm.Carrier == other, "another attacker picks it up");

            // planting: outside the site does nothing, letting go resets, 4 s inside plants
            held.Add(other); Step(bm, sim, 1f);
            Check(bm.ActionProgress == 0f, "E outside a site: no planting");
            sim.Set(other, 1, siteA + Vector3.right * 2f); Step(bm, sim, 2f);
            Check(bm.Actor == other && bm.ActionProgress > 0.4f && bm.ActionProgress < 0.6f, "planting in site A (half way after 2 s)");
            Check(bm.TestMoveFactor(other) == 0f, "the planter stands still");
            held.Remove(other); Step(bm, sim, 0.2f);
            Check(bm.ActionProgress == 0f && bm.State == BombMode.BombState.Carried, "letting go of E cancels the plant");
            held.Add(other); Step(bm, sim, BombMode.PLANT_SECONDS + 0.2f);
            Check(bm.State == BombMode.BombState.Planted && bm.PlantedSite == 0, $"planted at A after {BombMode.PLANT_SECONDS} s");
            Check(bm.PointsFor(other) == 1, "planter credited");
            held.Remove(other);

            // attackers all out after the plant: the round goes on (the bomb must be defused)
            sim.Set(other, 1, siteA, false); Step(bm, sim, 0.5f);
            Check(bm.RoundStage == RoundMode.Stage.Live, "attackers out after the plant: round continues");
            // defuse: 6 s next to the bomb, interrupted resets
            sim.Set(B1, 2, bm.BombPos + Vector3.forward * 1f); held.Add(B1);
            Step(bm, sim, 3f);
            Check(bm.Actor == B1 && bm.ActionProgress > 0.45f, "defusing");
            held.Remove(B1); Step(bm, sim, 0.2f);
            Check(bm.ActionProgress == 0f, "letting go resets the defuse");
            held.Add(B1); Step(bm, sim, BombMode.DEFUSE_SECONDS + 0.2f);
            Check(bm.State == BombMode.BombState.Defused && bm.RoundStage == RoundMode.Stage.Over && bm.RoundWinner == 2, "defused after 6 s: BLUE (defenders) win the round");
            Check(bm.TeamScore(2) == 1 && bm.PointsFor(B1) == 1, "round point + defuser credited");
            held.Clear();

            // round 2: roles swap, blue plants, it explodes after 40 s
            NextRound(bm, sim);
            Check(bm.Round == 2 && bm.AttackTeam == 2, "round 2: roles swapped (Blue attacks)");
            Check(bm.Carrier == B1 || bm.Carrier == B2, "a Blue player has the bomb");
            ulong bc = bm.Carrier;
            sim.Set(bc, 2, siteB); held.Add(bc); Step(bm, sim, BombMode.PLANT_SECONDS + 0.2f);
            Check(bm.State == BombMode.BombState.Planted && bm.PlantedSite == 1, "planted at B");
            held.Clear();
            Step(bm, sim, BombMode.FUSE_SECONDS - 1f);
            Check(bm.RoundStage == RoundMode.Stage.Live && bm.FuseLeft > 0f, "still ticking (the round clock is paused while planted)");
            Step(bm, sim, 1.5f);
            Check(bm.State == BombMode.BombState.Exploded && bm.RoundWinner == 2 && bm.TeamScore(2) == 2, "explodes after 40 s: attackers win");

            // round 3 (Red attacks): attackers eliminated before planting -> defenders win
            NextRound(bm, sim);
            Check(bm.AttackTeam == 1, "round 3: Red attacks again");
            sim.Set(R1, 1, north, false); sim.Set(R2, 1, north, false); Step(bm, sim, 0.2f);
            Check(bm.RoundWinner == 2 && bm.RoundReason.Contains("eliminated"), "all attackers out before a plant: defenders win");

            // round 4 (Blue attacks): defenders eliminated -> attackers win
            NextRound(bm, sim);
            sim.Set(R1, 1, north, false); sim.Set(R2, 1, north, false); Step(bm, sim, 0.2f);
            Check(bm.RoundWinner == 2 && bm.RoundReason.Contains("defenders"), "all defenders out: attackers win");

            // round 5 (Red attacks): time runs out -> defenders (Blue) win
            NextRound(bm, sim);
            Step(bm, sim, BombMode.ROUND_SECONDS - 1f, 0.5f);
            Check(bm.RoundStage == RoundMode.Stage.Live, "round still running before 2:30");
            Step(bm, sim, 1.5f);
            Check(bm.RoundWinner == 2 && bm.RoundReason.Contains("time"), "time out: defenders win");
            Check(bm.TeamScore(2) == 5 && bm.TeamScore(1) == 0, $"round score {bm.TeamScore(1)} : {bm.TeamScore(2)}");

            // a team nobody plays on can't be 'eliminated'
            var sim2 = new Sim();
            var solo = Make<BombMode>(MatchNet.GameModeType.Bomb, sim2, out var go2);
            solo.SetSites(new[] { siteA, siteB });
            sim2.Set(R1, 1, north);
            ToLive(solo, sim2);
            Step(solo, sim2, 1f);
            Check(solo.RoundStage == RoundMode.Stage.Live, "one player alone: no instant elimination win");
            Object.DestroyImmediate(go2);
            Object.DestroyImmediate(go);
        }

        // ───────────────────────── VIP escort ─────────────────────────
        static void CheckVip()
        {
            log.AppendLine(" VIP escort");
            var sim = new Sim();
            var vm = Make<VipEscort>(MatchNet.GameModeType.VipEscort, sim, out var go);
            vm.testUse = id => held.Contains(id);
            held.Clear();
            Vector3 start = new Vector3(0, 0, -35), extract = new Vector3(0, 0, 35.5f);
            vm.SetRoute(start, extract);
            const ulong R1 = 1, R2 = 3, B1 = 2, B2 = 4;
            Vector3 north = new Vector3(0, 0, 30), south = new Vector3(0, 0, -30);
            sim.Set(R1, 1, south); sim.Set(R2, 1, south); sim.Set(B1, 2, north); sim.Set(B2, 2, north);
            var pistol = new WeaponData { weaponName = "1911", model = "Pistol" };
            var rifle = new WeaponData { weaponName = "AK", model = "AK47" };

            Check(vm.TestAllowsWeapon(R1, rifle), "before the first round: any weapon");
            Step(vm, sim, 2.2f);
            Check(vm.Round == 1 && vm.AttackTeam == 1, "round 1: Red escorts");
            ulong vip = vm.Vip, mate = vip == R1 ? R2 : R1;
            Check(vip == R1 || vip == R2, "a random Red player is the VIP");
            var vp = vm.testRespawned.Find(x => x.id == vip);
            Check((vp.pose.position - start).sqrMagnitude < 0.1f, "the VIP starts at the VIP start");
            Check(!vm.TestAllowsWeapon(vip, rifle) && vm.TestAllowsWeapon(vip, pistol) && vm.TestAllowsWeapon(mate, rifle), "VIP: sidearms only (others: anything)");
            Check(vm.TestBlocksSprint(vip) && !vm.TestBlocksSprint(mate), "VIP can't sprint");
            ToLive(vm, sim);
            Check(Mathf.Approximately(vm.TestMoveFactor(vip), VipEscort.VIP_SPEED) && vm.TestMoveFactor(mate) == 1f, "VIP walks slower");

            // escorts respawn after the delay, the VIP never
            vm.ServerPlayerOut(mate, B1);
            sim.Set(mate, 1, south, false);
            Check(vm.TestHoldsRespawn(mate), "escort down: waits");
            Step(vm, sim, VipEscort.RESPAWN_DELAY + 0.2f);
            Check(!vm.TestHoldsRespawn(mate), $"escort respawns after {VipEscort.RESPAWN_DELAY} s");
            Check(vm.TestHoldsRespawn(vip), "the VIP never respawns during the round");
            sim.Set(mate, 1, south, true);

            // extraction: only the VIP, holding E inside the zone for 3 s
            sim.Set(mate, 1, extract); held.Add(mate); Step(vm, sim, VipEscort.EXTRACT_SECONDS + 0.5f);
            Check(vm.RoundStage == RoundMode.Stage.Live && vm.ExtractProgress == 0f, "an escort can't extract");
            held.Clear();
            sim.Set(vip, 1, extract + Vector3.left * 2f); Step(vm, sim, 1f);
            Check(vm.ExtractProgress == 0f, "VIP in the zone without E: nothing");
            held.Add(vip); Step(vm, sim, 1.5f);
            Check(vm.ExtractProgress > 0.4f && vm.RoundStage == RoundMode.Stage.Live, "extracting");
            Step(vm, sim, 2f);
            Check(vm.RoundStage == RoundMode.Stage.Over && vm.RoundWinner == 1 && vm.TeamScore(1) == 1, "VIP extracted: Red (escorts) win the round");
            Check(vm.PointsFor(vip) == 1, "VIP credited");
            held.Clear();

            // round 2: Blue escorts, Red takes the VIP down
            NextRound(vm, sim);
            Check(vm.Round == 2 && vm.AttackTeam == 2 && (vm.Vip == B1 || vm.Vip == B2), "round 2: Blue escorts a Blue VIP");
            ulong v2 = vm.Vip;
            Check(vm.TestAllowsWeapon(vip, rifle), "last round's VIP gets the rifles back");
            vm.ServerPlayerOut(v2, R1);
            sim.Set(v2, 2, north, false);
            Check(vm.RoundWinner == 1 && vm.RoundStage == RoundMode.Stage.Over, "VIP down: hunters (Red) win the round");

            // round 3: Red escorts; time runs out -> hunters (Blue) win
            NextRound(vm, sim);
            Check(vm.AttackTeam == 1, "round 3: Red escorts again");
            Step(vm, sim, VipEscort.ROUND_SECONDS - 1f, 0.5f);
            Check(vm.RoundStage == RoundMode.Stage.Live, "running before 3:00");
            Step(vm, sim, 1.5f);
            Check(vm.RoundWinner == 2 && vm.RoundReason.Contains("time"), "time out: hunters win");

            // VIP out in the tick (no kill report) also ends the round
            NextRound(vm, sim);
            ulong v4 = vm.Vip;
            sim.Set(v4, vm.AttackTeam, south, false); Step(vm, sim, 0.2f);
            Check(vm.RoundStage == RoundMode.Stage.Over && vm.RoundWinner == vm.DefendTeam, "VIP out in the tick: hunters win");
            Check(vm.TeamScore(1) == 3 && vm.TeamScore(2) == 1, $"round score {vm.TeamScore(1)} : {vm.TeamScore(2)}");
            Object.DestroyImmediate(go);
        }
    }
}
#endif
