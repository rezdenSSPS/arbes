#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AirsoftFPS.Editor
{
    // Server-rule checks for the Medic mode without networking: a detached Medic object gets scripted
    // players (positions, in / HIT) and a fake clock; ServerTick runs as on the host.
    //
    // Menu: Tools/Airsoft/Check Medic Rules
    // Batch: Unity -batchmode -projectPath <p> -executeMethod AirsoftFPS.Editor.MedicLogicCheck.RunBatch
    public static class MedicLogicCheck
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

        [MenuItem("Tools/Airsoft/Check Medic Rules")]
        public static bool Run()
        {
            fails = passes = 0;
            log.Clear();
            log.AppendLine("[Airsoft] Medic rule check");
            try { CheckNumbers(); } catch (System.Exception e) { fails++; log.AppendLine("  EXCEPTION (numbers) " + e); }
            try { CheckRules(); } catch (System.Exception e) { fails++; log.AppendLine("  EXCEPTION (rules) " + e); }
            try { CheckTimeout(); } catch (System.Exception e) { fails++; log.AppendLine("  EXCEPTION (timeout) " + e); }
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
            public void SetActive(ulong id, bool active)
            {
                for (int i = 0; i < players.Count; i++)
                    if (players[i].id == id) { var p = players[i]; p.active = active; players[i] = p; }
            }
            public void Move(ulong id, Vector3 pos)
            {
                for (int i = 0; i < players.Count; i++)
                    if (players[i].id == id) { var p = players[i]; p.position = pos; players[i] = p; }
            }
            public void Remove(ulong id) => players.RemoveAll(p => p.id == id);
        }

        static Medic Make(Sim sim, out GameObject go)
        {
            go = new GameObject("~MedicCheck") { hideFlags = HideFlags.HideAndDontSave };
            var o = (Medic)ObjectiveMode.CreateDetached(MatchNet.GameModeType.Medic, go);
            o.testParticipants = () => sim.players;
            o.testNow = 0;
            return o;
        }

        static void Step(Medic o, Sim sim, float seconds, float dt = 0.1f)
        {
            for (float t = 0; t < seconds - 1e-4f; t += dt)
            {
                sim.now += dt;
                o.testNow = sim.now;
                o.ServerTick(dt);
            }
        }

        // HIT -> the Milsim hook (MatchNet.ReportHit -> ObjectiveMode.NotifyHit -> ServerPlayerOut)
        static void Hit(Medic o, Sim sim, ulong id, ulong shooter)
        {
            sim.SetActive(id, false);
            o.ServerPlayerOut(id, shooter);
        }

        static void CheckNumbers()
        {
            log.AppendLine(" Numbers");
            Check(Medic.TicketsFor(4) == 25, "4 players: 25 tickets");
            Check(Medic.TicketsFor(1) == 10 && Medic.TicketsFor(2) == 15 && Medic.TicketsFor(3) == 20, "smaller teams: 10 / 15 / 20 tickets");
            Check(Medic.TicketsFor(6) == 35, "6 players: 35 tickets");
            Check(Medic.MedicsFor(1) == 1 && Medic.MedicsFor(4) == 1 && Medic.MedicsFor(5) == 2 && Medic.MedicsFor(8) == 2 && Medic.MedicsFor(9) == 3,
                  "1 medic per started group of 4");
            Check(ObjectiveMode.UsesHitRule(MatchNet.GameModeType.Medic) && MatchNet.IsHitRuleMode(MatchNet.GameModeType.Medic), "Medic plays with the HIT rule");
            Check(!ObjectiveMode.UsesHitRule(MatchNet.GameModeType.Domination) && MatchNet.IsHitRuleMode(MatchNet.GameModeType.Milsim), "other modes unchanged");
            Check(MatchNet.IsTeamMode(MatchNet.GameModeType.Medic), "Medic is a team mode");
            Check(!MatchNet.ModeHasBots(MatchNet.GameModeType.Medic), "no bots in Medic");
            Check(ObjectiveMode.TargetFor(MatchNet.GameModeType.Medic) == 0, "no score target (tickets run down)");
            Check(ObjectiveMode.IsAvailable(MatchNet.GameModeType.Medic, out _), "available without map points");
        }

        static void CheckRules()
        {
            log.AppendLine(" Rules");
            var sim = new Sim();
            var med = Make(sim, out var go);
            var revived = new List<ulong>();
            med.testRevived = id => { revived.Add(id); sim.SetActive(id, true); };
            med.testVolunteers = new HashSet<ulong> { 2 };
            Vector3 a = new Vector3(0, 0, 0);
            sim.Set(1, 1, a); sim.Set(2, 1, a + Vector3.right * 1.5f); sim.Set(3, 1, a + Vector3.forward * 20f);
            sim.Set(11, 2, new Vector3(0, 0, -30)); sim.Set(12, 2, new Vector3(3, 0, -30));
            try
            {
                Step(med, sim, 0.1f);
                Check(med.Tickets(1) == 20 && med.Tickets(2) == 15, $"tickets by team size: RED {med.Tickets(1)} (3 players), BLUE {med.Tickets(2)} (2)");
                Check(med.medics.Count == 2 && med.medics.Contains(2), "one medic per team, the RED volunteer (2) picked");
                Check(med.medics.Contains(11) || med.medics.Contains(12), "a BLUE medic picked");
                ulong blueMedic = med.medics.Contains(11) ? 11ul : 12ul;

                // 1 is hit next to medic 2
                Hit(med, sim, 1, 11);
                Check(med.StateOf(1) == Medic.CasualtyState.Downed, "HIT player is downed");
                Check(ObjectiveMode.IsDowned(1) == false, "(detached object isn't the Active one)");
                med.ServerSetHolding(2, true);
                Step(med, sim, 2f);
                var c1 = med.casualties[1];
                Check(c1.reviver == 2 && c1.progress > 0.45f && c1.progress < 0.55f, $"medic holding E: half way after 2 s ({c1.progress:0.00})");
                med.ServerSetHolding(2, false);
                Step(med, sim, 0.2f);
                Check(c1.progress == 0f && c1.reviver == ulong.MaxValue, "letting go resets the revive");
                med.ServerSetHolding(2, true);
                Step(med, sim, Medic.REVIVE_SECONDS - 0.3f);
                Check(revived.Count == 0, "not revived before 4 s");
                Step(med, sim, 0.5f);
                Check(revived.Count == 1 && revived[0] == 1, "revived after holding 4 s");
                Check(med.StateOf(1) == null && med.PointsFor(2) == 1, "casualty cleared, medic credited 1 revive");
                Check(med.Tickets(1) == 20, "a revive costs no ticket");
                med.ServerSetHolding(2, false);

                // out of reach
                Hit(med, sim, 3, 11);
                med.ServerSetHolding(2, true);
                Step(med, sim, 1f);
                Check(med.casualties[3].progress == 0f, "medic 20 m away: no progress");
                // enemy medic next to a RED downed player can't revive him
                sim.Move(blueMedic, sim.players.Find(p => p.id == 3).position + Vector3.right);
                med.ServerSetHolding(blueMedic, true);
                Step(med, sim, 1f);
                Check(med.casualties[3].progress == 0f, "enemy medic can't revive");
                med.ServerSetHolding(blueMedic, false);
                sim.Move(blueMedic, new Vector3(0, 0, -30));
                // a HIT medic can't revive
                sim.Move(2, sim.players.Find(p => p.id == 3).position + Vector3.right);
                Hit(med, sim, 2, 12);
                med.ServerSetHolding(2, true);   // still holding E while HIT
                Step(med, sim, 1f);
                Check(med.casualties[3].progress == 0f && med.StateOf(2) == Medic.CasualtyState.Downed, "a HIT medic can't revive (and is down himself)");
                // nor himself: put a second RED medic? No: 2 is the only one; a downed medic gets no self-revive
                Check(med.casualties[2].progress == 0f, "medic can't revive himself");

                // bleed out: 3 has been down ~3 s
                Step(med, sim, Medic.BLEED_SECONDS - 2.5f);
                Check(med.StateOf(3) == Medic.CasualtyState.WalkingBack && med.Tickets(1) == 19, $"bled out after 45 s: walking back, RED -1 ticket ({med.Tickets(1)})");
                Check(med.StateOf(2) == Medic.CasualtyState.Downed, "the medic (down 1 s later) still bleeding");
                Step(med, sim, 2f);
                Check(med.StateOf(2) == Medic.CasualtyState.WalkingBack && med.Tickets(1) == 18, "the downed medic bled out too (-1)");
                sim.SetActive(3, true); sim.SetActive(2, true);   // Health: respawned in the team zone
                Step(med, sim, 0.2f);
                Check(med.StateOf(3) == null && med.StateOf(2) == null, "respawned players are back in");

                // give up
                Hit(med, sim, 1, 11);
                med.ServerGiveUp(1);
                Step(med, sim, 0.1f);
                Check(med.StateOf(1) == Medic.CasualtyState.WalkingBack && med.Tickets(1) == 17, "give up: walk back right away, -1 ticket");
                sim.SetActive(1, true);
                Step(med, sim, 0.2f);

                // medic replacement: the BLUE medic leaves
                sim.Remove(blueMedic);
                Step(med, sim, 1.2f);
                ulong otherBlue = blueMedic == 11 ? 12ul : 11ul;
                Check(med.medics.Contains(otherBlue) && !med.medics.Contains(blueMedic), "a medic who left is replaced");

                // drain RED to 0 tickets
                int guard = 0;
                while (med.Tickets(1) > 0 && guard++ < 100)
                {
                    Hit(med, sim, 1, otherBlue);
                    med.ServerGiveUp(1);
                    Step(med, sim, 0.1f);
                    sim.SetActive(1, true);
                    Step(med, sim, 0.1f);
                }
                Check(med.Tickets(1) == 0, "RED drained to 0 tickets");
                Check(!med.HasDeclared, "0 tickets but players up: match goes on");
                Hit(med, sim, 3, otherBlue);
                med.ServerGiveUp(3);
                Step(med, sim, 0.1f);
                Check(med.StateOf(3) == Medic.CasualtyState.Eliminated, "no tickets: bleeding out = out for good");
                sim.SetActive(3, false);
                Hit(med, sim, 1, otherBlue);
                Step(med, sim, 0.2f);
                Check(!med.HasDeclared, "one RED player (medic 2) still up: no result");
                // the medic can still revive at 0 tickets
                sim.Move(2, sim.players.Find(p => p.id == 1).position + Vector3.forward);
                med.ServerSetHolding(2, true);
                Step(med, sim, Medic.REVIVE_SECONDS + 0.3f);
                Check(med.StateOf(1) == null && med.PointsFor(2) == 2, "revive still works at 0 tickets");
                med.ServerSetHolding(2, false);
                Hit(med, sim, 1, otherBlue);
                Hit(med, sim, 2, otherBlue);
                Step(med, sim, 0.2f);
                Check(med.HasDeclared && med.DeclaredWinner == 2 && !med.DeclaredDraw, "RED at 0 tickets, all out: BLUE wins");
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void CheckTimeout()
        {
            log.AppendLine(" Time limit");
            var sim = new Sim();
            var med = Make(sim, out var go);
            try
            {
                sim.Set(1, 1, Vector3.zero); sim.Set(2, 1, Vector3.one); sim.Set(3, 1, Vector3.one * 2); sim.Set(11, 2, Vector3.back * 30);
                Step(med, sim, 0.1f);
                med.WinnerOnTimeout(out int w, out bool draw);
                Check(w == 1 && !draw, $"time's up: more tickets left wins (RED {med.Tickets(1)} vs BLUE {med.Tickets(2)})");
            }
            finally { Object.DestroyImmediate(go); }
            sim = new Sim();
            med = Make(sim, out go);
            try
            {
                sim.Set(1, 1, Vector3.zero); sim.Set(11, 2, Vector3.back * 30);
                Step(med, sim, 0.1f);
                med.WinnerOnTimeout(out int w, out bool draw);
                Check(draw && w == 0, "equal tickets: draw");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
#endif
