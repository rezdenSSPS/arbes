#if UNITY_EDITOR
using System.Collections.Generic;
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

        [MenuItem("Tools/Airsoft/Rebuild Arena Only")]
        public static void BuildArenaOnly()
        {
            EnsureFolders();
            CreateMaterials();
            BuildArenaScene();
            UpdateBuildSettings();
            EditorSceneManager.OpenScene(ArenaPath);
            SetLobbyAsStartScene();
            Debug.Log("[Airsoft] Arena rebuilt (new low-poly map).");
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
            SaveNetworkHash(PlayerPrefabPath);
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
            SaveNetworkHash(EnemyPrefabPath);
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
            transport.MaxPacketQueueSize = PlayerSpawner.MIN_PACKET_QUEUE;
            nm.NetworkConfig = new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = transport;

            // Network prefabs go in a NetworkPrefabsList asset: NetworkConfig.Prefabs.Add() only fills a
            // runtime list that is NOT saved with the scene, so clients couldn't spawn the player or enemies.
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
            var prefabList = GetNetworkPrefabsList(playerPrefab, enemyPrefab);
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists = new List<NetworkPrefabsList> { prefabList };

            if (playerPrefab != null)
            {
                // No auto-spawn: PlayerSpawner spawns avatars only when the match scene loads
                var playerSpawner = nmObj.AddComponent<PlayerSpawner>();
                playerSpawner.playerPrefab = playerPrefab;
            }

            // EventSystem
            var esObj = new GameObject("EventSystem");
            esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esObj.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            // ─── UI ───
            // NetworkUI builds the menu, connecting and lobby screens at runtime (UIKit).
            var uiObj = new GameObject("NetworkUI");
            uiObj.AddComponent<NetworkUI>();

            EditorSceneManager.SaveScene(scene, LobbyPath);
        }

        // ────────── ARENA SCENE ──────────
        const string NetworkPrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";

        // The project's NetworkPrefabsList asset (created if missing), holding every given prefab.
        static NetworkPrefabsList GetNetworkPrefabsList(params GameObject[] prefabs)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, NetworkPrefabsListPath);
            }
            foreach (var prefab in prefabs)
            {
                if (prefab == null) continue;
                bool listed = false;
                foreach (var np in list.PrefabList)
                    if (np.Prefab == prefab) { listed = true; break; }
                if (!listed) list.Add(new NetworkPrefab { Prefab = prefab });
            }
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            return list;
        }

        static void BuildArenaScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── Environment: low-poly town (ground, roads, buildings, props, sky, post-processing) ──
            AirsoftMapBuilder.BuildEnvironment();

            BuildGameplayObjects(PlayerSpawns, EnemySpawns);

            // ── Objective points (CTF bases, Domination A/B/C) as the map's MapLayout ──
            ObjectiveMapSetup.AddTownLayout();

            EditorSceneManager.SaveScene(scene, ArenaPath);
        }

        // Gameplay objects every match map needs, built after its environment: team spawn points,
        // enemy spawn points, the baked NavMesh, GameManager, EnemySpawner and the HUD.
        // Used by BuildArenaScene and by the other map builders (AirsoftWarehouseBuilder).
        public static NavMeshSurface BuildGameplayObjects((Vector3 pos, int team)[] playerSpawns, Vector3[] enemySpawns)
        {
            // ── Spawn Points ──
            CreatePlayerSpawns(new GameObject("SpawnPoints").transform, playerSpawns);

            // Enemy spawn points
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
            return navSurface;
        }

        // ────────── SPAWN POINTS ──────────
        // Player spawns: Red (team 1) holds the north half, Blue (team 2) the south half. The four old
        // road-end spawns moved off the roads (out of the 80 m sightline) to spots the enemy half can
        // hardly see ("Check Spawn Points"): behind the crate stack, the green container, the small
        // building and under a park tree. AirsoftMapBuilder.Keep uses these.
        public static readonly (Vector3 pos, int team)[] PlayerSpawns =
        {
            (new Vector3(-30, 1, 30), 1), (new Vector3(30, 1, 30), 1), (new Vector3(-10, 1, 30), 1), (new Vector3(-34, 1, 15), 1),
            (new Vector3(30, 1, -30), 2), (new Vector3(-30, 1, -30), 2), (new Vector3(-18, 1, -31), 2), (new Vector3(35, 1, -14), 2),
        };

        // Co-op bot spawns (the old (0,0) crossroads spawn moved to the container yard edge).
        public static readonly Vector3[] EnemySpawns =
        {
            new Vector3(-25, 1, 25), new Vector3(25, 1, -25),
            new Vector3(-25, 1, -10), new Vector3(25, 1, 10),
            new Vector3(-15, 1, 21)
        };

        static void CreatePlayerSpawns(Transform parent) => CreatePlayerSpawns(parent, PlayerSpawns);

        static void CreatePlayerSpawns(Transform parent, (Vector3 pos, int team)[] spawns)
        {
            int n = 0;
            foreach (var (pos, team) in spawns)
            {
                var sp = new GameObject($"SpawnPoint_{(team == 1 ? "Red" : "Blue")}_{n++}");
                sp.transform.SetParent(parent);
                sp.transform.position = pos;
                Vector3 toCentre = -pos; toCentre.y = 0;
                sp.transform.rotation = Quaternion.LookRotation(toCentre);
                sp.tag = "SpawnPoint";
                sp.AddComponent<SpawnPoint>().team = team;
            }
        }

        // Updates only the spawn objects of the existing arena scene (it also holds the new UI and
        // hand-made changes, so it is not rebuilt): player spawns are recreated with their team, and
        // the enemy spawns keep their objects (EnemySpawner references them) but get the new positions.
        [MenuItem("Tools/Airsoft/Update Arena Spawn Points")]
        public static void UpdateArenaSpawnPoints()
        {
            var scene = EditorSceneManager.OpenScene(ArenaPath, OpenSceneMode.Single);
            var parent = GameObject.Find("SpawnPoints");
            if (parent == null) parent = new GameObject("SpawnPoints");
            for (int i = parent.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(parent.transform.GetChild(i).gameObject);
            foreach (var stray in GameObject.FindGameObjectsWithTag("SpawnPoint"))
                Object.DestroyImmediate(stray);
            CreatePlayerSpawns(parent.transform);

            var enemyParent = GameObject.Find("EnemySpawnPoints");
            for (int i = 0; i < EnemySpawns.Length; i++)
            {
                var t = enemyParent ? enemyParent.transform.Find($"EnemySpawn_{i}") : null;
                if (t) t.position = EnemySpawns[i];
                else Debug.LogWarning($"[Airsoft] EnemySpawn_{i} not found in the arena scene");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Airsoft] Arena spawn points updated (" + PlayerSpawns.Length + " player, " + EnemySpawns.Length + " enemy).");
            ReportSpawnPoints();
        }

        // Prints, for each spawn in the open arena: whether a player fits there, how far it is from the
        // road, and how exposed it is (share of a 2 m grid over the arena that can see a standing player
        // there). Old positions are listed for comparison.
        [MenuItem("Tools/Airsoft/Check Spawn Points")]
        public static void ReportSpawnPoints()
        {
            Physics.SyncTransforms();
            var sb = new System.Text.StringBuilder("[Airsoft] Spawn check (exposure = % of arena that sees the spawn)\n");
            void Row(string label, Vector3 p)
            {
                Vector3 g = new Vector3(p.x, 0, p.z);
                bool blocked = Physics.CheckCapsule(g + Vector3.up * 0.45f, g + Vector3.up * 1.55f, 0.4f, ~0, QueryTriggerInteraction.Ignore);
                float road = Mathf.Min(Mathf.Abs(p.x), Mathf.Abs(p.z)) - 6.2f;   // road half-width 4 + sidewalk 2.2
                int seen = 0, total = 0;
                for (float x = -39; x <= 39; x += 2)
                    for (float z = -39; z <= 39; z += 2)
                    {
                        var eye = new Vector3(x, 1.6f, z);
                        if (Physics.CheckSphere(eye, 0.2f, ~0, QueryTriggerInteraction.Ignore)) continue;   // inside a prop
                        total++;
                        var tgt = g + Vector3.up * 1.4f;
                        if (!Physics.Linecast(eye, tgt, ~0, QueryTriggerInteraction.Ignore)) seen++;
                    }
                sb.AppendLine($"  {label,-14} {p.x,6:0.#} {p.z,6:0.#}  fits={(!blocked ? "yes" : "NO ")}  offRoad={road,5:0.0} m  exposure={(100f * seen / Mathf.Max(1, total)),5:0.0}%");
            }
            foreach (var (pos, team) in PlayerSpawns) Row(team == 1 ? "Red" : "Blue", pos);
            for (int i = 0; i < EnemySpawns.Length; i++) Row("Enemy" + i, EnemySpawns[i]);
            foreach (var old in new[] { new Vector3(0, 1, 35), new Vector3(0, 1, -35), new Vector3(-35, 1, 0), new Vector3(35, 1, 0), new Vector3(0, 1, 0) })
                Row("(old)", old);
            Debug.Log(sb.ToString());
        }

        static void BuildHUDCanvas()
        {
            // HUDManager builds the HUD, scoreboard, death, pause and results screens at runtime (UIKit).
            var hudObj = new GameObject("HUD");
            hudObj.AddComponent<HUDManager>();

            // ── EventSystem ──
            var esObj = new GameObject("EventSystem");
            esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esObj.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // A prefab saved from a scene object keeps that object's GlobalObjectIdHash on disk.
        // Netcode recomputes the real hash in OnValidate, but only in memory -- a second
        // process (MPPM virtual player, a build) then reads the stale value and can't spawn
        // the prefab. Reimport so OnValidate runs on the asset, then write the hash back.
        static void SaveNetworkHash(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var no = go != null ? go.GetComponent<NetworkObject>() : null;
            if (no == null) return;
            EditorUtility.SetDirty(no);
            AssetDatabase.SaveAssetIfDirty(go);
        }

        [MenuItem("Tools/Airsoft/Fix Network Prefab IDs")]
        public static void FixNetworkPrefabIds()
        {
            SaveNetworkHash(PlayerPrefabPath);
            SaveNetworkHash(EnemyPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Airsoft] Network prefab IDs written to disk. Restart the MPPM virtual players.");
        }

        // Swaps the old scene-built UI objects for the runtime-built UI without
        // regenerating the map, prefabs or navmesh.
        [MenuItem("Tools/Airsoft/Rebuild UI Only")]
        public static void RebuildUIOnly()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var lobby = EditorSceneManager.OpenScene(LobbyPath);
            foreach (var n in Object.FindObjectsByType<NetworkUI>(FindObjectsInactive.Include))
                Object.DestroyImmediate(n.gameObject);
            new GameObject("NetworkUI").AddComponent<NetworkUI>();
            EditorSceneManager.SaveScene(lobby);

            var arena = EditorSceneManager.OpenScene(ArenaPath);
            foreach (var h in Object.FindObjectsByType<HUDManager>(FindObjectsInactive.Include))
                Object.DestroyImmediate(h.gameObject);
            new GameObject("HUD").AddComponent<HUDManager>();
            EditorSceneManager.SaveScene(arena);

            EditorSceneManager.OpenScene(LobbyPath);
            Debug.Log("[Airsoft] Lobby and arena UI replaced with the runtime-built UI.");
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
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(LobbyPath, true),
                new EditorBuildSettingsScene(ArenaPath, true)
            };
            // keep the other maps already in the list (e.g. AirsoftWarehouse)
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != LobbyPath && s.path != ArenaPath && System.IO.File.Exists(s.path))
                    scenes.Add(s);
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif
