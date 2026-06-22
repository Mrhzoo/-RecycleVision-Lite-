using System.Collections.Generic;
using RecycleVision;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;

namespace RecycleVision.Editor
{
    public static class RecycleVisionSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/RecycleVisionMain.unity";
        private const string NewScenePath = "Assets/Scenes/RecycleVisionMain_New.unity";
        private const string GeneratedFolder = "Assets/RecycleVision/Generated";
        private const string MaterialFolder = "Assets/RecycleVision/Generated/FactoryMaterials";

        private static readonly Dictionary<string, Material> FactoryMaterialCache = new Dictionary<string, Material>();

        [MenuItem("Tools/RecycleVision/Build Factory Demo Scene")]
        public static void BuildFactoryDemoSceneMenu() { BuildScene(); }

        [MenuItem("Tools/RecycleVision/Build Demo Scene")]
        public static void BuildDemoScene() { BuildScene(); }

        [MenuItem("Tools/RecycleVision/Build Demo Scene (New - Glass Dashboard)")]
        public static void BuildNewDemoScene() { BuildNewScene(); }

        public static void BuildSceneFromCommandLine() { BuildScene(); }

        // ═══════════════════════════════════════════════════════════════
        // ORIGINAL Build (unchanged)
        // ═══════════════════════════════════════════════════════════════
        public static void BuildScene()
        {
            EnsureFolders();
            FactoryMaterialCache.Clear();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            SetupRenderSettings();

            Material floorMat = CreateMat("Floor", new Color(0.18f, 0.2f, 0.22f), 0.28f, 0.1f, Color.black);
            Material wallMat = CreateMat("Wall", new Color(0.08f, 0.1f, 0.13f), 0.35f, 0.02f, Color.black);
            Material platformMat = CreateMat("Platform", new Color(0.16f, 0.18f, 0.21f), 0.42f, 0.14f, Color.black);
            Material beltMat = CreateMat("Belt", new Color(0.1f, 0.12f, 0.16f), 0.62f, 0.18f, new Color(0.09f, 0.16f, 0.28f) * 0.12f);
            Material panelMat = CreateMat("Panel", new Color(0.09f, 0.12f, 0.17f), 0.55f, 0.12f, new Color(0.11f, 0.18f, 0.31f) * 0.15f);
            Material accentMat = CreateMat("Accent", new Color(0.94f, 0.72f, 0.22f), 0.48f, 0.16f, new Color(0.94f, 0.72f, 0.22f) * 0.22f);
            Material railMat = CreateMat("Rail", new Color(0.42f, 0.48f, 0.55f), 0.72f, 0.62f, Color.black);
            Material glassMat = CreateTransparentMat("GlassAccent", new Color(0.44f, 0.78f, 0.96f, 0.36f), 0.82f, 0.04f);

            Transform root = new GameObject("RecycleVisionFactoryScene").transform;
            Transform env = CreateChild(root, "Environment");
            Transform station = CreateChild(root, "SortingStation");
            Transform systems = CreateChild(root, "Systems");

            CreateLightRig(root);
            CreateIndustrialRoom(env, floorMat, wallMat, panelMat, accentMat, glassMat);
            CreateSideShelves(env, railMat, platformMat);
            CreateFactoryProps(env);
            ConveyorBeltAnimator beltAnimator = CreateStationPlatform(station, platformMat, beltMat, railMat, accentMat, glassMat);

            Transform itemContainer = CreateChild(station, "ItemContainer");
            Transform spawnPoint = CreateChild(station, "SpawnPoint");
            spawnPoint.localPosition = new Vector3(0f, 1.12f, -0.1f);

            SortingBin[] bins = CreateBins(station, font);
            DashboardBoard dash = CreateDashOld(station, font, panelMat, accentMat);
            RecycleVisionHud hud = CreateHud(font);
            CreateEventSystem();
            StationCameraController cam = CreateCameraRig(root);

            SortingStationManager mgr = systems.gameObject.AddComponent<SortingStationManager>();
            SortEffects sortEffects = systems.gameObject.AddComponent<SortEffects>();
            mgr.sortEffects = sortEffects;
            mgr.conveyorBelt = beltAnimator;
            mgr.spawnPoint = spawnPoint;
            mgr.itemContainer = itemContainer;
            mgr.bins = bins;
            mgr.hud = hud;
            mgr.dashboard = dash;
            mgr.stationCamera = cam;
            mgr.trainingItemsPerSession = 24;
            mgr.quickSortItemsPerSession = 24;
            mgr.quickSortDuration = 90f;
            mgr.interItemDelay = 1f;
            mgr.itemDefinitions = WasteItemDefinition.CreateDefaults();
            mgr.robotAssistsInQuickSort = true;

            mgr.baselineAi = systems.gameObject.AddComponent<BaselineAiAdvisor>();
            mgr.logUploader = systems.gameObject.AddComponent<SessionLogUploader>();
            mgr.logUploader.uploadEnabled = true;
            mgr.logUploader.includeItemFeatures = true;

            mgr.sortingAgent = CreateAgentRig(systems, mgr.baselineAi);
            mgr.sortingAgent.manager = mgr;

            mgr.mlAgent = CreateMlAgentRig(systems);
            mgr.mlAgent.manager = mgr;

            mgr.robotArm = CreateRobotArm(station, mgr);

            cam.grabbableMask = ~0;
            Selection.activeObject = mgr.gameObject;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            UpdateBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("RecycleVision demo scene saved: " + ScenePath);
        }

