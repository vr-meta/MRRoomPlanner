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
