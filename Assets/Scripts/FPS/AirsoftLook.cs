using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirsoftFPS
{
    // Character look = which body model + hat + eyewear + colours. Packed into one int so it
    // is trivial to save (PlayerPrefs) and to sync over the network (MatchNet roster).
    public struct LookData
    {
        public int body, hat, hatColor, shirt, pants, skin, hair, eyewear;
    }

    public static class AirsoftLook
    {
        // ───────────── options ─────────────
        public static readonly string[] Bodies =
        {
            "Male_Casual", "Male_LongSleeve", "Male_Shirt", "Male_Suit",
            "Female_Casual", "Female_TankTop", "Female_Alternative", "Female_Dress", "Cowboy"
        };
        public static readonly string[] BodyNames =
        {
            "Casual", "Long Sleeve", "Shirt", "Suit",
            "Casual (F)", "Tank Top (F)", "Jacket (F)", "Dress (F)", "Cowboy"
        };
        public static readonly string[] Hats = { "None", "Cap", "Beanie", "Helmet", "Cowboy Hat", "Top Hat", "Crown" };
        public static readonly string[] Eyewear = { "None", "Goggles", "Shooting Glasses", "Full Face Mask", "Mesh Mask" };

        public static readonly string[] PaletteNames =
            { "Red", "Blue", "Green", "Yellow", "Orange", "Purple", "Black", "White", "Grey", "Olive", "Tan", "Pink" };
        public static readonly Color[] Palette =
        {
            new Color(0.80f, 0.16f, 0.16f), new Color(0.15f, 0.35f, 0.85f), new Color(0.20f, 0.62f, 0.25f),
            new Color(0.95f, 0.80f, 0.20f), new Color(0.95f, 0.50f, 0.12f), new Color(0.55f, 0.25f, 0.75f),
            new Color(0.12f, 0.12f, 0.13f), new Color(0.92f, 0.92f, 0.90f), new Color(0.48f, 0.50f, 0.52f),
            new Color(0.36f, 0.42f, 0.22f), new Color(0.72f, 0.60f, 0.42f), new Color(0.95f, 0.55f, 0.70f)
        };
        public static readonly string[] SkinNames = { "Light", "Fair", "Medium", "Tan", "Brown", "Dark" };
        public static readonly Color[] SkinTones =
        {
            new Color(1.00f, 0.86f, 0.74f), new Color(0.96f, 0.78f, 0.63f), new Color(0.87f, 0.66f, 0.50f),
            new Color(0.76f, 0.54f, 0.38f), new Color(0.56f, 0.38f, 0.25f), new Color(0.36f, 0.24f, 0.16f)
        };
        public static readonly string[] HairNames = { "Black", "Dark Brown", "Brown", "Blonde", "Ginger", "Grey", "Blue", "Pink" };
        public static readonly Color[] HairColors =
        {
            new Color(0.08f, 0.07f, 0.07f), new Color(0.22f, 0.14f, 0.09f), new Color(0.42f, 0.27f, 0.15f),
            new Color(0.90f, 0.76f, 0.42f), new Color(0.75f, 0.33f, 0.12f), new Color(0.65f, 0.65f, 0.65f),
            new Color(0.20f, 0.40f, 0.90f), new Color(0.95f, 0.45f, 0.70f)
        };

        // Team armband colours = the UI kit's team colours.
        public static Color TeamRed => UIKit.Red;
        public static Color TeamBlue => UIKit.Blue;

        // ───────────── pack / unpack ─────────────
        // 4 bits per field, eyewear in bits 28-30. Bit 31 stays clear so a packed look is
        // never negative (-1 = "not chosen"), and looks saved before eyewear existed have
        // zeros there, which decodes as "None".
        public static int Pack(LookData l)
        {
            return (l.body & 15) | ((l.hat & 15) << 4) | ((l.hatColor & 15) << 8) | ((l.shirt & 15) << 12)
                 | ((l.pants & 15) << 16) | ((l.skin & 15) << 20) | ((l.hair & 15) << 24) | ((l.eyewear & 7) << 28);
        }

        public static LookData Unpack(int v)
        {
            var l = new LookData
            {
                body = v & 15, hat = (v >> 4) & 15, hatColor = (v >> 8) & 15, shirt = (v >> 12) & 15,
                pants = (v >> 16) & 15, skin = (v >> 20) & 15, hair = (v >> 24) & 15, eyewear = (v >> 28) & 7
            };
            l.body = Mathf.Clamp(l.body, 0, Bodies.Length - 1);
            l.hat = Mathf.Clamp(l.hat, 0, Hats.Length - 1);
            l.hatColor = Mathf.Clamp(l.hatColor, 0, Palette.Length - 1);
            l.shirt = Mathf.Clamp(l.shirt, 0, Palette.Length - 1);
            l.pants = Mathf.Clamp(l.pants, 0, Palette.Length - 1);
            l.skin = Mathf.Clamp(l.skin, 0, SkinTones.Length - 1);
            l.hair = Mathf.Clamp(l.hair, 0, HairColors.Length - 1);
            l.eyewear = Mathf.Clamp(l.eyewear, 0, Eyewear.Length - 1);
            return l;
        }

        public static int Random(System.Random rng = null)
        {
            rng ??= new System.Random();
            return Pack(new LookData
            {
                body = rng.Next(Bodies.Length), hat = rng.Next(Hats.Length), hatColor = rng.Next(Palette.Length),
                shirt = rng.Next(Palette.Length), pants = rng.Next(Palette.Length),
                skin = rng.Next(SkinTones.Length), hair = rng.Next(HairColors.Length), eyewear = rng.Next(Eyewear.Length)
            });
        }

        // A stable look for players who never opened the customizer.
        public static int DefaultFor(ulong clientId) => Random(new System.Random((int)clientId * 7919 + 17));

        const string PrefKey = "Airsoft.Look";

        public static int LoadLocal()
        {
            int v = PlayerPrefs.GetInt(PrefKey, -1);
            if (v < 0) { v = Random(); SaveLocal(v); }
            return v;
        }

        public static void SaveLocal(int v)
        {
            PlayerPrefs.SetInt(PrefKey, v);
            PlayerPrefs.Save();
        }

        // ───────────── materials ─────────────
        static Material s_base;
        public static Material BaseMaterial
        {
            get
            {
                if (s_base == null) s_base = Resources.Load<Material>("AirsoftLit");
                if (s_base == null)
                {
                    var sh = Shader.Find("Universal Render Pipeline/Lit");
                    if (sh == null) sh = Shader.Find("Standard");
                    s_base = new Material(sh);
                }
                return s_base;
            }
        }

        public static Material MakeMat(Color c, Texture tex = null, string name = "AirsoftMat")
        {
            var m = new Material(BaseMaterial) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (tex != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            }
            return m;
        }

        static Color ColorOf(Material m)
        {
            if (m == null) return Color.white;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.white;
        }

        static Texture TexOf(Material m)
        {
            if (m == null) return null;
            if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) return m.GetTexture("_BaseMap");
            if (m.HasProperty("_MainTex")) return m.GetTexture("_MainTex");
            return null;
        }

        static string Clean(string n) => n == null ? "" : n.Replace(" (Instance)", "").Trim();

        // Rebuild every material as a URP material that keeps its colour/texture
        // (FBX materials can come in with the wrong shader -> pink).  The optional
        // recolor callback returns a new colour for a material name, or null to keep it.
        public static void FixMaterials(GameObject root, System.Func<string, Color?> recolor = null)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    string n = Clean(src[i] ? src[i].name : "");
                    Color c = ColorOf(src[i]);
                    Color? over = recolor?.Invoke(n);
                    var tex = TexOf(src[i]);
                    dst[i] = MakeMat(over ?? c, over.HasValue ? null : tex, n);
                }
                r.sharedMaterials = dst;
            }
        }

        // ───────────── helpers ─────────────
        public static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r) return r;
            }
            return null;
        }

        public static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject, layer);
        }

        public static Color Lighter(Color c, float k) => Color.Lerp(c, Color.white, k);
        public static Color Darker(Color c, float k) => Color.Lerp(c, Color.black, k);

        // ───────────── avatar ─────────────
        public class Avatar
        {
            public GameObject root;
            public Animation anim;
            public string idle, walk, run, death;
            public Transform head, rightHand;
            public Quaternion faceFrame = Quaternion.identity; // head-bone-relative rotation of the face (rest pose)
            public float scale = 1f;
        }

        // Builds the character under `parent` (feet at parent's origin, facing parent's +Z).
        // team: 0 = no armband, 1 = red armbands, 2 = blue armbands (the shirt keeps its colour).
        public static Avatar BuildAvatar(Transform parent, int packedLook, int team = 0, float height = 1.8f)
        {
            var l = Unpack(packedLook);
            var prefab = Resources.Load<GameObject>("AirsoftCharacters/" + Bodies[l.body]);
            if (prefab == null) prefab = Resources.Load<GameObject>("AirsoftCharacters/" + Bodies[0]);
            if (prefab == null) return null;

            var holder = new GameObject("Avatar");
            holder.transform.SetParent(parent, false);
            var inst = Object.Instantiate(prefab, holder.transform, false);
            inst.name = "Model";

            var av = new Avatar { root = holder };
            av.anim = inst.GetComponentInChildren<Animation>();
            bool cowboy = Bodies[l.body] == "Cowboy";

            // Colours
            Color shirt = Palette[l.shirt];
            Color pants = Palette[l.pants];
            Color skin = SkinTones[l.skin];
            Color hair = HairColors[l.hair];
            Color hatC = Palette[l.hatColor];
            FixMaterials(inst, n =>
            {
                switch (n)
                {
                    case "Skin": return skin;
                    case "Hair": case "Hair2": case "HairBase": case "Eyebrows": return hair;
                    case "Shirt": case "Jacket": case "Dress": return shirt;
                    case "Shirt2": case "LightJacket": return Lighter(shirt, 0.35f);
                    case "Pants": return pants;
                    case "HatBrown": return hatC;
                    case "HatLightBrown": return Lighter(hatC, 0.3f);
                }
                return null;
            });

            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.updateWhenOffscreen = true;

            // Cowboy has its own hat mesh: keep it only if "Cowboy Hat" (or nothing else) was chosen.
            if (cowboy)
            {
                var hatMesh = FindDeep(inst.transform, "Hat");
                if (hatMesh && l.hat != 4)
                    foreach (var r in hatMesh.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            }

            // Face orientation relative to the head bone, taken from the rest pose (before the
            // idle clip moves the head): forward is square to the shoulder line. Eyewear is
            // built in this frame so it sits straight on the face.
            {
                var hb = FindDeep(inst.transform, "Head");
                var sl = FindDeep(inst.transform, "UpperArm.L");
                var sr = FindDeep(inst.transform, "UpperArm.R");
                Vector3 up = holder.transform.up;
                Vector3 fwd = sl && sr ? Vector3.ProjectOnPlane(Vector3.Cross(sr.position - sl.position, up), up) : Vector3.zero;
                if (fwd.sqrMagnitude < 1e-8f) fwd = holder.transform.forward;
                if (hb) av.faceFrame = Quaternion.Inverse(hb.rotation) * Quaternion.LookRotation(fwd, up);
            }

            // Animation clips
            if (av.anim != null)
            {
                foreach (AnimationState st in av.anim)
                {
                    string n = st.name;
                    if (n.Contains("Idle") && av.idle == null) av.idle = n;
                    else if (n.EndsWith("_Walk")) av.walk = n;
                    else if (n.EndsWith("_Run")) av.run = n;
                    else if (n.EndsWith("_Death")) av.death = n;
                    else if (cowboy && n.EndsWith("6Step")) av.walk = n;
                }
                if (av.walk == null) av.walk = av.idle;
                if (av.run == null) av.run = av.walk;
                foreach (AnimationState st in av.anim)
                    st.wrapMode = st.name == av.death ? WrapMode.ClampForever : WrapMode.Loop;
                av.anim.playAutomatically = false;
                if (av.idle != null) { av.anim.Play(av.idle); av.anim.Sample(); }
            }

            // Measure (bind/idle pose) and fit to the requested height
            var t = inst.transform;
            Transform headTop = FindDeep(t, "Head_end") ?? FindDeep(t, "HeadTop_end") ?? FindDeep(t, "Head");
            av.head = FindDeep(t, "Head");
            av.rightHand = FindDeep(t, "Palm.R");
            Transform toeL = FindDeep(t, "Foot.L_end") ?? FindDeep(t, "FootIK.L_end");
            Transform ankL = FindDeep(t, "Foot.L") ?? FindDeep(t, "FootIK.L");
            Transform toeR = FindDeep(t, "Foot.R_end") ?? FindDeep(t, "FootIK.R_end");

            float ground = holder.transform.position.y;
            var feet = new List<Transform> { toeL, ankL, toeR };
            float minY = float.MaxValue;
            foreach (var f in feet) if (f) minY = Mathf.Min(minY, f.position.y);
            if (minY == float.MaxValue) minY = ground;
            float top = headTop ? headTop.position.y : minY + height;
            float h = Mathf.Max(0.01f, top - minY);
            float k = height / h;

            // Facing: toes point forward
            if (toeL && ankL)
            {
                Vector3 fwd = toeL.position - ankL.position; fwd.y = 0;
                Vector3 want = holder.transform.forward; want.y = 0;
                if (fwd.sqrMagnitude > 1e-6f && want.sqrMagnitude > 1e-6f)
                {
                    float ang = Vector3.SignedAngle(fwd, want, Vector3.up);
                    t.RotateAround(holder.transform.position, Vector3.up, ang);
                }
            }

            t.localScale *= k;
            av.scale = k;
            // feet on the holder origin
            minY = float.MaxValue;
            foreach (var f in feet) if (f) minY = Mathf.Min(minY, f.position.y);
            if (minY != float.MaxValue)
                t.position += Vector3.up * (holder.transform.position.y - minY + 0.02f * height);
            // centre horizontally over the holder
            if (av.head)
            {
                Vector3 off = av.head.position - holder.transform.position; off.y = 0;
                t.position -= off;
            }

            // Procedural hat (Cowboy body uses its own for option 4)
            if (av.head && l.hat > 0 && !(cowboy && l.hat == 4))
                BuildHat(av, headTop, l.hat, hatC, height);
            if (av.head && l.eyewear > 0)
                BuildEyewear(av, headTop, l.eyewear, height, cowboy);
            if (team == 1 || team == 2)
                BuildArmbands(t, team == 1 ? TeamRed : TeamBlue, ArmRadius[l.body]);

            return av;
        }

        static GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material m, Vector3 euler = default)
        {
            var g = GameObject.CreatePrimitive(type);
            var col = g.GetComponent<Collider>();
            if (col) Object.Destroy(col);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localEulerAngles = euler;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        static void BuildHat(Avatar av, Transform headTop, int hat, Color c, float height)
        {
            // Hat is built in head space, then parented to the head bone so it follows animations.
            var pivot = HeadPivot(av, headTop, height, "Hat", out float u);

            var m = MakeMat(c, null, "Hat");
            var dark = MakeMat(Darker(c, 0.35f), null, "HatDark");
            var gold = MakeMat(new Color(1f, 0.78f, 0.2f), null, "Gold");

            switch (hat)
            {
                case 1: // Cap
                    Prim(PrimitiveType.Sphere, pivot, new Vector3(0, -0.22f * u, 0), new Vector3(1.12f * u, 0.75f * u, 1.18f * u), m);
                    Prim(PrimitiveType.Cube, pivot, new Vector3(0, -0.36f * u, 0.62f * u), new Vector3(0.85f * u, 0.05f * u, 0.55f * u), dark, new Vector3(-8, 0, 0));
                    break;
                case 2: // Beanie
                    Prim(PrimitiveType.Sphere, pivot, new Vector3(0, -0.18f * u, 0), new Vector3(1.16f * u, 0.95f * u, 1.18f * u), m);
                    Prim(PrimitiveType.Cylinder, pivot, new Vector3(0, -0.5f * u, 0), new Vector3(1.2f * u, 0.12f * u, 1.22f * u), dark);
                    Prim(PrimitiveType.Sphere, pivot, new Vector3(0, 0.3f * u, 0), Vector3.one * 0.3f * u, dark);
                    break;
                case 3: // Helmet
                    Prim(PrimitiveType.Sphere, pivot, new Vector3(0, -0.28f * u, 0), new Vector3(1.3f * u, 1.0f * u, 1.36f * u), m);
                    Prim(PrimitiveType.Cylinder, pivot, new Vector3(0, -0.6f * u, 0), new Vector3(1.5f * u, 0.03f * u, 1.55f * u), dark);
                    Prim(PrimitiveType.Cube, pivot, new Vector3(0, -0.08f * u, 0.6f * u), new Vector3(0.3f * u, 0.12f * u, 0.12f * u), dark); // NVG mount
                    break;
                case 4: // Cowboy hat
                    Prim(PrimitiveType.Cylinder, pivot, new Vector3(0, -0.3f * u, 0), new Vector3(2.1f * u, 0.03f * u, 2.1f * u), m);
                    Prim(PrimitiveType.Cylinder, pivot, new Vector3(0, -0.02f * u, 0), new Vector3(1.0f * u, 0.3f * u, 1.0f * u), m);
                    Prim(PrimitiveType.Cylinder, pivot, new Vector3(0, -0.22f * u, 0), new Vector3(1.03f * u, 0.07f * u, 1.03f * u), dark);
                    break;
                case 5: // Top hat
                    Prim(PrimitiveType.Cylinder, pivot, new Vector3(0, -0.25f * u, 0), new Vector3(1.55f * u, 0.025f * u, 1.55f * u), m);
                    Prim(PrimitiveType.Cylinder, pivot, new Vector3(0, 0.25f * u, 0), new Vector3(0.9f * u, 0.5f * u, 0.9f * u), m);
                    Prim(PrimitiveType.Cylinder, pivot, new Vector3(0, -0.12f * u, 0), new Vector3(0.93f * u, 0.08f * u, 0.93f * u), dark);
                    break;
                case 6: // Crown
                    Prim(PrimitiveType.Cylinder, pivot, new Vector3(0, -0.1f * u, 0), new Vector3(0.95f * u, 0.14f * u, 0.95f * u), gold);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI * 2f / 6f;
                        var p = new Vector3(Mathf.Sin(a) * 0.45f * u, 0.12f * u, Mathf.Cos(a) * 0.45f * u);
                        Prim(PrimitiveType.Cube, pivot, p, new Vector3(0.16f * u, 0.22f * u, 0.16f * u), gold, new Vector3(0, a * Mathf.Rad2Deg + 45, 45));
                        Prim(PrimitiveType.Sphere, pivot, p + Vector3.up * 0.16f * u, Vector3.one * 0.1f * u, MakeMat(c, null, "Gem"));
                    }
                    break;
            }
            pivot.SetParent(av.head, true);
        }

        // Head space shared by hats and eyewear: origin = top of head, unit = head height,
        // axes = character root axes (+Z = facing) or, with faceAligned, the face's rest-pose
        // axes on the head bone. Parent the result to the head bone afterwards.
        static Transform HeadPivot(Avatar av, Transform headTop, float height, string name, out float u, bool faceAligned = false)
        {
            Vector3 topPos = headTop ? headTop.position : av.head.position + Vector3.up * 0.25f;
            u = Mathf.Clamp(Vector3.Distance(av.head.position, topPos), 0.12f * height / 1.8f, 0.45f * height / 1.8f);
            var pivot = new GameObject(name).transform;
            pivot.SetParent(av.root.transform, false);
            pivot.position = topPos;
            pivot.rotation = faceAligned ? av.head.rotation * av.faceFrame : av.root.transform.rotation;
            return pivot;
        }

        // Face measurements in face space (origin = top of head, unit = head height u),
        // measured from the Quaternius meshes. Cowboy has a different, smaller head.
        struct Face { public float eyeY, front, halfW, back, chin; }
        static readonly Face HumanFace = new Face { eyeY = -0.53f, front = 0.88f, halfW = 0.54f, back = -0.46f, chin = -1.4f };
        static readonly Face CowboyFace = new Face { eyeY = -0.5f, front = 0.45f, halfW = 0.38f, back = -0.44f, chin = -1.2f };

        static void BuildEyewear(Avatar av, Transform headTop, int kind, float height, bool cowboy)
        {
            var pivot = HeadPivot(av, headTop, height, "Eyewear", out float u, true);
            var f = cowboy ? CowboyFace : HumanFace;
            float Y = f.eyeY, F = f.front, W = f.halfW, cz = (f.front + f.back) * 0.5f, rz = (f.front - f.back) * 0.5f;
            var black = MakeMat(new Color(0.09f, 0.09f, 0.10f), null, "EyewearBlack");
            var strap = MakeMat(new Color(0.16f, 0.17f, 0.15f), null, "EyewearStrap");
            Vector3 P(float x, float y, float z) => new Vector3(x * u, y * u, z * u);
            void Strap(float y) => Prim(PrimitiveType.Cylinder, pivot, P(0, y, cz), P(2.24f * W, 0.07f, 2.1f * rz), strap);
            // z of the front of an ellipsoid shell (centre c, half-size h) at offset (dx, dy)
            float ShellZ(Vector3 c, Vector3 h, float dx, float dy)
            {
                float k = 1f - (dx * dx) / (h.x * h.x) - (dy * dy) / (h.y * h.y);
                return c.z + h.z * Mathf.Sqrt(Mathf.Max(0f, k));
            }

            switch (kind)
            {
                case 1: // Goggles: chunky frame, amber lens, elastic strap round the head
                {
                    var lens = MakeMat(new Color(0.98f, 0.62f, 0.16f), null, "LensAmber");
                    Strap(Y);
                    Prim(PrimitiveType.Cube, pivot, P(0, Y, F - 0.07f), P(2f * W * 0.98f, 0.34f, 0.2f), black);
                    Prim(PrimitiveType.Cube, pivot, P(0, Y, F + 0.03f), P(2f * W * 0.84f, 0.24f, 0.04f), lens);
                    break;
                }
                case 2: // Shooting glasses: wrap-around lens on the nose bridge, top bar, temple arms
                {
                    var lens = MakeMat(new Color(0.18f, 0.30f, 0.42f), null, "LensSmoke");
                    float z = F - 0.03f;
                    Prim(PrimitiveType.Cube, pivot, P(0, Y, z), P(2f * W * 0.66f, 0.22f, 0.03f), lens);
                    Prim(PrimitiveType.Cube, pivot, P(0, Y + 0.12f, z), P(2f * W * 0.7f, 0.045f, 0.05f), black);
                    foreach (float sx in new[] { -1f, 1f })
                    {
                        Prim(PrimitiveType.Cube, pivot, P(sx * W * 0.82f, Y, z - 0.1f), P(0.26f, 0.2f, 0.03f), lens, new Vector3(0, -sx * 50f, 0));
                        float z0 = z - 0.2f, z1 = cz - 0.15f;
                        Prim(PrimitiveType.Cube, pivot, P(sx * (W + 0.02f), Y + 0.07f, (z0 + z1) * 0.5f), P(0.035f, 0.045f, z0 - z1), black);
                    }
                    break;
                }
                case 3: // Full face mask: one shell over the whole face, dark lens, mouth vents
                {
                    var shell = MakeMat(new Color(0.30f, 0.34f, 0.20f), null, "MaskShell");
                    var lens = MakeMat(new Color(0.12f, 0.14f, 0.16f), null, "LensDark");
                    float top = Y + 0.2f;
                    var c = new Vector3(0, (top + f.chin) * 0.5f, F - 0.32f);
                    var h = new Vector3(W * 1.02f, (top - f.chin) * 0.5f, 0.42f);
                    Strap(Y + 0.02f);
                    Prim(PrimitiveType.Sphere, pivot, P(c.x, c.y, c.z), P(2f * h.x, 2f * h.y, 2f * h.z), shell);
                    Prim(PrimitiveType.Cube, pivot, P(0, Y + 0.02f, ShellZ(c, h, 0, Y + 0.02f - c.y)), P(2f * W * 0.78f, 0.24f, 0.08f), lens);
                    for (int i = 0; i < 3; i++)
                    {
                        float y = c.y - 0.12f - i * 0.07f;
                        Prim(PrimitiveType.Cube, pivot, P(0, y, ShellZ(c, h, 0, y - c.y)), P(0.36f - i * 0.06f, 0.03f, 0.05f), black);
                    }
                    break;
                }
                case 4: // Mesh lower-face mask: covers nose, mouth and chin, grille in front
                {
                    var grille = MakeMat(new Color(0.42f, 0.44f, 0.42f), null, "MaskMesh");
                    float top = Y - 0.08f;
                    var c = new Vector3(0, (top + f.chin) * 0.5f, F - 0.32f);
                    var h = new Vector3(W * 1.08f, (top - f.chin) * 0.5f, 0.46f);
                    Strap(c.y + 0.08f);
                    Prim(PrimitiveType.Sphere, pivot, P(c.x, c.y, c.z), P(2f * h.x, 2f * h.y, 2f * h.z), black);
                    for (int i = -1; i <= 1; i++)
                    {
                        float y = c.y + i * h.y * 0.45f;
                        Prim(PrimitiveType.Cube, pivot, P(0, y, ShellZ(c, h, 0, y - c.y) - 0.03f), P(2f * h.x * 0.5f * (1f - Mathf.Abs(i) * 0.2f), 0.025f, 0.06f), grille);
                    }
                    for (int i = -2; i <= 2; i++)
                    {
                        float x = i * h.x * 0.2f;
                        Prim(PrimitiveType.Cube, pivot, P(x, c.y, ShellZ(c, h, x, 0) - 0.01f), P(0.025f, 2f * h.y * 0.75f, 0.04f), grille, new Vector3(0, Mathf.Atan2(x, h.x) * Mathf.Rad2Deg * 0.8f, 0));
                    }
                    break;
                }
            }
            pivot.SetParent(av.head, true);
        }

        // A band round each upper arm in the team colour, parented to the arm bones so it
        // follows the animation. Built in bone space: the band's axis runs from the
        // upper-arm bone to the forearm bone and its size is relative to the bone length.
        // ArmRadius (per body, same order as Bodies) = upper-arm thickness incl. sleeve,
        // measured from the meshes, plus a little so the band sits on top of the sleeve.
        const float ArmbandAt = 0.4f, ArmbandWidth = 0.28f;
        static readonly float[] ArmRadius = { 0.31f, 0.31f, 0.31f, 0.31f, 0.28f, 0.28f, 0.28f, 0.28f, 0.21f };

        static void BuildArmbands(Transform model, Color c, float radius)
        {
            var m = MakeMat(c, null, "Armband");
            var edge = MakeMat(Darker(c, 0.45f), null, "ArmbandEdge");
            foreach (string side in new[] { "L", "R" })
            {
                var upper = FindDeep(model, "UpperArm." + side);
                if (upper == null) continue;
                var lower = FindDeep(model, "LowerArm." + side);
                Vector3 axis = lower && lower.parent == upper ? lower.localPosition : Vector3.up;
                float len = axis.magnitude;
                if (len < 1e-5f) continue;
                axis /= len;
                var rot = Quaternion.FromToRotation(Vector3.up, axis);
                float d = 2f * radius * len, w = ArmbandWidth * len;

                var band = new GameObject("Armband." + side).transform;
                band.SetParent(upper, false);
                band.localPosition = axis * (ArmbandAt * len);
                band.localRotation = rot;
                // cylinder primitive: height 2, diameter 1
                Prim(PrimitiveType.Cylinder, band, Vector3.zero, new Vector3(d, w * 0.5f, d), m);
                foreach (float e in new[] { -0.5f, 0.5f })
                    Prim(PrimitiveType.Cylinder, band, new Vector3(0, e * w, 0), new Vector3(d * 1.03f, w * 0.06f, d * 1.03f), edge);
            }
        }

        // Plays idle/walk/run by horizontal speed (m/s); call every frame.
        public static void Animate(Avatar av, float speed, bool dead)
        {
            if (av == null || av.anim == null) return;
            string want = dead ? av.death : speed > 6.5f ? av.run : speed > 0.4f ? av.walk : av.idle;
            if (string.IsNullOrEmpty(want)) return;
            if (!av.anim.IsPlaying(want)) av.anim.CrossFade(want, dead ? 0.1f : 0.2f);
        }

        public static void SetShadowsOnly(GameObject go, bool shadowsOnly)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = shadowsOnly ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }
    }
}
