using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirsoftFPS
{
    // Character look = which body model + hat + colours. Packed into one int so it is
    // trivial to save (PlayerPrefs) and to sync over the network (MatchNet roster).
    public struct LookData
    {
        public int body, hat, hatColor, shirt, pants, skin, hair;
    }

    public static class ArbesLook
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

        public static readonly Color TeamRed = new Color(0.85f, 0.15f, 0.15f);
        public static readonly Color TeamBlue = new Color(0.15f, 0.35f, 0.90f);

        // ───────────── pack / unpack ─────────────
        public static int Pack(LookData l)
        {
            return (l.body & 15) | ((l.hat & 15) << 4) | ((l.hatColor & 15) << 8) | ((l.shirt & 15) << 12)
                 | ((l.pants & 15) << 16) | ((l.skin & 15) << 20) | ((l.hair & 15) << 24);
        }

        public static LookData Unpack(int v)
        {
            var l = new LookData
            {
                body = v & 15, hat = (v >> 4) & 15, hatColor = (v >> 8) & 15, shirt = (v >> 12) & 15,
                pants = (v >> 16) & 15, skin = (v >> 20) & 15, hair = (v >> 24) & 15
            };
            l.body = Mathf.Clamp(l.body, 0, Bodies.Length - 1);
            l.hat = Mathf.Clamp(l.hat, 0, Hats.Length - 1);
            l.hatColor = Mathf.Clamp(l.hatColor, 0, Palette.Length - 1);
            l.shirt = Mathf.Clamp(l.shirt, 0, Palette.Length - 1);
            l.pants = Mathf.Clamp(l.pants, 0, Palette.Length - 1);
            l.skin = Mathf.Clamp(l.skin, 0, SkinTones.Length - 1);
            l.hair = Mathf.Clamp(l.hair, 0, HairColors.Length - 1);
            return l;
        }

        public static int Random(System.Random rng = null)
        {
            rng ??= new System.Random();
            return Pack(new LookData
            {
                body = rng.Next(Bodies.Length), hat = rng.Next(Hats.Length), hatColor = rng.Next(Palette.Length),
                shirt = rng.Next(Palette.Length), pants = rng.Next(Palette.Length),
                skin = rng.Next(SkinTones.Length), hair = rng.Next(HairColors.Length)
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
                if (s_base == null) s_base = Resources.Load<Material>("ArbesLit");
                if (s_base == null)
                {
                    var sh = Shader.Find("Universal Render Pipeline/Lit");
                    if (sh == null) sh = Shader.Find("Standard");
                    s_base = new Material(sh);
                }
                return s_base;
            }
        }

        public static Material MakeMat(Color c, Texture tex = null, string name = "ArbesMat")
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
            public float scale = 1f;
        }

        // Builds the character under `parent` (feet at parent's origin, facing parent's +Z).
        // team: 0 = use chosen shirt colour, 1 = red shirt, 2 = blue shirt.
        public static Avatar BuildAvatar(Transform parent, int packedLook, int team = 0, float height = 1.8f)
        {
            var l = Unpack(packedLook);
            var prefab = Resources.Load<GameObject>("ArbesCharacters/" + Bodies[l.body]);
            if (prefab == null) prefab = Resources.Load<GameObject>("ArbesCharacters/" + Bodies[0]);
            if (prefab == null) return null;

            var holder = new GameObject("Avatar");
            holder.transform.SetParent(parent, false);
            var inst = Object.Instantiate(prefab, holder.transform, false);
            inst.name = "Model";

            var av = new Avatar { root = holder };
            av.anim = inst.GetComponentInChildren<Animation>();
            bool cowboy = Bodies[l.body] == "Cowboy";

            // Colours
            Color shirt = team == 1 ? TeamRed : team == 2 ? TeamBlue : Palette[l.shirt];
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
            // Hat is built in a "head space": origin = top of head, axes = character root axes,
            // unit = head size.  Then parented to the head bone so it follows animations.
            Vector3 topPos = headTop ? headTop.position : av.head.position + Vector3.up * 0.25f;
            float headLen = Mathf.Clamp(Vector3.Distance(av.head.position, topPos), 0.12f * height / 1.8f, 0.45f * height / 1.8f);
            float u = headLen; // ~ height of the head

            var pivot = new GameObject("Hat").transform;
            pivot.SetParent(av.root.transform, false);
            pivot.position = topPos;
            pivot.rotation = av.root.transform.rotation;

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
