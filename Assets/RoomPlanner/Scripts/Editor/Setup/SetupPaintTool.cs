#if UNITY_EDITOR
using UnityEditor;
using RoomPlanner.Tools;

namespace RoomPlanner.EditorTools
{
    /// <summary>Paint tool ("Pnt", design/04 v1): controller wiring only — presets live
    /// in code, the color row is schema-generated.</summary>
    internal static class SetupPaintTool
    {
        public static void Build(RigContext ctx)
        {
            ctx.Paint = ctx.Rig.AddComponent<PaintController>();

            var so = new SerializedObject(ctx.Paint);
            so.FindProperty("pointer").objectReferenceValue = ctx.Pointer;
            so.FindProperty("input").objectReferenceValue = ctx.Input;
            so.FindProperty("manager").objectReferenceValue = ctx.Manager;
            so.FindProperty("sceneModel").objectReferenceValue = ctx.SceneModel;
            so.FindProperty("walls").objectReferenceValue = ctx.Rig.GetComponent<RoomPlanner.Walls.WallGraphRenderer>();
            so.FindProperty("ssaoFeature").objectReferenceValue = ctx.Ssao;
            so.ApplyModifiedProperties();
        }
    }
}
#endif
