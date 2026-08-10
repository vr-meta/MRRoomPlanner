#if UNITY_EDITOR
using UnityEditor;
using RoomPlanner.Paint;

namespace RoomPlanner.EditorTools
{
    /// <summary>Paint tool: controller wiring only — it paints existing walls, no prefabs.</summary>
    internal static class SetupPaintTool
    {
        public static void Build(RigContext ctx)
        {
            ctx.Paint = ctx.Rig.AddComponent<PaintController>();
            var so = new SerializedObject(ctx.Paint);
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
