#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RoomPlanner.Walls;
using RoomPlanner.Editing;

namespace RoomPlanner.EditorTools
{
    /// <summary>Wall tool: wall prefab, preview line and controller wiring.</summary>
    internal static class SetupWallTool
    {
        public static void Build(RigContext ctx)
        {
            Wall wallPrefab = CreateWallPrefab(ctx.WallMat, ctx.WallEdgeMat);

            ctx.WallTool = ctx.Rig.AddComponent<WallController>();

            var wallPrevGo = new GameObject("WallPreview");
            wallPrevGo.transform.SetParent(ctx.Rig.transform, false);
            var wallPrev = wallPrevGo.AddComponent<LineRenderer>();
            wallPrev.useWorldSpace = true;
            wallPrev.widthMultiplier = 0.01f;
            wallPrev.numCapVertices = 4;
            wallPrev.positionCount = 2;
            wallPrev.sharedMaterial = ctx.LineMat;
            wallPrev.enabled = false;

            var so = new SerializedObject(ctx.WallTool);
            so.FindProperty("pointer").objectReferenceValue = ctx.Pointer;
            so.FindProperty("input").objectReferenceValue = ctx.Input;
            so.FindProperty("raycaster").objectReferenceValue = ctx.Raycaster;
            so.FindProperty("reticle").objectReferenceValue = ctx.Reticle.transform;
            so.FindProperty("wallPrefab").objectReferenceValue = wallPrefab;
            so.FindProperty("previewLine").objectReferenceValue = wallPrev;
            so.FindProperty("manager").objectReferenceValue = ctx.Manager;
            so.FindProperty("sceneModel").objectReferenceValue = ctx.SceneModel;
            so.ApplyModifiedProperties();
        }

        private static Wall CreateWallPrefab(Material mat, Material edgeMat)
        {
            var root = new GameObject("Wall");
            root.AddComponent<MeshFilter>();
            root.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var wall = root.AddComponent<Wall>();
            root.AddComponent<Selectable>();

            var edges = new GameObject("Edges");
            edges.transform.SetParent(root.transform, false);
            var edgesFilter = edges.AddComponent<MeshFilter>();
            edges.AddComponent<MeshRenderer>().sharedMaterial = edgeMat;

            var wso = new SerializedObject(wall);
            wso.FindProperty("edgesFilter").objectReferenceValue = edgesFilter;
            wso.ApplyModifiedProperties();

            SetupAssets.SetLayerRecursively(root, SetupCoreRig.SelectableLayer);   // picked by Select, ignored by surface raycaster
            string path = SetupAssets.PrefabDir + "/Wall.prefab";
            var asset = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return asset.GetComponent<Wall>();
        }
    }
}
#endif
