#if UNITY_EDITOR
using UnityEditor;
using RoomPlanner.Tools;

namespace RoomPlanner.EditorTools
{
    internal static class SetupPlumbingTool
    {
        public static void Build(RigContext ctx)
        {
            var tool = ctx.Rig.AddComponent<PlumbingController>();
            var so = new SerializedObject(tool);
            so.FindProperty("pointer").objectReferenceValue = ctx.Pointer;
            so.FindProperty("input").objectReferenceValue = ctx.Input;
            so.FindProperty("raycaster").objectReferenceValue = ctx.Raycaster;
            so.FindProperty("sceneModel").objectReferenceValue = ctx.SceneModel;
            so.FindProperty("manager").objectReferenceValue = ctx.Manager;
            so.FindProperty("reticle").objectReferenceValue = ctx.Reticle.transform;
            so.FindProperty("fixtureMaterial").objectReferenceValue = ctx.FixtureMat;
            so.FindProperty("pipeMaterial").objectReferenceValue = ctx.PlumbingMat;
            so.FindProperty("dimensionMaterial").objectReferenceValue = ctx.LineMat;
            so.ApplyModifiedProperties();
            var manager = new SerializedObject(ctx.Manager);
            manager.FindProperty("plumbing").objectReferenceValue = tool;
            manager.ApplyModifiedProperties();
        }
    }
}
#endif
