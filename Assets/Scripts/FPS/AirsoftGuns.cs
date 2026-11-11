using System.Collections.Generic;
using UnityEngine;

namespace AirsoftFPS
{
    // Loads a low-poly gun model (Resources/AirsoftGuns/<model>) and normalises it:
    //  pivot = grip, +Z = barrel direction, +Y = up, overall length = `length` metres.
    // Works for any model orientation by measuring the mesh.
    public static class AirsoftGuns
    {
        public class Gun
        {
            public GameObject root;
            public Vector3 muzzleLocal;   // muzzle position in root space
        }

        // weapon: the weapon's name (WeaponData.weaponName), for weapons that share a model but not a finish.
        public static Gun Build(string model, float length, Transform parent, string weapon = null)
        {
            if (string.IsNullOrEmpty(model)) return null;
            var prefab = Resources.Load<GameObject>("AirsoftGuns/" + model);
            if (prefab == null) return null;

            var root = new GameObject("GunModel_" + model);
            var inst = Object.Instantiate(prefab);    // keep the prefab's own (upright) rotation
            inst.transform.position = Vector3.zero;

            // Pose skinned guns at bind pose, collect world-space vertices
            var pts = new List<Vector3>();
            var tmp = new Mesh();
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var M = mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices) pts.Add(M.MultiplyPoint3x4(v));
            }
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                smr.updateWhenOffscreen = true;
                try
                {
                    smr.BakeMesh(tmp, true);
                    var M = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    foreach (var v in tmp.vertices) pts.Add(M.MultiplyPoint3x4(v));
                }
                catch { }
            }
            Object.Destroy(tmp);

            if (pts.Count < 4)
            {
                // Could not measure (mesh not readable) -> just use renderer bounds
                var b = new Bounds(inst.transform.position, Vector3.zero);
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) b.Encapsulate(r.bounds);
                pts.Add(b.min); pts.Add(b.max);
                pts.Add(new Vector3(b.min.x, b.max.y, b.min.z)); pts.Add(new Vector3(b.max.x, b.min.y, b.max.z));
            }

            Vector3 mn = pts[0], mx = pts[0];
            foreach (var p in pts) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            Vector3 size = mx - mn;

            // Barrel axis = the longer horizontal axis
            bool alongX = size.x > size.z;
            float lo = alongX ? mn.x : mn.z, hi = alongX ? mx.x : mx.z;
            float len = Mathf.Max(1e-4f, hi - lo);
            float slice = len * 0.15f;

            // The thinner end (barrel) is the muzzle; the thick end has grip/stock.
            float loMinY = float.MaxValue, loMaxY = float.MinValue, hiMinY = float.MaxValue, hiMaxY = float.MinValue;
            float loSumY = 0, hiSumY = 0; int loN = 0, hiN = 0;
            float lowestY = float.MaxValue, lowestA = 0;
            foreach (var p in pts)
            {
                float a = alongX ? p.x : p.z;
                if (a < lo + slice) { loMinY = Mathf.Min(loMinY, p.y); loMaxY = Mathf.Max(loMaxY, p.y); loSumY += p.y; loN++; }
                if (a > hi - slice) { hiMinY = Mathf.Min(hiMinY, p.y); hiMaxY = Mathf.Max(hiMaxY, p.y); hiSumY += p.y; hiN++; }
                if (p.y < lowestY) { lowestY = p.y; lowestA = a; }
            }
            bool muzzleAtHi = (hiMaxY - hiMinY) < (loMaxY - loMinY);
            float muzzleA = muzzleAtHi ? hi : lo;
            float rearA = muzzleAtHi ? lo : hi;
            float muzzleY = muzzleAtHi ? (hiN > 0 ? hiSumY / hiN : mx.y) : (loN > 0 ? loSumY / loN : mx.y);

            // Grip point: between the rear and the lowest point (pistol grip / magazine), upper part of the body
            float gripA = Mathf.Lerp(rearA, lowestA, 0.75f);
            float gripY = Mathf.Lerp(mn.y, mx.y, 0.6f);
            float midC = alongX ? (mn.z + mx.z) * 0.5f : (mn.x + mx.x) * 0.5f;
            Vector3 grip = alongX ? new Vector3(gripA, gripY, midC) : new Vector3(midC, gripY, gripA);
            Vector3 muzzle = alongX ? new Vector3(muzzleA, muzzleY, midC) : new Vector3(midC, muzzleY, muzzleA);

            Vector3 dir = alongX ? new Vector3(Mathf.Sign(muzzleA - rearA), 0, 0) : new Vector3(0, 0, Mathf.Sign(muzzleA - rearA));
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            Quaternion toFwd = Quaternion.Euler(0, -yaw, 0);
            float k = length / len;

            // Re-parent: root at grip, model rotated so barrel = +Z, scaled to length
            inst.transform.SetParent(root.transform, true);
            root.transform.position = Vector3.zero;
            inst.transform.position = -grip;                 // grip at origin (pre-rotation)
            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(root.transform, false);
            inst.transform.SetParent(pivot, true);
            pivot.localRotation = toFwd;
            pivot.localScale = Vector3.one * k;

            var g = new Gun { root = root, muzzleLocal = toFwd * ((muzzle - grip) * k) + Vector3.forward * 0.02f };

            AirsoftLook.FixMaterials(inst);
            Paint(inst, model, weapon);
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            foreach (var a in inst.GetComponentsInChildren<Animation>(true)) a.enabled = false;
            foreach (var a in inst.GetComponentsInChildren<Animator>(true)) a.enabled = false;

            if (parent)
            {
                root.transform.SetParent(parent, false);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
            }
            return g;
        }

        // ───────────── finishes ─────────────
        // Most models come with their parts as named materials (Metal, Wood, Magazine, Barrel…) but all of
        // them the same flat grey, so the guns looked like one white piece. Each part gets its own colour
        // and surface here: gunmetal, blued steel, black polymer, wood, brass rounds. Names without an entry
        // keep the model's own colour (AK47, BoltRifle and Uzi bring real colours in their material names).
        struct Finish { public Color c; public float smooth, metal; public Finish(string hex, float s, float m) { c = UIKit.Hex(hex); smooth = s; metal = m; } }

        static Finish Steel(string hex) => new Finish(hex, 0.55f, 0.6f);
        static Finish Polymer(string hex) => new Finish(hex, 0.25f, 0f);
        static Finish WoodF(string hex) => new Finish(hex, 0.3f, 0f);
        static Finish Brass(string hex) => new Finish(hex, 0.7f, 0.8f);

        static readonly Dictionary<string, Finish> ByPart = new Dictionary<string, Finish>
        {
            { "Metal", Steel("#6B7075") }, { "DarkerMetal", Steel("#33363A") }, { "DarkMetal", Steel("#3A3D41") },
            { "Barrel", Steel("#2A2C2F") }, { "Barrels", Steel("#2A2C2F") }, { "Muzzle", Steel("#25272A") },
            { "Black", Polymer("#1D1E20") }, { "Magazine", Polymer("#2B2D30") }, { "Trigger", Steel("#121314") },
            { "Wood", WoodF("#8E5428") }, { "LightWood", WoodF("#B0713A") }, { "DarkWood", WoodF("#5C3219") },
            { "Green", Polymer("#55613F") },
            { "BulletYellow", Brass("#D4A23C") }, { "BulletOrange", Brass("#B8692C") }, { "BulletTip", Brass("#B87333") },
            { "BulletRed", Polymer("#A82D26") },
        };

        // Parts named per model (generic material names, or one material over several pieces).
        static bool ByModel(string model, string renderer, string mat, string weapon, out Finish f)
        {
            f = default;
            switch (model)
            {
                case "Pistol":   // 1911: steel slide and frame, wooden grips
                    if (mat == "Material.003") { f = Steel("#A3A8AD"); return true; }   // hammer / small parts
                    break;
                case "P90":      // FDE (tan) body, black top rail and magazine parts
                    if (mat == "Material.001") { f = Polymer("#B29C74"); return true; }
                    if (mat == "Material.004") { f = Polymer("#1F2022"); return true; }
                    if (mat == "Material.003") { f = Steel("#3A3D40"); return true; }
                    break;
                case "Uzi":      // black steel body, darker grip panel, grey stock
                    if (renderer == "Cube.001") { f = Steel("#2E3134"); return true; }
                    if (renderer == "Cube") { f = Polymer("#1A1B1C"); return true; }
                    if (renderer == "Cube.002") { f = Steel("#4C5054"); return true; }
                    break;
                case "AK47":     // AK: orange-brown furniture; RPK: darker walnut and a lighter receiver
                    bool rpk = weapon != null && weapon.Contains("RPK");
                    if (mat.EndsWith("C56100")) { f = WoodF(rpk ? "#6E3A17" : "#A4531C"); return true; }
                    if (mat.EndsWith("2B2B2B")) { f = Steel(rpk ? "#3E4246" : "#2A2C2F"); return true; }
                    break;
                case "BoltRifle":   // dark wood stock, blued action, bright bolt
                    if (mat.EndsWith("911F00")) { f = WoodF("#6A2E14"); return true; }
                    if (mat.EndsWith("3B3B3B")) { f = Steel("#2F3236"); return true; }
                    if (mat.EndsWith("C2C2C2")) { f = Steel("#B9BEC3"); return true; }
                    break;
                case "Shotgun":   // M870: black polymer furniture, lighter steel receiver and barrel
                    if (mat == "DarkMetal") { f = Steel("#5A5F64"); return true; }
                    if (mat == "Barrels") { f = Steel("#3F4347"); return true; }
                    break;
                case "SniperRifle":   // L96: olive stock
                    if (mat == "Green") { f = Polymer("#56633D"); return true; }
                    break;
            }
            return false;
        }

        static void Paint(GameObject inst, string model, string weapon)
        {
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    string n = m.name;
                    if (!ByModel(model, r.name, n, weapon, out var f) && !ByPart.TryGetValue(n, out f)) continue;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", f.c);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", f.c);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", f.smooth);
                    if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", f.smooth);
                    if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", f.metal);
                }
            }
        }
    }
}
