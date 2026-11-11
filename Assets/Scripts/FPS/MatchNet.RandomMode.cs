using System.Collections.Generic;

namespace AirsoftFPS
{
    // RANDOM MODE (part of MatchNet). The host may pick "Random mode" in the lobby instead of a mode: the
    // server then draws the real mode at START among the modes the chosen map can play (ObjectiveMode.IsAvailableOn),
    // not the mode played last when there is another. With RANDOM MAP as well, the mode is drawn among the
    // modes any map can play, and ChooseMatchMap then draws a map that can play it, so the pair is always valid.
    // The drawn mode becomes Mode for the match (HUD, intro, results show the real mode); RandomMode stays set,
    // so back in the lobby the selection is Random again until the host picks a mode.
    // There is no GameModeType value for it on purpose: everything that switches on Mode only ever sees real modes.
    public partial class MatchNet
    {
        // Host flag, synced in the lobby state (BroadcastState): the host picked RANDOM MODE.
        public bool RandomMode { get; private set; }

        // Lobby with RANDOM MODE picked: the mode isn't known yet, so mode-dependent lobby settings
        // (time limit, bots, medic volunteers) stay visible and usable: the drawn mode may use them.
        public bool RandomModePending => RandomMode && CurrentPhase == Phase.Lobby;

        // Server: the mode of the last started match (null = none yet this session); the draw avoids it.
        GameModeType? lastPlayedMode;

        static readonly System.Random drawRng = new System.Random();

        // Host picks RANDOM MODE (lobby card / M).
        public void HostSetRandomMode()
        {
            if (!IsHost || CurrentPhase != Phase.Lobby || RandomMode) return;
            if (!RandomModeAvailable(out _)) return;
            RandomMode = true;
            BroadcastState();
        }

        // Map scenes the draw may use right now: the voted / locked map, or every map with RANDOM MAP winning.
        List<string> RandomModeScenes()
        {
            var scenes = new List<string>();
            if (RandomMapPending) for (int i = 0; i < MapCatalog.Count; i++) scenes.Add(MapCatalog.SceneOf(i));
            else scenes.Add(CurrentMapScene);
            return scenes;
        }

        // Lobby: can RANDOM MODE start? (At least one mode is available on the map; reason for the lobby otherwise.)
        public bool RandomModeAvailable(out string reason)
        {
            reason = null;
            if (RandomModePool(RandomModeScenes(), null, roster.Count).Count > 0) return true;
            reason = RandomMapPending ? "No mode is available on any map" : "No mode is available on this map";
            return false;
        }

        // Server, at START with RANDOM MODE: draw the match mode (false = nothing can be played).
        bool DrawMatchMode()
        {
            var drawn = DrawRandomMode(RandomModeScenes(), lastPlayedMode, roster.Count, drawRng);
            if (drawn == null) return false;
            Mode = drawn.Value;
            return true;
        }

        // ───────── the draw (pure: no networking, used by ObjectiveLogicCheck) ─────────

        // A mode is worth drawing for this many players (bots included): alone, only Co-op makes sense.
        public static bool RandomFitsPlayers(GameModeType m, int players) => m == GameModeType.Coop || players >= 2;

        // Modes RANDOM MODE may draw: available on at least one of the map scenes; of those, the ones that fit
        // the player count (when any does); then without lastMode when another mode is left.
        public static List<GameModeType> RandomModePool(IReadOnlyList<string> mapScenes, GameModeType? lastMode, int players)
        {
            var pool = new List<GameModeType>();
            if (mapScenes == null) return pool;
            for (int i = 0; i < MODE_COUNT; i++)
            {
                var m = (GameModeType)i;
                foreach (var scene in mapScenes)
                    if (ObjectiveMode.IsAvailableOn(m, scene, out _)) { pool.Add(m); break; }
            }
            var fits = pool.FindAll(m => RandomFitsPlayers(m, players));
            if (fits.Count > 0) pool = fits;
            if (lastMode.HasValue && pool.Count > 1) pool.Remove(lastMode.Value);
            return pool;
        }

        // Draws the mode for a match on these map scenes (several = RANDOM MAP); null when no mode is available.
        public static GameModeType? DrawRandomMode(IReadOnlyList<string> mapScenes, GameModeType? lastMode, int players, System.Random rng)
        {
            var pool = RandomModePool(mapScenes, lastMode, players);
            if (pool.Count == 0) return null;
            return pool[(rng ?? drawRng).Next(pool.Count)];
        }

        public static GameModeType? DrawRandomMode(string mapScene, GameModeType? lastMode, int players, System.Random rng)
            => DrawRandomMode(new[] { mapScene }, lastMode, players, rng);
    }
}
