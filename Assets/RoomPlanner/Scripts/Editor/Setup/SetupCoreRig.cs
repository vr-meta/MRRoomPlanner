#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RoomPlanner.Measure;
using RoomPlanner.Tools;
using RoomPlanner.Editing;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Core rig: root object, pointer/input/raycaster/SceneModel/ToolManager components,
    /// reticle, layer naming and EffectMesh colliders. Tool-specific wiring lives in the
    /// per-tool Setup* modules.
    /// </summary>
    internal static class SetupCoreRig
    {
        public const int SelectableLayer = 6;   // named "Selectable"; excluded from the surface raycaster
        public const int MenuLayer = 2;         // IgnoreRaycast

        /// <summary>Remove the previous rig and any orphaned menu/inspector from earlier Setups.
        /// Primary path: our own RigMarker. Legacy path: name + component (never a bare generic
        /// name — the scene may legitimately contain another "Inspector").</summary>
        public static void DestroyPrevious()
        {
            foreach (var m in Object.FindObjectsByType<RigMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (m != null) Object.DestroyImmediate(m.gameObject);
            DestroyAllNamed("MeasureRig", typeof(MeasureController));
            DestroyAllNamed("ToolMenu", typeof(ToolMenu));
            DestroyAllNamed("Inspector", typeof(InspectorPanel));
        }

        public static void Build(RigContext ctx)
        {
            var rig = new GameObject("MeasureRig");
            rig.AddComponent<RigMarker>();
            ctx.Rig = rig;
            EnsureLighting(rig);
            ctx.Ssao = EnsureSsaoFeature();
            ctx.Raycaster = rig.AddComponent<SceneRaycaster>();
            ctx.Pointer = rig.AddComponent<PointerProvider>();
            ctx.Input = rig.AddComponent<MeasureInput>();
            ctx.SceneModel = rig.AddComponent<SceneModel>();
            ctx.Manager = rig.AddComponent<ToolManager>();

            var reticle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            reticle.name = "Reticle";
            reticle.transform.SetParent(rig.transform, false);
            reticle.transform.localScale = Vector3.one * 0.04f;
            SetupAssets.RemoveCollider(reticle);
            reticle.GetComponent<Renderer>().sharedMaterial = ctx.ReticleMat;
            ctx.Reticle = reticle;

            Transform anchor = FindControllerAnchor();
            var pso = new SerializedObject(ctx.Pointer);
            if (anchor != null) pso.FindProperty("controllerAnchor").objectReferenceValue = anchor;
            pso.ApplyModifiedProperties();
        }

        // ---- helpers shared by modules ----

        private static void DestroyAllNamed(string name, System.Type requiredComponent)
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (go != null && go.name == name && go.GetComponent(requiredComponent) != null)
                    Object.DestroyImmediate(go);
        }

        /// <summary>Give the layer a name in TagManager if it has none — the rig relies on
        /// layer 6 being 'Selectable'; on a fresh project it would silently stay unnamed.</summary>
        /// <summary>
        /// One directional light with soft shadows + a lifted ambient (design/04 realism
        /// pass): surfaces are Lit now, and without a light they render black. Under the
        /// rig, so re-running Setup never duplicates it. Ambient is flat and fairly bright —
        /// in passthrough the "sky" is the user's real room, so pitch-black shadows read
        /// as holes, not shading.
        /// </summary>
        private static void EnsureLighting(GameObject rig)
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (l.type == LightType.Directional) Object.DestroyImmediate(l.gameObject);

            var go = new GameObject("Sun");
            go.transform.SetParent(rig.transform, false);
            go.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.97f, 0.92f);   // warm daylight
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.55f;                // soft interior shading, not noir

            // A weak cool shadowless fill from the opposite side — the poor man's bounce
            // light: walls facing away from the sun still read as surfaces, not voids.
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(rig.transform, false);
            fillGo.transform.rotation = Quaternion.Euler(30f, 145f, 0f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.78f, 0.84f, 0.95f);
            fill.intensity = 0.35f;
            fill.shadows = LightShadows.None;

            // Trilight ambient (sky brighter than ground) — a cheap GI stand-in that gives
            // ceilings/floors different base shading even where no light reaches.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.62f, 0.70f);
            RenderSettings.ambientEquatorColor = new Color(0.47f, 0.48f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.31f, 0.29f);
        }

        /// <summary>
        /// SSAO renderer feature on the URP renderer asset (design/04): created once,
        /// inactive by default, toggled at runtime from the Paint settings. Source=Depth
        /// with AfterOpaque, so our custom shader needs no extra passes or keywords.
        /// </summary>
        private static Object EnsureSsaoFeature()
        {
            const string path = "Assets/Settings/URP-Renderer.asset";
            var data = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.ScriptableRendererData>(path);
            if (data == null)
            {
                Debug.LogWarning("[Setup] URP renderer asset not found — SSAO toggle unavailable");
                return null;
            }

            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
                if (sub is UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion existing)
                    return existing;

            var feature = ScriptableObject.CreateInstance<UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion>();
            feature.name = "SSAO";
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            var so = new SerializedObject(data);
            var list = so.FindProperty("m_RendererFeatures");
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
            var map = so.FindProperty("m_RendererFeatureMap");
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedProperties();

            var fso = new SerializedObject(feature);
            fso.FindProperty("m_Active").boolValue = false;                    // opt-in
            var settings = fso.FindProperty("m_Settings");
            settings.FindPropertyRelative("Source").enumValueIndex = 1;        // Depth
            settings.FindPropertyRelative("Downsample").boolValue = true;
            settings.FindPropertyRelative("AfterOpaque").boolValue = true;     // mobile-cheap
            settings.FindPropertyRelative("Intensity").floatValue = 1.2f;
            settings.FindPropertyRelative("Radius").floatValue = 0.25f;
            settings.FindPropertyRelative("DirectLightingStrength").floatValue = 0.25f;
            fso.ApplyModifiedProperties();

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] SSAO renderer feature created (inactive)");
            return feature;
        }

        public static void EnsureLayerName(int layer, string name)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var layers = so.FindProperty("layers");
            if (layers == null || layer >= layers.arraySize) return;
            var el = layers.GetArrayElementAtIndex(layer);
            if (string.IsNullOrEmpty(el.stringValue))
            {
                el.stringValue = name;
                so.ApplyModifiedProperties();
                Debug.Log($"[Setup] named layer {layer} '{name}'");
            }
        }

        public static Transform FindControllerAnchor()
        {
            foreach (var name in new[] { "RightControllerAnchor", "RightHandAnchor" })
            {
                var go = GameObject.Find(name);
                if (go != null) return go.transform;
            }
            return null;
        }

        public static Transform FindLeftControllerAnchor()
        {
            foreach (var name in new[] { "LeftControllerAnchor", "LeftHandAnchor" })
            {
                var go = GameObject.Find(name);
                if (go != null) return go.transform;
            }
            return null;
        }

        public static void TryEnableEffectMeshColliders()
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
