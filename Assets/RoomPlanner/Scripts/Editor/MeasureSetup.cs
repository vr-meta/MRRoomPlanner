#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RoomPlanner.Measure;

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

            TryEnableEffectMeshColliders();

            Selection.activeGameObject = rig;
            EditorSceneManager.MarkSceneDirty(rig.scene);

            EditorUtility.DisplayDialog("Measure Rig",
                "Пересоздано (URP).\n\n" +
                "Управление:\n" +
                "• триггер по стене — точка A, затем B;\n" +
                "• навёл на ЛЮБУЮ точку → всплывает «+» рядом — триггер по нему продолжает цепочку;\n" +
                "• навёл на точку (шарик) + удержание триггера — тащить; + B — удалить измерение;\n" +
                "• совпавшие точки схлопываются в одну; в режиме таскания «+» скрыт;\n" +
                "• грип — привязка к оси; концы магнитятся друг к другу.\n\n" +
                "Дальше: Ctrl+S → Ctrl+B.",
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
            labelGo.transform.localScale = Vector3.one * 0.2f;
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text = "0 см";
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
