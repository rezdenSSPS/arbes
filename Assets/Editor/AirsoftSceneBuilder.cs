#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using TMPro;

namespace AirsoftFPS.Editor
{
    public static class AirsoftSceneBuilder
    {
        static readonly string LobbyPath = "Assets/Scenes/AirsoftLobby.unity";
        static readonly string ArenaPath = "Assets/Scenes/AirsoftArena.unity";
        static readonly string PlayerPrefabPath = "Assets/Prefabs/FPSPlayer.prefab";
        static readonly string EnemyPrefabPath = "Assets/Prefabs/Enemy.prefab";

        [InitializeOnLoadMethod]
        static void AutoBuild()
        {
            if (!System.IO.File.Exists(LobbyPath) || !System.IO.File.Exists(ArenaPath))
                EditorApplication.delayCall += BuildAll;
            else
                EditorApplication.delayCall += SetLobbyAsStartScene;
        }

        // Makes AirsoftLobby the scene that runs when you press Play,
        // no matter which scene is currently open in the editor.
        static void SetLobbyAsStartScene()
        {
            var lobbyAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(LobbyPath);
            if (lobbyAsset != null)
                EditorSceneManager.playModeStartScene = lobbyAsset;
        }

        [MenuItem("Tools/Airsoft/Rebuild All Scenes")]
        public static void BuildAll()
        {
            EnsureFolders();
            CreateMaterials();
            CreatePlayerPrefab();
            CreateEnemyPrefab();
            BuildLobbyScene();
            BuildArenaScene();
            UpdateBuildSettings();
            EditorSceneManager.OpenScene(LobbyPath);
            SetLobbyAsStartScene();
            Debug.Log("[Airsoft] All scenes and prefabs built. AirsoftLobby set as the Play start scene.");
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
        }

        // ────────── MATERIALS ──────────
        static Material GetOrCreateMat(string name, Color color)
        {
            string path = $"Assets/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = color;
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.color = color;
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        static Material matFloor, matWall, matCrate, matPlayer, matEnemy, matMetal, matGun;

        static void CreateMaterials()
        {
            matFloor = GetOrCreateMat("AirsoftFloor", new Color(0.25f, 0.25f, 0.22f));
            matWall  = GetOrCreateMat("AirsoftWall",  new Color(0.4f, 0.38f, 0.35f));
            matCrate = GetOrCreateMat("AirsoftCrate", new Color(0.55f, 0.42f, 0.25f));
            matPlayer= GetOrCreateMat("AirsoftPlayer",new Color(0.2f, 0.35f, 0.2f));
            matEnemy = GetOrCreateMat("AirsoftEnemy", new Color(0.7f, 0.15f, 0.1f));
            matMetal = GetOrCreateMat("AirsoftMetal", new Color(0.3f, 0.3f, 0.32f));
            matGun   = GetOrCreateMat("AirsoftGun",   new Color(0.15f, 0.15f, 0.15f));
            AssetDatabase.SaveAssets();
        }

        // ────────── PLAYER PREFAB ──────────
        static void CreatePlayerPrefab()
        {
            var root = new GameObject("FPSPlayer");

            // Character controller
            var cc = root.AddComponent<CharacterController>();
            cc.height = 2f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0, 1f, 0);

            // Network
            root.AddComponent<NetworkObject>();
            var netTransform = root.AddComponent<ClientNetworkTransform>(); // owner-authoritative player movement

            // Body (visible to other players)
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform);
            body.transform.localPosition = new Vector3(0, 1, 0);
            body.GetComponent<Renderer>().sharedMaterial = matPlayer;
            Object.DestroyImmediate(body.GetComponent<Collider>());

            // Camera holder
            var camHolder = new GameObject("CameraHolder");
            camHolder.transform.SetParent(root.transform);
            camHolder.transform.localPosition = new Vector3(0, 1.7f, 0);

            // Camera
            var camObj = new GameObject("PlayerCamera");
            camObj.transform.SetParent(camHolder.transform);
            camObj.transform.localPosition = Vector3.zero;
            var cam = camObj.AddComponent<Camera>();
            cam.fieldOfView = 60;
            cam.nearClipPlane = 0.1f;
            camObj.AddComponent<AudioListener>();
            camObj.tag = "MainCamera";

            // Weapon holder (child of camera)
            var weaponHolder = new GameObject("WeaponHolder");
            weaponHolder.transform.SetParent(camObj.transform);
            weaponHolder.transform.localPosition = new Vector3(0.3f, -0.25f, 0.5f);

            // Create 10 weapon meshes
            CreateWeaponMeshes(weaponHolder.transform);

            // Muzzle point
            var muzzle = new GameObject("MuzzlePoint");
            muzzle.transform.SetParent(camObj.transform);
            muzzle.transform.localPosition = new Vector3(0.3f, -0.15f, 1.2f);
            var muzzleLight = muzzle.AddComponent<Light>();
            muzzleLight.type = LightType.Point;
            muzzleLight.color = new Color(1f, 0.8f, 0.3f);
            muzzleLight.intensity = 3f;
            muzzleLight.range = 5f;
            muzzleLight.enabled = false;

            // FPS Controller script
            var fps = root.AddComponent<FPSController>();
            fps.cameraHolder = camHolder.transform;
            fps.playerCamera = cam;

            // Weapon Controller
            var wc = root.AddComponent<WeaponController>();
            wc.weaponHolder = weaponHolder.transform;
            wc.muzzlePoint = muzzle.transform;

            // Health
            var hp = root.AddComponent<Health>();
            hp.maxHealth = 100;
            hp.isPlayer = true;

            // World health bar for other players to see
            CreateWorldHealthBar(root.transform, true);

            // Layer and tag
            root.layer = LayerMask.NameToLayer("Default");

