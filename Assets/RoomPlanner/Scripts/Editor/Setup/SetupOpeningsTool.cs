#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RoomPlanner.Walls;

namespace RoomPlanner.EditorTools
{
    /// <summary>Openings tool ("Open", design/03 v1, audit F1): controller wiring plus
    /// the ghost rectangle that previews the door/window/garage frame on the wall.</summary>
    internal static class SetupOpeningsTool
    {
        public static void Build(RigContext ctx)
        {
            var tool = ctx.Rig.AddComponent<OpeningsController>();

            var ghostGo = new GameObject("OpeningGhost");
            ghostGo.transform.SetParent(ctx.Rig.transform, false);
            var ghost = ghostGo.AddComponent<LineRenderer>();
            ghost.useWorldSpace = true;
            ghost.widthMultiplier = 0.012f;
            ghost.numCapVertices = 2;
            ghost.positionCount = 5;
            ghost.sharedMaterial = ctx.LineMat;
            ghost.enabled = false;

            var so = new SerializedObject(tool);
            so.FindProperty("pointer").objectReferenceValue = ctx.Pointer;
            so.FindProperty("input").objectReferenceValue = ctx.Input;
            so.FindProperty("manager").objectReferenceValue = ctx.Manager;
            so.FindProperty("sceneModel").objectReferenceValue = ctx.SceneModel;
            so.FindProperty("walls").objectReferenceValue = ctx.Rig.GetComponent<WallGraphRenderer>();
            so.FindProperty("ghost").objectReferenceValue = ghost;
            so.ApplyModifiedProperties();

            var mso = new SerializedObject(ctx.Manager);
            mso.FindProperty("openings").objectReferenceValue = tool;
            mso.ApplyModifiedProperties();
        }
    }
}
#endif
