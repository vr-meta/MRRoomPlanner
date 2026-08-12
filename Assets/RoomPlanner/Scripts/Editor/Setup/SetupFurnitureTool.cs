#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RoomPlanner.Furniture;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Furniture tool ("Furn", design/27, issue #71): the catalog library and the glTF
    /// loader live on the rig next to the controller, plus a footprint ghost (the same
    /// LineRenderer language the Openings tool uses) and a parent for placed pieces so
    /// the hierarchy stays readable.
    /// </summary>
    internal static class SetupFurnitureTool
    {
        public static void Build(RigContext ctx)
        {
            var library = ctx.Rig.GetComponent<FurnitureLibrary>();
            if (library == null) library = ctx.Rig.AddComponent<FurnitureLibrary>();

            var loader = ctx.Rig.GetComponent<FurnitureLoader>();
            if (loader == null) loader = ctx.Rig.AddComponent<FurnitureLoader>();
            var lso = new SerializedObject(loader);
            lso.FindProperty("library").objectReferenceValue = library;
            lso.ApplyModifiedProperties();

            var tool = ctx.Rig.AddComponent<FurnitureController>();

            var ghostGo = new GameObject("FurnitureGhost");
            ghostGo.transform.SetParent(ctx.Rig.transform, false);
            var ghost = ghostGo.AddComponent<LineRenderer>();
            ghost.useWorldSpace = true;
            ghost.widthMultiplier = 0.012f;
            ghost.numCapVertices = 2;
            ghost.positionCount = 5;
            ghost.sharedMaterial = ctx.LineMat;
            ghost.enabled = false;

            // Placed pieces live OUTSIDE the rig: the rig moves with the user (teleport,
            // walking), and furniture must stay where the room is.
            var root = GameObject.Find("Furniture");
            if (root == null) root = new GameObject("Furniture");

            var so = new SerializedObject(tool);
            so.FindProperty("pointer").objectReferenceValue = ctx.Pointer;
            so.FindProperty("input").objectReferenceValue = ctx.Input;
            so.FindProperty("manager").objectReferenceValue = ctx.Manager;
            so.FindProperty("sceneModel").objectReferenceValue = ctx.SceneModel;
            so.FindProperty("raycaster").objectReferenceValue = ctx.Raycaster;
            so.FindProperty("library").objectReferenceValue = library;
            so.FindProperty("loader").objectReferenceValue = loader;
            so.FindProperty("ghost").objectReferenceValue = ghost;
            so.FindProperty("reticle").objectReferenceValue = ctx.Reticle.transform;
            so.FindProperty("itemsRoot").objectReferenceValue = root.transform;
            so.ApplyModifiedProperties();

            var mso = new SerializedObject(ctx.Manager);
            mso.FindProperty("furniture").objectReferenceValue = tool;
            mso.ApplyModifiedProperties();
        }
    }
}
#endif