            // Save prefab
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);
        }

        static void CreateWeaponMeshes(Transform parent)
        {
            string[] names = { "M1911","Glock 18","MP5","P90","M4A1","AK-47","SCAR-H","M870","AWP","M249" };
            // Size: length, height, width - representing different gun sizes
            float[][] sizes = {
                new[]{0.25f, 0.15f, 0.06f},  // M1911 - small pistol
                new[]{0.22f, 0.14f, 0.05f},  // Glock - compact pistol
                new[]{0.45f, 0.18f, 0.06f},  // MP5
                new[]{0.50f, 0.20f, 0.07f},  // P90
                new[]{0.65f, 0.18f, 0.06f},  // M4A1
                new[]{0.70f, 0.19f, 0.07f},  // AK-47
                new[]{0.68f, 0.19f, 0.07f},  // SCAR-H
                new[]{0.75f, 0.16f, 0.07f},  // M870
                new[]{0.90f, 0.17f, 0.06f},  // AWP
                new[]{0.80f, 0.20f, 0.08f},  // M249
            };

            // Downloaded FBX model file names (Assets/Models/Weapons/<name>.fbx)
            string[] modelFiles = { "M1911","Glock18","MP5","P90","M4A1","AK47","SCARH","M870","AWP","M249" };
            // First-person placement for downloaded models (tuned after preview)
            Vector3 weaponModelPos = new Vector3(0f, -0.02f, 0.12f);
            Vector3 weaponModelRot = new Vector3(0f, 0f, 0f);
            float weaponModelScale = 0.45f;

            for (int i = 0; i < names.Length; i++)
            {
                // Prefer a downloaded FBX model; fall back to the primitive gun
                var fbxModel = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Models/Weapons/" + modelFiles[i] + ".fbx");
                if (fbxModel != null)
                {
                    var m = (GameObject)PrefabUtility.InstantiatePrefab(fbxModel);
                    m.name = names[i];
                    m.transform.SetParent(parent);
                    m.transform.localPosition = weaponModelPos;
                    m.transform.localEulerAngles = weaponModelRot;
                    m.transform.localScale = Vector3.one * weaponModelScale;
                    foreach (var rend in m.GetComponentsInChildren<Renderer>())
                        rend.sharedMaterial = matGun; // URP material (avoids magenta FBX materials)
                    m.SetActive(i == 0);
                    continue;
                }

                var gun = new GameObject(names[i]);
                gun.transform.SetParent(parent);
                gun.transform.localPosition = Vector3.zero;
                gun.transform.localRotation = Quaternion.identity;

                // Main body
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = "Body";
                body.transform.SetParent(gun.transform);
                body.transform.localPosition = new Vector3(0, 0, sizes[i][0] * 0.3f);
                body.transform.localScale = new Vector3(sizes[i][2], sizes[i][1], sizes[i][0]);
                body.GetComponent<Renderer>().sharedMaterial = matGun;
                Object.DestroyImmediate(body.GetComponent<Collider>());

                // Barrel
                var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                barrel.name = "Barrel";
                barrel.transform.SetParent(gun.transform);
                barrel.transform.localRotation = Quaternion.Euler(90, 0, 0);
                float barrelLen = sizes[i][0] * 0.6f;
                barrel.transform.localPosition = new Vector3(0, 0.01f, sizes[i][0] * 0.6f + barrelLen * 0.5f);
                barrel.transform.localScale = new Vector3(0.025f, barrelLen * 0.5f, 0.025f);
                barrel.GetComponent<Renderer>().sharedMaterial = matMetal;
                Object.DestroyImmediate(barrel.GetComponent<Collider>());

                // Grip (for pistols and rifles)
                var grip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                grip.name = "Grip";
                grip.transform.SetParent(gun.transform);
                grip.transform.localPosition = new Vector3(0, -sizes[i][1] * 0.7f, 0);
                grip.transform.localScale = new Vector3(sizes[i][2] * 0.7f, sizes[i][1] * 0.8f, sizes[i][2]);
                grip.GetComponent<Renderer>().sharedMaterial = matGun;
                Object.DestroyImmediate(grip.GetComponent<Collider>());

                // Magazine (visible rectangle below body)
                if (i != 7) // not shotgun
                {
                    var mag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    mag.name = "Magazine";
                    mag.transform.SetParent(gun.transform);
                    mag.transform.localPosition = new Vector3(0, -sizes[i][1] * 0.4f, sizes[i][0] * 0.15f);
                    float magH = i == 9 ? 0.12f : 0.08f; // M249 has bigger mag
                    mag.transform.localScale = new Vector3(sizes[i][2] * 0.6f, magH, sizes[i][2] * 1.2f);
                    mag.GetComponent<Renderer>().sharedMaterial = matMetal;
                    Object.DestroyImmediate(mag.GetComponent<Collider>());
                }

                // Iron sight on top
                var sight = GameObject.CreatePrimitive(PrimitiveType.Cube);
                sight.name = "Sight";
                sight.transform.SetParent(gun.transform);
                sight.transform.localPosition = new Vector3(0, sizes[i][1] * 0.6f, sizes[i][0] * 0.22f);
                sight.transform.localScale = new Vector3(sizes[i][2] * 0.4f, sizes[i][1] * 0.3f, sizes[i][0] * 0.08f);
                sight.GetComponent<Renderer>().sharedMaterial = matMetal;
                Object.DestroyImmediate(sight.GetComponent<Collider>());

                // Trigger guard
                var guard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                guard.name = "TriggerGuard";
                guard.transform.SetParent(gun.transform);
                guard.transform.localPosition = new Vector3(0, -sizes[i][1] * 0.35f, sizes[i][0] * 0.05f);
                guard.transform.localScale = new Vector3(sizes[i][2] * 0.5f, sizes[i][1] * 0.25f, sizes[i][0] * 0.18f);
                guard.GetComponent<Renderer>().sharedMaterial = matGun;
                Object.DestroyImmediate(guard.GetComponent<Collider>());

                // Stock for long guns
                if (sizes[i][0] > 0.5f)
                {
                    var stock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    stock.name = "Stock";
                    stock.transform.SetParent(gun.transform);
                    stock.transform.localPosition = new Vector3(0, -sizes[i][1] * 0.15f, -sizes[i][0] * 0.3f);
                    stock.transform.localScale = new Vector3(sizes[i][2] * 0.8f, sizes[i][1] * 0.85f, sizes[i][0] * 0.3f);
                    stock.GetComponent<Renderer>().sharedMaterial = matGun;
                    Object.DestroyImmediate(stock.GetComponent<Collider>());
                }

                // Scope for scoped weapons (M4A1, AK-47, SCAR-H, AWP)
                if (i == 4 || i == 5 || i == 6 || i == 8)
                {
                    var scope = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    scope.name = "Scope";
                    scope.transform.SetParent(gun.transform);
                    scope.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    scope.transform.localPosition = new Vector3(0, sizes[i][1] * 0.85f, sizes[i][0] * 0.12f);
                    float scopeLen = (i == 8) ? 0.18f : 0.1f;
                    scope.transform.localScale = new Vector3(0.05f, scopeLen, 0.05f);
                    scope.GetComponent<Renderer>().sharedMaterial = matMetal;
                    Object.DestroyImmediate(scope.GetComponent<Collider>());
                }

                gun.SetActive(i == 0); // only first weapon visible
            }
        }

        static void CreateWorldHealthBar(Transform parent, bool isPlayerBar)
        {
            var canvasObj = new GameObject("HealthBarCanvas");
            canvasObj.transform.SetParent(parent);
            canvasObj.transform.localPosition = new Vector3(0, 2.3f, 0);

            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = canvasObj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1f, 0.15f);
            canvasObj.transform.localScale = Vector3.one * (isPlayerBar ? 0.8f : 1f);

            // Background
            var bgObj = new GameObject("BG");
            bgObj.transform.SetParent(canvasObj.transform);
            var bgRT = bgObj.AddComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
            var bgImg = bgObj.AddComponent<Image>();
            bgImg.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);

            // Slider
            var sliderObj = new GameObject("Slider");
            sliderObj.transform.SetParent(canvasObj.transform);
            var sliderRT = sliderObj.AddComponent<RectTransform>();
            sliderRT.anchorMin = Vector2.zero; sliderRT.anchorMax = Vector2.one;
            sliderRT.offsetMin = new Vector2(2, 2); sliderRT.offsetMax = new Vector2(-2, -2);
            var slider = sliderObj.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.interactable = false;

            // Fill area
            var fillArea = new GameObject("FillArea");
            fillArea.transform.SetParent(sliderObj.transform);
            var fillAreaRT = fillArea.AddComponent<RectTransform>();
            fillAreaRT.anchorMin = Vector2.zero; fillAreaRT.anchorMax = Vector2.one;
            fillAreaRT.offsetMin = Vector2.zero; fillAreaRT.offsetMax = Vector2.zero;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform);
            var fillRT = fill.AddComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero; fillRT.offsetMax = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = Color.green;

            slider.fillRect = fillRT;
            slider.maxValue = 100;
            slider.value = 100;

            // Health bar script
            var whb = canvasObj.AddComponent<WorldHealthBar>();
            whb.slider = slider;
            whb.fillImage = fillImg;
            whb.target = parent;
        }

        // ────────── ENEMY PREFAB ──────────
        static void CreateEnemyPrefab()
        {
            var root = new GameObject("Enemy");
            root.AddComponent<NetworkObject>();
            root.AddComponent<Unity.Netcode.Components.NetworkTransform>();

            // Hit box on the root so raycasts resolve to the NetworkObject
            var cap = root.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0, 1f, 0);
            cap.height = 2f;
            cap.radius = 0.4f;

            // Persisted materials (so the prefab keeps them after reload)
            var bodyMat = matEnemy;
            var skinMat = GetOrCreateMat("AirsoftEnemySkin", new Color(0.82f, 0.62f, 0.48f));
            var gearMat = GetOrCreateMat("AirsoftEnemyGear", new Color(0.16f, 0.18f, 0.14f));
            var eyeMat  = GetOrCreateMat("AirsoftEnemyEye",  new Color(1f, 0.25f, 0.15f));

            // Visual model (toppled on death)
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform);
            model.transform.localPosition = Vector3.zero;

            // Torso
            var torso = GameObject.CreatePrimitive(PrimitiveType.Cube);
            torso.name = "Torso";
            torso.transform.SetParent(model.transform);
            torso.transform.localPosition = new Vector3(0, 1.15f, 0);
            torso.transform.localScale = new Vector3(0.55f, 0.75f, 0.32f);
            torso.GetComponent<Renderer>().sharedMaterial = bodyMat;
            Object.DestroyImmediate(torso.GetComponent<Collider>());

            // Vest
            var vest = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vest.name = "Vest";
            vest.transform.SetParent(model.transform);
            vest.transform.localPosition = new Vector3(0, 1.2f, 0.17f);
            vest.transform.localScale = new Vector3(0.5f, 0.52f, 0.08f);
            vest.GetComponent<Renderer>().sharedMaterial = gearMat;
            Object.DestroyImmediate(vest.GetComponent<Collider>());

            // Head
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(model.transform);
            head.transform.localPosition = new Vector3(0, 1.72f, 0);
            head.transform.localScale = Vector3.one * 0.32f;
            head.GetComponent<Renderer>().sharedMaterial = skinMat;
            Object.DestroyImmediate(head.GetComponent<Collider>());

            // Helmet
            var helmet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            helmet.name = "Helmet";
            helmet.transform.SetParent(model.transform);
            helmet.transform.localPosition = new Vector3(0, 1.79f, 0);
            helmet.transform.localScale = new Vector3(0.4f, 0.28f, 0.4f);
            helmet.GetComponent<Renderer>().sharedMaterial = gearMat;
            Object.DestroyImmediate(helmet.GetComponent<Collider>());

            // Glowing eyes
            foreach (float ex in new[] { -0.09f, 0.09f })
            {
                var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                eye.name = "Eye";
                eye.transform.SetParent(model.transform);
                eye.transform.localPosition = new Vector3(ex, 1.73f, 0.15f);
                eye.transform.localScale = Vector3.one * 0.07f;
                eye.GetComponent<Renderer>().sharedMaterial = eyeMat;
                Object.DestroyImmediate(eye.GetComponent<Collider>());
            }

            // Limb pivot + segment helper
            Transform MakeLimb(string pivotName, Vector3 pivotPos, float len, float thick, Material mat, string segName)
            {
                var pivot = new GameObject(pivotName);
                pivot.transform.SetParent(model.transform);
                pivot.transform.localPosition = pivotPos;
                var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seg.name = segName;
                seg.transform.SetParent(pivot.transform);
                seg.transform.localPosition = new Vector3(0, -len * 0.5f, 0);
                seg.transform.localScale = new Vector3(thick, len, thick);
                seg.GetComponent<Renderer>().sharedMaterial = mat;
                Object.DestroyImmediate(seg.GetComponent<Collider>());
                return pivot.transform;
            }

            var shoulderL = MakeLimb("ShoulderL", new Vector3(-0.34f, 1.45f, 0), 0.6f, 0.14f, bodyMat, "ArmL");
            var shoulderR = MakeLimb("ShoulderR", new Vector3(0.34f, 1.45f, 0), 0.6f, 0.14f, bodyMat, "ArmR");
            var hipL = MakeLimb("HipL", new Vector3(-0.16f, 0.82f, 0), 0.8f, 0.18f, gearMat, "LegL");
            var hipR = MakeLimb("HipR", new Vector3(0.16f, 0.82f, 0), 0.8f, 0.18f, gearMat, "LegR");

            // Rifle held in the right hand
            var gun = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gun.name = "Weapon";
            gun.transform.SetParent(shoulderR);
            gun.transform.localPosition = new Vector3(0.02f, -0.5f, 0.3f);
            gun.transform.localRotation = Quaternion.Euler(75f, 0, 0);
            gun.transform.localScale = new Vector3(0.09f, 0.55f, 0.11f);
            gun.GetComponent<Renderer>().sharedMaterial = matGun;
            Object.DestroyImmediate(gun.GetComponent<Collider>());

            // Muzzle flash light (used by EnemyAI.ShootEffectRpc)
            var muzzle = new GameObject("MuzzleFlash");
            muzzle.transform.SetParent(gun.transform);
            muzzle.transform.localPosition = new Vector3(0, 0.6f, 0);
            var ml = muzzle.AddComponent<Light>();
            ml.type = LightType.Point;
            ml.color = new Color(1f, 0.7f, 0.2f);
            ml.intensity = 2.5f;
            ml.range = 4f;
            ml.enabled = false;

            // NavMeshAgent
            var agent = root.AddComponent<NavMeshAgent>();
            agent.speed = 3.8f;
            agent.angularSpeed = 300;
            agent.acceleration = 12;
            agent.stoppingDistance = 1f;
            agent.radius = 0.4f;
            agent.height = 2f;

            // AI
            root.AddComponent<EnemyAI>();

            // Health
            var hp = root.AddComponent<Health>();
            hp.maxHealth = 80;
            hp.isPlayer = false;

            // Procedural animator (walk cycle + death topple)
            var anim = root.AddComponent<EnemyAnimator>();
            anim.model = model.transform;
            anim.hipL = hipL; anim.hipR = hipR;
            anim.shoulderL = shoulderL; anim.shoulderR = shoulderR;
            anim.health = hp;

            // World health bar
            CreateWorldHealthBar(root.transform, false);

            // Save
            PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath);
            Object.DestroyImmediate(root);
        }

        // ────────── LOBBY SCENE ──────────
        static void BuildLobbyScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Camera
            var camObj = new GameObject("Main Camera");
            camObj.tag = "MainCamera";
            var cam = camObj.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.07f, 0.1f);
            camObj.AddComponent<AudioListener>();

            // Light
            var light = new GameObject("Light");
            var dl = light.AddComponent<Light>();
            dl.type = LightType.Directional;
            dl.color = Color.white;
            dl.intensity = 1;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            // NetworkManager
            var nmObj = new GameObject("NetworkManager");
            var nm = nmObj.AddComponent<NetworkManager>();
            var transport = nmObj.AddComponent<UnityTransport>();
            nm.NetworkConfig = new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = transport;

            // Register player prefab
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab != null)
            {
                nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = playerPrefab });
                // No auto-spawn: PlayerSpawner spawns avatars only when the match scene loads
                var playerSpawner = nmObj.AddComponent<PlayerSpawner>();
                playerSpawner.playerPrefab = playerPrefab;
            }

            // Register enemy prefab
            var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
            if (enemyPrefab != null)
                nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = enemyPrefab });

            // EventSystem
            var esObj = new GameObject("EventSystem");
            esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esObj.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            // ─── CANVAS ───
            var canvasObj = new GameObject("Canvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObj.AddComponent<GraphicRaycaster>();

            // Background panel
            var bg = CreateUIPanel(canvasObj.transform, "BG", Color.clear, true);

            // Title
            var titleObj = CreateText(bg.transform, "Title", "ARBES AIRSOFT", 72,
                new Color(1f, 0.55f, 0.15f), TextAlignmentOptions.Center);
            var titleRT = titleObj.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0.5f, 1); titleRT.anchorMax = new Vector2(0.5f, 1);
            titleRT.anchoredPosition = new Vector2(0, -120);
            titleRT.sizeDelta = new Vector2(800, 100);

            // Subtitle
            var subObj = CreateText(bg.transform, "Sub", "LAN CO-OP  ·  10 WEAPONS  ·  WAVE SURVIVAL", 24,
                new Color(0.7f, 0.7f, 0.7f), TextAlignmentOptions.Center);
            var subRT = subObj.GetComponent<RectTransform>();
            subRT.anchorMin = new Vector2(0.5f, 1); subRT.anchorMax = new Vector2(0.5f, 1);
            subRT.anchoredPosition = new Vector2(0, -200);
            subRT.sizeDelta = new Vector2(800, 40);

            // -- CONNECT PANEL (host/join screen) --
            var connectPanel = CreateUIPanel(bg.transform, "ConnectPanel", Color.clear, true);

            var ipLabel = CreateText(connectPanel.transform, "ConnectLabel", "Join code (Online)   or   Host IP (LAN):", 20,
                new Color(0.75f, 0.75f, 0.75f), TextAlignmentOptions.Center);
            var ipLabelRT = ipLabel.GetComponent<RectTransform>();
            ipLabelRT.anchorMin = new Vector2(0.5f, 0.5f); ipLabelRT.anchorMax = new Vector2(0.5f, 0.5f);
            ipLabelRT.anchoredPosition = new Vector2(0, 210);
            ipLabelRT.sizeDelta = new Vector2(640, 30);

            var ipField = CreateInputField(connectPanel.transform, "ConnectInput", "code or IP");
            var ipRT = ipField.GetComponent<RectTransform>();
            ipRT.anchorMin = new Vector2(0.5f, 0.5f); ipRT.anchorMax = new Vector2(0.5f, 0.5f);
            ipRT.anchoredPosition = new Vector2(0, 165);

            var mainPanel = CreateUIPanel(connectPanel.transform, "MainPanel", Color.clear, false);
            var mainLayout = mainPanel.AddComponent<VerticalLayoutGroup>();
            mainLayout.spacing = 10;
            mainLayout.childAlignment = TextAnchor.MiddleCenter;
            mainLayout.childControlWidth = false; mainLayout.childControlHeight = false;
            var mainRT = mainPanel.GetComponent<RectTransform>();
            mainRT.anchorMin = new Vector2(0.5f, 0.5f); mainRT.anchorMax = new Vector2(0.5f, 0.5f);
            mainRT.anchoredPosition = new Vector2(0, -55);
            mainRT.sizeDelta = new Vector2(460, 380);

            var hostOnlineBtn = CreateButton(mainPanel.transform, "HostOnlineButton", "HOST ONLINE");
            var joinOnlineBtn = CreateButton(mainPanel.transform, "JoinOnlineButton", "JOIN ONLINE");
            var hostLanBtn = CreateButton(mainPanel.transform, "HostLanButton", "HOST LAN");
            var joinLanBtn = CreateButton(mainPanel.transform, "JoinLanButton", "JOIN LAN");
            var quitBtn = CreateButton(mainPanel.transform, "QuitButton", "QUIT");

            var statusObj = CreateText(connectPanel.transform, "StatusText", "", 19,
                new Color(0.85f, 0.72f, 0.4f), TextAlignmentOptions.Center);
            var statusRT = statusObj.GetComponent<RectTransform>();
            statusRT.anchorMin = new Vector2(0.5f, 0); statusRT.anchorMax = new Vector2(0.5f, 0);
            statusRT.anchoredPosition = new Vector2(0, 110);
            statusRT.sizeDelta = new Vector2(1100, 60);

            var hintObj = CreateText(connectPanel.transform, "Hint",
                "WASD Move · Shift Sprint · C Crouch · Space Jump · 1-0 Weapons · R Reload · RMB Aim",
                16, new Color(0.5f, 0.5f, 0.5f), TextAlignmentOptions.Center);
            var hintRT = hintObj.GetComponent<RectTransform>();
            hintRT.anchorMin = new Vector2(0.5f, 0); hintRT.anchorMax = new Vector2(0.5f, 0);
            hintRT.anchoredPosition = new Vector2(0, 50);
            hintRT.sizeDelta = new Vector2(900, 30);

            // -- WAITING ROOM PANEL (hidden until you host/join) --
            var waitingPanel = CreateUIPanel(bg.transform, "WaitingPanel", Color.clear, true);

            var waitTitle = CreateText(waitingPanel.transform, "WaitTitle", "WAITING ROOM", 40,
                new Color(1f, 0.55f, 0.15f), TextAlignmentOptions.Center);
            var waitTitleRT = waitTitle.GetComponent<RectTransform>();
            waitTitleRT.anchorMin = new Vector2(0.5f, 0.5f); waitTitleRT.anchorMax = new Vector2(0.5f, 0.5f);
            waitTitleRT.anchoredPosition = new Vector2(0, 170);
            waitTitleRT.sizeDelta = new Vector2(700, 60);

            var waitCode = CreateText(waitingPanel.transform, "WaitCode", "", 34,
                new Color(0.35f, 0.9f, 0.5f), TextAlignmentOptions.Center);
            var waitCodeRT = waitCode.GetComponent<RectTransform>();
            waitCodeRT.anchorMin = new Vector2(0.5f, 0.5f); waitCodeRT.anchorMax = new Vector2(0.5f, 0.5f);
            waitCodeRT.anchoredPosition = new Vector2(0, 90);
            waitCodeRT.sizeDelta = new Vector2(1000, 60);
            waitCode.AddComponent<ClickToCopy>();

            var waitPlayers = CreateText(waitingPanel.transform, "WaitPlayers", "", 24,
                new Color(0.8f, 0.8f, 0.8f), TextAlignmentOptions.Center);
            var waitPlayersRT = waitPlayers.GetComponent<RectTransform>();
            waitPlayersRT.anchorMin = new Vector2(0.5f, 0.5f); waitPlayersRT.anchorMax = new Vector2(0.5f, 0.5f);
            waitPlayersRT.anchoredPosition = new Vector2(0, 20);
            waitPlayersRT.sizeDelta = new Vector2(700, 40);

            var waitBtnPanel = CreateUIPanel(waitingPanel.transform, "WaitButtons", Color.clear, false);
            var waitLayout = waitBtnPanel.AddComponent<VerticalLayoutGroup>();
            waitLayout.spacing = 12;
            waitLayout.childAlignment = TextAnchor.MiddleCenter;
            waitLayout.childControlWidth = false; waitLayout.childControlHeight = false;
            var waitBtnRT = waitBtnPanel.GetComponent<RectTransform>();
            waitBtnRT.anchorMin = new Vector2(0.5f, 0.5f); waitBtnRT.anchorMax = new Vector2(0.5f, 0.5f);
            waitBtnRT.anchoredPosition = new Vector2(0, -90);
            waitBtnRT.sizeDelta = new Vector2(460, 160);

            var startBtn = CreateButton(waitBtnPanel.transform, "StartButton", "START GAME");
            var leaveBtn = CreateButton(waitBtnPanel.transform, "LeaveButton", "LEAVE");

            waitingPanel.SetActive(false);

            // -- NetworkUI Script --
            var netUI = canvasObj.AddComponent<NetworkUI>();
            netUI.connectPanel = connectPanel;
            netUI.waitingPanel = waitingPanel;
            netUI.hostOnlineButton = hostOnlineBtn.GetComponent<Button>();
            netUI.joinOnlineButton = joinOnlineBtn.GetComponent<Button>();
            netUI.hostLanButton = hostLanBtn.GetComponent<Button>();
            netUI.joinLanButton = joinLanBtn.GetComponent<Button>();
            netUI.quitButton = quitBtn.GetComponent<Button>();
            netUI.codeInput = ipField;
            netUI.statusText = statusObj.GetComponent<TextMeshProUGUI>();
            netUI.waitCodeText = waitCode.GetComponent<TextMeshProUGUI>();
            netUI.waitPlayersText = waitPlayers.GetComponent<TextMeshProUGUI>();
            netUI.startButton = startBtn.GetComponent<Button>();
            netUI.leaveButton = leaveBtn.GetComponent<Button>();

            EditorSceneManager.SaveScene(scene, LobbyPath);
        }

        // ────────── ARENA SCENE ──────────
        static void BuildArenaScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── Lighting ──
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.3f, 0.3f, 0.35f);

            var light = new GameObject("Directional Light");
            var dl = light.AddComponent<Light>();
            dl.type = LightType.Directional;
            dl.color = new Color(1f, 0.95f, 0.85f);
            dl.intensity = 1.2f;
            dl.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(45, -30, 0);

            // ── Floor ──
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.position = new Vector3(0, -0.5f, 0);
            floor.transform.localScale = new Vector3(80, 1, 80);
            floor.GetComponent<Renderer>().sharedMaterial = matFloor;
            floor.isStatic = true;

            // ── Perimeter Walls ──
            CreateWall("WallN", new Vector3(0, 3, 40),   new Vector3(80, 6, 1));
            CreateWall("WallS", new Vector3(0, 3, -40),  new Vector3(80, 6, 1));
            CreateWall("WallE", new Vector3(40, 3, 0),   new Vector3(1, 6, 80));
            CreateWall("WallW", new Vector3(-40, 3, 0),  new Vector3(1, 6, 80));

            // ── Interior Walls (create rooms/corridors) ──
            // Central divider with gap
            CreateWall("DividerL", new Vector3(-12, 2, 0), new Vector3(20, 4, 0.5f));
            CreateWall("DividerR", new Vector3(12, 2, 0),  new Vector3(20, 4, 0.5f));

            // Side rooms
            CreateWall("Room1_W", new Vector3(-25, 2, 15), new Vector3(0.5f, 4, 15));
            CreateWall("Room1_N", new Vector3(-30, 2, 22), new Vector3(10, 4, 0.5f));
            CreateWall("Room2_W", new Vector3(25, 2, -15), new Vector3(0.5f, 4, 15));
            CreateWall("Room2_N", new Vector3(30, 2, -22), new Vector3(10, 4, 0.5f));

            // Corridors
            CreateWall("Corridor1", new Vector3(0, 2, 20), new Vector3(8, 4, 0.5f));
            CreateWall("Corridor2", new Vector3(0, 2, -20), new Vector3(8, 4, 0.5f));

            // L-shaped walls
            CreateWall("L1a", new Vector3(-15, 2, -15), new Vector3(12, 4, 0.5f));
            CreateWall("L1b", new Vector3(-9, 2, -12), new Vector3(0.5f, 4, 6));
            CreateWall("L2a", new Vector3(15, 2, 15), new Vector3(12, 4, 0.5f));
            CreateWall("L2b", new Vector3(9, 2, 12), new Vector3(0.5f, 4, 6));

            // ── Crates / Cover ──
            CreateCrate(new Vector3(-5, 0.75f, 5), new Vector3(1.5f, 1.5f, 1.5f));
            CreateCrate(new Vector3(-5, 2.0f, 5), new Vector3(1.5f, 1.0f, 1.5f));
            CreateCrate(new Vector3(6, 0.75f, -8), new Vector3(2f, 1.5f, 1f));
            CreateCrate(new Vector3(6, 0.75f, -6), new Vector3(2f, 1.5f, 1f));
            CreateCrate(new Vector3(-20, 0.5f, 0), new Vector3(1, 1, 1));
            CreateCrate(new Vector3(-19, 0.5f, 0), new Vector3(1, 1, 1));
            CreateCrate(new Vector3(-19.5f, 1.5f, 0), new Vector3(1, 1, 1));
            CreateCrate(new Vector3(18, 0.75f, 10), new Vector3(2, 1.5f, 2));
            CreateCrate(new Vector3(-10, 0.75f, -25), new Vector3(3, 1.5f, 1));
            CreateCrate(new Vector3(10, 0.75f, 25), new Vector3(3, 1.5f, 1));
            CreateCrate(new Vector3(30, 0.5f, 5), new Vector3(1, 1, 3));
            CreateCrate(new Vector3(-30, 0.5f, -5), new Vector3(1, 1, 3));

            // ── Barrels ──
            for (int i = 0; i < 6; i++)
            {
                var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                barrel.name = $"Barrel_{i}";
                float angle = i * 60 * Mathf.Deg2Rad;
                barrel.transform.position = new Vector3(Mathf.Cos(angle) * 8, 0.75f, Mathf.Sin(angle) * 8);
                barrel.transform.localScale = new Vector3(0.8f, 0.75f, 0.8f);
                barrel.GetComponent<Renderer>().sharedMaterial = matMetal;
                barrel.isStatic = true;
            }

            // ── Spawn Points ──
            Vector3[] spawnPositions = {
                new Vector3(-30, 1, 30),  new Vector3(30, 1, 30),
                new Vector3(-30, 1, -30), new Vector3(30, 1, -30),
                new Vector3(0, 1, 35),    new Vector3(0, 1, -35),
                new Vector3(-35, 1, 0),   new Vector3(35, 1, 0)
            };
            var spawnParent = new GameObject("SpawnPoints");
            foreach (var pos in spawnPositions)
            {
                var sp = new GameObject("SpawnPoint");
                sp.transform.SetParent(spawnParent.transform);
                sp.transform.position = pos;
                sp.tag = "SpawnPoint";
            }

            // Enemy spawn points
            Vector3[] enemySpawns = {
                new Vector3(-25, 1, 25), new Vector3(25, 1, -25),
                new Vector3(-25, 1, -10), new Vector3(25, 1, 10),
                new Vector3(0, 1, 0)
            };
            var enemySpawnParent = new GameObject("EnemySpawnPoints");
            Transform[] enemySpawnTransforms = new Transform[enemySpawns.Length];
            for (int i = 0; i < enemySpawns.Length; i++)
            {
                var esp = new GameObject($"EnemySpawn_{i}");
                esp.transform.SetParent(enemySpawnParent.transform);
                esp.transform.position = enemySpawns[i];
                enemySpawnTransforms[i] = esp.transform;
            }

            // ── NavMesh Surface ──
            var navObj = new GameObject("NavMeshSurface");
            var navSurface = navObj.AddComponent<NavMeshSurface>();
            navSurface.collectObjects = CollectObjects.All;
            navSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            navSurface.BuildNavMesh();

            // ── Game Manager ──
            var gmObj = new GameObject("GameManager");
            gmObj.AddComponent<GameManager>();
            gmObj.AddComponent<NetworkObject>();

            // ── Enemy Spawner ── (plain MonoBehaviour; spawns networked enemies on the host)
            var spawnerObj = new GameObject("EnemySpawner");
            var spawner = spawnerObj.AddComponent<EnemySpawner>();
            var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
            spawner.enemyPrefab = enemyPrefab;
            spawner.spawnPoints = enemySpawnTransforms;
            spawner.navSurface = navSurface;

            // ── HUD Canvas ──
            BuildHUDCanvas();

            EditorSceneManager.SaveScene(scene, ArenaPath);
        }

        static void BuildHUDCanvas()
        {
            var canvasObj = new GameObject("HUDCanvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObj.AddComponent<GraphicRaycaster>();

            // ── Crosshair ──
            var crosshair = new GameObject("Crosshair");
            crosshair.transform.SetParent(canvasObj.transform);
            var chRT = crosshair.AddComponent<RectTransform>();
            chRT.anchorMin = new Vector2(0.5f, 0.5f); chRT.anchorMax = new Vector2(0.5f, 0.5f);
            chRT.anchoredPosition = Vector2.zero;
            chRT.sizeDelta = new Vector2(20, 20);

            // Cross lines
            string[] dirs = { "Top", "Bot", "Left", "Right" };
            Vector2[] positions = { new(0, 8), new(0, -8), new(-8, 0), new(8, 0) };
            Vector2[] sizes = { new(2, 10), new(2, 10), new(10, 2), new(10, 2) };
            for (int i = 0; i < 4; i++)
            {
                var line = new GameObject(dirs[i]);
                line.transform.SetParent(crosshair.transform);
                var lrt = line.AddComponent<RectTransform>();
                lrt.anchorMin = new Vector2(0.5f, 0.5f); lrt.anchorMax = new Vector2(0.5f, 0.5f);
                lrt.anchoredPosition = positions[i];
                lrt.sizeDelta = sizes[i];
                var img = line.AddComponent<Image>();
                img.color = new Color(1, 1, 1, 0.8f);
            }

            // Center dot
            var dot = new GameObject("Dot");
            dot.transform.SetParent(crosshair.transform);
            var dotRT = dot.AddComponent<RectTransform>();
            dotRT.anchorMin = new Vector2(0.5f, 0.5f); dotRT.anchorMax = new Vector2(0.5f, 0.5f);
            dotRT.anchoredPosition = Vector2.zero;
            dotRT.sizeDelta = new Vector2(3, 3);
            dot.AddComponent<Image>().color = Color.white;

            // ── Hit Marker ──
            var hitMarker = new GameObject("HitMarker");
            hitMarker.transform.SetParent(canvasObj.transform);
            var hmRT = hitMarker.AddComponent<RectTransform>();
            hmRT.anchorMin = new Vector2(0.5f, 0.5f); hmRT.anchorMax = new Vector2(0.5f, 0.5f);
            hmRT.anchoredPosition = Vector2.zero;
            hmRT.sizeDelta = new Vector2(30, 30);
            var hmGroup = hitMarker.AddComponent<CanvasGroup>();
            hmGroup.alpha = 0;

            // X shape for hit marker
            for (int i = 0; i < 4; i++)
            {
                var hml = new GameObject($"HM{i}");
                hml.transform.SetParent(hitMarker.transform);
                var hmlRT = hml.AddComponent<RectTransform>();
                hmlRT.anchorMin = new Vector2(0.5f, 0.5f); hmlRT.anchorMax = new Vector2(0.5f, 0.5f);
                float angle = 45 + i * 90;
                float rad = angle * Mathf.Deg2Rad;
                hmlRT.anchoredPosition = new Vector2(Mathf.Cos(rad) * 8, Mathf.Sin(rad) * 8);
                hmlRT.sizeDelta = new Vector2(2, 10);
                hmlRT.localRotation = Quaternion.Euler(0, 0, angle);
                hml.AddComponent<Image>().color = Color.white;
            }

            // ── Health bar (bottom left) ──
            var healthPanel = new GameObject("HealthPanel");
            healthPanel.transform.SetParent(canvasObj.transform);
            var hpRT = healthPanel.AddComponent<RectTransform>();
            hpRT.anchorMin = new Vector2(0, 0); hpRT.anchorMax = new Vector2(0, 0);
            hpRT.pivot = new Vector2(0, 0);
            hpRT.anchoredPosition = new Vector2(30, 30);
            hpRT.sizeDelta = new Vector2(300, 50);

            // Health BG
            var hpBg = new GameObject("HealthBG");
            hpBg.transform.SetParent(healthPanel.transform);
            var hpBgRT = hpBg.AddComponent<RectTransform>();
            hpBgRT.anchorMin = Vector2.zero; hpBgRT.anchorMax = Vector2.one;
            hpBgRT.offsetMin = Vector2.zero; hpBgRT.offsetMax = Vector2.zero;
            hpBg.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.7f);

            // Health Slider
            var hpSliderObj = new GameObject("HealthSlider");
            hpSliderObj.transform.SetParent(healthPanel.transform);
            var hpSliderRT = hpSliderObj.AddComponent<RectTransform>();
            hpSliderRT.anchorMin = Vector2.zero; hpSliderRT.anchorMax = Vector2.one;
            hpSliderRT.offsetMin = new Vector2(4, 4); hpSliderRT.offsetMax = new Vector2(-60, -4);
            var hpSlider = hpSliderObj.AddComponent<Slider>();
            hpSlider.transition = Selectable.Transition.None;
            hpSlider.interactable = false;

            var hpFillArea = new GameObject("FillArea");
            hpFillArea.transform.SetParent(hpSliderObj.transform);
            var hpFART = hpFillArea.AddComponent<RectTransform>();
            hpFART.anchorMin = Vector2.zero; hpFART.anchorMax = Vector2.one;
            hpFART.offsetMin = Vector2.zero; hpFART.offsetMax = Vector2.zero;
            var hpFill = new GameObject("Fill");
            hpFill.transform.SetParent(hpFillArea.transform);
            var hpFillRT = hpFill.AddComponent<RectTransform>();
            hpFillRT.anchorMin = Vector2.zero; hpFillRT.anchorMax = Vector2.one;
            hpFillRT.offsetMin = Vector2.zero; hpFillRT.offsetMax = Vector2.zero;
            var hpFillImg = hpFill.AddComponent<Image>();
            hpFillImg.color = new Color(0.1f, 0.8f, 0.2f);
            hpSlider.fillRect = hpFillRT;
            hpSlider.maxValue = 100;
            hpSlider.value = 100;

            // Health text
            var hpTextObj = CreateText(healthPanel.transform, "HealthText", "100", 24,
                Color.white, TextAlignmentOptions.Right);
            var hpTextRT = hpTextObj.GetComponent<RectTransform>();
            hpTextRT.anchorMin = new Vector2(1, 0); hpTextRT.anchorMax = new Vector2(1, 1);
            hpTextRT.offsetMin = new Vector2(-55, 0); hpTextRT.offsetMax = new Vector2(-5, 0);

            // ── Ammo (bottom right) ──
            var ammoText = CreateText(canvasObj.transform, "AmmoText", "30 / 120", 36,
                Color.white, TextAlignmentOptions.Right);
            var ammoRT = ammoText.GetComponent<RectTransform>();
            ammoRT.anchorMin = new Vector2(1, 0); ammoRT.anchorMax = new Vector2(1, 0);
            ammoRT.pivot = new Vector2(1, 0);
            ammoRT.anchoredPosition = new Vector2(-30, 30);
            ammoRT.sizeDelta = new Vector2(300, 50);

            // Weapon name
            var weaponText = CreateText(canvasObj.transform, "WeaponName", "[1] M1911", 20,
                new Color(0.8f, 0.8f, 0.8f), TextAlignmentOptions.Right);
            var weapRT = weaponText.GetComponent<RectTransform>();
            weapRT.anchorMin = new Vector2(1, 0); weapRT.anchorMax = new Vector2(1, 0);
            weapRT.pivot = new Vector2(1, 0);
            weapRT.anchoredPosition = new Vector2(-30, 80);
            weapRT.sizeDelta = new Vector2(300, 30);

            // ── Wave text (top center) ──
            var waveText = CreateText(canvasObj.transform, "WaveText", "WAVE 1", 32,
                new Color(1f, 0.55f, 0.15f), TextAlignmentOptions.Center);
            var waveRT = waveText.GetComponent<RectTransform>();
            waveRT.anchorMin = new Vector2(0.5f, 1); waveRT.anchorMax = new Vector2(0.5f, 1);
            waveRT.anchoredPosition = new Vector2(0, -30);
            waveRT.sizeDelta = new Vector2(300, 50);

            // Online join code (top-left; only shows when hosting online)
            var codeText = CreateText(canvasObj.transform, "JoinCodeText", "", 26,
                new Color(0.35f, 0.9f, 0.5f), TextAlignmentOptions.Left);
            var codeRT = codeText.GetComponent<RectTransform>();
            codeRT.anchorMin = new Vector2(0, 1); codeRT.anchorMax = new Vector2(0, 1);
            codeRT.pivot = new Vector2(0, 1);
            codeRT.anchoredPosition = new Vector2(30, -24);
            codeRT.sizeDelta = new Vector2(520, 40);
            codeText.AddComponent<ClickToCopy>();

            // ── Death overlay ──
            var deathPanel = new GameObject("DeathOverlay");
            deathPanel.transform.SetParent(canvasObj.transform);
            var dpRT = deathPanel.AddComponent<RectTransform>();
            dpRT.anchorMin = Vector2.zero; dpRT.anchorMax = Vector2.one;
            dpRT.offsetMin = Vector2.zero; dpRT.offsetMax = Vector2.zero;
            deathPanel.AddComponent<Image>().color = new Color(0.5f, 0, 0, 0.5f);
            var deathGroup = deathPanel.AddComponent<CanvasGroup>();
            deathGroup.alpha = 0;
            deathGroup.blocksRaycasts = false;

            var deathText = CreateText(deathPanel.transform, "DeathText", "ELIMINATED\nRespawning...", 48,
                Color.white, TextAlignmentOptions.Center);
            var dtRT = deathText.GetComponent<RectTransform>();
            dtRT.anchorMin = Vector2.zero; dtRT.anchorMax = Vector2.one;
            dtRT.offsetMin = Vector2.zero; dtRT.offsetMax = Vector2.zero;

            // ── Pause Panel ──
            var pausePanel = CreateUIPanel(canvasObj.transform, "PausePanel", new Color(0, 0, 0, 0.8f), true);
            var pauseLayout = pausePanel.AddComponent<VerticalLayoutGroup>();
            pauseLayout.spacing = 16;
            pauseLayout.childAlignment = TextAnchor.MiddleCenter;
            pauseLayout.childControlWidth = false; pauseLayout.childControlHeight = false;

            var pauseTitle = CreateText(pausePanel.transform, "PauseTitle", "PAUSED", 48,
                new Color(1f, 0.55f, 0.15f), TextAlignmentOptions.Center);
            pauseTitle.GetComponent<RectTransform>().sizeDelta = new Vector2(400, 60);

            var resumeBtn = CreateButton(pausePanel.transform, "ResumeBtn", "RESUME");
            var menuBtn = CreateButton(pausePanel.transform, "MenuBtn", "MAIN MENU");
            var quitBtn = CreateButton(pausePanel.transform, "QuitBtn", "QUIT");
            pausePanel.SetActive(false);

            // ── Weapon Selector (bottom center) ──
            string[] shortNames = { "Pistol","Revolv","RayGun","PulseSMG","P90","AK-47","PulseRf","M870","Lightng","AWP" };
            string[] slotDigits = { "1","2","3","4","5","6","7","8","9","0" };
            var selRoot = new GameObject("WeaponSelector");
            selRoot.transform.SetParent(canvasObj.transform);
            var selRT = selRoot.AddComponent<RectTransform>();
            selRT.anchorMin = new Vector2(0.5f, 0); selRT.anchorMax = new Vector2(0.5f, 0);
            selRT.pivot = new Vector2(0.5f, 0);
            selRT.anchoredPosition = new Vector2(0, 42);
            selRT.sizeDelta = new Vector2(10 * 66, 48);

            var slotImages = new Image[10];
            var slotLabels = new TextMeshProUGUI[10];
            for (int i = 0; i < 10; i++)
            {
                var slot = new GameObject($"Slot{i}");
                slot.transform.SetParent(selRoot.transform);
                var sRT = slot.AddComponent<RectTransform>();
                sRT.anchorMin = new Vector2(0.5f, 0.5f); sRT.anchorMax = new Vector2(0.5f, 0.5f);
                sRT.anchoredPosition = new Vector2(-297 + i * 66, 0);
                sRT.sizeDelta = new Vector2(60, 44);
                var sImg = slot.AddComponent<Image>();
                sImg.color = (i == 0) ? new Color(1f, 0.55f, 0.15f, 0.95f) : new Color(0.12f, 0.12f, 0.15f, 0.6f);
                slotImages[i] = sImg;

                var digit = CreateText(slot.transform, "Digit", slotDigits[i], 18,
                    (i == 0) ? Color.white : new Color(0.65f, 0.65f, 0.65f, 0.9f), TextAlignmentOptions.Center);
                var dRT = digit.GetComponent<RectTransform>();
                dRT.anchorMin = new Vector2(0, 0.5f); dRT.anchorMax = new Vector2(1, 1);
                dRT.offsetMin = Vector2.zero; dRT.offsetMax = Vector2.zero;
                slotLabels[i] = digit.GetComponent<TextMeshProUGUI>();

                var nm = CreateText(slot.transform, "Name", shortNames[i], 11,
                    new Color(0.85f, 0.85f, 0.85f, 0.9f), TextAlignmentOptions.Center);
                var nRT = nm.GetComponent<RectTransform>();
                nRT.anchorMin = new Vector2(0, 0); nRT.anchorMax = new Vector2(1, 0.5f);
                nRT.offsetMin = Vector2.zero; nRT.offsetMax = Vector2.zero;
            }

            // ── HUDManager Script ──
            var hud = canvasObj.AddComponent<HUDManager>();
            hud.healthSlider = hpSlider;
            hud.healthFill = hpFillImg;
            hud.healthText = hpTextObj.GetComponent<TextMeshProUGUI>();
            hud.ammoText = ammoText.GetComponent<TextMeshProUGUI>();
            hud.weaponNameText = weaponText.GetComponent<TextMeshProUGUI>();
            hud.hitMarkerGroup = hmGroup;
            hud.deathOverlay = deathGroup;
            hud.deathText = deathText.GetComponent<TextMeshProUGUI>();
            hud.waveText = waveText.GetComponent<TextMeshProUGUI>();
            hud.joinCodeText = codeText.GetComponent<TextMeshProUGUI>();
            hud.pausePanel = pausePanel;
            hud.weaponSlots = slotImages;
            hud.weaponSlotLabels = slotLabels;

            // Wire pause buttons
            resumeBtn.GetComponent<Button>().onClick.AddListener(() => hud.ResumeGame());
            menuBtn.GetComponent<Button>().onClick.AddListener(() => hud.QuitToMenu());
            quitBtn.GetComponent<Button>().onClick.AddListener(() => hud.QuitGame());

            // ── Controls hint ──
            var hint = CreateText(canvasObj.transform, "ControlsHint",
                "WASD Move · Shift Sprint · C Crouch · Space Jump · 1-0 Weapons · R Reload · RMB Aim · Scroll Switch",
                14, new Color(0.6f, 0.6f, 0.6f, 0.6f), TextAlignmentOptions.Center);
            var hintRT = hint.GetComponent<RectTransform>();
            hintRT.anchorMin = new Vector2(0.5f, 0); hintRT.anchorMax = new Vector2(0.5f, 0);
            hintRT.anchoredPosition = new Vector2(0, 10);
            hintRT.sizeDelta = new Vector2(1000, 20);

            // ── EventSystem ──
            var esObj = new GameObject("EventSystem");
            esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esObj.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // ────────── HELPERS ──────────
        static void CreateWall(string name, Vector3 pos, Vector3 scale)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.position = pos;
            wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().sharedMaterial = matWall;
            wall.isStatic = true;
        }

        static void CreateCrate(Vector3 pos, Vector3 scale)
        {
            var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "Crate";
            crate.transform.position = pos;
            crate.transform.localScale = scale;
            crate.GetComponent<Renderer>().sharedMaterial = matCrate;
            crate.isStatic = true;
        }

        static GameObject CreateUIPanel(Transform parent, string name, Color color, bool fullscreen)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent);
            var rt = obj.AddComponent<RectTransform>();
            if (fullscreen)
            {
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            }
            if (color.a > 0)
                obj.AddComponent<Image>().color = color;
            return obj;
        }

        static GameObject CreateText(Transform parent, string name, string text, int fontSize,
            Color color, TextAlignmentOptions align)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent);
            obj.AddComponent<RectTransform>();
            var tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            return obj;
        }

        static TMP_InputField CreateInputField(Transform parent, string name, string placeholder)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent);
            var rt = obj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(420, 54);
            var img = obj.AddComponent<Image>();
            img.color = new Color(0.12f, 0.12f, 0.16f, 0.95f);
            var input = obj.AddComponent<TMP_InputField>();

            var textArea = new GameObject("TextArea");
            textArea.transform.SetParent(obj.transform);
            var taRT = textArea.AddComponent<RectTransform>();
            taRT.anchorMin = Vector2.zero; taRT.anchorMax = Vector2.one;
            taRT.offsetMin = new Vector2(14, 7); taRT.offsetMax = new Vector2(-14, -7);
            textArea.AddComponent<RectMask2D>();

            var ph = CreateText(textArea.transform, "Placeholder", placeholder, 22,
                new Color(0.55f, 0.55f, 0.55f, 0.7f), TextAlignmentOptions.Left);
            var phRT = ph.GetComponent<RectTransform>();
            phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
            phRT.offsetMin = Vector2.zero; phRT.offsetMax = Vector2.zero;

            var txt = CreateText(textArea.transform, "Text", "", 22,
                Color.white, TextAlignmentOptions.Left);
            var txtRT = txt.GetComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = Vector2.zero; txtRT.offsetMax = Vector2.zero;

            input.textViewport = taRT;
            input.textComponent = txt.GetComponent<TextMeshProUGUI>();
            input.placeholder = ph.GetComponent<TextMeshProUGUI>();
            input.text = "";
            return input;
        }

        static GameObject CreateButton(Transform parent, string name, string label)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent);
            var rt = obj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(420, 64);

            var img = obj.AddComponent<Image>();
            img.color = new Color(0.15f, 0.15f, 0.2f);

            var btn = obj.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = new Color(0.15f, 0.15f, 0.2f);
            colors.highlightedColor = new Color(1f, 0.55f, 0.15f);
            colors.pressedColor = new Color(0.9f, 0.45f, 0.1f);
            colors.selectedColor = new Color(1f, 0.55f, 0.15f);
            btn.colors = colors;

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(obj.transform);
            var textRT = textObj.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero; textRT.offsetMax = Vector2.zero;
            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 26;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.font = TMP_Settings.defaultFontAsset;

            return obj;
        }

        static void UpdateBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(LobbyPath, true),
                new EditorBuildSettingsScene(ArenaPath, true)
            };
        }
    }
}
#endif
