#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RoomPlanner.Editing;

namespace RoomPlanner.EditorTools
{
    /// <summary>Select tool: controller wiring only (its UI is the shared selection group).</summary>
    internal static class SetupSelectTool
    {
        public static void Build(RigContext ctx)
        {
            ctx.Select = ctx.Rig.AddComponent<SelectController>();
            var so = new SerializedObject(ctx.Select);
            so.FindProperty("pointer").objectReferenceValue = ctx.Pointer;
            so.FindProperty("input").objectReferenceValue = ctx.Input;
            so.FindProperty("sceneModel").objectReferenceValue = ctx.SceneModel;
            so.FindProperty("reticle").objectReferenceValue = ctx.Reticle.transform;
            so.FindProperty("manager").objectReferenceValue = ctx.Manager;
            so.ApplyModifiedProperties();
        }
    }
}
#endif
