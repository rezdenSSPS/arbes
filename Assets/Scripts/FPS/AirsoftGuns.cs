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

        public static Gun Build(string model, float length, Transform parent)
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
    }
}
