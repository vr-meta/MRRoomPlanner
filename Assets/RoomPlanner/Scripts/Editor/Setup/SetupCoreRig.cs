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
            ctx.Sun = EnsureLighting(rig);
            EnsureShadowQuality();
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

            BuildLocomotion(ctx, rig);
        }

        /// <summary>Left-hand navigation (design/21-locomotion.md): the portal arc and
        /// landing-ring renderers plus the TeleportLocomotion component wiring.</summary>
        private static void BuildLocomotion(RigContext ctx, GameObject rig)
        {
            ctx.Locomotion = rig.AddComponent<TeleportLocomotion>();

            var arcGo = new GameObject("TeleportArcLine");
            arcGo.transform.SetParent(rig.transform, false);
            var arc = arcGo.AddComponent<LineRenderer>();
            arc.useWorldSpace = true;
            arc.widthMultiplier = 0.012f;
            arc.numCapVertices = 2;
            arc.numCornerVertices = 2;
            arc.sharedMaterial = ctx.LineMat;
            arc.enabled = false;

            var ringGo = new GameObject("TeleportRing");
            ringGo.transform.SetParent(rig.transform, false);
            var ring = ringGo.AddComponent<LineRenderer>();
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.widthMultiplier = 0.02f;
            ring.sharedMaterial = ctx.LineMat;
            ring.enabled = false;

            var lso = new SerializedObject(ctx.Locomotion);
            lso.FindProperty("input").objectReferenceValue = ctx.Input;
            lso.FindProperty("sceneModel").objectReferenceValue = ctx.SceneModel;
            lso.FindProperty("manager").objectReferenceValue = ctx.Manager;
            lso.FindProperty("arcLine").objectReferenceValue = arc;
            lso.FindProperty("portalRing").objectReferenceValue = ring;
            lso.ApplyModifiedProperties();
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
        private static Light EnsureLighting(GameObject rig)
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (l.type == LightType.Directional) Object.DestroyImmediate(l.gameObject);

            var go = new GameObject("Sun");
            go.transform.SetParent(rig.transform, false);
            go.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.97f, 0.92f);   // warm daylight
            // 1.5: the outdoor (scan-off) world reads bright daylight, not overcast
            // (feedback 2026-08-11) — interiors still balance against the trilight ambient
            light.intensity = 1.5f;
            light.shadows = LightShadows.Soft;
            // 0.75: sun patches through the windows actually read on the floor
            // (feedback 2026-08-11) while shadows stay lifted by the ambient, not noir
            light.shadowStrength = 0.75f;

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
            return light;
        }

        /// <summary>
        /// Shadow quality on the URP asset (feedback 2026-08-11: sun patches through the
        /// windows must land inside the house): main-light shadows on, 2K map, soft,
        /// 25 m distance — enough for a house, cheap enough for Quest.
        /// </summary>
        private static void EnsureShadowQuality()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(
                "Assets/Settings/URP-Asset.asset");
            if (asset == null)
            {
                Debug.LogWarning("[Setup] URP-Asset.asset not found — shadow quality left as-is");
                return;
            }
            var so = new SerializedObject(asset);
            so.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            so.FindProperty("m_MainLightShadowmapResolution").intValue = 2048;
            so.FindProperty("m_SoftShadowsSupported").boolValue = true;
            so.FindProperty("m_ShadowDistance").floatValue = 25f;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// SSAO renderer feature on the URP renderer asset (design/04): created once,
        /// runtime default OFF (ToolManager.Start), the Rendering-page toggle opts in.
        /// Settings are (re)applied every Setup: full-res + DepthNormals — the original
        /// half-res depth-only AO "swam" separately from the geometry on device
        /// (headset feedback 2026-08-11).
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

            UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion feature = null;
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
                if (sub is UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion existing)
                {
                    feature = existing;
                    break;
                }

            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion>();
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
                Debug.Log("[Setup] SSAO renderer feature created");
            }

            var fso = new SerializedObject(feature);
            // INACTIVE: SSAO is shelved — three device attempts smeared (Multiview
            // frame-late depth); no runtime toggle references it anymore, so let the
            // build strip the shaders and skip the DepthNormals prepass cost.
            fso.FindProperty("m_Active").boolValue = false;
            var settings = fso.FindProperty("m_Settings");
            // DepthNormals + FULL resolution: the half-res (Downsample) AO drifted
            // visibly against the geometry in stereo ("тени двигаются отдельно",
            // headset feedback 2026-08-11)
            settings.FindPropertyRelative("Source").enumValueIndex = 1;        // DepthNormals
            settings.FindPropertyRelative("Downsample").boolValue = false;
            settings.FindPropertyRelative("AfterOpaque").boolValue = true;     // mobile-cheap
            settings.FindPropertyRelative("Intensity").floatValue = 1.0f;
            settings.FindPropertyRelative("Radius").floatValue = 0.18f;
            settings.FindPropertyRelative("DirectLightingStrength").floatValue = 0.25f;
            fso.ApplyModifiedProperties();

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
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
