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

        public static bool IsStarter(string id) => Array.IndexOf(StarterWeapons, id) >= 0;

        // Can this weapon be put in a loadout (free starter or already bought)?
        public static bool OwnedWeapon(string id) => IsStarter(id) || PlayerProfile.Owns(id);

        public static bool TryGet(string id, out Item item)
        {
            foreach (var i in weapons) if (i.id == id) { item = i; return true; }
            item = default;
            return false;
        }
    }
}
