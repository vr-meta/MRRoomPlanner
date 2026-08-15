#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RoomPlanner.Plumbing;
using RoomPlanner.Editing;

namespace RoomPlanner.EditorTools
{
    /// <summary>Plumb tool (design/28): fixture/pipe prefabs, pipe preview line and
    /// controller wiring — the SetupElectricTool pattern.</summary>
    internal static class SetupPlumbTool
    {
        public static void Build(RigContext ctx)
        {
            PlumbFixture fixturePrefab = CreateFixturePrefab(ctx.PipeMat);
            PipeRoute pipePrefab = CreatePipePrefab(ctx.PipeMat);
            var tool = ctx.Rig.AddComponent<PlumbController>();

            var prevGo = new GameObject("PipePreview");
            prevGo.transform.SetParent(ctx.Rig.transform, false);
            var prev = prevGo.AddComponent<LineRenderer>();
            prev.useWorldSpace = true;
            // preview reads as the thinnest pipe; built tubes carry the real diameter
            prev.widthMultiplier = PipeSpec.Radius(PipeDiameter.D40) * 2f;
            prev.numCapVertices = 4;
            prev.numCornerVertices = 2;
            prev.sharedMaterial = ctx.PipeMat;
            prev.enabled = false;

            var so = new SerializedObject(tool);
            so.FindProperty("pointer").objectReferenceValue = ctx.Pointer;
            so.FindProperty("input").objectReferenceValue = ctx.Input;
            so.FindProperty("raycaster").objectReferenceValue = ctx.Raycaster;
            so.FindProperty("reticle").objectReferenceValue = ctx.Reticle.transform;
            so.FindProperty("fixturePrefab").objectReferenceValue = fixturePrefab;
            so.FindProperty("pipePrefab").objectReferenceValue = pipePrefab;
            so.FindProperty("previewLine").objectReferenceValue = prev;
            so.FindProperty("manager").objectReferenceValue = ctx.Manager;
            so.FindProperty("sceneModel").objectReferenceValue = ctx.SceneModel;
            so.ApplyModifiedProperties();

            var mso = new SerializedObject(ctx.Manager);
            mso.FindProperty("plumb").objectReferenceValue = tool;
            mso.ApplyModifiedProperties();
        }

        private static PlumbFixture CreateFixturePrefab(Material mat)
        {
            var root = new GameObject("PlumbFixture");
            root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var fx = root.AddComponent<PlumbFixture>();
            root.AddComponent<PlumbFixtureParameters>();   // per-instance angle
            root.AddComponent<Selectable>();

            SetupAssets.SetLayerRecursively(root, SetupCoreRig.SelectableLayer);
            string path = SetupAssets.PrefabDir + "/PlumbFixture.prefab";
            var asset = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return asset.GetComponent<PlumbFixture>();
        }

        private static PipeRoute CreatePipePrefab(Material mat)
        {
            var root = new GameObject("PipeRoute");
            root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var route = root.AddComponent<PipeRoute>();
            root.AddComponent<PipeRouteParameters>();   // per-instance diameter / riser reserve
            root.AddComponent<PipeHandles>();           // draggable bend points
            root.AddComponent<Selectable>();

            SetupAssets.SetLayerRecursively(root, SetupCoreRig.SelectableLayer);
            string path = SetupAssets.PrefabDir + "/PipeRoute.prefab";
            var asset = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return asset.GetComponent<PipeRoute>();
        }
    }
}
#endif
