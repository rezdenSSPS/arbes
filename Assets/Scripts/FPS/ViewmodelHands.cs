using System.Collections.Generic;
using UnityEngine;

namespace AirsoftFPS
{
    // First-person support hand (owner only, purely visual): a low-poly gloved left hand that reloads the
    // replica and steadies it while you inspect it (T). Driven by WeaponController.LateUpdate, which asks
    // Animate() for an extra pose of the weapon holder each frame.
    //
    // Everything is a pure function of the reload progress (0..1 of WeaponData.reloadTime) or the inspect
    // time, so a cancelled reload (switch, death) or inspect just stops drawing: the gun gets its full mesh
    // back and the hand and the loose parts are hidden.
    //
    // The hand and the loose parts live in the current weapon slot's space (child of the slot, so they follow
    // recoil, bob and the reload tilt). Magazines are cut out of the gun mesh by material / object name
    // (Pistol, Uzi, P90, AK, L96); a gun without a separate magazine gets a small proxy part instead
    // (a stripper clip for the M14 / bolt rifle, shells for the shotgun, a speedloader for the revolver).
    [DisallowMultipleComponent]
    public class ViewmodelHands : MonoBehaviour
    {
        public const float INSPECT_SECONDS = 2.6f;

        enum Kind { Mag, TopClip, Shells, Revolver }

        // Per gun model, in slot space at the model's reference length (scaled for other lengths, e.g. RPK).
        class Spec
        {
            public Kind kind = Kind.Mag;
            public float refLen = 0.5f;
            public string takeMat, takeObj;           // the magazine inside the gun mesh (material / object name)
            public Vector3 magOut = Vector3.down;     // pull-out direction of the magazine
            public Vector3 magCenter, magSize;        // proxy mag / clip seat / shell port / cylinder centre
            public Vector3 support;                   // where the hand steadies the gun (inspect, pump)
            public Vector3 charge;                    // slide / charging handle / bolt (rack after a reload)
            public bool hasCharge;
            public Vector3 tiltPos, tiltRot;          // holder offset while reloading (camera space / euler)
        }

        static Spec SpecFor(string model)
        {
            switch (model)
            {
                case "Pistol":
                    return new Spec { refLen = 0.22f, takeMat = "Magazine", magOut = new Vector3(0f, -1f, -0.22f),
                        magCenter = new Vector3(0f, -0.06f, 0.05f), magSize = new Vector3(0.016f, 0.09f, 0.045f),
                        support = new Vector3(0f, -0.03f, 0.075f), charge = new Vector3(0f, 0.03f, 0.075f), hasCharge = true,
                        tiltPos = new Vector3(-0.14f, 0.17f, -0.02f), tiltRot = new Vector3(-4f, -22f, -24f) };
                case "Revolver":
                    return new Spec { kind = Kind.Revolver, refLen = 0.3f,
                        magCenter = new Vector3(0f, 0f, 0.152f), magSize = new Vector3(0.05f, 0.05f, 0.065f),
                        support = new Vector3(0f, -0.03f, 0.14f), charge = new Vector3(0f, 0f, 0.152f),
                        tiltPos = new Vector3(-0.14f, 0.17f, -0.02f), tiltRot = new Vector3(-22f, -16f, -40f) };
                case "Uzi":
                    return new Spec { refLen = 0.36f, takeObj = "Cube", magOut = Vector3.down,
                        magCenter = new Vector3(0f, -0.095f, 0.096f), magSize = new Vector3(0.02f, 0.13f, 0.044f),
                        support = new Vector3(0f, 0.01f, 0.24f), charge = new Vector3(0f, 0.065f, 0.21f), hasCharge = true,
                        tiltPos = new Vector3(-0.14f, 0.16f, -0.02f), tiltRot = new Vector3(-8f, -14f, -30f) };
                case "P90":
                    return new Spec { refLen = 0.5f, takeMat = "Material.004", magOut = new Vector3(0f, 1f, -0.15f),
                        magCenter = new Vector3(0f, -0.008f, 0.07f), magSize = new Vector3(0.06f, 0.02f, 0.26f),
                        support = new Vector3(0f, -0.06f, 0.22f), charge = new Vector3(0f, 0.04f, 0.2f),
                        tiltPos = new Vector3(-0.15f, 0.16f, -0.12f), tiltRot = new Vector3(-6f, -10f, 26f) };
                case "Shotgun":
                    return new Spec { kind = Kind.Shells, refLen = 0.85f,
                        magCenter = new Vector3(0f, -0.03f, 0.40f), magSize = new Vector3(0.019f, 0.019f, 0.06f),
                        support = new Vector3(0f, -0.005f, 0.64f), charge = new Vector3(0f, -0.005f, 0.64f), hasCharge = true,
                        tiltPos = new Vector3(-0.16f, 0.14f, -0.28f), tiltRot = new Vector3(-6f, -12f, -42f) };
                case "AK47":
                    return new Spec { refLen = 0.68f, takeObj = "Cube.001", magOut = new Vector3(0f, -1f, 0.35f),
                        magCenter = new Vector3(0f, -0.08f, 0.09f), magSize = new Vector3(0.03f, 0.16f, 0.06f),
                        support = new Vector3(0f, 0.0f, 0.22f), charge = new Vector3(0.03f, 0.05f, 0.05f),
                        tiltPos = new Vector3(-0.17f, 0.2f, -0.16f), tiltRot = new Vector3(-8f, -12f, -30f) };
                case "Rifle":
                    return new Spec { kind = Kind.TopClip, refLen = 0.9f,
                        magCenter = new Vector3(0f, 0.012f, 0.36f), magSize = new Vector3(0.014f, 0.03f, 0.055f),
                        support = new Vector3(0f, -0.01f, 0.62f), charge = new Vector3(0.02f, 0.02f, 0.33f),
                        tiltPos = new Vector3(-0.15f, 0.13f, -0.3f), tiltRot = new Vector3(-4f, -10f, 26f) };
                case "BoltRifle":
                    return new Spec { kind = Kind.TopClip, refLen = 0.95f,
                        magCenter = new Vector3(0f, 0.016f, 0.43f), magSize = new Vector3(0.014f, 0.03f, 0.055f),
                        support = new Vector3(0f, -0.02f, 0.62f), charge = new Vector3(0.022f, 0.022f, 0.38f), hasCharge = true,
                        tiltPos = new Vector3(-0.15f, 0.13f, -0.3f), tiltRot = new Vector3(-4f, -10f, 26f) };
                case "SniperRifle":
                    return new Spec { refLen = 1.0f, takeMat = "Magazine", magOut = Vector3.down,
                        magCenter = new Vector3(0f, -0.07f, 0.225f), magSize = new Vector3(0.02f, 0.05f, 0.06f),
                        support = new Vector3(0f, -0.03f, 0.40f), charge = new Vector3(0.02f, 0.012f, 0.13f), hasCharge = true,
                        tiltPos = new Vector3(-0.16f, 0.17f, -0.26f), tiltRot = new Vector3(-6f, -10f, -28f) };
            }
            return null;
        }

        // A hand pose (slot space): palm centre, rotation (+Z fingers, +Y back of the hand, +X thumb side),
        // grip 0 = open hand .. 1 = fist.
        struct Key
        {
            public float u, grip;
            public Vector3 p;
            public Quaternion r;
            public bool cam;     // p is in camera space (off-screen spots)
            public Key(float u, Vector3 p, Quaternion r, float grip) { this.u = u; this.p = p; this.r = r; this.grip = grip; cam = false; }
            public static Key Cam(float u, Vector3 p, Quaternion r, float grip) => new Key(u, p, r, grip) { cam = true };
        }

        // camera space: below the bottom edge of the screen (hand away / fetching a fresh mag), the shoulder
        static readonly Vector3 OFF_CAM = new Vector3(-0.1f, -0.5f, 0.25f);
        static readonly Vector3 FETCH_CAM = new Vector3(0.0f, -0.46f, 0.38f);
        static readonly Vector3 SHOULDER_CAM = new Vector3(-0.1f, -0.75f, 0.12f);

        class Rig
        {
            public Transform slot;
            public Spec spec;
            public Kind kind;
            public float k = 1f;              // model length / spec reference length
            public bool rack;                 // slide / charging handle / bolt after the reload
            public MeshFilter[] mfs;          // gun pieces whose mesh is swapped while the mag is out
            public Mesh[] full, cut;
            public Transform oldMag, newMag;  // Mag: the pulled one (drops away) and the fresh one
            public Transform item;            // TopClip: clip; Shells: shell; Revolver: speedloader
            public Transform loaderRounds;    // Revolver: the speedloader's rounds (stay in the cylinder)
            public Transform[] casings;       // Revolver: empty shells falling out
            public Vector3 magCenter, magOut, support, charge, pivot;
            public float magHalfLen;
            public Vector3 holdPos;           // the carried part, in hand space
            public Quaternion holdRot = Quaternion.identity;
            public Key[] reload;
            public bool meshCut;
        }

        // reload timeline (fractions of the reload time); the mag goes in at 0.55 (reload_in sound), rack at 0.85
        const float U_GRIP = 0.13f, U_RELEASE = 0.30f, U_FETCH = 0.40f, U_SEAT = 0.56f;
        const int SHELLS = 3;

        static readonly Color GloveCol = new Color(0.42f, 0.44f, 0.36f);
        static readonly Color SuedeCol = new Color(0.64f, 0.56f, 0.44f);
        static readonly Color CuffCol = new Color(0.27f, 0.28f, 0.24f);
        static readonly Color StrapCol = new Color(0.17f, 0.175f, 0.16f);
        static readonly Color SleeveCol = new Color(0.46f, 0.45f, 0.35f);

        static Material s_glove, s_suede, s_cuff, s_strap, s_sleeve, s_magMat, s_brass, s_shellRed, s_black;
        static Mesh s_box;
        static readonly Dictionary<long, Mesh> s_prisms = new Dictionary<long, Mesh>();

        WeaponData[] weapons;
        Rig[] rigs;
        int cur = -1;
        float inspectT = -1f;

        Transform hand, sleeve, camT;
        readonly Transform[] fingerA = new Transform[4], fingerB = new Transform[4];
        Transform thumbA, thumbB;
        readonly List<Mesh> ownedMeshes = new List<Mesh>();

        public bool Inspecting => inspectT >= 0f;

        // Has this weapon slot a hand rig (otherwise WeaponController keeps its plain reload dip)?
        public bool Has(int weapon) => rigs != null && weapon >= 0 && weapon < rigs.Length && rigs[weapon] != null;

        public void StartInspect()
        {
            if (cur >= 0 && rigs != null && rigs[cur] != null && inspectT < 0f) inspectT = 0f;
        }

        public void CancelInspect() => inspectT = -1f;

        // ───────── setup ─────────

        public void Init(Transform weaponHolder, WeaponData[] weaponList)
        {
            weapons = weaponList;
            camT = weaponHolder.parent != null ? weaponHolder.parent : weaponHolder;
            rigs = new Rig[weapons.Length];
            BuildHand();
            for (int i = 0; i < weapons.Length && i < weaponHolder.childCount; i++)
            {
                var slot = weaponHolder.GetChild(i);
                Transform model = null;
                foreach (Transform c in slot) if (c.name.StartsWith("GunModel_")) { model = c; break; }
                var spec = SpecFor(weapons[i].model);
                if (model == null || spec == null) continue;
                try { rigs[i] = BuildRig(slot, model, spec, weapons[i]); }
                catch (System.Exception e) { Debug.LogWarning("[Airsoft] viewmodel hand rig " + weapons[i].model + ": " + e.Message); }
            }
            hand.gameObject.SetActive(false);
        }

        Rig BuildRig(Transform slot, Transform model, Spec spec, WeaponData w)
        {
            var r = new Rig { slot = slot, spec = spec, kind = spec.kind, k = w.modelLength / spec.refLen };
            r.magOut = spec.magOut.normalized;
            r.magCenter = spec.magCenter * r.k;
            r.support = spec.support * r.k;
            r.charge = spec.charge * r.k;
            r.rack = spec.hasCharge && AirsoftAudio.ShotKind(w) != "aeg";
            r.pivot = new Vector3(0f, 0f, w.modelLength * 0.35f);
            Vector3 magSize = spec.magSize * r.k;

            if (r.kind == Kind.Mag)
            {
                Mesh magMesh = null;
                Material[] magMats = null;
                if (!string.IsNullOrEmpty(spec.takeMat) || !string.IsNullOrEmpty(spec.takeObj))
                    magMesh = CutMagazine(r, model, spec, out magMats, ref magSize);
                r.oldMag = MagObject(slot, "MagOld", magMesh, magMats, magSize);
                r.newMag = MagObject(slot, "MagNew", magMesh, magMats, magSize);
                r.magHalfLen = 0.5f * (Mathf.Abs(r.magOut.x) * magSize.x + Mathf.Abs(r.magOut.y) * magSize.y + Mathf.Abs(r.magOut.z) * magSize.z);
            }
            else if (r.kind == Kind.TopClip) r.item = ClipObject(slot, magSize);
            else if (r.kind == Kind.Shells) r.item = ShellObject(slot, r.k);
            else
            {
                r.item = SpeedloaderObject(slot, out r.loaderRounds);
                r.casings = new Transform[6];
                for (int i = 0; i < 6; i++)
                {
                    var c = Part(slot, "Casing", PrismMesh(6, 0.0045f, 0.026f), Brass());
                    c.gameObject.SetActive(false);
                    r.casings[i] = c;
                }
            }

            BuildReloadKeys(r, magSize);
            return r;
        }

