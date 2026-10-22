using System.Collections.Generic;
using UnityEngine;

namespace AirsoftFPS
{
    // What a collider is made of, for BB impact and footstep sounds. The maps are built from named
    // materials (AirsoftMapBuilder / AirsoftWarehouseBuilder: "Asphalt", "Container_...", "PalletWood",
    // "Grass", ...) and named objects, so the material / object name says enough. Cached per collider.
    public static class Surfaces
    {
        public enum Kind { Concrete, Metal, Wood, Glass, Soft, Body }

        static readonly Dictionary<Collider, Kind> cache = new Dictionary<Collider, Kind>();

        static readonly string[] Glass = { "glass", "screen", "window", "whiteboard" };
        static readonly string[] Metal =
        {
            "metal", "steel", "container", "car_", "truck", "trailer", "rack", "dumpster", "gate", "cage", "forklift",
            "vending", "server", "cabinet", "rollerdoor", "roller", "shedroof", "shedwall", "drum", "iron", "pipe",
            "gun", "lamp", "sign", "fence", "bin", "streetlight", "trafficlight", "pole"
        };
        static readonly string[] Wood = { "wood", "crate", "pallet", "desk", "plank", "tree", "trunk", "log", "door", "table", "shelf", "yellowbox" };
        static readonly string[] Soft =
        {
            "grass", "verge", "sand", "bag", "dirt", "mud", "ground", "shrink", "cloth", "tyre", "tire", "cardboard",
            "bush", "leaf", "leaves", "foliage", "hay", "mattress", "cushion"
        };

        public static void ResetStatics() => cache.Clear();

        public static Kind Of(Collider c)
        {
            if (c == null) return Kind.Concrete;
            if (cache.TryGetValue(c, out var k)) return k;
            if (cache.Count > 4096) cache.Clear();   // colliders of unloaded scenes
            k = Classify(c);
            cache[c] = k;
            return k;
        }

        static Kind Classify(Collider c)
        {
            if (c.GetComponentInParent<Health>() != null) return Kind.Body;

            // Material of the renderer the collider belongs to (same object, else its children, else
            // the parent), then the physics material, then the object / parent names.
            var r = c.GetComponent<Renderer>();
            if (r == null) r = c.GetComponentInChildren<Renderer>();
            if (r == null && c.transform.parent != null) r = c.transform.parent.GetComponent<Renderer>();
            string mat = r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "";
            string text = (mat + " " + (c.sharedMaterial != null ? c.sharedMaterial.name : "") + " " + c.name + " " +
                           (c.transform.parent != null ? c.transform.parent.name : "")).ToLowerInvariant();

            // the material decides first (a "Wall" object made of Steel is metal)
            if (mat.Length > 0)
            {
                var byMat = Match(mat.ToLowerInvariant());
                if (byMat.HasValue) return byMat.Value;
            }
            return Match(text) ?? Kind.Concrete;
        }

        static Kind? Match(string s)
        {
            if (Any(s, Glass)) return Kind.Glass;
            if (Any(s, Metal)) return Kind.Metal;
            if (Any(s, Wood)) return Kind.Wood;
            if (Any(s, Soft)) return Kind.Soft;
            return null;
        }

        static bool Any(string s, string[] words)
        {
            foreach (var w in words) if (s.Contains(w)) return true;
            return false;
        }
    }
}
