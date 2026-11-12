using System;
using System.Collections.Generic;

namespace AirsoftFPS
{
    // What the player can buy with Credits. Weapon ids are the WeaponData.weaponName strings, so the
    // shop, ownership (PlayerProfile.owned) and the loadout all speak the same ids.
    public static class ShopCatalog
    {
        public struct Item
        {
            public string id;        // weaponName / part id
            public string label;     // short display name
            public int cost;         // credits
            public int rank;         // level required to buy
            public string note;      // one-line blurb
        }

        // Owned from the start (free): a sidearm, a revolver, an SMG and the shotgun.
        public static readonly string[] StarterWeapons =
        {
            "1911 GBB Pistol", "CO2 Revolver", "Uzi GBB SMG", "M870 Spring Shotgun"
        };

        // Buyable weapons (the rest of the ten). Costs/ranks are the first-pass economy; tune freely.
        static readonly Item[] weapons =
        {
            new Item { id = "P90 AEG",          label = "P90 AEG",   cost = 1200, rank = 3,  note = "High-capacity CQB AEG" },
            new Item { id = "AK AEG",           label = "AK AEG",    cost = 1600, rank = 5,  note = "All-round assault rifle" },
            new Item { id = "RPK Support AEG",  label = "RPK AEG",   cost = 2200, rank = 9,  note = "300-round support gun" },
            new Item { id = "M14 DMR",          label = "M14 DMR",   cost = 2400, rank = 12, note = "Semi-auto marksman rifle" },
            new Item { id = "Spring Bolt Rifle",label = "Bolt rifle",cost = 2800, rank = 16, note = "Quiet bolt-action" },
            new Item { id = "L96 Spring Sniper",label = "L96 sniper",cost = 3400, rank = 22, note = "One-shot sniper rifle" },
        };

        public static IReadOnlyList<Item> Weapons => weapons;

        // BB weights (index = BBBallistics.Weights). 0.20 g and 0.25 g are free; heavier BBs are bought once.
        public const int DEFAULT_BB = 1;
        static readonly Item[] bbs =
        {
            new Item { id = "",           label = "0.20 g", cost = 0,   rank = 1, note = "Fastest, drifts most in the wind" },
            new Item { id = "",           label = "0.25 g", cost = 0,   rank = 1, note = "All-round" },
            new Item { id = "BB 0.28 g",  label = "0.28 g", cost = 400, rank = 2, note = "Steadier in the wind" },
            new Item { id = "BB 0.32 g",  label = "0.32 g", cost = 900, rank = 6, note = "Slow but barely drifts" },
        };

        public static IReadOnlyList<Item> Bbs => bbs;

        // Free weights have no id (PlayerProfile.Owns treats an empty id as owned).
        public static bool OwnedBb(int index) =>
            index >= 0 && index < bbs.Length && PlayerProfile.Owns(bbs[index].id);

        public static bool IsStarter(string id) => Array.IndexOf(StarterWeapons, id) >= 0;

        // Can this weapon be put in a loadout (free starter or already bought)?
        public static bool OwnedWeapon(string id) => IsStarter(id) || PlayerProfile.Owns(id);

        // What the replica is, for the Armory card under the spinning model (two short lines).
        static readonly Dictionary<string, string> descriptions = new Dictionary<string, string>
        {
            { "1911 GBB Pistol",    "Gas blowback sidearm: snappy, accurate, 15 rounds. Always with you." },
            { "CO2 Revolver",       "Six CO2 shells that hit twice as hard as the 1911. Slow to reload." },
            { "Uzi GBB SMG",        "Full-auto gas SMG, 15 shots a second. Sprays at range." },
            { "M870 Spring Shotgun","Pump spring shotgun, 3 BBs per shot. Brutal up close, useless far." },
            { "P90 AEG",            "Electric PDW with a 68-round mag and almost no kick. Made for CQB." },
            { "AK AEG",             "All-round electric rifle: 120-round mid-cap, iron-sight zoom." },
            { "RPK Support AEG",    "300-round box mag to keep heads down. Heavy and slow to reload." },
            { "M14 DMR",            "Semi-auto marksman AEG with a tight barrel. Hits hard far away." },
            { "Spring Bolt Rifle",  "Quiet spring bolt-action on iron sights. Two hits at most." },
            { "L96 Spring Sniper",  "Scoped bolt-action sniper: one hit and they are out. Slow bolt." },
        };

        public static string Describe(string id) => descriptions.TryGetValue(id, out var d) ? d : "";

        public static bool TryGet(string id, out Item item)
        {
            foreach (var i in weapons) if (i.id == id) { item = i; return true; }
            item = default;
            return false;
        }
    }
}