        // Cut the magazine out of the gun mesh: triangles of the given material / object become a separate
        // mesh (centred on the magazine, slot space); the gun swaps to the cut mesh while the mag is out.
        Mesh CutMagazine(Rig r, Transform model, Spec spec, out Material[] mats, ref Vector3 magSize)
        {
            mats = null;
            var mfs = new List<MeshFilter>();
            var fulls = new List<Mesh>();
            var cuts = new List<Mesh>();
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var matList = new List<Material>();
            var subTris = new List<List<int>>();
            Vector3 mn = Vector3.one * 1e6f, mx = -Vector3.one * 1e6f;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                var src = mf.sharedMesh;
                var rend = mf.GetComponent<Renderer>();
                if (src == null || !src.isReadable || rend == null) continue;
                if (!string.IsNullOrEmpty(spec.takeObj) && mf.name != spec.takeObj) continue;
                var srcMats = rend.sharedMaterials;
                var M = r.slot.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var v = src.vertices;
                var n = src.normals;
                var uv = src.uv;
                var cut = Instantiate(src);
                cut.name = src.name + "_nomag";
                bool any = false;
                int baseV = verts.Count;
                for (int s = 0; s < src.subMeshCount; s++)
                {
                    string matName = s < srcMats.Length && srcMats[s] != null ? srcMats[s].name : "";
                    bool take = string.IsNullOrEmpty(spec.takeMat) || matName.StartsWith(spec.takeMat);
                    var t = src.GetTriangles(s);
                    if (!take) continue;
                    any = true;
                    cut.SetTriangles(new int[0], s);
                    var list = new List<int>(t.Length);
                    foreach (int idx in t) list.Add(baseV + idx);
                    subTris.Add(list);
                    matList.Add(s < srcMats.Length ? srcMats[s] : null);
                }
                if (!any) { Destroy(cut); continue; }
                for (int i = 0; i < v.Length; i++)
                {
                    verts.Add(M.MultiplyPoint3x4(v[i]));
                    norms.Add(n != null && n.Length == v.Length ? M.MultiplyVector(n[i]).normalized : Vector3.up);
                    uvs.Add(uv != null && uv.Length == v.Length ? uv[i] : Vector2.zero);
                }
                foreach (var list in subTris)
                    foreach (int idx in list)
                        if (idx >= baseV) { mn = Vector3.Min(mn, verts[idx]); mx = Vector3.Max(mx, verts[idx]); }
                ownedMeshes.Add(cut);
                mfs.Add(mf); fulls.Add(src); cuts.Add(cut);
            }
            if (mfs.Count == 0 || subTris.Count == 0) return null;

            r.mfs = mfs.ToArray(); r.full = fulls.ToArray(); r.cut = cuts.ToArray();
            r.magCenter = (mn + mx) * 0.5f;
            magSize = mx - mn;
            for (int i = 0; i < verts.Count; i++) verts[i] -= r.magCenter;
            var mesh = new Mesh { name = "VMMag" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = subTris.Count;
            for (int s = 0; s < subTris.Count; s++) mesh.SetTriangles(subTris[s], s);
            mesh.RecalculateBounds();
            ownedMeshes.Add(mesh);
            mats = matList.ToArray();
            return mesh;
        }

        Transform MagObject(Transform slot, string name, Mesh mesh, Material[] mats, Vector3 size)
        {
            Transform t;
            if (mesh != null)
            {
                t = new GameObject(name).transform;
                t.SetParent(slot, false);
                t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = t.gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mats;
                Quiet(mr);
            }
            else
            {
                t = new GameObject(name).transform;
                t.SetParent(slot, false);
                var body = Part(t, "Body", BoxMesh(), MagMat());
                body.localScale = size;
                var plate = Part(t, "Plate", BoxMesh(), Black());
                plate.localScale = new Vector3(size.x * 1.15f, 0.006f, size.z * 1.1f);
                plate.localPosition = new Vector3(0f, -size.y * 0.5f, 0f);
            }
            t.gameObject.SetActive(false);
            return t;
        }

        // An M1-style stripper clip with a row of brass rounds (pushed down into the receiver).
        Transform ClipObject(Transform slot, Vector3 size)
        {
            var t = new GameObject("Clip").transform;
            t.SetParent(slot, false);
            var body = Part(t, "Body", BoxMesh(), MagMat());
            body.localScale = new Vector3(size.x, size.y * 0.75f, size.z);
            for (int i = 0; i < 5; i++)
            {
                var rd = Part(t, "Round", PrismMesh(6, size.x * 0.33f, size.x * 0.9f), Brass());
                rd.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                rd.localPosition = new Vector3(0f, size.y * 0.37f, Mathf.Lerp(-size.z * 0.4f, size.z * 0.4f, i / 4f));
            }
            t.gameObject.SetActive(false);
            return t;
        }

        Transform ShellObject(Transform slot, float k)
        {
            var t = new GameObject("Shell").transform;
            t.SetParent(slot, false);
            var body = Part(t, "Hull", PrismMesh(8, 0.0095f, 0.05f), ShellRed());
            body.localPosition = new Vector3(0f, 0f, -0.02f);
            var head = Part(t, "Head", PrismMesh(8, 0.0102f, 0.012f), Brass());
            head.localPosition = new Vector3(0f, 0f, -0.032f);
            t.gameObject.SetActive(false);
            return t;
        }