        // ═══════════════════════════════════════════════════════════════
        // NEW Build - with FIXED bins and COMPACT dashboard on glass
        // ═══════════════════════════════════════════════════════════════
        public static void BuildNewScene()
        {
            EnsureFolders();
            FactoryMaterialCache.Clear();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            SetupRenderSettings();

            Material floorMat = CreateMat("Floor", new Color(0.18f, 0.2f, 0.22f), 0.28f, 0.1f, Color.black);
            Material wallMat = CreateMat("Wall", new Color(0.08f, 0.1f, 0.13f), 0.35f, 0.02f, Color.black);
            Material platformMat = CreateMat("Platform", new Color(0.16f, 0.18f, 0.21f), 0.42f, 0.14f, Color.black);
            Material beltMat = CreateMat("Belt", new Color(0.1f, 0.12f, 0.16f), 0.62f, 0.18f, new Color(0.09f, 0.16f, 0.28f) * 0.12f);
            Material panelMat = CreateMat("Panel", new Color(0.09f, 0.12f, 0.17f), 0.55f, 0.12f, new Color(0.11f, 0.18f, 0.31f) * 0.15f);
            Material accentMat = CreateMat("Accent", new Color(0.94f, 0.72f, 0.22f), 0.48f, 0.16f, new Color(0.94f, 0.72f, 0.22f) * 0.22f);
            Material railMat = CreateMat("Rail", new Color(0.42f, 0.48f, 0.55f), 0.72f, 0.62f, Color.black);
            Material glassMat = CreateTransparentMat("GlassAccent", new Color(0.44f, 0.78f, 0.96f, 0.36f), 0.82f, 0.04f);
            Material boardMat = CreateMat("BoardSurface", new Color(0.06f, 0.08f, 0.12f), 0.45f, 0.06f, new Color(0.03f, 0.05f, 0.09f) * 0.2f);

            Transform root = new GameObject("RecycleVisionFactoryScene").transform;
            Transform env = CreateChild(root, "Environment");
            Transform station = CreateChild(root, "SortingStation");
            Transform systems = CreateChild(root, "Systems");

            CreateLightRig(root);
            CreateIndustrialRoom(env, floorMat, wallMat, panelMat, accentMat, glassMat);
            CreateSideShelves(env, railMat, platformMat);
            CreateFactoryProps(env);
            ConveyorBeltAnimator beltAnimator = CreateStationPlatform(station, platformMat, beltMat, railMat, accentMat, glassMat);

            Transform itemContainer = CreateChild(station, "ItemContainer");
            Transform spawnPoint = CreateChild(station, "SpawnPoint");
            spawnPoint.localPosition = new Vector3(0f, 1.12f, -0.1f);

            // ✅ FIX: Create bins with LARGER/LOWER trigger zones
            SortingBin[] bins = CreateFixedBins(station, font);

            // ✅ FIX: Create compact dashboard on glass
            Transform glass = FindChild(env, "ObservationGlass");
            Transform dashParent = glass != null ? glass : station;
            bool onGlass = glass != null;
            DashboardBoard dash = CreateCompactDashboard(dashParent, font, boardMat, accentMat, onGlass);

            RecycleVisionHud hud = CreateHud(font);
            CreateEventSystem();
            StationCameraController cam = CreateCameraRig(root);

            SortingStationManager mgr = systems.gameObject.AddComponent<SortingStationManager>();
            SortEffects sortEffects = systems.gameObject.AddComponent<SortEffects>();
            mgr.sortEffects = sortEffects;
            mgr.conveyorBelt = beltAnimator;
            mgr.spawnPoint = spawnPoint;
            mgr.itemContainer = itemContainer;
            mgr.bins = bins;
            mgr.hud = hud;
            mgr.dashboard = dash;
            mgr.stationCamera = cam;
            mgr.trainingItemsPerSession = 24;
            mgr.quickSortItemsPerSession = 24;
            mgr.quickSortDuration = 90f;
            mgr.interItemDelay = 1f;
            mgr.itemDefinitions = WasteItemDefinition.CreateDefaults();
            mgr.robotAssistsInQuickSort = true;

            mgr.baselineAi = systems.gameObject.AddComponent<BaselineAiAdvisor>();
            mgr.logUploader = systems.gameObject.AddComponent<SessionLogUploader>();
            mgr.logUploader.uploadEnabled = true;
            mgr.logUploader.includeItemFeatures = true;

            mgr.sortingAgent = CreateAgentRig(systems, mgr.baselineAi);
            mgr.sortingAgent.manager = mgr;

            mgr.mlAgent = CreateMlAgentRig(systems);
            mgr.mlAgent.manager = mgr;

            mgr.robotArm = CreateRobotArm(station, mgr);

            cam.grabbableMask = ~0;
            Selection.activeObject = mgr.gameObject;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, NewScenePath);
            UpdateBuildSettings(NewScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("RecycleVision NEW scene saved: " + NewScenePath);
            Debug.Log("Features: Dashboard on glass | Live bars | Fixed bin detection | Compact UI");
        }

        // ─── Helpers ────────────────────────────────────────────────
        private static Transform CreateChild(Transform parent, string name)
        {
            Transform t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        private static Transform FindChild(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform c = parent.GetChild(i);
                if (c.name == name) return c;
                Transform found = FindChild(c, name);
                if (found != null) return found;
            }
            return null;
        }

