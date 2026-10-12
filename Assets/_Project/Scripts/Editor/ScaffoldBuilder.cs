using System.IO;
using Blobs.Cameras;
using Blobs.Players;
using Blobs.Research;
using Blobs.Research.Minigames;
using Blobs.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Blobs.Editor
{
    /// <summary>
    /// Generates the prototype's scenes, prefabs and materials from code (Tools > Blobs > Build Scaffold).
    /// Re-running it overwrites the generated assets, so once you start hand-editing a scene or prefab,
    /// stop using this for that asset. It doubles as readable documentation of how everything is wired.
    /// </summary>
    public static class ScaffoldBuilder
    {
        const string Root = "Assets/_Project";
        const string MaterialsDir = Root + "/Art/Materials";
        const string PlayerPrefabsDir = Root + "/Prefabs/Players";
        const string WorldPrefabsDir = Root + "/Prefabs/World";
        const string ScenesDir = Root + "/Scenes";
        const string InputActionsPath = Root + "/Input/BlobsInput.inputactions";

        const string GameScenePath = ScenesDir + "/Game.unity";
        const string ResearchScenePath = ScenesDir + "/Research.unity";

        [MenuItem("Tools/Blobs/Build Scaffold")]
        public static void BuildAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var materials = CreateMaterials();
            var player = CreatePlayerPrefab(materials);
            var terminal = CreateTerminalPrefab(materials);
            CreateResearchScene();
            CreateGameScene(materials, player, terminal);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(GameScenePath, true),
                new EditorBuildSettingsScene(ResearchScenePath, true),
            };

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(GameScenePath);
            Debug.Log("Blobs scaffold built. Open Scenes/Game and press Play, then press a button on each controller to join.");
        }

        sealed class Materials
        {
            public Material Ground;
            public Material Rock;
            public Material Player;
            public Material Visor;
            public Material Terminal;
            public Material Screen;
            public Material SplitScreenComposite;
        }

        static Materials CreateMaterials()
        {
            EnsureFolder(MaterialsDir);
            return new Materials
            {
                Ground = CreateLit("Ground", new Color(0.18f, 0.2f, 0.28f)),
                Rock = CreateLit("Rock", new Color(0.45f, 0.38f, 0.55f)),
                Player = CreateLit("Player", Color.white),
                Visor = CreateLit("Visor", new Color(0.08f, 0.08f, 0.12f), smoothness: 0.9f),
                Terminal = CreateLit("Terminal", new Color(0.7f, 0.72f, 0.78f)),
                Screen = CreateLit("Terminal Screen", Color.white),
                SplitScreenComposite = CreateMaterial("Split Screen Composite", Shader.Find("Blobs/SplitScreenComposite")),
            };
        }

        static Material CreateLit(string materialName, Color color, float smoothness = 0.3f)
        {
            var material = CreateMaterial(materialName, Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material CreateMaterial(string materialName, Shader shader)
        {
            if (shader == null)
                throw new System.InvalidOperationException($"Shader for material '{materialName}' not found");

            var path = $"{MaterialsDir}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Dynamic split-screen: one view camera per player slot rendering to a texture, and a composite camera that
        /// draws the Voronoi split of those textures to the screen. See SplitScreenDirector.
        /// </summary>
        static void BuildCameras(CoopSession coopSession, Material compositeMaterial)
        {
            var rig = new GameObject("Camera Director");
            var idleFocus = new GameObject("Idle Focus").transform;
            idleFocus.SetParent(rig.transform, false);

            var director = rig.AddComponent<SplitScreenDirector>();
            var viewCameras = new Object[SplitScreenDirector.MaxViews];
            for (var slot = 0; slot < viewCameras.Length; slot++)
            {
                var viewObject = new GameObject($"View Camera P{slot + 1}");
                viewObject.transform.SetParent(rig.transform, false);
                var viewCamera = viewObject.AddComponent<Camera>();
                viewCamera.fieldOfView = 40f;
                viewCamera.depth = -10f + slot;
                viewCamera.enabled = slot == 0; // slot 0 previews the idle framing in edit mode
                if (slot == 0)
                {
                    // Gameplay code uses Camera.main for camera-relative movement; every view shares its angle.
                    viewObject.tag = "MainCamera";
                    viewObject.AddComponent<AudioListener>();
                    var rotation = Quaternion.Euler(50f, 0f, 0f);
                    viewObject.transform.SetPositionAndRotation(idleFocus.position - rotation * Vector3.forward * 22f, rotation);
                }
                viewCameras[slot] = viewCamera;
            }

            // The composite camera sits far below the level and sees only a full-screen quad.
            var compositeObject = new GameObject("Composite Camera");
            compositeObject.transform.SetParent(rig.transform, false);
            compositeObject.transform.position = new Vector3(0f, -10000f, 0f);
            var compositeCamera = compositeObject.AddComponent<Camera>();
            compositeCamera.orthographic = true;
            compositeCamera.orthographicSize = 0.5f;
            compositeCamera.nearClipPlane = 0.01f;
            compositeCamera.farClipPlane = 2f;
            compositeCamera.clearFlags = CameraClearFlags.SolidColor;
            compositeCamera.backgroundColor = Color.black;
            compositeCamera.depth = 0f;      // research panels (depth 10) draw on top
            compositeCamera.enabled = false; // turned on at runtime, once there are views to composite
            var compositeData = compositeCamera.GetUniversalAdditionalCameraData();
            compositeData.renderShadows = false;
            compositeData.renderPostProcessing = false;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Composite Quad";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(compositeObject.transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 1f);
            quad.transform.localScale = new Vector3(16f / 9f, 1f, 1f);
            var quadRenderer = quad.GetComponent<MeshRenderer>();
            quadRenderer.sharedMaterial = compositeMaterial;
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;

            SetReference(director, "_session", coopSession);
            SetReferences(director, "_viewCameras", viewCameras);
            SetReference(director, "_compositeCamera", compositeCamera);
            SetReference(director, "_compositeQuad", quad.transform);
            SetReference(director, "_compositeMaterial", compositeMaterial);
            SetReference(director, "_idleFocus", idleFocus);
        }

        static GameObject CreatePlayerPrefab(Materials materials)
        {
            EnsureFolder(PlayerPrefabsDir);
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (actions == null)
                throw new FileNotFoundException("Input actions asset missing", InputActionsPath);

            // Root at the feet so spawn points sit on the ground.
            var root = new GameObject("Player");
            var controller = root.AddComponent<CharacterController>();
            controller.center = new Vector3(0f, 1f, 0f);
            controller.height = 2f;
            controller.radius = 0.5f;

            var body = CreatePrimitive(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 1f, 0f), Vector3.one, materials.Player);
            CreatePrimitive(PrimitiveType.Cube, "Visor", root.transform, new Vector3(0f, 1.45f, 0.4f), new Vector3(0.7f, 0.25f, 0.25f), materials.Visor);

            var input = root.AddComponent<PlayerInput>();
            input.actions = actions;
            input.defaultActionMap = Core.InputNames.PlayerMap;
            input.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
            // Each player keeps the device/scheme they joined with; LocalJoinController handles new devices.
            input.neverAutoSwitchControlSchemes = true;

            var coopPlayer = root.AddComponent<CoopPlayer>();
            SetReferences(coopPlayer, "_tintedRenderers", body.GetComponent<Renderer>());
            root.AddComponent<PlayerMotor>();

            return SavePrefab(root, $"{PlayerPrefabsDir}/Player.prefab");
        }

        static GameObject CreateTerminalPrefab(Materials materials)
        {
            EnsureFolder(WorldPrefabsDir);

            // The trigger volume on the root is what PlayerMotor's interact query finds.
            var root = new GameObject("Research Terminal");
            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 1f, 0f);
            trigger.size = new Vector3(3f, 2f, 3f);

            CreatePrimitive(PrimitiveType.Cube, "Console", root.transform, new Vector3(0f, 0.6f, 0f), new Vector3(1.2f, 1.2f, 0.7f), materials.Terminal, keepCollider: true);
            var screen = CreatePrimitive(PrimitiveType.Cube, "Screen", root.transform, new Vector3(0f, 1.45f, -0.1f), new Vector3(1f, 0.6f, 0.08f), materials.Screen);
            screen.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);

            var terminal = root.AddComponent<ResearchTerminal>();
            SetReference(terminal, "_screen", screen.GetComponent<Renderer>());

            return SavePrefab(root, $"{WorldPrefabsDir}/Research Terminal.prefab");
        }

        static void CreateResearchScene()
        {
            EnsureFolder(ScenesDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("Research Minigame");
            var minigame = root.AddComponent<SignalTraceMinigame>();
            var minigameRoot = root.AddComponent<ResearchMinigameRoot>();

            // No AudioListener: only the 3D game's camera should have one.
            var cameraObject = new GameObject("Panel Camera");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
            var panelCamera = cameraObject.AddComponent<Camera>();
            panelCamera.orthographic = true;
            panelCamera.orthographicSize = 5f;
            panelCamera.clearFlags = CameraClearFlags.SolidColor;
            panelCamera.backgroundColor = new Color(0.05f, 0.06f, 0.1f);
            panelCamera.nearClipPlane = 0.1f;
            panelCamera.farClipPlane = 20f;
            panelCamera.depth = 10f;      // draws over the shared 3D camera
            panelCamera.enabled = false;  // ResearchMinigameRoot enables it once placed in a panel

            SetReference(minigameRoot, "_camera", panelCamera);
            SetReference(minigameRoot, "_minigame", minigame);

            EditorSceneManager.SaveScene(scene, ResearchScenePath);
        }

        static void CreateGameScene(Materials materials, GameObject playerPrefab, GameObject terminalPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            BuildEnvironment(materials);

            // Joining: LocalJoinController spawns players through PlayerInputManager when a gamepad button or
            // either half of the keyboard is pressed.
            var session = new GameObject("Coop Session");
            var inputManager = session.AddComponent<PlayerInputManager>();
            inputManager.joinBehavior = PlayerJoinBehavior.JoinPlayersManually;
            inputManager.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
            inputManager.playerPrefab = playerPrefab;
            var managerSettings = new SerializedObject(inputManager);
            managerSettings.FindProperty("m_MaxPlayerCount").intValue = 4;
            managerSettings.ApplyModifiedPropertiesWithoutUndo();

            var spawnPoints = new Transform[4];
            for (var i = 0; i < spawnPoints.Length; i++)
            {
                var spawn = new GameObject($"Spawn P{i + 1}").transform;
                spawn.SetParent(session.transform, false);
                spawn.position = new Vector3(i % 2 == 0 ? -1.5f : 1.5f, 0.05f, i < 2 ? 1.5f : -1.5f);
                spawnPoints[i] = spawn;
            }

            var coopSession = session.AddComponent<CoopSession>();
            SetReferences(coopSession, "_spawnPoints", spawnPoints);
            session.AddComponent<LocalJoinController>();
            session.AddComponent<JoinPromptHud>();

            BuildCameras(coopSession, materials.SplitScreenComposite);

            // Research: one terminal per player so all four can research at once.
            var research = new GameObject("Research Sessions");
            var sessions = research.AddComponent<ResearchSessionManager>();
            SetReference(sessions, "_coopSession", coopSession);

            var terminalPositions = new[]
            {
                new Vector3(-9f, 0f, 7f), new Vector3(9f, 0f, 7f),
                new Vector3(-9f, 0f, -7f), new Vector3(9f, 0f, -7f),
            };
            for (var i = 0; i < terminalPositions.Length; i++)
            {
                var terminal = (GameObject)PrefabUtility.InstantiatePrefab(terminalPrefab, scene);
                terminal.name = $"Research Terminal {i + 1}";
                terminal.transform.SetParent(research.transform, true);
                terminal.transform.position = terminalPositions[i];
                // Screens face the middle of the arena, where the players start.
                terminal.transform.rotation = Quaternion.LookRotation(-terminalPositions[i]);
                SetReference(terminal.GetComponent<ResearchTerminal>(), "_sessions", sessions);
            }

            EditorSceneManager.SaveScene(scene, GameScenePath);
        }

        static void BuildEnvironment(Materials materials)
        {
            const float platformSize = 160f; // big enough for players to outrun the camera's framing
            const float halfSize = platformSize * 0.5f;
            var environment = new GameObject("Environment").transform;

            // Unity's plane primitive is 10x10 units.
            CreatePrimitive(PrimitiveType.Plane, "Ground", environment, Vector3.zero,
                new Vector3(platformSize / 10f, 1f, platformSize / 10f), materials.Ground, keepCollider: true);

            // Rim so nobody walks off the edge and falls forever.
            var rim = new GameObject("Rim").transform;
            rim.SetParent(environment, false);
            const float rimHeight = 1.5f;
            for (var side = 0; side < 4; side++)
            {
                var alongX = side < 2;
                var sign = side % 2 == 0 ? 1f : -1f;
                var position = alongX ? new Vector3(0f, rimHeight * 0.5f, sign * halfSize) : new Vector3(sign * halfSize, rimHeight * 0.5f, 0f);
                var scale = alongX ? new Vector3(platformSize + 1f, rimHeight, 1f) : new Vector3(1f, rimHeight, platformSize + 1f);
                CreatePrimitive(PrimitiveType.Cube, $"Rim {side + 1}", rim, position, scale, materials.Terminal, keepCollider: true);
            }

            // Marker posts on a 20m grid: on a flat-coloured floor they are what makes distance and speed readable.
            var markers = new GameObject("Distance Markers").transform;
            markers.SetParent(environment, false);
            for (var x = -halfSize + 20f; x < halfSize; x += 20f)
            {
                for (var z = -halfSize + 20f; z < halfSize; z += 20f)
                {
                    if (Mathf.Approximately(x, 0f) && Mathf.Approximately(z, 0f)) continue; // keep the spawn clear
                    CreatePrimitive(PrimitiveType.Cylinder, $"Marker {x},{z}", markers, new Vector3(x, 1f, z),
                        new Vector3(0.4f, 1f, 0.4f), materials.Terminal, keepCollider: true);
                }
            }

            // Scattered "asteroid" rocks to walk around. Fixed seed so every rebuild gives the same layout.
            var rocks = new GameObject("Rocks").transform;
            rocks.SetParent(environment, false);
            var random = new System.Random(42);
            const float clearRadius = 14f; // spawn area and the four research terminals
            for (var placed = 0; placed < 70;)
            {
                var position = new Vector3(
                    Mathf.Lerp(-halfSize + 4f, halfSize - 4f, (float)random.NextDouble()), 0f,
                    Mathf.Lerp(-halfSize + 4f, halfSize - 4f, (float)random.NextDouble()));
                if (new Vector2(position.x, position.z).magnitude < clearRadius) continue;

                var size = Mathf.Lerp(1f, 4.5f, (float)random.NextDouble());
                position.y = size * 0.35f; // partly sunk into the ground
                placed++;
                CreatePrimitive(PrimitiveType.Sphere, $"Rock {placed}", rocks, position, Vector3.one * size, materials.Rock, keepCollider: true);
            }
        }

        static GameObject CreatePrimitive(PrimitiveType type, string objectName, Transform parent, Vector3 localPosition,
            Vector3 localScale, Material material, bool keepCollider = false)
        {
            var primitive = GameObject.CreatePrimitive(type);
            primitive.name = objectName;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localScale = localScale;
            primitive.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider)
                Object.DestroyImmediate(primitive.GetComponent<Collider>());
            return primitive;
        }

        static GameObject SavePrefab(GameObject instance, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
            return prefab;
        }

        static void SetReference(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetReferences(Object target, string field, params Object[] values)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