        // Six rounds around a black knob, rounds pointing +Z.
        Transform SpeedloaderObject(Transform slot, out Transform rounds)
        {
            var t = new GameObject("Speedloader").transform;
            t.SetParent(slot, false);
            var hub = Part(t, "Hub", PrismMesh(6, 0.016f, 0.012f), Black());
            hub.localPosition = new Vector3(0f, 0f, -0.016f);
            var knob = Part(t, "Knob", PrismMesh(6, 0.006f, 0.012f), Black());
            knob.localPosition = new Vector3(0f, 0f, -0.028f);
            rounds = new GameObject("Rounds").transform;
            rounds.SetParent(t, false);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                var rd = Part(rounds, "Round", PrismMesh(6, 0.0042f, 0.026f), Brass());
                rd.localPosition = new Vector3(Mathf.Cos(a) * 0.0105f, Mathf.Sin(a) * 0.0105f, -0.004f);
            }
            t.gameObject.SetActive(false);
            return t;
        }

        // ───────── the glove ─────────

        void BuildHand()
        {
            hand = new GameObject("VMHand").transform;
            hand.SetParent(transform, false);
            var box = BoxMesh();
            Transform Bx(Transform p, string n, Vector3 c, Vector3 s, Material m)
            {
                var t = Part(p, n, box, m);
                t.localPosition = c;
                t.localScale = s;
                return t;
            }
            Bx(hand, "Palm", new Vector3(0f, 0f, 0f), new Vector3(0.084f, 0.03f, 0.094f), Glove());
            Bx(hand, "PalmPad", new Vector3(0.004f, -0.0155f, -0.004f), new Vector3(0.068f, 0.004f, 0.072f), Suede());
            Bx(hand, "Knuckles", new Vector3(0f, 0.0165f, 0.03f), new Vector3(0.078f, 0.009f, 0.024f), Cuff());
            Bx(hand, "BackPad", new Vector3(0f, 0.0158f, -0.012f), new Vector3(0.06f, 0.004f, 0.04f), Cuff());
            Bx(hand, "Cuff", new Vector3(0f, 0.001f, -0.068f), new Vector3(0.074f, 0.04f, 0.05f), Cuff());
            Bx(hand, "Strap", new Vector3(0f, 0.001f, -0.066f), new Vector3(0.078f, 0.044f, 0.016f), Strap());
            sleeve = new GameObject("Forearm").transform;      // aimed at the shoulder every frame (wrist bends)
            sleeve.SetParent(hand, false);
            sleeve.localPosition = new Vector3(0f, 0.003f, -0.075f);
            Bx(sleeve, "Sleeve", new Vector3(0f, 0f, -0.33f), new Vector3(0.07f, 0.06f, 0.66f), Sleeve());

            float[] fx = { 0.031f, 0.0105f, -0.0105f, -0.031f };      // index .. pinky (thumb side = +X)
            float[] l1 = { 0.042f, 0.046f, 0.043f, 0.034f };
            float[] l2 = { 0.036f, 0.040f, 0.037f, 0.030f };
            float[] fz = { 0.046f, 0.047f, 0.046f, 0.042f };
            for (int i = 0; i < 4; i++)
            {
                var a = new GameObject("FingerA" + i).transform;
                a.SetParent(hand, false);
                a.localPosition = new Vector3(fx[i], -0.002f, fz[i]);
                a.localRotation = Quaternion.Euler(0f, Splay[i], 0f);
                Bx(a, "Seg", new Vector3(0f, 0f, l1[i] * 0.5f), new Vector3(0.0185f, 0.02f, l1[i] + 0.004f), Glove());
                var b = new GameObject("FingerB" + i).transform;
                b.SetParent(a, false);
                b.localPosition = new Vector3(0f, 0f, l1[i]);
                Bx(b, "Seg", new Vector3(0f, 0f, l2[i] * 0.5f), new Vector3(0.0175f, 0.0185f, l2[i]), Glove());
                Bx(b, "Tip", new Vector3(0f, -0.0085f, l2[i] * 0.62f), new Vector3(0.0155f, 0.004f, l2[i] * 0.62f), Suede());
                fingerA[i] = a; fingerB[i] = b;
            }
            thumbA = new GameObject("ThumbA").transform;
            thumbA.SetParent(hand, false);
            thumbA.localPosition = new Vector3(0.036f, -0.008f, -0.022f);
            Bx(thumbA, "Seg", new Vector3(0f, 0f, 0.021f), new Vector3(0.022f, 0.021f, 0.046f), Glove());
            thumbB = new GameObject("ThumbB").transform;
            thumbB.SetParent(thumbA, false);
            thumbB.localPosition = new Vector3(0f, 0f, 0.042f);
            Bx(thumbB, "Seg", new Vector3(0f, 0f, 0.016f), new Vector3(0.0195f, 0.019f, 0.034f), Glove());
            Bx(thumbB, "Tip", new Vector3(0f, -0.0085f, 0.02f), new Vector3(0.016f, 0.004f, 0.022f), Suede());
            SetGrip(0.3f);
        }

        static readonly float[] Splay = { 4f, 1f, -2f, -6f };

        void SetGrip(float g)
        {
            g = Mathf.Clamp01(g);
            for (int i = 0; i < 4; i++)
            {
                float extra = i * 4f;   // the little finger curls a bit more
                fingerA[i].localRotation = Quaternion.Euler(Mathf.Lerp(6f, 72f + extra, g), Mathf.Lerp(Splay[i], 0f, g), 0f);
                fingerB[i].localRotation = Quaternion.Euler(Mathf.Lerp(8f, 88f, g), 0f, 0f);
            }
            thumbA.localRotation = Quaternion.Euler(Mathf.Lerp(12f, 34f, g), Mathf.Lerp(48f, 18f, g), Mathf.Lerp(-20f, -45f, g));
            thumbB.localRotation = Quaternion.Euler(Mathf.Lerp(6f, 38f, g), 0f, 0f);
        }

        // ───────── keyframes ─────────

