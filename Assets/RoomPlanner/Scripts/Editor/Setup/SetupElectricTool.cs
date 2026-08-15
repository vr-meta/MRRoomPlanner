#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RoomPlanner.Electrical;
using RoomPlanner.Editing;

namespace RoomPlanner.EditorTools
{
    /// <summary>Electric tool: fixture/wire prefabs, wire preview line and controller wiring
    /// (docs/design/19-electrical.md).</summary>
    internal static class SetupElectricTool
    {
        public static void Build(RigContext ctx)
        {
            ElectricFixture fixturePrefab = CreateFixturePrefab(ctx.FixtureMat, ctx.FixtureAccentMat);
            WireRoute wirePrefab = CreateWirePrefab(ctx.WireMat);
            ctx.Electric = ctx.Rig.AddComponent<ElectricController>();

            var prevGo = new GameObject("WirePreview");
            prevGo.transform.SetParent(ctx.Rig.transform, false);
            var prev = prevGo.AddComponent<LineRenderer>();
            prev.useWorldSpace = true;
            prev.widthMultiplier = WireRoute.Radius * 2f;   // preview matches the built tube
            prev.numCapVertices = 4;
            prev.numCornerVertices = 2;
            prev.sharedMaterial = ctx.WireMat;
            prev.enabled = false;

            var so = new SerializedObject(ctx.Electric);
            so.FindProperty("pointer").objectReferenceValue = ctx.Pointer;
            so.FindProperty("input").objectReferenceValue = ctx.Input;
            so.FindProperty("raycaster").objectReferenceValue = ctx.Raycaster;
            so.FindProperty("reticle").objectReferenceValue = ctx.Reticle.transform;
            so.FindProperty("fixturePrefab").objectReferenceValue = fixturePrefab;
            so.FindProperty("wirePrefab").objectReferenceValue = wirePrefab;
            so.FindProperty("previewLine").objectReferenceValue = prev;
            so.FindProperty("manager").objectReferenceValue = ctx.Manager;
            so.FindProperty("sceneModel").objectReferenceValue = ctx.SceneModel;
            so.ApplyModifiedProperties();
        }

        private static ElectricFixture CreateFixturePrefab(Material mat, Material accentMat)
        {
            var root = new GameObject("ElectricFixture");
            root.AddComponent<MeshFilter>();
            // two slots (issue #134): 0 = plastic body (paint lands here), 1 = accents
            root.AddComponent<MeshRenderer>().sharedMaterials =
                new[] { mat, accentMat != null ? accentMat : mat };
            var fx = root.AddComponent<ElectricFixture>();
            root.AddComponent<ElectricFixtureParameters>();   // per-instance posts/keys/height/reserve
            root.AddComponent<Selectable>();

            SetupAssets.SetLayerRecursively(root, SetupCoreRig.SelectableLayer);
            string path = SetupAssets.PrefabDir + "/ElectricFixture.prefab";
            var asset = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return asset.GetComponent<ElectricFixture>();
        }

        private static WireRoute CreateWirePrefab(Material mat)
        {
            var root = new GameObject("WireRoute");
            root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var route = root.AddComponent<WireRoute>();
            root.AddComponent<WireRouteParameters>();   // per-instance cable type
            root.AddComponent<RouteHandles>();          // draggable bend points
            root.AddComponent<Selectable>();

            SetupAssets.SetLayerRecursively(root, SetupCoreRig.SelectableLayer);
            string path = SetupAssets.PrefabDir + "/WireRoute.prefab";
            var asset = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return asset.GetComponent<WireRoute>();
        }
    }
}
#endif
