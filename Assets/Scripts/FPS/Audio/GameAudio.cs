using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AirsoftFPS
{
    // Match and objective sounds, derived every frame on every peer from state that is already synced
    // (MatchNet phase / roster / clock, the active ObjectiveMode's state, the local player's Health).
    // Nothing extra goes over the network: a transition seen locally plays its sound locally.
    public static class GameAudio
    {
        const ulong NONE = ulong.MaxValue;

        // match
        static MatchNet lastMn;
        static MatchNet.Phase lastPhase;
        static bool matchStartPlayed, readyKnown, lastReady;
        static int lastClockSec = -1, lastKills = -1, lastBackIn = -1;

        // objectives (snapshot of the last frame; reset when a new ObjectiveMode appears)
        static ObjectiveMode lastObj;
        static int lastRound = -1, lastPrepBeep = -1;
        static RoundMode.Stage lastStage;
        static float lastExtract;
        static BombMode.BombState lastBomb;
        static float nextBeep, nextTick;
        static readonly CaptureTheFlag.FlagState[] lastFlag = new CaptureTheFlag.FlagState[3];
        static readonly int[] lastScore = new int[3];
        static readonly List<int> lastOwner = new List<int>();
        static readonly List<float> lastProgress = new List<float>();
        static readonly Dictionary<ulong, (Medic.CasualtyState state, bool reviving, Vector3 pos)> lastCas =
            new Dictionary<ulong, (Medic.CasualtyState, bool, Vector3)>();

        // stingers a little after the event that triggers them
        static readonly List<(float at, string cue, float vol)> pending = new List<(float, string, float)>();

        public static void ResetStatics()
        {
            lastMn = null; lastObj = null;
            matchStartPlayed = readyKnown = lastReady = false;
            lastClockSec = lastKills = lastBackIn = -1;
            lastPhase = MatchNet.Phase.Lobby;
            pending.Clear(); lastCas.Clear(); lastOwner.Clear(); lastProgress.Clear();
        }

        static float Now => Time.unscaledTime;

        static void Later(float delay, string cue, float vol) => pending.Add((Now + delay, cue, vol));

        public static void Tick()
        {
            for (int i = pending.Count - 1; i >= 0; i--)
                if (Now >= pending[i].at) { AirsoftAudio.Play2D(pending[i].cue, pending[i].vol); pending.RemoveAt(i); }

            ChooseMusic();

            var mn = MatchNet.Instance;
            var nm = NetworkManager.Singleton;
            if (mn == null || nm == null || !nm.IsListening)
            {
                lastMn = null; lastObj = null; readyKnown = false; matchStartPlayed = false;
                lastKills = lastClockSec = -1;
                return;
            }
            if (mn != lastMn) { lastMn = mn; lastPhase = mn.CurrentPhase; readyKnown = false; }

            ulong me = nm.LocalClientId;
            var phase = mn.CurrentPhase;
            bool inMap = MapCatalog.IsMatchScene(SceneManager.GetActiveScene().name);

            // lobby: your ready toggle
            if (phase == MatchNet.Phase.Lobby)
            {
                bool r = mn.LocalReady;
                if (readyKnown && r != lastReady) AirsoftAudio.Play2D(r ? "ui_ready" : "ui_unready", 0.7f);
                lastReady = r;
                readyKnown = true;
                matchStartPlayed = false;
            }
            else readyKnown = false;

            // match start: the first frame we're in the map, in the match, with our player spawned
            var local = LocalHealth(nm);
            if (phase == MatchNet.Phase.InMatch && inMap && !matchStartPlayed && local != null)
            {
                matchStartPlayed = true;
                AirsoftAudio.Play2D("match_start", 0.6f);
                AirsoftAudio.Play2D("whistle", 0.5f);
            }

            // match end: end whistle, then win / lose / draw
            if (phase == MatchNet.Phase.Results && lastPhase == MatchNet.Phase.InMatch)
            {
                AirsoftAudio.Play2D("whistle_end", 0.45f);
                Later(0.9f, Outcome(mn, me), 0.8f);
            }
            lastPhase = phase;

            if (phase != MatchNet.Phase.InMatch) { lastKills = lastClockSec = lastBackIn = -1; lastObj = null; return; }

            // your kills (Milsim: hits) -> confirm tick
            int kills = KillsOf(mn, me);
            if (lastKills >= 0 && kills > lastKills) AirsoftAudio.Play2D("killconfirm", 0.6f);
            lastKills = kills;

            // last 10 s of the match clock
            float left = mn.TimeLeft;
            if (left > 0f && left <= 10f)
            {
                int sec = Mathf.CeilToInt(left);
                if (sec != lastClockSec)
                {
                    lastClockSec = sec;
                    if (sec <= 5) AirsoftAudio.Play2D("countdown", 0.4f);
                    else AirsoftAudio.Play2D("clock_tick", 0.55f);
                }
            }
            else lastClockSec = -1;

            // Milsim: "BACK IN 3" in your team zone
            if (local != null && local.IsOut && local.hitRespawnAt.Value > 0)
            {
                int sec = Mathf.CeilToInt((float)(local.hitRespawnAt.Value - nm.ServerTime.Time));
                if (sec >= 1 && sec <= 3 && sec != lastBackIn) AirsoftAudio.Play2D("countdown", 0.4f);
                lastBackIn = sec;
            }
            else lastBackIn = -1;

            TickObjective(mn, me, local);
        }

        static Health LocalHealth(NetworkManager nm)
        {
            var po = nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            return po != null ? po.GetComponent<Health>() : null;
        }

        static int KillsOf(MatchNet mn, ulong id)
        {
            foreach (var e in mn.Roster) if (e.clientId == id) return e.kills;
            return 0;
        }

        static string Outcome(MatchNet mn, ulong me)
        {
            if (mn.IsDraw) return "draw";
            if (mn.TeamMode)
            {
                if (mn.WinnerTeam == 0) return "draw";
                return mn.TeamOf(me) == mn.WinnerTeam ? "victory" : "defeat";
            }
            if (mn.Mode == MatchNet.GameModeType.Coop) return mn.WinnerClientId == NONE ? "defeat" : "victory";
            return mn.WinnerClientId == me ? "victory" : "defeat";
        }

        // ───────────────────────── music / ambience ─────────────────────────
        static void ChooseMusic()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (!MapCatalog.IsMatchScene(scene))
                AirsoftAudio.SetMusic("music_menu", 0.5f, AirsoftAudio.Bus.Music);
            else if (GameSettings.Ambience)
                AirsoftAudio.SetMusic(scene.Contains("Warehouse") ? "amb_indoor" : "amb_outdoor", 0.5f, AirsoftAudio.Bus.Sfx);
            else
                AirsoftAudio.SetMusic(null, 0f, AirsoftAudio.Bus.Sfx);
        }

        // ───────────────────────── objectives ─────────────────────────
        static void TickObjective(MatchNet mn, ulong me, Health local)
        {
            var obj = ObjectiveMode.Active;
            if (obj != lastObj)
            {
                lastObj = obj;
                Snapshot(obj);
                return;
            }
            if (obj == null) return;
            int team = mn.TeamOf(me);

            if (obj is RoundMode rm) TickRound(rm, team);
            switch (obj)
            {
                case BombMode b: TickBomb(b, team); break;
                case CaptureTheFlag c: TickFlags(c, team); break;
                case Domination d: TickPoints(d, team, local); break;
                case Medic m: TickMedic(m, me); break;
            }
        }

        // Remember the current state without playing anything (joining mid-match, a new objective).
        static void Snapshot(ObjectiveMode obj)
        {
            lastRound = -1; lastPrepBeep = -1; lastExtract = 0f;
            lastCas.Clear(); lastOwner.Clear(); lastProgress.Clear();
            if (obj is RoundMode rm) { lastRound = rm.Round; lastStage = rm.RoundStage; }
            if (obj is BombMode b) lastBomb = b.State;
            if (obj is VipEscort v) lastExtract = v.ExtractProgress;
            if (obj is CaptureTheFlag c)
                for (int t = 1; t <= 2; t++) { lastScore[t] = c.TeamScore(t); lastFlag[t] = c.FlagOf(t)?.state ?? CaptureTheFlag.FlagState.Home; }
            if (obj is Domination d)
                foreach (var p in d.points) { lastOwner.Add(p.owner); lastProgress.Add(p.progress); }
            if (obj is Medic m)
                foreach (var c2 in m.casualties.Values) lastCas[c2.id] = (c2.state, c2.reviver != NONE, c2.pos);
        }

        // Bomb / VIP: 3-2-1 before a round, whistle when it goes live, win / lose sting when it's over.
        static void TickRound(RoundMode rm, int team)
        {
            var vip = rm as VipEscort;
            if (rm.Round != lastRound) { lastRound = rm.Round; lastPrepBeep = -1; }
            if (rm.RoundStage == RoundMode.Stage.Prep)
            {
                int sec = Mathf.CeilToInt(rm.StageLeft);
                if (sec >= 1 && sec <= 3 && sec != lastPrepBeep) { lastPrepBeep = sec; AirsoftAudio.Play2D("countdown", 0.45f); }
            }
            if (rm.RoundStage != lastStage)
            {
                if (rm.RoundStage == RoundMode.Stage.Live)
                {
                    AirsoftAudio.Play2D("whistle", 0.45f);
                    AirsoftAudio.Play2D("countdown_go", 0.35f);
                }
                else if (rm.RoundStage == RoundMode.Stage.Over)
                {
                    float delay = 0.35f;
                    if (vip != null && Mathf.Max(lastExtract, vip.ExtractProgress) >= 0.95f)
                    {
                        AirsoftAudio.Play2D("vip_extract", 0.6f);
                        delay = 1.4f;
                    }
                    string cue = team == 0 || rm.RoundWinner == 0 ? "obj_neutral" : rm.RoundWinner == team ? "round_win" : "round_lose";
                    Later(delay, cue, 0.7f);
                }
                lastStage = rm.RoundStage;
            }
            if (vip != null)
            {
                if (vip.ExtractProgress > 0f && vip.RoundStage == RoundMode.Stage.Live && Now >= nextTick)
                {
                    nextTick = Now + 0.5f;
                    AirsoftAudio.PlayAt("progress_tick", vip.ExtractPoint + Vector3.up, 0.6f, 4f, 40f, 0f);
                }
                lastExtract = vip.ExtractProgress;
            }
        }

        static void TickBomb(BombMode b, int team)
        {
            var s = b.State;
            bool attacker = team == b.AttackTeam;
            if (s != lastBomb)
            {
                switch (s)
                {
                    case BombMode.BombState.Carried:
                        if (lastBomb == BombMode.BombState.Dropped && attacker) AirsoftAudio.Play2D("obj_good", 0.5f);   // picked up again
                        break;
                    case BombMode.BombState.Dropped:
                        if (lastBomb == BombMode.BombState.Carried && attacker) AirsoftAudio.Play2D("obj_bad", 0.5f);
                        break;
                    case BombMode.BombState.Planted:
                        AirsoftAudio.PlayAt("bomb_plant", b.BombPos + Vector3.up * 0.3f, 1f, 3f, 45f, 0f);
                        AirsoftAudio.Play2D(attacker ? "obj_good" : "obj_bad", 0.6f);
                        nextBeep = Now + 1.3f;
                        break;
                    case BombMode.BombState.Defused:
                        if (lastBomb == BombMode.BombState.Planted)
                            AirsoftAudio.PlayAt("bomb_defuse", b.BombPos + Vector3.up * 0.3f, 1f, 3f, 45f, 0f);
                        break;
                    case BombMode.BombState.Exploded:
                        if (lastBomb == BombMode.BombState.Planted)
                            AirsoftAudio.PlayAt("bomb_explode", b.BombPos + Vector3.up, 1f, 12f, 220f, 0.02f);
                        break;
                }
                lastBomb = s;
            }
            // the planted bomb beeps faster and faster as the fuse runs down
            if (s == BombMode.BombState.Planted && Now >= nextBeep)
            {
                float k = Mathf.Clamp01(b.FuseLeft / BombMode.FUSE_SECONDS);
                AirsoftAudio.PlayAt("bomb_beep", b.BombPos + Vector3.up * 0.3f, 0.5f, 2.5f, 45f, 0f);
                nextBeep = Now + Mathf.Lerp(0.13f, 1.1f, k * k);
            }
            // planting / defusing: keypad presses
            if (b.Actor != NONE && b.ActionProgress > 0f && Now >= nextTick)
            {
                nextTick = Now + 0.42f;
                AirsoftAudio.PlayAt("progress_tick", b.BombPos + Vector3.up * 0.5f, 0.55f, 2f, 20f, 0.06f);
            }
        }

        static void TickFlags(CaptureTheFlag c, int team)
        {
            for (int t = 1; t <= 2; t++)
            {
                int sc = c.TeamScore(t);
                if (sc > lastScore[t])
                    AirsoftAudio.Play2D(team == 0 ? "obj_neutral" : t == team ? "flag_capture_good" : "flag_capture_bad", 0.75f);
                lastScore[t] = sc;
            }
            for (int t = 1; t <= 2; t++)
            {
                var f = c.FlagOf(t);
                if (f == null || f.state == lastFlag[t]) continue;
                var prev = lastFlag[t];
                lastFlag[t] = f.state;
                switch (f.state)
                {
                    case CaptureTheFlag.FlagState.Carried:   // our flag was taken / we took theirs
                        AirsoftAudio.PlayAt("flag_pickup", f.pos + Vector3.up * 1.5f, 0.8f, 2f, 30f);
                        AirsoftAudio.Play2D(f.team == team ? "obj_bad" : "obj_good", 0.55f);
                        break;
                    case CaptureTheFlag.FlagState.Dropped:
                        AirsoftAudio.Play2D("obj_neutral", 0.45f);
                        break;
                    case CaptureTheFlag.FlagState.Home:
                        if (prev == CaptureTheFlag.FlagState.Dropped)   // returned (a capture plays its own sting)
                            AirsoftAudio.Play2D(f.team == team ? "obj_good" : "obj_neutral", 0.45f);
                        break;
                }
            }
        }

        static void TickPoints(Domination d, int team, Health local)
        {
            while (lastOwner.Count < d.points.Count) { lastOwner.Add(d.points[lastOwner.Count].owner); lastProgress.Add(0f); }
            bool ticked = false;
            for (int i = 0; i < d.points.Count; i++)
            {
                var p = d.points[i];
                if (p.owner != lastOwner[i])
                {
                    string cue = p.owner == 0 ? (lastOwner[i] == team ? "obj_bad" : "obj_neutral")
                               : p.owner == team ? "obj_good" : "obj_bad";
                    AirsoftAudio.Play2D(cue, 0.55f);
                    lastOwner[i] = p.owner;
                }
                // you're standing on a point that is being captured: progress ticks
                if (!ticked && local != null && !local.isDead.Value && Now >= nextTick
                    && Mathf.Abs(p.progress - lastProgress[i]) > 0.0005f && p.progress > 0f && p.progress < 1f)
                {
                    Vector3 dp = local.transform.position - p.pos; dp.y = 0f;
                    if (dp.sqrMagnitude <= Domination.RADIUS * Domination.RADIUS)
                    {
                        AirsoftAudio.Play2D("progress_tick", 0.35f);
                        nextTick = Now + 0.4f;
                        ticked = true;
                    }
                }
                lastProgress[i] = p.progress;
            }
        }

        static void TickMedic(Medic m, ulong me)
        {
            // a revive: a downed player with a medic on them is no longer a casualty
            foreach (var kv in lastCas)
            {
                if (m.casualties.ContainsKey(kv.Key)) continue;
                if (kv.Value.state == Medic.CasualtyState.Downed && kv.Value.reviving)
                {
                    AirsoftAudio.PlayAt("revive", kv.Value.pos + Vector3.up, 0.9f, 2f, 25f, 0.03f);
                    if (kv.Key == me) AirsoftAudio.Play2D("obj_good", 0.5f);
                }
            }
            lastCas.Clear();
            bool tick = false;
            foreach (var c in m.casualties.Values)
            {
                bool reviving = c.reviver != NONE && c.progress > 0f;
                lastCas[c.id] = (c.state, reviving, c.pos);
                if (reviving && (c.reviver == me || c.id == me)) tick = true;
            }
            if (tick && Now >= nextTick)
            {
                nextTick = Now + 0.45f;
                AirsoftAudio.Play2D("progress_tick", 0.35f);
            }
        }
    }
}