        static float Ease(float t) => t * t * (3f - 2f * t);
        static float Seg(float u, float a, float b) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, u));
        static float Bump(float u, float at, float width)
        {
            float x = (u - at) / width;
            return x <= 0f || x >= 1f ? 0f : Mathf.Sin(x * Mathf.PI);
        }

        Vector3 KeyPos(Transform slot, Key k) => k.cam ? slot.InverseTransformPoint(camT.TransformPoint(k.p)) : k.p;

        void Eval(Transform slot, Key[] keys, float u, out Vector3 p, out Quaternion r, out float g)
        {
            if (u <= keys[0].u) { p = KeyPos(slot, keys[0]); r = keys[0].r; g = keys[0].grip; return; }
            for (int i = 0; i < keys.Length - 1; i++)
            {
                var a = keys[i]; var b = keys[i + 1];
                if (u > b.u) continue;
                float t = Ease(Mathf.InverseLerp(a.u, b.u, u));
                p = Vector3.Lerp(KeyPos(slot, a), KeyPos(slot, b), t);
                r = Quaternion.Slerp(a.r, b.r, t);
                g = Mathf.Lerp(a.grip, b.grip, t);
                return;
            }
            var l = keys[keys.Length - 1];
            p = KeyPos(slot, l); r = l.r; g = l.grip;
        }

        static Quaternion Look(Vector3 fingers, Vector3 back) => Quaternion.LookRotation(fingers.normalized, back.normalized);

        void BuildReloadKeys(Rig r, Vector3 magSize)
        {
            var keys = new List<Key>();
            switch (r.kind)
            {
                case Kind.Mag:
                {
                    Vector3 o = r.magOut, up = -o;
                    bool top = o.y > 0.5f;                                   // P90: the mag comes off the top
                    Vector3 fwd = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
                    Quaternion rg = top ? Look(fwd, Vector3.up * 0.6f + Vector3.left) : Look(fwd * 0.85f + up * 0.5f, Vector3.left);
                    Vector3 grip = r.magCenter + Vector3.left * (magSize.x * 0.5f + 0.019f) + o * (r.magHalfLen * (top ? 0f : 0.35f))
                                   - fwd * 0.02f + (top ? Vector3.up * 0.01f : Vector3.zero);
                    Vector3 pulled = grip + o * (r.magHalfLen * 2f + 0.025f) + Vector3.left * 0.015f;
                    Vector3 below = grip + o * (r.magHalfLen * 2f + 0.02f);
                    keys.Add(Key.Cam(0f, OFF_CAM, rg, 0.35f));
                    keys.Add(new Key(U_GRIP - 0.04f, grip + o * 0.025f + Vector3.left * 0.03f, rg, 0.2f));
                    keys.Add(new Key(U_GRIP, grip, rg, 0.8f));
                    keys.Add(new Key(U_RELEASE, pulled, rg * Quaternion.Euler(0f, 0f, -12f), 0.8f));
                    keys.Add(new Key(U_RELEASE + 0.03f, pulled + o * 0.03f + Vector3.left * 0.03f, rg * Quaternion.Euler(0f, 0f, -12f), 0.35f));
                    keys.Add(Key.Cam(U_FETCH, FETCH_CAM, rg, 0.8f));
                    keys.Add(new Key(U_SEAT - 0.07f, below, rg, 0.8f));
                    keys.Add(new Key(U_SEAT, grip, rg, 0.8f));
                    keys.Add(new Key(U_SEAT + 0.04f, grip - o * 0.006f, rg, 0.35f));      // slap it home
                    keys.Add(new Key(U_SEAT + 0.09f, grip + o * 0.04f + Vector3.left * 0.04f, rg, 0.3f));
                    r.holdRot = Quaternion.Inverse(rg);
                    r.holdPos = Quaternion.Inverse(rg) * (r.magCenter - grip);
                    AddRackOrLeave(r, keys, rg);
                    break;
                }
                case Kind.TopClip:
                {
                    Quaternion rc = Look(new Vector3(0.8f, -0.3f, 0.5f), new Vector3(-0.3f, 1f, 0f));
                    Vector3 seat = r.magCenter;
                    Vector3 hold = new Vector3(0.015f, -0.03f, 0.06f);          // clip under the finger tips
                    Vector3 above = seat + Vector3.up * 0.075f;
                    keys.Add(Key.Cam(0f, OFF_CAM, rc, 0.55f));
                    keys.Add(new Key(0.16f, HandFor(above, rc, hold), rc, 0.55f));
                    keys.Add(new Key(0.40f, HandFor(above - Vector3.up * 0.02f, rc, hold), rc, 0.55f));
                    keys.Add(new Key(U_SEAT - 0.02f, HandFor(seat, rc, hold), rc, 0.6f));
                    keys.Add(new Key(U_SEAT + 0.06f, HandFor(seat + Vector3.up * 0.05f + Vector3.left * 0.03f, rc, hold), rc, 0.3f));
                    r.holdRot = Quaternion.Inverse(rc);
                    r.holdPos = hold;
                    AddRackOrLeave(r, keys, rc);
                    break;
                }
                case Kind.Shells:
                {
                    Quaternion rs = Look(Vector3.forward + Vector3.up * 0.2f, Vector3.down + Vector3.left * 0.5f);
                    Vector3 port = r.magCenter;
                    Vector3 hold = new Vector3(0.01f, -0.028f, 0.05f);          // shell lying on the finger tips
                    float u0 = 0.10f, u1 = 0.70f, c = (u1 - u0) / SHELLS;
                    keys.Add(Key.Cam(0f, OFF_CAM, rs, 0.4f));
                    for (int i = 0; i < SHELLS; i++)
                    {
                        float s = u0 + i * c;
                        Vector3 entry = port + Vector3.down * 0.05f + Vector3.back * 0.02f;
                        keys.Add(new Key(s + c * 0.35f, HandFor(entry, rs, hold), rs, 0.45f));
                        keys.Add(new Key(s + c * 0.6f, HandFor(port + Vector3.forward * 0.02f, rs, hold), rs, 0.5f));
                        keys.Add(new Key(s + c * 0.95f, HandFor(port + new Vector3(-0.04f, -0.13f, -0.04f), rs, hold), rs, 0.45f));
                    }
                    r.holdRot = Quaternion.Inverse(rs);
                    r.holdPos = hold;
                    // pump: hand under the fore-end, rack it back and forward
                    Quaternion rp = Look(Vector3.right + Vector3.up * 0.2f, Vector3.down + Vector3.left * 0.3f);
                    Vector3 fe = r.charge + new Vector3(-0.006f, -0.034f, 0f);
                    keys.Add(new Key(0.76f, fe, rp, 0.35f));
                    keys.Add(new Key(0.79f, fe, rp, 0.85f));
                    keys.Add(new Key(0.85f, fe + Vector3.back * 0.075f, rp, 0.85f));
                    keys.Add(new Key(0.90f, fe, rp, 0.85f));
                    keys.Add(new Key(0.93f, fe, rp, 0.4f));
                    keys.Add(Key.Cam(1f, OFF_CAM, rp, 0.4f));
                    break;
                }
                case Kind.Revolver:
                {
                    Vector3 cyl = r.magCenter;
                    Quaternion rpush = Look(Vector3.up, Vector3.forward + Vector3.left * 0.6f);   // palm pushing back on the ejector
                    Quaternion rl = Look(new Vector3(0.4f, 0.85f, 0.3f), new Vector3(-0.85f, 0.4f, 0f));   // from below-left, rounds at the finger tips
                    Vector3 push = cyl + new Vector3(-0.03f, -0.06f, 0.075f);
                    Vector3 hold = new Vector3(0f, -0.005f, 0.085f);
                    keys.Add(Key.Cam(0f, OFF_CAM, rpush, 0.3f));
                    keys.Add(new Key(0.14f, push + Vector3.forward * 0.02f, rpush, 0.25f));
                    keys.Add(new Key(0.24f, push + Vector3.back * 0.012f, rpush, 0.25f));     // ejector: casings drop out
                    keys.Add(new Key(0.30f, push + new Vector3(-0.03f, -0.05f, 0.02f), rpush, 0.3f));
                    keys.Add(Key.Cam(0.40f, FETCH_CAM, rl, 0.55f));
                    keys.Add(new Key(0.50f, HandFor(cyl + Vector3.left * 0.06f, rl, hold), rl, 0.55f));
                    keys.Add(new Key(0.58f, HandFor(cyl + Vector3.left * 0.03f, rl, hold), rl, 0.55f));   // rounds in
                    keys.Add(new Key(0.64f, HandFor(cyl + Vector3.left * 0.03f, rl, hold), rl * Quaternion.Euler(0f, 0f, 35f), 0.55f));   // twist
                    keys.Add(new Key(0.72f, HandFor(cyl + Vector3.left * 0.09f + Vector3.down * 0.03f, rl, hold), rl, 0.55f));
                    keys.Add(Key.Cam(0.86f, OFF_CAM, rl, 0.4f));
                    keys.Add(Key.Cam(1f, OFF_CAM, rl, 0.4f));
                    r.holdRot = Quaternion.Inverse(rl) * Quaternion.LookRotation(Vector3.right, Vector3.up);
                    r.holdPos = hold;
                    break;
                }
            }
            r.reload = keys.ToArray();
        }

        // Where the hand must be for the part it holds (hand-space offset `hold`) to sit at `at`.
        static Vector3 HandFor(Vector3 at, Quaternion handRot, Vector3 hold) => at - handRot * hold;

        // After the mag is in: rack the slide / charging handle / bolt (gas, spring guns), or just leave.
        void AddRackOrLeave(Rig r, List<Key> keys, Quaternion rest)
        {
            if (!r.rack) { keys.Add(Key.Cam(0.82f, OFF_CAM, rest, 0.35f)); keys.Add(Key.Cam(1f, OFF_CAM, rest, 0.35f)); return; }
            Quaternion ro = Look(Vector3.right * 0.8f + Vector3.forward * 0.6f + Vector3.down * 0.3f, Vector3.up + Vector3.left * 0.25f);   // overhand
            Vector3 at = r.charge + new Vector3(-0.012f, 0.032f, 0f);
            keys.Add(new Key(0.74f, at + Vector3.up * 0.02f + Vector3.forward * 0.01f, ro, 0.3f));
            keys.Add(new Key(0.79f, at, ro, 0.75f));
            keys.Add(new Key(0.85f, at + Vector3.back * 0.04f, ro, 0.75f));
            keys.Add(new Key(0.88f, at + Vector3.back * 0.035f + Vector3.up * 0.02f, ro, 0.3f));
            keys.Add(Key.Cam(1f, OFF_CAM, ro, 0.3f));
        }

        // ───────── per frame ─────────

        // Extra holder pose for this frame (camera space position offset, local rotation applied after
        // baseRot) and the hand / loose parts. reloadU = reload progress 0..1, or < 0 when not reloading.
        public void Animate(int weapon, float reloadU, Quaternion baseRot, out Vector3 dPos, out Quaternion dRot)
        {
            dPos = Vector3.zero;
            dRot = Quaternion.identity;
            if (rigs == null) return;
            if (weapon != cur)
            {
                if (cur >= 0 && cur < rigs.Length) ResetRig(rigs[cur]);
                cur = weapon;
                inspectT = -1f;
                var rr = cur >= 0 && cur < rigs.Length ? rigs[cur] : null;
                if (rr != null) hand.SetParent(rr.slot, false);
            }
            var r = cur >= 0 && cur < rigs.Length ? rigs[cur] : null;
            if (r == null) { hand.gameObject.SetActive(false); return; }

            if (reloadU >= 0f) inspectT = -1f;
            if (inspectT >= 0f)
            {
                inspectT += Time.deltaTime;
                if (inspectT >= INSPECT_SECONDS) inspectT = -1f;
            }

            Vector3 pos = Vector3.zero, euler = Vector3.zero;
            if (reloadU >= 0f) ReloadPose(r, Mathf.Clamp01(reloadU), out pos, out euler);
            else if (inspectT >= 0f) InspectPose(r, inspectT / INSPECT_SECONDS, out pos, out euler);
            else { ResetRig(r); hand.gameObject.SetActive(false); return; }

            dRot = Quaternion.Euler(euler);
            // rotate about the gun's middle rather than the grip
            dPos = pos + baseRot * (r.pivot - dRot * r.pivot);
        }

        void PlaceHand(Rig r, float u)
        {
            Eval(r.slot, r.reload, u, out var p, out var q, out var g);
            hand.localPosition = p;
            hand.localRotation = q;
            SetGrip(g);
            if (!hand.gameObject.activeSelf) hand.gameObject.SetActive(true);
            AimForearm();
        }

        // The forearm points from the wrist back to the shoulder (lower left, behind the camera), bending at
        // the wrist at most 80 degrees.
        void AimForearm()
        {
            Vector3 wrist = sleeve.position;
            Vector3 d = (camT.TransformPoint(SHOULDER_CAM) - wrist).normalized;
            Vector3 back = -hand.forward;
            float ang = Vector3.Angle(back, d);
            if (ang > 80f) d = Vector3.Slerp(back, d, 80f / ang);
            sleeve.rotation = Quaternion.LookRotation(-d, hand.up);
        }

        static void Show(Transform t, bool on)
        {
            if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
        }

        void Carry(Rig r, Transform t)
        {
            t.localPosition = hand.localPosition + hand.localRotation * r.holdPos;
            t.localRotation = hand.localRotation * r.holdRot;
        }

        void SetMagCut(Rig r, bool cut)
        {
            if (r.mfs == null || r.meshCut == cut) return;
            r.meshCut = cut;
            for (int i = 0; i < r.mfs.Length; i++)
                if (r.mfs[i] != null) r.mfs[i].sharedMesh = cut ? r.cut[i] : r.full[i];
        }

        void ResetRig(Rig r)
        {
            if (r == null) return;
            SetMagCut(r, false);
            Show(r.oldMag, false); Show(r.newMag, false); Show(r.item, false);
            if (r.loaderRounds != null) Show(r.loaderRounds, true);
            if (r.casings != null) foreach (var c in r.casings) Show(c, false);
        }

        void ReloadPose(Rig r, float u, out Vector3 pos, out Vector3 euler)
        {
            float rt = weapons[cur].reloadTime;
            PlaceHand(r, u);
            float endTilt = r.kind == Kind.Shells || r.rack ? 0.9f : 0.8f;
            float a = Seg(u, 0f, 0.12f) * (1f - Seg(u, endTilt, 1f));
            pos = r.spec.tiltPos * a;
            euler = r.spec.tiltRot * a;
            if (r.rack && r.kind != Kind.Revolver) euler.z *= 1f - Seg(u, 0.6f, 0.72f);   // level it for the slide / bolt

            switch (r.kind)
            {
                case Kind.Mag:
                {
                    SetMagCut(r, u >= U_GRIP && u < U_SEAT);
                    // the empty mag: in the hand, then dropped (falls away, tumbling)
                    if (u >= U_GRIP && u < U_RELEASE) { Show(r.oldMag, true); Carry(r, r.oldMag); }
                    else if (u >= U_RELEASE)
                    {
                        float t = (u - U_RELEASE) * rt;
                        Show(r.oldMag, t < 0.7f);
                        if (t < 0.7f)
                        {
                            Key rel = r.reload[3];
                            Vector3 p0 = rel.p + rel.r * r.holdPos;
                            Vector3 g = r.slot.InverseTransformDirection(Vector3.down) * 9.81f;
                            Vector3 v = r.magOut * 0.6f + Vector3.left * 0.25f;
                            r.oldMag.localPosition = p0 + v * t + 0.5f * g * t * t;
                            r.oldMag.localRotation = rel.r * r.holdRot * Quaternion.Euler(t * 260f, t * 60f, t * 120f);
                        }
                    }
                    else Show(r.oldMag, false);
                    bool carryNew = u >= U_FETCH - 0.02f && u < U_SEAT;
                    Show(r.newMag, carryNew);
                    if (carryNew) Carry(r, r.newMag);
                    float slap = Bump(u, U_SEAT - 0.01f, 0.08f);
                    pos += new Vector3(0f, 0.01f, 0f) * slap;
                    euler += new Vector3(-3.5f, 0f, 0f) * slap;
                    break;
                }
                case Kind.TopClip:
                {
                    bool carry = u < U_SEAT - 0.02f;
                    Show(r.item, carry);
                    if (carry) Carry(r, r.item);
                    float push = Bump(u, U_SEAT - 0.06f, 0.1f);
                    pos += new Vector3(0f, -0.008f, 0f) * push;
                    break;
                }
                case Kind.Shells:
                {
                    float u0 = 0.10f, c = (0.70f - u0) / SHELLS;
                    bool carry = false;
                    for (int i = 0; i < SHELLS; i++)
                    {
                        float s = u0 + i * c;
                        if (u >= s - c * 0.2f && u < s + c * 0.6f) carry = true;
                        pos += new Vector3(0f, 0.006f, 0f) * Bump(u, s + c * 0.5f, c * 0.2f);
                    }
                    if (u < u0) carry = u > 0.02f;
                    Show(r.item, carry);
                    if (carry) Carry(r, r.item);
                    float pump = Bump(u, 0.79f, 0.11f);
                    pos += new Vector3(0f, 0f, -0.012f) * pump;
                    break;
                }
                case Kind.Revolver:
                {
                    // casings drop out of the cylinder after the ejector push
                    float te = (u - 0.24f) * rt;
                    Vector3 g = r.slot.InverseTransformDirection(Vector3.down) * 9.81f;
                    for (int i = 0; i < r.casings.Length; i++)
                    {
                        float ti = te - i * 0.025f;
                        bool on = ti > 0f && ti < 0.6f;
                        Show(r.casings[i], on);
                        if (!on) continue;
                        float ang = i * Mathf.PI / 3f;
                        Vector3 p0 = r.magCenter + new Vector3(Mathf.Cos(ang) * 0.011f, Mathf.Sin(ang) * 0.011f, -0.03f);
                        Vector3 v = new Vector3(-0.25f + Mathf.Cos(ang) * 0.1f, 0.15f + Mathf.Sin(ang) * 0.1f, -0.35f);
                        r.casings[i].localPosition = p0 + v * ti + 0.5f * g * ti * ti;
                        r.casings[i].localRotation = Quaternion.Euler(ti * 400f + i * 30f, ti * 200f, 0f);
                    }
                    bool carry = u >= 0.38f && u < 0.86f;
                    Show(r.item, carry);
                    if (carry) Carry(r, r.item);
                    Show(r.loaderRounds, u < 0.64f);
                    // flick the cylinder shut at the end (rack sound at 0.85)
                    float flick = Bump(u, 0.8f, 0.1f);
                    euler += new Vector3(0f, 0f, 12f) * flick;
                    break;
                }
            }

            if (r.rack)
            {
                float tug = Bump(u, 0.8f, 0.08f);
                pos += new Vector3(0f, 0f, -0.012f) * tug;
                euler += new Vector3(-2f, 0f, 0f) * tug;
            }
        }

        void InspectPose(Rig r, float u, out Vector3 pos, out Vector3 euler)
        {
            ResetRig(r);
            float len = weapons[cur].modelLength;
            float lf = Mathf.InverseLerp(0.22f, 1f, len);
            float y1 = Mathf.Lerp(70f, 50f, lf);                 // long guns turn less
            float z = -Mathf.Lerp(0.06f, 0.26f, lf);
            // left side, drift; roll it over to show the top / right side (the hand steadies it), drift, back
            var p = new[] { Vector3.zero, new Vector3(-0.15f, 0.13f, z), new Vector3(-0.155f, 0.135f, z),
                            new Vector3(-0.14f, 0.12f, z), new Vector3(-0.14f, 0.125f, z), Vector3.zero };
            var e = new[] { Vector3.zero, new Vector3(-6f, -y1, -18f), new Vector3(-10f, -y1 - 6f, -24f),
                            new Vector3(-4f, -y1 * 0.85f, 120f), new Vector3(-8f, -y1 * 0.85f - 6f, 132f), Vector3.zero };
            var t = new[] { 0f, 0.17f, 0.42f, 0.6f, 0.82f, 1f };
            pos = Vector3.zero; euler = Vector3.zero;
            for (int i = 0; i < t.Length - 1; i++)
            {
                if (u > t[i + 1]) continue;
                float s = Ease(Mathf.InverseLerp(t[i], t[i + 1], u));
                pos = Vector3.Lerp(p[i], p[i + 1], s);
                euler = Quaternion.Slerp(Quaternion.Euler(e[i]), Quaternion.Euler(e[i + 1]), s).eulerAngles;
                break;
            }

            // the hand: palm up under the gun (in camera space, whatever way the gun is turned), from 0.44 to 0.92
            float w = Seg(u, 0.42f, 0.52f) * (1f - Seg(u, 0.84f, 0.94f));
            if (w <= 0f) { hand.gameObject.SetActive(false); return; }
            if (!hand.gameObject.activeSelf) hand.gameObject.SetActive(true);
            pendingSupport = true;
            supportW = w;
        }

        bool pendingSupport;
        float supportW;

        // After the holder has its final pose for this frame (WeaponController calls this): put the inspect hand
        // under the gun's support point.
        public void LateHands()
        {
            if (!pendingSupport) return;
            pendingSupport = false;
            var r = cur >= 0 && cur < rigs.Length ? rigs[cur] : null;
            if (r == null) return;
            Vector3 at = r.slot.TransformPoint(r.support) - camT.up * 0.04f;
            Quaternion q = Quaternion.LookRotation(camT.right * 0.9f + camT.forward * 0.35f + camT.up * 0.1f, -camT.up + camT.right * 0.2f);
            Vector3 off = camT.TransformPoint(OFF_CAM);
            hand.SetPositionAndRotation(Vector3.Lerp(off, at, supportW), q);
            SetGrip(Mathf.Lerp(0.3f, 0.7f, supportW));
            AimForearm();
        }

        void OnDestroy()
        {
            if (rigs != null) foreach (var r in rigs) if (r != null) SetMagCut(r, false);
            foreach (var m in ownedMeshes) if (m != null) Destroy(m);
        }

        // ───────── meshes / materials ─────────

        static Transform Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            Quiet(mr);
            return go.transform;
        }

        static void Quiet(MeshRenderer mr)
        {
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        // Unit cube, one normal per face (flat shaded).
        static Mesh BoxMesh()
        {
            if (s_box != null) return s_box;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var d in dirs)
            {
                Vector3 a = d.y != 0f ? Vector3.right : Vector3.up;
                Vector3 b = Vector3.Cross(d, a);
                int i0 = v.Count;
                v.Add((d - a - b) * 0.5f); v.Add((d - a + b) * 0.5f); v.Add((d + a + b) * 0.5f); v.Add((d + a - b) * 0.5f);
                for (int k = 0; k < 4; k++) n.Add(d);
                t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
            }
            s_box = new Mesh { name = "VMBox" };
            s_box.SetVertices(v); s_box.SetNormals(n); s_box.SetTriangles(t, 0);
            FixWinding(s_box);
            s_box.RecalculateBounds();
            return s_box;
        }

        // n-sided prism along +Z from 0 to len, flat shaded.
        static Mesh PrismMesh(int sides, float radius, float len)
        {
            long key = sides * 1000003L + Mathf.RoundToInt(radius * 1e5f) * 1009L + Mathf.RoundToInt(len * 1e5f);
            if (s_prisms.TryGetValue(key, out var m) && m != null) return m;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * radius, p1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * radius;
                Vector3 nn = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), Mathf.Sin((a0 + a1) * 0.5f), 0f);
                int b = v.Count;
                v.Add(p0); v.Add(p1); v.Add(p1 + Vector3.forward * len); v.Add(p0 + Vector3.forward * len);
                for (int k = 0; k < 4; k++) n.Add(nn);
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }
            foreach (float z in new[] { 0f, len })
            {
                int c = v.Count;
                Vector3 nn = z == 0f ? Vector3.back : Vector3.forward;
                v.Add(new Vector3(0f, 0f, z)); n.Add(nn);
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides;
                    v.Add(new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, z)); n.Add(nn);
                }
                for (int i = 0; i < sides; i++) { t.Add(c); t.Add(c + 1 + i); t.Add(c + 1 + (i + 1) % sides); }
            }
            m = new Mesh { name = "VMPrism" };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0);
            FixWinding(m);
            m.RecalculateBounds();
            s_prisms[key] = m;
            return m;
        }

        // Make every triangle face along its vertex normal (Unity: clockwise = front), whatever order it was built in.
        static void FixWinding(Mesh m)
        {
            var v = m.vertices; var n = m.normals; var t = m.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 face = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(face, n[t[i]]) < 0f) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; }
            }
            m.triangles = t;
        }

        static Material Mat(ref Material m, Color c, float smooth = 0.15f)
        {
            if (m == null)
            {
                m = AirsoftLook.MakeMat(c, null, "VMHand");
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            }
            return m;
        }

        static Material Glove() => Mat(ref s_glove, GloveCol);
        static Material Suede() => Mat(ref s_suede, SuedeCol, 0.05f);
        static Material Cuff() => Mat(ref s_cuff, CuffCol);
        static Material Strap() => Mat(ref s_strap, StrapCol);
        static Material Sleeve() => Mat(ref s_sleeve, SleeveCol, 0.05f);
        static Material MagMat() => Mat(ref s_magMat, new Color(0.13f, 0.13f, 0.14f), 0.3f);
        static Material Black() => Mat(ref s_black, new Color(0.07f, 0.07f, 0.075f), 0.3f);
        static Material Brass() => Mat(ref s_brass, new Color(0.78f, 0.6f, 0.25f), 0.55f);
        static Material ShellRed() => Mat(ref s_shellRed, new Color(0.62f, 0.1f, 0.08f), 0.35f);
    }
}
