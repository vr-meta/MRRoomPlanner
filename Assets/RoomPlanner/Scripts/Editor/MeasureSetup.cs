#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RoomPlanner.Measure;
using RoomPlanner.Tools;
using RoomPlanner.Walls;
using RoomPlanner.Floors;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Одно-кнопочная сборка рулетки: RoomPlanner → Setup Measure Rig.
    /// Идемпотентно, материалы — URP .mat-ассеты. Создаёт префаб измерения (с бейджем
    /// и ручками-концами) и префаб кнопки «+».
    /// </summary>
    public static class MeasureSetup
    {
        private const string PrefabDir = "Assets/RoomPlanner/Prefabs";
        private const string MeasurePrefabPath = PrefabDir + "/Measurement.prefab";
        private const string ContPrefabPath = PrefabDir + "/ContinueButton.prefab";
        private const string MatDir = "Assets/RoomPlanner/Materials";

        [MenuItem("RoomPlanner/Setup Measure Rig")]
        public static void SetupMeasureRig()
        {
            var existing = GameObject.Find("MeasureRig");
            if (existing != null) Object.DestroyImmediate(existing);

            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory(PrefabDir);

            Material lineMat = CreateMat("Measure_Line", new Color(0.2f, 1f, 0.4f));
            Material markerMat = CreateMat("Measure_Marker", new Color(0.2f, 0.8f, 1f));
            Material reticleMat = CreateMat("Measure_Reticle", new Color(1f, 0.9f, 0.2f));
            Material contMat = CreateMat("Measure_Continue", new Color(0.2f, 1f, 0.4f));
            Texture2D badgeTex = CreateRoundedBadgeTexture();
            Material badgeMat = CreateBadgeMat("Measure_Badge", new Color(0.45f, 0.32f, 0.82f), badgeTex);

            Measurement measurePrefab = CreateMeasurementPrefab(lineMat, markerMat, badgeMat);
            MeasureContinueButton contPrefab = CreateContinueButtonPrefab(contMat);

            var rig = new GameObject("MeasureRig");
            var raycaster = rig.AddComponent<SceneRaycaster>();
            var pointer = rig.AddComponent<PointerProvider>();
            var input = rig.AddComponent<MeasureInput>();
            var controller = rig.AddComponent<MeasureController>();

            var reticle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            reticle.name = "Reticle";
            reticle.transform.SetParent(rig.transform, false);
            reticle.transform.localScale = Vector3.one * 0.04f;
            RemoveCollider(reticle);
            reticle.GetComponent<Renderer>().sharedMaterial = reticleMat;

            Transform anchor = FindControllerAnchor();

            var pso = new SerializedObject(pointer);
            if (anchor != null) pso.FindProperty("controllerAnchor").objectReferenceValue = anchor;
            pso.ApplyModifiedProperties();

            var cso = new SerializedObject(controller);
            cso.FindProperty("pointer").objectReferenceValue = pointer;
            cso.FindProperty("input").objectReferenceValue = input;
            cso.FindProperty("raycaster").objectReferenceValue = raycaster;
            cso.FindProperty("reticle").objectReferenceValue = reticle.transform;
            cso.FindProperty("measurementPrefab").objectReferenceValue = measurePrefab;
            cso.FindProperty("continueButtonPrefab").objectReferenceValue = contPrefab;
            cso.ApplyModifiedProperties();

            // ---- Стены + меню инструментов + менеджер ----
            Material wallMat = CreateBadgeMat("Wall_Surface", new Color(0.60f, 0.75f, 1f, 0.45f), null);
            Material wallEdgeMat = CreateMat("Wall_Edge", new Color(0.10f, 0.16f, 0.32f));
            Material panelMat = CreateMat("Menu_Panel", new Color(0.06f, 0.07f, 0.10f));   // opaque (no shader-variant stripping on device)
            Material btnMat = CreateMat("Menu_Button", new Color(0.16f, 0.18f, 0.24f));
            Material activeMat = CreateMat("Menu_Active", new Color(0.2f, 1f, 0.5f));

            Wall wallPrefab = CreateWallPrefab(wallMat, wallEdgeMat);

            var wall = rig.AddComponent<WallController>();
            var manager = rig.AddComponent<ToolManager>();

            var wallPrevGo = new GameObject("WallPreview");
            wallPrevGo.transform.SetParent(rig.transform, false);
            var wallPrev = wallPrevGo.AddComponent<LineRenderer>();
            wallPrev.useWorldSpace = true;
            wallPrev.widthMultiplier = 0.01f;
            wallPrev.numCapVertices = 4;
            wallPrev.positionCount = 2;
            wallPrev.sharedMaterial = lineMat;
            wallPrev.enabled = false;

            var wso = new SerializedObject(wall);
            wso.FindProperty("pointer").objectReferenceValue = pointer;
            wso.FindProperty("input").objectReferenceValue = input;
            wso.FindProperty("raycaster").objectReferenceValue = raycaster;
            wso.FindProperty("reticle").objectReferenceValue = reticle.transform;
            wso.FindProperty("wallPrefab").objectReferenceValue = wallPrefab;
            wso.FindProperty("previewLine").objectReferenceValue = wallPrev;
            wso.FindProperty("manager").objectReferenceValue = manager;
            wso.ApplyModifiedProperties();

            // ---- Пол (план-режим) ----
            Material floorMat = CreateFloorMat("Floor_Top", Color.white);
            Floor floorPrefab = CreateFloorPrefab(floorMat);
            var floor = rig.AddComponent<FloorController>();

            var floorPrevGo = new GameObject("FloorPreview");
            floorPrevGo.transform.SetParent(rig.transform, false);
            var floorPrev = floorPrevGo.AddComponent<LineRenderer>();
            floorPrev.useWorldSpace = true;
            floorPrev.widthMultiplier = 0.01f;
            floorPrev.numCapVertices = 4;
            floorPrev.loop = true;
            floorPrev.sharedMaterial = lineMat;
            floorPrev.enabled = false;

            var fso = new SerializedObject(floor);
            fso.FindProperty("pointer").objectReferenceValue = pointer;
            fso.FindProperty("input").objectReferenceValue = input;
            fso.FindProperty("reticle").objectReferenceValue = reticle.transform;
            fso.FindProperty("floorPrefab").objectReferenceValue = floorPrefab;
            fso.FindProperty("previewLine").objectReferenceValue = floorPrev;
            fso.FindProperty("manager").objectReferenceValue = manager;
            fso.FindProperty("planMaterial").objectReferenceValue = floorMat;
            fso.ApplyModifiedProperties();

            Transform leftAnchor = FindLeftControllerAnchor();
            ToolMenu menu = BuildPalette(panelMat, btnMat, activeMat, leftAnchor);
            InspectorPanel inspector = BuildInspector(panelMat, btnMat, activeMat);

            var mgrSo = new SerializedObject(manager);
            mgrSo.FindProperty("pointer").objectReferenceValue = pointer;
            mgrSo.FindProperty("input").objectReferenceValue = input;
            mgrSo.FindProperty("reticle").objectReferenceValue = reticle.transform;
            mgrSo.FindProperty("menu").objectReferenceValue = menu;
            mgrSo.FindProperty("inspector").objectReferenceValue = inspector;
            mgrSo.FindProperty("measure").objectReferenceValue = controller;
            mgrSo.FindProperty("wall").objectReferenceValue = wall;
            mgrSo.FindProperty("floor").objectReferenceValue = floor;
            mgrSo.ApplyModifiedProperties();

            var cso2 = new SerializedObject(controller);
            cso2.FindProperty("manager").objectReferenceValue = manager;
            cso2.ApplyModifiedProperties();

            TryEnableEffectMeshColliders();

            Selection.activeGameObject = rig;
            EditorSceneManager.MarkSceneDirty(rig.scene);

            EditorUtility.DisplayDialog("Tools Rig",
                "Rebuilt (URP): measure + walls + floor. NEW UI (Phase 1):\n" +
                "  • Left-hand PALETTE = tool type (Meas/Wall/Floor) + snap toggles (Cor/Edg/Grd/Ang/Scan).\n" +
                "  • Floating INSPECTOR (appears for Walls/Floor; none for Measure) holds that tool's settings;\n" +
                "    grab its title bar with GRIP to move/park it. Steppers/cycles via right-ray + trigger.\n" +
                "Floor tool: 2 corners → slab at Level (Thk). Plan image = plan.png in app folder;\n" +
                "  Plan scale in inspector, right stick moves it.\n" +
                "Measure: trigger = points; '+' at any point = chain; point+trigger = drag; point+B = delete.\n" +
                "Walls: trigger = centerline points (chain), B = finish. Thickness grows outward.\n" +
                "Common: mid-air points (depth via stick), grip = axis/angle snap.\n\n" +
                "Next: Ctrl+S → Ctrl+B.",
                "OK");
        }

        private static Material CreateMat(string name, Color color)
        {
            var mat = new Material(UnlitShader());
            SetColor(mat, color);
            string path = $"{MatDir}/{name}.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Белая текстура скруглённого прямоугольника (альфа) — подложка бейджа.</summary>
        private static Texture2D CreateRoundedBadgeTexture()
        {
            const int W = 320, H = 160;
            const float radius = 46f;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "Measure_BadgeTex"
            };
            var px = new Color[W * H];
            float hw = W * 0.5f, hh = H * 0.5f;
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - hw) - (hw - radius);
                float qy = Mathf.Abs(y + 0.5f - hh) - (hh - radius);
                float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                float d = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                float a = Mathf.Clamp01(0.5f - d); // ~1px антиалиасинг по краю
                px[y * W + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px);
            tex.Apply();
            string path = $"{MatDir}/Measure_BadgeTex.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        /// <summary>Прозрачный двусторонний URP-Unlit материал бейджа с тинтом-цветом.</summary>
        private static Material CreateBadgeMat(string name, Color color, Texture2D tex)
        {
            var mat = new Material(UnlitShader());
            if (tex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }
            SetColor(mat, color);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = 2990;
            string path = $"{MatDir}/{name}.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static Measurement CreateMeasurementPrefab(Material lineMat, Material markerMat, Material badgeMat)
        {
            var root = new GameObject("Measurement");
            var measurement = root.AddComponent<Measurement>();

            var lineGo = new GameObject("Line");
            lineGo.transform.SetParent(root.transform, false);
            var line = lineGo.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = 0.008f;
            line.numCapVertices = 4;
            line.positionCount = 2;
            line.sharedMaterial = lineMat;

            var markerA = MakeMarker("MarkerA", root.transform, markerMat, true);
            var markerB = MakeMarker("MarkerB", root.transform, markerMat, false);

            // Подпись-бейдж
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localScale = Vector3.one * 0.08f; // мельче шрифт (бейдж масштабируется вместе)
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text = "0 cm";
            tmp.fontSize = 4f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(4f, 1f);
            var label = labelGo.AddComponent<MeasurementLabel>();

            var badge = GameObject.CreatePrimitive(PrimitiveType.Quad);
            badge.name = "Badge";
            badge.transform.SetParent(labelGo.transform, false);
            RemoveCollider(badge);
            badge.GetComponent<Renderer>().sharedMaterial = badgeMat;
            badge.transform.localScale = new Vector3(4f, 1.4f, 1f);
            badge.transform.localPosition = new Vector3(0f, 0f, 0.02f); // позади текста (к камере — текст)

            var mso = new SerializedObject(measurement);
            mso.FindProperty("line").objectReferenceValue = line;
            mso.FindProperty("label").objectReferenceValue = label;
            mso.FindProperty("markerA").objectReferenceValue = markerA.transform;
            mso.FindProperty("markerB").objectReferenceValue = markerB.transform;
            mso.ApplyModifiedProperties();

            var lso = new SerializedObject(label);
            lso.FindProperty("text").objectReferenceValue = tmp;
            lso.FindProperty("background").objectReferenceValue = badge.transform;
            lso.ApplyModifiedProperties();

            AssetDatabase.DeleteAsset(MeasurePrefabPath);
            var asset = PrefabUtility.SaveAsPrefabAsset(root, MeasurePrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return asset.GetComponent<Measurement>();
        }

        private static MeasureContinueButton CreateContinueButtonPrefab(Material mat)
        {
            var root = new GameObject("ContinueButton");
            root.AddComponent<MeasureContinueButton>();
            var col = root.AddComponent<SphereCollider>();
            col.radius = 0.06f;

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Visual";
            sphere.transform.SetParent(root.transform, false);
            sphere.transform.localScale = Vector3.one * 0.05f;
            RemoveCollider(sphere);
            sphere.GetComponent<Renderer>().sharedMaterial = mat;

            var labelGo = new GameObject("Plus");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -0.04f);
            labelGo.transform.localScale = Vector3.one * 0.06f;
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text = "+";
            tmp.fontSize = 6f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.black;
            tmp.rectTransform.sizeDelta = new Vector2(2f, 2f);

            AssetDatabase.DeleteAsset(ContPrefabPath);
            var asset = PrefabUtility.SaveAsPrefabAsset(root, ContPrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return asset.GetComponent<MeasureContinueButton>();
        }

        private static GameObject MakeMarker(string name, Transform parent, Material mat, bool isEndA)
        {
            var m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m.name = name;
            m.transform.SetParent(parent, false);
            m.transform.localScale = Vector3.one * 0.04f;
            m.GetComponent<Renderer>().sharedMaterial = mat;

            // коллайдер оставляем (для выделения/таскания), делаем чуть крупнее визуала
            var col = m.GetComponent<SphereCollider>();
            if (col != null) col.radius = 1.6f;

            var handle = m.AddComponent<MeasurePointHandle>();
            var hso = new SerializedObject(handle);
            hso.FindProperty("isEndA").boolValue = isEndA;
            hso.ApplyModifiedProperties();
            return m;
        }

        private static Wall CreateWallPrefab(Material mat, Material edgeMat)
        {
            var root = new GameObject("Wall");
            root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var wall = root.AddComponent<Wall>();

            var edges = new GameObject("Edges");
            edges.transform.SetParent(root.transform, false);
            var edgesFilter = edges.AddComponent<MeshFilter>();
            edges.AddComponent<MeshRenderer>().sharedMaterial = edgeMat;

            var wso = new SerializedObject(wall);
            wso.FindProperty("edgesFilter").objectReferenceValue = edgesFilter;
            wso.ApplyModifiedProperties();

            string path = PrefabDir + "/Wall.prefab";
            AssetDatabase.DeleteAsset(path);
            var asset = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return asset.GetComponent<Wall>();
        }

        private static Material CreateFloorMat(string name, Color color)
        {
            var mat = new Material(UnlitShader());
            SetColor(mat, color);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            string path = $"{MatDir}/{name}.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static Floor CreateFloorPrefab(Material mat)
        {
            var root = new GameObject("Floor");
            root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>().sharedMaterial = mat;
            root.AddComponent<Floor>();

            string path = PrefabDir + "/Floor.prefab";
            AssetDatabase.DeleteAsset(path);
            var asset = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return asset.GetComponent<Floor>();
        }

        // Left-hand palette: tool types + global snap toggles only.
        private static ToolMenu BuildPalette(Material panelMat, Material btnMat, Material activeMat, Transform leftAnchor)
        {
            var root = new GameObject("ToolMenu");
            var menu = root.AddComponent<ToolMenu>();

            var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = "Panel";
            RemoveCollider(panel);
            panel.transform.SetParent(root.transform, false);
            panel.transform.localScale = new Vector3(0.24f, 0.16f, 1f);
            panel.transform.localPosition = new Vector3(0f, 0f, 0.006f);
            panel.GetComponent<Renderer>().sharedMaterial = panelMat;

            var toolS = new Vector2(0.066f, 0.05f);
            var measureBtn = MakeMenuButton(root.transform, "BtnMeasure", "Meas", MenuAction.SelectMeasure,
                new Vector3(-0.075f, 0.045f, 0f), toolS, btnMat, activeMat, true);
            var wallBtn = MakeMenuButton(root.transform, "BtnWall", "Wall", MenuAction.SelectWall,
                new Vector3(0f, 0.045f, 0f), toolS, btnMat, activeMat, true);
            var floorBtn = MakeMenuButton(root.transform, "BtnFloor", "Floor", MenuAction.SelectFloor,
                new Vector3(0.075f, 0.045f, 0f), toolS, btnMat, activeMat, true);

            var snapSize = new Vector2(0.042f, 0.04f);
            var snapCornerBtn = MakeMenuButton(root.transform, "BtnSnapCorner", "Cor", MenuAction.ToggleSnapCorner,
                new Vector3(-0.092f, -0.03f, 0f), snapSize, btnMat, activeMat, true);
            var snapEdgeBtn = MakeMenuButton(root.transform, "BtnSnapEdge", "Edg", MenuAction.ToggleSnapEdge,
                new Vector3(-0.046f, -0.03f, 0f), snapSize, btnMat, activeMat, true);
            var snapGridBtn = MakeMenuButton(root.transform, "BtnSnapGrid", "Grd", MenuAction.ToggleSnapGrid,
                new Vector3(0f, -0.03f, 0f), snapSize, btnMat, activeMat, true);
            var snapAngleBtn = MakeMenuButton(root.transform, "BtnSnapAngle", "Ang", MenuAction.ToggleSnapAngle,
                new Vector3(0.046f, -0.03f, 0f), snapSize, btnMat, activeMat, true);
            var scanBtn = MakeMenuButton(root.transform, "BtnScan", "Scan", MenuAction.ToggleScan,
                new Vector3(0.092f, -0.03f, 0f), snapSize, btnMat, activeMat, true);

            var so = new SerializedObject(menu);
            if (leftAnchor != null) so.FindProperty("follow").objectReferenceValue = leftAnchor;
            so.FindProperty("measureBtn").objectReferenceValue = measureBtn;
            so.FindProperty("wallBtn").objectReferenceValue = wallBtn;
            so.FindProperty("floorBtn").objectReferenceValue = floorBtn;
            so.FindProperty("snapCornerBtn").objectReferenceValue = snapCornerBtn;
            so.FindProperty("snapEdgeBtn").objectReferenceValue = snapEdgeBtn;
            so.FindProperty("snapGridBtn").objectReferenceValue = snapGridBtn;
            so.FindProperty("snapAngleBtn").objectReferenceValue = snapAngleBtn;
            so.FindProperty("scanBtn").objectReferenceValue = scanBtn;
            so.ApplyModifiedProperties();

            SetLayerRecursively(root, 2);
            return menu;
        }

        // Floating, grabbable settings panel with a per-tool group (Walls / Floor).
        private static InspectorPanel BuildInspector(Material panelMat, Material btnMat, Material activeMat)
        {
            var root = new GameObject("Inspector");
            var inspector = root.AddComponent<InspectorPanel>();

            var panelRoot = new GameObject("Panel");
            panelRoot.transform.SetParent(root.transform, false);

            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "Bg"; RemoveCollider(bg);
            bg.transform.SetParent(panelRoot.transform, false);
            bg.transform.localScale = new Vector3(0.36f, 0.44f, 1f);
            bg.transform.localPosition = new Vector3(0f, 0f, 0.006f);
            bg.GetComponent<Renderer>().sharedMaterial = panelMat;

            // title bar = grab handle
            var title = new GameObject("Title");
            title.transform.SetParent(panelRoot.transform, false);
            title.transform.localPosition = new Vector3(0f, 0.19f, 0f);
            title.AddComponent<BoxCollider>().size = new Vector3(0.36f, 0.05f, 0.04f);
            title.AddComponent<InspectorGrab>();
            var titleBg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            titleBg.name = "TitleBg"; RemoveCollider(titleBg);
            titleBg.transform.SetParent(title.transform, false);
            titleBg.transform.localScale = new Vector3(0.36f, 0.05f, 1f);
            titleBg.GetComponent<Renderer>().sharedMaterial = btnMat;
            MakeValueLabel(title.transform, "TitleText", "Settings (grip to move)", new Vector3(0f, 0f, 0f), new Vector2(0.34f, 0.045f));

            var wallGroup = new GameObject("WallGroup");
            wallGroup.transform.SetParent(panelRoot.transform, false);
            var thickLabel = MakeStepperRow(wallGroup.transform, "Thk", "Thickness", MenuAction.ThicknessDown, MenuAction.ThicknessUp, 0.13f, btnMat, activeMat);
            var heightLabel = MakeStepperRow(wallGroup.transform, "H", "Height", MenuAction.HeightDown, MenuAction.HeightUp, 0.075f, btnMat, activeMat);
            var angleLabel = MakeStepperRow(wallGroup.transform, "Ang", "Angle step", MenuAction.AngleDown, MenuAction.AngleUp, 0.02f, btnMat, activeMat);
            var offsetLabel = MakeCycleRow(wallGroup.transform, "Off", "Offset", MenuAction.CycleOffset, -0.04f, btnMat, activeMat);
            var joinLabel = MakeCycleRow(wallGroup.transform, "Join", "Corner", MenuAction.CycleJoin, -0.095f, btnMat, activeMat);
            var placeLabel = MakeCycleRow(wallGroup.transform, "Place", "Place", MenuAction.CyclePlace, -0.15f, btnMat, activeMat);

            var floorGroup = new GameObject("FloorGroup");
            floorGroup.transform.SetParent(panelRoot.transform, false);
            var levelLabel = MakeStepperRow(floorGroup.transform, "Lvl", "Level", MenuAction.LevelDown, MenuAction.LevelUp, 0.13f, btnMat, activeMat);
            var floorThickLabel = MakeStepperRow(floorGroup.transform, "FThk", "Thickness", MenuAction.ThicknessDown, MenuAction.ThicknessUp, 0.075f, btnMat, activeMat);
            var planLabel = MakeStepperRow(floorGroup.transform, "Plan", "Plan scale", MenuAction.PlanDown, MenuAction.PlanUp, 0.02f, btnMat, activeMat);

            var so = new SerializedObject(inspector);
            so.FindProperty("panelRoot").objectReferenceValue = panelRoot;
            so.FindProperty("wallGroup").objectReferenceValue = wallGroup;
            so.FindProperty("floorGroup").objectReferenceValue = floorGroup;
            so.FindProperty("thickLabel").objectReferenceValue = thickLabel;
            so.FindProperty("heightLabel").objectReferenceValue = heightLabel;
            so.FindProperty("angleLabel").objectReferenceValue = angleLabel;
            so.FindProperty("offsetLabel").objectReferenceValue = offsetLabel;
            so.FindProperty("joinLabel").objectReferenceValue = joinLabel;
            so.FindProperty("placeLabel").objectReferenceValue = placeLabel;
            so.FindProperty("levelLabel").objectReferenceValue = levelLabel;
            so.FindProperty("floorThickLabel").objectReferenceValue = floorThickLabel;
            so.FindProperty("planLabel").objectReferenceValue = planLabel;
            so.ApplyModifiedProperties();

            SetLayerRecursively(root, 2);
            panelRoot.SetActive(false); // shown when a tool with settings becomes active
            return inspector;
        }

        // caption + [−] value [+]
        private static TMP_Text MakeStepperRow(Transform group, string id, string caption,
            MenuAction down, MenuAction up, float y, Material btnMat, Material activeMat)
        {
            var s = new Vector2(0.045f, 0.045f);
            MakeValueLabel(group, id + "Cap", caption, new Vector3(-0.115f, y, 0f), new Vector2(0.13f, 0.04f));
            MakeMenuButton(group, id + "-", "−", down, new Vector3(-0.01f, y, 0f), s, btnMat, activeMat, false);
            var v = MakeValueLabel(group, id + "Val", "—", new Vector3(0.06f, y, 0f), new Vector2(0.09f, 0.04f));
            MakeMenuButton(group, id + "+", "+", up, new Vector3(0.14f, y, 0f), s, btnMat, activeMat, false);
            return v;
        }

        // caption + value + [▸] cycle
        private static TMP_Text MakeCycleRow(Transform group, string id, string caption,
            MenuAction cycle, float y, Material btnMat, Material activeMat)
        {
            MakeValueLabel(group, id + "Cap", caption, new Vector3(-0.115f, y, 0f), new Vector2(0.13f, 0.04f));
            var v = MakeValueLabel(group, id + "Val", "—", new Vector3(0.03f, y, 0f), new Vector2(0.11f, 0.04f));
            MakeMenuButton(group, id + "Cyc", ">", cycle, new Vector3(0.14f, y, 0f), new Vector2(0.045f, 0.045f), btnMat, activeMat, false);
            return v;
        }

        private static MenuButton MakeMenuButton(Transform parent, string name, string label, MenuAction action,
            Vector3 lp, Vector2 size, Material bgMat, Material activeMat, bool withActiveMark)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = lp;
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(size.x, size.y, 0.04f);
            var mb = root.AddComponent<MenuButton>();

            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "Bg";
            RemoveCollider(bg);
            bg.transform.SetParent(root.transform, false);
            bg.transform.localScale = new Vector3(size.x, size.y, 1f);
            bg.GetComponent<Renderer>().sharedMaterial = bgMat;

            MakeTextChild(root.transform, "Label", label, size);

            GameObject mark = null;
            if (withActiveMark)
            {
                mark = GameObject.CreatePrimitive(PrimitiveType.Quad);
                mark.name = "Active";
                RemoveCollider(mark);
                mark.transform.SetParent(root.transform, false);
                mark.transform.localScale = new Vector3(size.x * 0.9f, size.y * 0.14f, 1f);
                mark.transform.localPosition = new Vector3(0f, -size.y * 0.62f, -0.004f);
                mark.GetComponent<Renderer>().sharedMaterial = activeMat;
                mark.SetActive(false);
            }

            var so = new SerializedObject(mb);
            so.FindProperty("action").enumValueIndex = (int)action;
            if (mark != null) so.FindProperty("activeMark").objectReferenceValue = mark;
            so.ApplyModifiedProperties();
            return mb;
        }

        private static TMP_Text MakeValueLabel(Transform parent, string name, string text, Vector3 lp, Vector2 size)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = lp;
            return MakeTextChild(root.transform, "Text", text, size);
        }

        private static TMP_Text MakeTextChild(Transform parent, string name, string text, Vector2 size)
        {
            var t = new GameObject(name);
            t.transform.SetParent(parent, false);
            t.transform.localPosition = new Vector3(0f, 0f, -0.006f);
            var tmp = t.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.enableWordWrapping = false;
            tmp.rectTransform.sizeDelta = new Vector2(size.x * 0.95f, size.y * 0.95f);
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.004f;
            tmp.fontSizeMax = size.y * 0.6f;
            return tmp;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursively(c.gameObject, layer);
        }

        private static Transform FindLeftControllerAnchor()
        {
            foreach (var name in new[] { "LeftControllerAnchor", "LeftHandAnchor" })
            {
                var go = GameObject.Find(name);
                if (go != null) return go.transform;
            }
            return null;
        }

        private static void RemoveCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }

        private static void SetColor(Material mat, Color color)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            mat.color = color;
        }

        private static Shader UnlitShader()
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Unlit/Color");
            if (s == null) s = Shader.Find("Sprites/Default");
            return s != null ? s : Shader.Find("Standard");
        }

        private static Transform FindControllerAnchor()
        {
            foreach (var name in new[] { "RightControllerAnchor", "RightHandAnchor" })
            {
                var go = GameObject.Find(name);
                if (go != null) return go.transform;
            }
            return null;
        }

        private static void TryEnableEffectMeshColliders()
        {
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb == null || mb.GetType().Name != "EffectMesh") continue;
                var so = new SerializedObject(mb);
                var it = so.GetIterator();
                bool changed = false;
                while (it.NextVisible(true))
                {
                    if (it.propertyType == SerializedPropertyType.Boolean && it.name.ToLower().Contains("collider"))
                    {
                        it.boolValue = true;
                        changed = true;
                    }
                }
                if (changed) so.ApplyModifiedProperties();
            }
        }
    }
}
#endif
