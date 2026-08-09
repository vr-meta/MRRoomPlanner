#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RoomPlanner.Floors;
using RoomPlanner.Editing;

namespace RoomPlanner.EditorTools
{
    /// <summary>Floor tool: floor prefab, preview rectangle and controller wiring.</summary>
    internal static class SetupFloorTool
    {
        public static void Build(RigContext ctx)
        {
            Floor floorPrefab = CreateFloorPrefab(ctx.FloorMat);
            ctx.FloorTool = ctx.Rig.AddComponent<FloorController>();

            var floorPrevGo = new GameObject("FloorPreview");
            floorPrevGo.transform.SetParent(ctx.Rig.transform, false);
            var floorPrev = floorPrevGo.AddComponent<LineRenderer>();
            floorPrev.useWorldSpace = true;
            floorPrev.widthMultiplier = 0.01f;
            floorPrev.numCapVertices = 4;
            floorPrev.loop = true;
            floorPrev.sharedMaterial = ctx.LineMat;
            floorPrev.enabled = false;

            var so = new SerializedObject(ctx.FloorTool);
            so.FindProperty("pointer").objectReferenceValue = ctx.Pointer;
            so.FindProperty("input").objectReferenceValue = ctx.Input;
            so.FindProperty("reticle").objectReferenceValue = ctx.Reticle.transform;
            so.FindProperty("floorPrefab").objectReferenceValue = floorPrefab;
            so.FindProperty("previewLine").objectReferenceValue = floorPrev;
            so.FindProperty("manager").objectReferenceValue = ctx.Manager;
            so.FindProperty("sceneModel").objectReferenceValue = ctx.SceneModel;
            // "blueprint" is wired by SetupBlueprintTool (built after this module)
            so.ApplyModifiedProperties();
        }

        private static Floor CreateFloorPrefab(Material mat)
        {
            var root = new GameObject("Floor");
            root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>().sharedMaterial = mat;
            root.AddComponent<Floor>();
            root.AddComponent<Selectable>();

            SetupAssets.SetLayerRecursively(root, SetupCoreRig.SelectableLayer);   // picked by Select, ignored by surface raycaster
            string path = SetupAssets.PrefabDir + "/Floor.prefab";
            var asset = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return asset.GetComponent<Floor>();
        }
    }
}
#endif