        private static void SetupRenderSettings()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.22f, 0.25f, 0.3f);
            RenderSettings.ambientEquatorColor = new Color(0.15f, 0.17f, 0.2f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.06f, 0.07f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.008f;
            RenderSettings.fogColor = new Color(0.06f, 0.08f, 0.11f);
        }

        // ═══════════════════════════════════════════════════════════════
        // ✅ FIXED Bins - LARGER trigger zone, positioned LOWER
        // ═══════════════════════════════════════════════════════════════
        private static SortingBin[] CreateFixedBins(Transform parent, Font font)
        {
            Color[] colors = {
                new Color(0.22f, 0.62f, 0.97f),  // Plastic
                new Color(0.9f, 0.72f, 0.24f),   // Paper
                new Color(0.32f, 0.84f, 0.62f),  // Glass
                new Color(0.73f, 0.56f, 0.24f)   // Organic
            };
            Vector3[] positions = {
                new Vector3(-1.8f, 1.04f, 0.62f),
                new Vector3(-0.4f, 1.04f, 0.62f),
                new Vector3(1.0f, 1.04f, 0.62f),
                new Vector3(2.4f, 1.04f, 0.62f)
            };

            SortingBin[] bins = new SortingBin[4];
            for (int i = 0; i < 4; i++)
            {
                RecycleCategory cat = (RecycleCategory)i;
                Material shellMat = CreateMat(cat.ToString(), colors[i], 0.44f, 0.08f, colors[i] * 0.15f);
                Material darkMat = CreateMat(cat + "_Insert", new Color(0.07f, 0.08f, 0.1f), 0.2f, 0f, Color.black);

                GameObject binRoot = new GameObject(cat.ToString());
                binRoot.transform.SetParent(parent, false);
                binRoot.transform.localPosition = positions[i];

                // Shell: slightly deeper and wider
                CreatePrimitive(binRoot.transform, "Shell", PrimitiveType.Cube, new Vector3(0f, 0.24f, 0f), new Vector3(0.76f, 0.48f, 0.76f), shellMat);
                // Cavity (visual indent)
                CreatePrimitive(binRoot.transform, "Cavity", PrimitiveType.Cube, new Vector3(0f, 0.42f, 0f), new Vector3(0.56f, 0.26f, 0.56f), darkMat);
                // Lip
                GameObject lip = CreatePrimitive(binRoot.transform, "Lip", PrimitiveType.Cube, new Vector3(0f, 0.62f, 0.34f), new Vector3(0.80f, 0.02f, 0.22f), shellMat);
                lip.transform.localRotation = Quaternion.Euler(-50f, 0f, 0f);

                // Remove cavity collider
                Object.DestroyImmediate(binRoot.transform.Find("Cavity").GetComponent<BoxCollider>());

                // ✅ FIX: Drop zone moved LOWER and bigger
                // Old: pos(0, 0.46, 0) size(0.56, 0.34, 0.56)
                // New: pos(0, 0.28, 0) size(0.70, 0.50, 0.70) — catches items released lower
                GameObject trigger = new GameObject("DropZone");
                trigger.transform.SetParent(binRoot.transform, false);
                trigger.transform.localPosition = new Vector3(0f, 0.28f, 0f);
                BoxCollider tc = trigger.AddComponent<BoxCollider>();
                tc.isTrigger = true;
                tc.size = new Vector3(0.70f, 0.50f, 0.70f);

                Transform dropAnchor = new GameObject("DropAnchor").transform;
                dropAnchor.SetParent(binRoot.transform, false);
                dropAnchor.localPosition = new Vector3(0f, 0.18f, 0f);

                TextMesh label = CreateTextMesh(binRoot.transform, cat + "Label", FormatLabel(cat), font, 44, TextAlignment.Center);
                label.transform.localPosition = new Vector3(0f, 0.82f, 0f);
                label.characterSize = 0.022f;

                SortingBin sb = binRoot.AddComponent<SortingBin>();
                sb.binType = cat;
                sb.dropAnchor = dropAnchor;
                sb.renderers = new[] { binRoot.transform.Find("Shell").GetComponent<Renderer>(), lip.GetComponent<Renderer>() };
                sb.label = label;
                bins[i] = sb;
            }
            return bins;
        }

        // ═══════════════════════════════════════════════════════════════
        // ✅ COMPACT DASHBOARD - not stretched, clean layout
        // ═══════════════════════════════════════════════════════════════
        private static DashboardBoard CreateCompactDashboard(Transform parent, Font font, Material boardMat, Material accentMat, bool onGlass)
        {
            // More compact dimensions: 2.4w x 1.0h (smaller, better proportions)
            float w = 2.4f;
            float h = 1.0f;

            GameObject root = new GameObject("DashboardBoard");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = onGlass ? new Vector3(0f, 0f, -0.04f) : new Vector3(5.0f, 2.0f, -0.12f);
            root.transform.localRotation = onGlass ? Quaternion.identity : Quaternion.Euler(0f, -18f, 0f);
            root.transform.localScale = onGlass ? Vector3.one * 0.95f : Vector3.one;

            // Board surface — thinner
            CreatePrimitive(root.transform, "BoardSurface", PrimitiveType.Cube, Vector3.zero, new Vector3(w, h, 0.08f), boardMat);
            // Gold trim at top
            CreatePrimitive(root.transform, "BoardTrim", PrimitiveType.Cube,
                new Vector3(0f, h * 0.5f - 0.015f, 0.03f), new Vector3(w + 0.03f, 0.03f, 0.12f), accentMat);

            DashboardBoard dash = root.AddComponent<DashboardBoard>();

            // ── Title (bold, centered top) ──
            float titleY = h * 0.5f - 0.06f;
            dash.titleText = CreateTextMesh(root.transform, "Title", "SORTING", font, 40, TextAlignment.Center);
            dash.titleText.transform.localPosition = new Vector3(0f, titleY, -0.025f);
            dash.titleText.characterSize = 0.035f;

            // ── Mode (slim line below title) ──
            float modeY = titleY - 0.09f;
            dash.modeText = CreateTextMesh(root.transform, "Mode", "Training", font, 24, TextAlignment.Center);
            dash.modeText.transform.localPosition = new Vector3(0f, modeY, -0.025f);
            dash.modeText.characterSize = 0.02f;
            dash.modeText.color = new Color(0.94f, 0.78f, 0.26f);

            // ── Stats column (left, compact) ──
            float lx = -w * 0.5f + 0.12f;
            float sy = modeY - 0.12f;

            dash.humanAccuracyText = CreateTextMesh(root.transform, "Human", "H: n/a", font, 20, TextAlignment.Left);
            dash.humanAccuracyText.anchor = TextAnchor.MiddleLeft;
            dash.humanAccuracyText.transform.localPosition = new Vector3(lx, sy, -0.025f);
            dash.humanAccuracyText.characterSize = 0.017f;

            dash.aiAccuracyText = CreateTextMesh(root.transform, "AI", "AI: 0%", font, 20, TextAlignment.Left);
            dash.aiAccuracyText.anchor = TextAnchor.MiddleLeft;
            dash.aiAccuracyText.transform.localPosition = new Vector3(lx, sy - 0.07f, -0.025f);
            dash.aiAccuracyText.characterSize = 0.017f;

            dash.suggestionText = CreateTextMesh(root.transform, "Suggest", "S: wait", font, 17, TextAlignment.Left);
            dash.suggestionText.anchor = TextAnchor.MiddleLeft;
            dash.suggestionText.transform.localPosition = new Vector3(lx, sy - 0.14f, -0.025f);
            dash.suggestionText.characterSize = 0.014f;

            dash.confusionText = CreateTextMesh(root.transform, "Confuse", "Mix: --", font, 15, TextAlignment.Left);
            dash.confusionText.anchor = TextAnchor.MiddleLeft;
            dash.confusionText.transform.localPosition = new Vector3(lx, sy - 0.20f, -0.025f);
            dash.confusionText.characterSize = 0.012f;

            dash.historyText = CreateTextMesh(root.transform, "History", "H: --", font, 14, TextAlignment.Left);
            dash.historyText.anchor = TextAnchor.MiddleLeft;
            dash.historyText.transform.localPosition = new Vector3(lx, sy - 0.26f, -0.025f);
            dash.historyText.characterSize = 0.011f;

            // ── Bars (right side, wider spacing, clean) ──
            Transform[] bars = new Transform[4];
            TextMesh[] barLabels = new TextMesh[4];
            Color[] barColors = {
                new Color(0.22f, 0.62f, 0.97f),
                new Color(0.9f, 0.72f, 0.24f),
                new Color(0.32f, 0.84f, 0.62f),
                new Color(0.73f, 0.56f, 0.24f)
            };
            string[] shortNames = { "Pls", "Pap", "Gls", "Org" };

            float bx = 0.05f;
            float bSpacing = 0.48f;
            float bBottom = -h * 0.5f + 0.18f;
            float bMaxH = 0.50f;
            float bW = 0.20f;

            for (int i = 0; i < 4; i++)
            {
                float x = bx + i * bSpacing;
                GameObject bar = CreatePrimitive(root.transform, $"Bar{i}",
                    PrimitiveType.Cube,
                    new Vector3(x, bBottom + bMaxH * 0.5f, -0.025f),
                    new Vector3(bW, bMaxH, 0.08f),
                    CreateMat($"Bar_{i}", barColors[i], 0.3f, 0.05f, barColors[i] * 0.08f));
                bars[i] = bar.transform;

                barLabels[i] = CreateTextMesh(root.transform, $"BL{i}", shortNames[i], font, 16, TextAlignment.Center);
                barLabels[i].transform.localPosition = new Vector3(x, bBottom - 0.04f, -0.025f);
                barLabels[i].characterSize = 0.014f;
                barLabels[i].color = new Color(0.9f, 0.92f, 0.96f);
            }

            dash.classBars = bars;
            dash.classBarLabels = barLabels;
            return dash;
        }

        // ─── Shared helpers below ────────────────────────────────────

        private static string FormatLabel(RecycleCategory c)
        {
            string s = c.ToDisplayName();
            if (s.Contains(" / ")) s = s.Replace(" / ", "\n");
            return s;
        }

        private static RobotSorterArmController CreateRobotArm(Transform parent, SortingStationManager m)
        {
            GameObject prefab = LoadPrefab("Assets/UnityFactorySceneHDRP/Scene_Factory/Movable/Prefabs/Arm.prefab");
            GameObject inst;
            if (prefab != null)
            {
                inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.name = "SortingRobotArm";
                inst.transform.SetParent(parent, false);
                inst.transform.localPosition = new Vector3(-1.98f, 0.86f, -0.36f);
                inst.transform.localRotation = Quaternion.Euler(0f, 28f, 0f);
                inst.transform.localScale = Vector3.one * 0.92f;
                StylizeFactoryHierarchy(inst);
            }
            else
            {
                inst = new GameObject("SortingRobotArmFallback");
                inst.transform.SetParent(parent, false);
                inst.transform.localPosition = new Vector3(-1.98f, 0.86f, -0.36f);
            }
            RobotSorterArmController c = inst.AddComponent<RobotSorterArmController>();
            c.manager = m;
            return c;
        }

        private static void CreateIndustrialRoom(Transform p, Material f, Material w, Material pa, Material a, Material g)
        {
            CreatePrimitive(p, "Floor", PrimitiveType.Cube, new Vector3(0f, -0.02f, 0f), new Vector3(14f, 0.08f, 14f), f);
            CreatePrimitive(p, "BackWall", PrimitiveType.Cube, new Vector3(0f, 2.2f, 4.6f), new Vector3(14f, 4.5f, 0.2f), w);
            CreatePrimitive(p, "LeftWall", PrimitiveType.Cube, new Vector3(-6.6f, 2.2f, 0f), new Vector3(0.2f, 4.5f, 14f), w);
            CreatePrimitive(p, "RightWall", PrimitiveType.Cube, new Vector3(6.6f, 2.2f, 0f), new Vector3(0.2f, 4.5f, 14f), w);
            CreatePrimitive(p, "CeilingBeam", PrimitiveType.Cube, new Vector3(0f, 4.26f, 0f), new Vector3(14f, 0.12f, 14f), pa);
            CreatePrimitive(p, "ObservationGlass", PrimitiveType.Cube, new Vector3(0f, 2.15f, 4.48f), new Vector3(3.4f, 1.55f, 0.06f), g);
            CreatePrimitive(p, "SafetyStripe", PrimitiveType.Cube, new Vector3(0f, 0.02f, -0.9f), new Vector3(5.8f, 0.01f, 0.22f), a);
            CreatePrimitive(p, "TitlePanel", PrimitiveType.Cube, new Vector3(0f, 3.58f, 4.32f), new Vector3(4.6f, 0.4f, 0.1f), pa);
            CreateNeonBar(p, new Vector3(-2.8f, 3.65f, 4.25f), new Vector3(1.6f, 0.05f, 0.08f), new Color(0.24f, 0.68f, 0.96f));
            CreateNeonBar(p, new Vector3(2.8f, 3.65f, 4.25f), new Vector3(1.6f, 0.05f, 0.08f), new Color(0.94f, 0.72f, 0.22f));
        }

        private static void CreateSideShelves(Transform p, Material r, Material pl)
        {
            CreateShelf(p, "LeftShelf", new Vector3(-4.7f, 0.82f, 2.3f), r, pl);
            CreateShelf(p, "RightShelf", new Vector3(4.7f, 0.82f, 2.3f), r, pl);
            PlaceDecor(p.Find("LeftShelf"), "Assets/GVOZDY/Cardboard Boxes with Tape/Prefabs/box_2_greyTape.prefab", new Vector3(-0.55f, 0.24f, 0f), Quaternion.identity, Vector3.one * 0.22f);
            PlaceDecor(p.Find("LeftShelf"), "Assets/DanielRiches/BooksEssentials/Prefabs/Book3.prefab", new Vector3(0.52f, 0.22f, 0f), Quaternion.Euler(0f, 90f, 0f), Vector3.one * 0.42f);
            PlaceDecor(p.Find("RightShelf"), "Assets/LowpolyDrinkGlasses/Prefabs/Collection_4.prefab", new Vector3(-0.42f, 0.22f, 0f), Quaternion.identity, Vector3.one * 0.16f);
            PlaceDecor(p.Find("RightShelf"), "Assets/ithappy/Food_FREE/Prefabs/apple_001.prefab", new Vector3(0.45f, 0.2f, 0f), Quaternion.identity, Vector3.one * 0.74f);
        }

        private static Transform CreateShelf(Transform p, string n, Vector3 pos, Material r, Material pl)
        {
            GameObject o = new GameObject(n);
            o.transform.SetParent(p, false);
            o.transform.localPosition = pos;
            CreatePrimitive(o.transform, "Top", PrimitiveType.Cube, new Vector3(0f, 0.64f, 0f), new Vector3(1.65f, 0.08f, 0.45f), pl);
            CreatePrimitive(o.transform, "Bottom", PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(1.65f, 0.08f, 0.45f), pl);
            CreatePrimitive(o.transform, "LeftLeg", PrimitiveType.Cube, new Vector3(-0.78f, 0.3f, 0f), new Vector3(0.06f, 0.64f, 0.06f), r);
            CreatePrimitive(o.transform, "RightLeg", PrimitiveType.Cube, new Vector3(0.78f, 0.3f, 0f), new Vector3(0.06f, 0.64f, 0.06f), r);
            return o.transform;
        }

        private static void CreateFactoryProps(Transform p)
        {
            PlaceProp(p, "Assets/UnityFactorySceneHDRP/Scene_Factory/Background/Prefabs/Controller_1.prefab", "ControllerLeft", new Vector3(-5.2f, 0f, -0.9f), Quaternion.Euler(0f, 65f, 0f), Vector3.one * 0.9f);
            PlaceProp(p, "Assets/UnityFactorySceneHDRP/Scene_Factory/Background/Prefabs/Controller_2.prefab", "ControllerRight", new Vector3(5.18f, 0f, -0.85f), Quaternion.Euler(0f, -65f, 0f), Vector3.one * 0.9f);
            PlaceProp(p, "Assets/UnityFactorySceneHDRP/Scene_Factory/Background/Prefabs/Cart.prefab", "ServiceCart", new Vector3(3.85f, 0f, 1.85f), Quaternion.Euler(0f, -28f, 0f), Vector3.one);
            PlaceProp(p, "Assets/UnityFactorySceneHDRP/Scene_Factory/Background/Prefabs/Line_01.prefab", "BackLine", new Vector3(0f, 0f, 3.25f), Quaternion.Euler(0f, 180f, 0f), Vector3.one * 0.18f);
        }

        private static void PlaceProp(Transform p, string path, string n, Vector3 pos, Quaternion rot, Vector3 s)
        {
            GameObject prefab = LoadPrefab(path);
            if (prefab == null) return;
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.name = n;
            inst.transform.SetParent(p, false);
            inst.transform.localPosition = pos;
            inst.transform.localRotation = rot;
            inst.transform.localScale = s;
            StylizeFactoryHierarchy(inst);
        }

        private static void PlaceDecor(Transform p, string path, Vector3 pos, Quaternion rot, Vector3 s)
        {
            GameObject prefab = LoadPrefab(path);
            if (prefab == null) return;
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.SetParent(p, false);
            inst.transform.localPosition = pos;
            inst.transform.localRotation = rot;
            inst.transform.localScale = s;
            foreach (Rigidbody rb in inst.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (MeshCollider mc in inst.GetComponentsInChildren<MeshCollider>(true)) mc.convex = true;
        }

        private static ConveyorBeltAnimator CreateStationPlatform(Transform p, Material pl, Material be, Material r, Material a, Material g)
        {
            CreatePrimitive(p, "MainDeck", PrimitiveType.Cube, new Vector3(0f, 0.88f, 0f), new Vector3(7.2f, 0.18f, 3.2f), pl);
            GameObject belt = CreatePrimitive(p, "Conveyor", PrimitiveType.Cube, new Vector3(-1.2f, 1.05f, -0.2f), new Vector3(3.2f, 0.06f, 1.05f), be);
            ConveyorBeltAnimator beltAnimator = belt.AddComponent<ConveyorBeltAnimator>();
            beltAnimator.scrollDirection = new Vector2(0f, -1f);
            beltAnimator.scrollSpeed = 0.35f;
            CreatePrimitive(p, "ConveyorFrame", PrimitiveType.Cube, new Vector3(-1.2f, 0.96f, -0.2f), new Vector3(3.36f, 0.08f, 1.18f), r);
            CreatePrimitive(p, "SortDeck", PrimitiveType.Cube, new Vector3(1.55f, 1.02f, 0.48f), new Vector3(3.8f, 0.05f, 2.05f), pl);
            CreatePrimitive(p, "TitleBar", PrimitiveType.Cube, new Vector3(0f, 1.26f, -1.38f), new Vector3(7.2f, 0.03f, 0.08f), a);
            CreatePrimitive(p, "AiScreen", PrimitiveType.Cube, new Vector3(-2.65f, 1.42f, -0.92f), new Vector3(1.0f, 0.5f, 0.06f), g);
            foreach (Vector3 lp in new[] {
                new Vector3(-3.25f, 0.42f, -1.25f), new Vector3(3.25f, 0.42f, -1.25f),
                new Vector3(-3.25f, 0.42f, 1.25f), new Vector3(3.25f, 0.42f, 1.25f) })
                CreatePrimitive(p, $"Leg_{lp.x}", PrimitiveType.Cube, lp, new Vector3(0.16f, 0.84f, 0.16f), r);

            return beltAnimator;
        }

        // OLD bins (kept for reference, used by BuildScene)
        private static SortingBin[] CreateBins(Transform parent, Font font)
        {
            Color[] colors = { new Color(0.22f, 0.62f, 0.97f), new Color(0.9f, 0.72f, 0.24f), new Color(0.32f, 0.84f, 0.62f), new Color(0.73f, 0.56f, 0.24f) };
            Vector3[] pos = { new Vector3(-1.8f, 1.04f, 0.62f), new Vector3(-0.4f, 1.04f, 0.62f), new Vector3(1.0f, 1.04f, 0.62f), new Vector3(2.4f, 1.04f, 0.62f) };
            SortingBin[] bins = new SortingBin[4];
            for (int i = 0; i < 4; i++)
            {
                RecycleCategory c = (RecycleCategory)i;
                Material sm = CreateMat(c.ToString(), colors[i], 0.44f, 0.08f, colors[i] * 0.15f);
                Material dm = CreateMat(c + "_I", new Color(0.07f, 0.08f, 0.1f), 0.2f, 0f, Color.black);
                GameObject r = new GameObject(c.ToString());
                r.transform.SetParent(parent, false);
                r.transform.localPosition = pos[i];
                CreatePrimitive(r.transform, "Shell", PrimitiveType.Cube, new Vector3(0f, 0.26f, 0f), new Vector3(0.72f, 0.52f, 0.72f), sm);
                GameObject cav = CreatePrimitive(r.transform, "Cavity", PrimitiveType.Cube, new Vector3(0f, 0.43f, 0f), new Vector3(0.54f, 0.28f, 0.54f), dm);
                GameObject lip = CreatePrimitive(r.transform, "Lip", PrimitiveType.Cube, new Vector3(0f, 0.64f, 0.32f), new Vector3(0.78f, 0.02f, 0.2f), sm);
                lip.transform.localRotation = Quaternion.Euler(-50f, 0f, 0f);
                Object.DestroyImmediate(cav.GetComponent<BoxCollider>());
                GameObject trig = new GameObject("DropZone");
                trig.transform.SetParent(r.transform, false);
                trig.transform.localPosition = new Vector3(0f, 0.46f, 0f);
                BoxCollider tc = trig.AddComponent<BoxCollider>();
                tc.isTrigger = true; tc.size = new Vector3(0.56f, 0.34f, 0.56f);
                Transform da = new GameObject("DropAnchor").transform;
                da.SetParent(r.transform, false); da.localPosition = new Vector3(0f, 0.22f, 0f);
                TextMesh lb = CreateTextMesh(r.transform, c + "Label", FormatLabel(c), font, 48, TextAlignment.Center);
                lb.transform.localPosition = new Vector3(0f, 0.88f, 0f); lb.characterSize = 0.024f;
                SortingBin sb = r.AddComponent<SortingBin>();
                sb.binType = c; sb.dropAnchor = da; sb.renderers = new[] { r.transform.Find("Shell").GetComponent<Renderer>(), lip.GetComponent<Renderer>() }; sb.label = lb;
                bins[i] = sb;
            }
            return bins;
        }

        // OLD dashboard — kept for reference
        private static DashboardBoard CreateDashOld(Transform p, Font f, Material pa, Material a)
        {
            GameObject r = new GameObject("DashboardBoard");
            r.transform.SetParent(p, false); r.transform.localPosition = new Vector3(5.35f, 2.05f, -0.12f); r.transform.localRotation = Quaternion.Euler(0f, -18f, 0f);
            CreatePrimitive(r.transform, "BoardSurface", PrimitiveType.Cube, Vector3.zero, new Vector3(3.0f, 1.95f, 0.12f), pa);
            CreatePrimitive(r.transform, "BoardTrim", PrimitiveType.Cube, new Vector3(0f, 0.96f, 0.04f), new Vector3(3.04f, 0.05f, 0.14f), a);
            DashboardBoard d = r.AddComponent<DashboardBoard>();
            d.titleText = CreateTextMesh(r.transform, "Title", "Factory Sorting Dashboard", f, 56, TextAlignment.Center);
            d.titleText.transform.localPosition = new Vector3(0f, 0.78f, -0.03f); d.titleText.characterSize = 0.035f;
            d.modeText = CreateTextMesh(r.transform, "Mode", "Training Mode", f, 40, TextAlignment.Center);
            d.modeText.transform.localPosition = new Vector3(0f, 0.58f, -0.03f); d.modeText.characterSize = 0.028f;
            d.humanAccuracyText = CreateTextMesh(r.transform, "HA", "Human accuracy: 0%", f, 32, TextAlignment.Left);
            d.humanAccuracyText.anchor = TextAnchor.MiddleLeft; d.humanAccuracyText.transform.localPosition = new Vector3(-1.34f, 0.28f, -0.03f); d.humanAccuracyText.characterSize = 0.023f;
            d.aiAccuracyText = CreateTextMesh(r.transform, "AA", "AI accuracy: 0%", f, 32, TextAlignment.Left);
            d.aiAccuracyText.anchor = TextAnchor.MiddleLeft; d.aiAccuracyText.transform.localPosition = new Vector3(-1.34f, 0.1f, -0.03f); d.aiAccuracyText.characterSize = 0.023f;
            d.confusionText = CreateTextMesh(r.transform, "Conf", "Top human mix-up: none", f, 24, TextAlignment.Left);
            d.confusionText.anchor = TextAnchor.UpperLeft; d.confusionText.transform.localPosition = new Vector3(-1.34f, -0.18f, -0.03f); d.confusionText.characterSize = 0.02f;
            d.historyText = CreateTextMesh(r.transform, "Hist", "History: no saved sessions yet", f, 24, TextAlignment.Left);
            d.historyText.anchor = TextAnchor.MiddleLeft; d.historyText.transform.localPosition = new Vector3(-1.34f, -0.56f, -0.03f); d.historyText.characterSize = 0.02f;
            d.suggestionText = CreateTextMesh(r.transform, "Sug", "Live suggestion: waiting for next item", f, 24, TextAlignment.Left);
            d.suggestionText.anchor = TextAnchor.MiddleLeft; d.suggestionText.transform.localPosition = new Vector3(-1.34f, -0.78f, -0.03f); d.suggestionText.characterSize = 0.02f;
            Transform[] bars = new Transform[4]; TextMesh[] lbls = new TextMesh[4];
            Color[] bc = { new Color(0.22f, 0.62f, 0.97f), new Color(0.9f, 0.72f, 0.24f), new Color(0.32f, 0.84f, 0.62f), new Color(0.73f, 0.56f, 0.24f) };
            for (int i = 0; i < 4; i++)
            {
                float x = -0.96f + i * 0.64f;
                GameObject b = CreatePrimitive(r.transform, $"Bar{i}", PrimitiveType.Cube, new Vector3(x, -0.05f, -0.03f), new Vector3(0.24f, 0.66f, 0.12f), CreateMat($"FBar{i}", bc[i], 0.3f, 0.05f, bc[i] * 0.08f));
                bars[i] = b.transform;
                lbls[i] = CreateTextMesh(r.transform, $"BL{i}", ((RecycleCategory)i).ToDisplayName(), f, 24, TextAlignment.Center);
                lbls[i].transform.localPosition = new Vector3(x, -0.5f, -0.03f); lbls[i].characterSize = 0.019f;
            }
            d.classBars = bars; d.classBarLabels = lbls;
            return d;
        }

        private static RecycleVisionHud CreateHud(Font f)
        {
            GameObject go = new GameObject("HUDCanvas");
            Canvas c = go.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.pixelPerfect = true;
            go.AddComponent<GraphicRaycaster>();
            CanvasScaler cs = go.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(1920f, 1080f); cs.matchWidthOrHeight = 0.5f;
            RecycleVisionHud h = go.AddComponent<RecycleVisionHud>();
            Color pc = new Color(0.03f, 0.05f, 0.08f, 0.84f); Color ac = new Color(0.97f, 0.79f, 0.24f, 0.95f);
            GameObject lp = CreateUIPanel("InfoPanel", go.transform, pc, new Vector2(0.025f, 0.53f), new Vector2(0.39f, 0.95f));
            h.titleText = CreateUIText("TitleText", lp.transform, f, 40, TextAnchor.UpperLeft, "RecycleVision Lab", Color.white, new Vector2(0.04f, 0.78f), new Vector2(0.96f, 0.96f));
            h.titleText.fontStyle = FontStyle.Bold; h.titleText.fontSize = 42;
            h.modeText = CreateUIText("ModeText", lp.transform, f, 25, TextAnchor.UpperLeft, "Training Mode", new Color(0.84f, 0.9f, 0.98f), new Vector2(0.04f, 0.64f), new Vector2(0.96f, 0.82f));
            h.modeText.fontStyle = FontStyle.Bold; h.modeText.fontSize = 28;
            h.itemText = CreateUIText("ItemText", lp.transform, f, 29, TextAnchor.UpperLeft, "Current item:", Color.white, new Vector2(0.04f, 0.46f), new Vector2(0.96f, 0.66f)); h.itemText.fontSize = 31;
            h.hintText = CreateUIText("HintText", lp.transform, f, 24, TextAnchor.UpperLeft, "", new Color(0.87f, 0.9f, 0.95f), new Vector2(0.04f, 0.18f), new Vector2(0.96f, 0.46f));
            h.feedbackText = CreateUIText("FeedbackText", lp.transform, f, 24, TextAnchor.UpperLeft, "", Color.white, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.18f)); h.feedbackText.fontSize = 23;
            GameObject rp = CreateUIPanel("StatsPanel", go.transform, pc, new Vector2(0.65f, 0.53f), new Vector2(0.975f, 0.95f));
            h.aiSuggestionText = CreateUIText("AISuggestionText", rp.transform, f, 26, TextAnchor.UpperLeft, "AI suggestion: waiting for item", Color.white, new Vector2(0.05f, 0.45f), new Vector2(0.95f, 0.94f));
            h.aiSuggestionText.fontStyle = FontStyle.Bold;
            h.summaryText = CreateUIText("SummaryText", rp.transform, f, 28, TextAnchor.UpperLeft, "Human n/a (0 manual)   |   AI 0/0 (0%)", new Color(0.93f, 0.95f, 1f), new Vector2(0.05f, 0.23f), new Vector2(0.95f, 0.4f));
            h.summaryText.fontStyle = FontStyle.Bold; h.summaryText.fontSize = 30;
            h.logStatusText = CreateUIText("LogStatusText", rp.transform, f, 20, TextAnchor.UpperLeft, "Logging: On (features, no frames) - press L to toggle", new Color(0.78f, 0.85f, 0.92f), new Vector2(0.05f, 0.14f), new Vector2(0.95f, 0.23f));
            Image timerRing = CreateUIImage("TimerRing", rp.transform, new Vector2(0.03f, 0.04f), new Vector2(0.12f, 0.14f));
            timerRing.color = ac;
            timerRing.raycastTarget = false;
            h.timerRingImage = timerRing;

            h.timerText = CreateUIText("TimerText", rp.transform, f, 30, TextAnchor.MiddleLeft, "Time left: 0.0s", ac, new Vector2(0.14f, 0.05f), new Vector2(0.95f, 0.14f));
            h.timerText.fontStyle = FontStyle.Bold; h.timerText.fontSize = 34; h.timerText.enabled = false;
            GameObject br = CreateUIPanel("ButtonRow", go.transform, new Color(0.04f, 0.06f, 0.09f, 0.76f), new Vector2(0.27f, 0.04f), new Vector2(0.73f, 0.16f));
            h.trainingButton = CreateButton("TrainingButton", br.transform, f, "Training", new Color(0.2f, 0.46f, 0.89f), new Vector2(0.04f, 0.18f), new Vector2(0.31f, 0.82f));
            h.quickSortButton = CreateButton("QuickSortButton", br.transform, f, "Quick Sort", new Color(0.18f, 0.68f, 0.46f), new Vector2(0.35f, 0.18f), new Vector2(0.62f, 0.82f));
            h.restartButton = CreateButton("RestartButton", br.transform, f, "Restart", new Color(0.86f, 0.55f, 0.2f), new Vector2(0.66f, 0.18f), new Vector2(0.93f, 0.82f));
            GameObject rpt = CreateUIPanel("ReportPanel", go.transform, new Color(0.02f, 0.04f, 0.07f, 0.94f), new Vector2(0.24f, 0.18f), new Vector2(0.76f, 0.8f));
            Text rt = CreateUIText("ReportTitle", rpt.transform, f, 38, TextAnchor.UpperCenter, "Session Report", Color.white, new Vector2(0.08f, 0.84f), new Vector2(0.92f, 0.96f));
            rt.fontStyle = FontStyle.Bold; rt.fontSize = 42;
            h.reportText = CreateUIText("ReportText", rpt.transform, f, 24, TextAnchor.UpperLeft, "", new Color(0.92f, 0.94f, 0.98f), new Vector2(0.08f, 0.18f), new Vector2(0.92f, 0.8f)); h.reportText.fontSize = 28;
            h.closeReportButton = CreateButton("CloseReportButton", rpt.transform, f, "Close", ac, new Vector2(0.34f, 0.04f), new Vector2(0.66f, 0.14f));
            h.reportPanel = rpt; rpt.SetActive(false);
            return h;
        }

        private static void CreateEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            GameObject e = new GameObject("EventSystem"); e.AddComponent<EventSystem>(); e.AddComponent<InputSystemUIInputModule>();
        }

        private static StationCameraController CreateCameraRig(Transform p)
        {
            GameObject rig = new GameObject("CameraRig");
            rig.transform.SetParent(p, false); rig.transform.localPosition = new Vector3(0.52f, 2.1f, -6.15f); rig.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            GameObject cgo = new GameObject("Main Camera"); cgo.transform.SetParent(rig.transform, false); cgo.tag = "MainCamera";
            Camera cam = cgo.AddComponent<Camera>(); cam.fieldOfView = 54f; cam.allowHDR = true; cam.allowMSAA = true; cam.nearClipPlane = 0.05f; cam.farClipPlane = 60f;
            cgo.AddComponent<AudioListener>();
            Transform ha = new GameObject("HoldAnchor").transform; ha.SetParent(rig.transform, false); ha.localPosition = new Vector3(0f, -0.08f, 2f);
            StationCameraController cc = rig.AddComponent<StationCameraController>();
            cc.playerCamera = cam; cc.holdAnchor = ha; cc.moveSpeed = 4.2f; cc.lookSensitivity = 0.1f;
            cc.xBounds = new Vector2(-4.2f, 4.2f); cc.zBounds = new Vector2(-7.4f, 1.8f);
            return cc;
        }

        private static RecycleVisionSortingAgent CreateAgentRig(Transform p, BaselineAiAdvisor adv)
        {
            GameObject go = new GameObject("RecycleVisionAgent"); go.transform.SetParent(p, false); go.transform.localPosition = new Vector3(-0.6f, 2.2f, -2.5f); go.transform.localRotation = Quaternion.Euler(18f, 6f, 0f);
            RecycleVisionSortingAgent a = go.AddComponent<RecycleVisionSortingAgent>(); a.baselineAi = adv; a.simulatedDecisionDelay = 0.18f;
            GameObject sc = new GameObject("AgentCamera"); sc.transform.SetParent(go.transform, false);
            Camera ca = sc.AddComponent<Camera>(); ca.fieldOfView = 40f; ca.nearClipPlane = 0.1f; ca.farClipPlane = 20f; ca.clearFlags = CameraClearFlags.SolidColor; ca.backgroundColor = new Color(0.08f, 0.1f, 0.13f); ca.enabled = false;
            return a;
        }

        private static RecycleVisionMlAgent CreateMlAgentRig(Transform p)
        {
            GameObject go = new GameObject("RecycleVisionMlAgent"); go.transform.SetParent(p, false); go.transform.localPosition = new Vector3(0.1f, 2.2f, -2.5f); go.transform.localRotation = Quaternion.Euler(18f, 6f, 0f);
            BehaviorParameters bp = go.AddComponent<BehaviorParameters>(); bp.BehaviorName = "RecycleVisionSorter"; bp.BehaviorType = BehaviorType.Default;
            bp.BrainParameters.VectorObservationSize = RecycleVisionMlAgent.VectorObservationSize; bp.BrainParameters.NumStackedVectorObservations = 1;
            bp.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(RecycleVisionMlAgent.DiscreteActionCount);
            RecycleVisionMlAgent a = go.AddComponent<RecycleVisionMlAgent>();
            GameObject sc = new GameObject("AgentCamera"); sc.transform.SetParent(go.transform, false);
            Camera ca = sc.AddComponent<Camera>(); ca.fieldOfView = 40f; ca.nearClipPlane = 0.1f; ca.farClipPlane = 20f; ca.clearFlags = CameraClearFlags.SolidColor; ca.backgroundColor = new Color(0.08f, 0.1f, 0.13f); ca.enabled = false;
            CameraSensorComponent sensor = go.AddComponent<CameraSensorComponent>(); sensor.Camera = ca; sensor.SensorName = "RecycleVisionCamera"; sensor.Width = 84; sensor.Height = 84; sensor.Grayscale = false; sensor.ObservationStacks = 1; sensor.RuntimeCameraEnable = false;
            return a;
        }

        private static void CreateLightRig(Transform p)
        {
            GameObject s = new GameObject("Sun"); s.transform.SetParent(p, false); s.transform.rotation = Quaternion.Euler(38f, -36f, 0f);
            Light d = s.AddComponent<Light>(); d.type = LightType.Directional; d.intensity = 1.15f; d.shadows = LightShadows.Soft;
            CreatePointLight(p, "KeyLeft", new Vector3(-2.8f, 2.8f, -1.4f), new Color(0.26f, 0.68f, 1f), 14f, 5.5f);
            CreatePointLight(p, "KeyRight", new Vector3(3.4f, 2.5f, -0.4f), new Color(1f, 0.74f, 0.22f), 12f, 4.8f);
            CreatePointLight(p, "BackGlow", new Vector3(0f, 3.1f, 3.2f), new Color(0.72f, 0.82f, 1f), 16f, 4f);
        }

        private static void CreatePointLight(Transform p, string n, Vector3 pos, Color c, float r, float i)
        {
            GameObject l = new GameObject(n); l.transform.SetParent(p, false); l.transform.localPosition = pos;
            Light lt = l.AddComponent<Light>(); lt.type = LightType.Point; lt.color = c; lt.range = r; lt.intensity = i;
        }

        private static void CreateNeonBar(Transform p, Vector3 pos, Vector3 s, Color c)
        {
            CreatePrimitive(p, "NeonBar", PrimitiveType.Cube, pos, s, CreateMat($"Neon_{c}", c, 0.6f, 0.12f, c * 0.55f));
        }

        private static void StylizeFactoryHierarchy(GameObject r)
        {
            foreach (Renderer re in r.GetComponentsInChildren<Renderer>(true))
            {
                Material[] sm = re.sharedMaterials;
                for (int i = 0; i < sm.Length; i++) sm[i] = GetOverrideMat(sm[i]);
                re.sharedMaterials = sm; re.receiveShadows = true; re.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        private static Material GetOverrideMat(Material s)
        {
            string n = s != null ? s.name : "Default";
            if (FactoryMaterialCache.TryGetValue(n, out Material c)) return c;
            string l = n.ToLowerInvariant();
            Material g;
            if (l.Contains("glass") || l.Contains("window") || l.Contains("acryl"))
                g = CreateTransparentMat($"Fact_{Sanitize(n)}", new Color(0.54f, 0.78f, 0.95f, 0.34f), 0.85f, 0.02f);
            else if (l.Contains("light") || l.Contains("exit") || l.Contains("lamp"))
                g = CreateMat($"Fact_{Sanitize(n)}", new Color(0.96f, 0.88f, 0.58f), 0.55f, 0.1f, new Color(0.96f, 0.88f, 0.58f) * 0.48f);
            else if (l.Contains("arm") || l.Contains("steel") || l.Contains("sus") || l.Contains("chrome") || l.Contains("pipe") || l.Contains("frame"))
                g = CreateMat($"Fact_{Sanitize(n)}", new Color(0.58f, 0.63f, 0.68f), 0.78f, 0.72f, Color.black);
            else if (l.Contains("button") || l.Contains("controller") || l.Contains("console"))
                g = CreateMat($"Fact_{Sanitize(n)}", new Color(0.18f, 0.22f, 0.28f), 0.4f, 0.12f, Color.black);
            else if (l.Contains("floor"))
                g = CreateMat($"Fact_{Sanitize(n)}", new Color(0.22f, 0.24f, 0.27f), 0.3f, 0.08f, Color.black);
            else if (l.Contains("cart") || l.Contains("wagon"))
                g = CreateMat($"Fact_{Sanitize(n)}", new Color(0.91f, 0.63f, 0.18f), 0.42f, 0.18f, Color.black);
            else
                g = CreateMat($"Fact_{Sanitize(n)}", new Color(0.34f, 0.38f, 0.44f), 0.42f, 0.18f, Color.black);
            if (s != null && s.mainTexture != null)
            {
                if (g.HasProperty("_BaseMap")) g.SetTexture("_BaseMap", s.mainTexture);
                g.mainTexture = s.mainTexture;
            }
            FactoryMaterialCache[n] = g;
            return g;
        }

        private static GameObject LoadPrefab(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

        private static GameObject CreatePrimitive(Transform p, string n, PrimitiveType t, Vector3 pos, Vector3 s, Material m)
        {
            GameObject o = GameObject.CreatePrimitive(t); o.name = n; o.transform.SetParent(p, false);
            o.transform.localPosition = pos; o.transform.localRotation = Quaternion.identity; o.transform.localScale = s;
            o.GetComponent<Renderer>().sharedMaterial = m; return o;
        }

        private static TextMesh CreateTextMesh(Transform p, string n, string t, Font f, int fs, TextAlignment a)
        {
            GameObject o = new GameObject(n); o.transform.SetParent(p, false);
            TextMesh tm = o.AddComponent<TextMesh>(); tm.font = f; tm.fontSize = fs * 2; tm.characterSize = 0.025f * (fs / (float)(fs * 2));
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = a; tm.text = t; tm.color = Color.white;
            o.GetComponent<MeshRenderer>().sharedMaterial = f.material; return tm;
        }

        private static GameObject CreateUIPanel(string n, Transform p, Color c, Vector2 amin, Vector2 amax)
        {
            GameObject o = new GameObject(n, typeof(RectTransform), typeof(Image)); o.transform.SetParent(p, false);
            RectTransform r = o.GetComponent<RectTransform>(); r.anchorMin = amin; r.anchorMax = amax; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            o.GetComponent<Image>().color = c; return o;
        }

        private static Text CreateUIText(string n, Transform p, Font f, int fs, TextAnchor a, string t, Color c, Vector2 amin, Vector2 amax)
        {
            GameObject o = new GameObject(n, typeof(RectTransform), typeof(Text)); o.transform.SetParent(p, false);
            RectTransform r = o.GetComponent<RectTransform>(); r.anchorMin = amin; r.anchorMax = amax; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            Text tx = o.GetComponent<Text>(); tx.font = f; tx.fontSize = fs; tx.alignment = a; tx.color = c; tx.text = t;
            tx.horizontalOverflow = HorizontalWrapMode.Wrap; tx.verticalOverflow = VerticalWrapMode.Overflow; return tx;
        }

        private static Image CreateUIImage(string n, Transform p, Vector2 amin, Vector2 amax)
        {
            GameObject o = new GameObject(n, typeof(RectTransform), typeof(Image)); o.transform.SetParent(p, false);
            RectTransform r = o.GetComponent<RectTransform>(); r.anchorMin = amin; r.anchorMax = amax; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            Image img = o.GetComponent<Image>(); img.color = Color.white; return img;
        }

        private static Button CreateButton(string n, Transform p, Font f, string l, Color c, Vector2 amin, Vector2 amax)
        {
            GameObject o = new GameObject(n, typeof(RectTransform), typeof(Image), typeof(Button)); o.transform.SetParent(p, false);
            RectTransform r = o.GetComponent<RectTransform>(); r.anchorMin = amin; r.anchorMax = amax; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            o.GetComponent<Image>().color = c; Button b = o.GetComponent<Button>();
            ColorBlock cb = b.colors; cb.normalColor = c; cb.highlightedColor = Color.Lerp(c, Color.white, 0.18f); cb.pressedColor = Color.Lerp(c, Color.black, 0.14f); cb.selectedColor = cb.highlightedColor; b.colors = cb;
            Text lt = CreateUIText("Label", o.transform, f, 24, TextAnchor.MiddleCenter, l, Color.white, Vector2.zero, Vector2.one);
            lt.fontStyle = FontStyle.Bold; lt.fontSize = 28; return b;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/RecycleVision")) AssetDatabase.CreateFolder("Assets", "RecycleVision");
            if (!AssetDatabase.IsValidFolder(GeneratedFolder)) AssetDatabase.CreateFolder("Assets/RecycleVision", "Generated");
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder(GeneratedFolder, "FactoryMaterials");
        }

        private static Material CreateMat(string fn, Color bc, float sm, float mt, Color ec)
        {
            string ap = $"{MaterialFolder}/{fn}.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(ap);
            if (m == null) { Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"); m = new Material(s); AssetDatabase.CreateAsset(m, ap); }
            m.color = bc; m.SetColor("_BaseColor", bc);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", sm);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", mt);
            if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", ec); }
            EditorUtility.SetDirty(m); return m;
        }

        private static Material CreateTransparentMat(string fn, Color bc, float sm, float mt)
        {
            Material m = CreateMat(fn, bc, sm, mt, bc * 0.15f);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent; m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.color = bc; m.SetColor("_BaseColor", bc); EditorUtility.SetDirty(m); return m;
        }

        private static void UpdateBuildSettings(string scenePath)
        {
            List<EditorBuildSettingsScene> list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (EditorBuildSettingsScene s in list) if (s.path == scenePath) return;
            list.Add(new EditorBuildSettingsScene(scenePath, true)); EditorBuildSettings.scenes = list.ToArray();
        }

        private static string Sanitize(string n)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n.Replace(" ", "_");
        }
    }
}
