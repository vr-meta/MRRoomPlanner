#if UNITY_EDITOR
using UnityEditor;
using RoomPlanner.Floors;

namespace RoomPlanner.EditorTools
{
    /// <summary>Blueprint tool ("Plan"): controller wiring; the plan texture lands on the
    /// shared floor-top material. Also hands the floor tool its plan-placement source.</summary>
    internal static class SetupBlueprintTool
    {
        public static void Build(RigContext ctx)
        {
            ctx.Blueprint = ctx.Rig.AddComponent<BlueprintController>();

            var so = new SerializedObject(ctx.Blueprint);
            so.FindProperty("pointer").objectReferenceValue = ctx.Pointer;
            so.FindProperty("input").objectReferenceValue = ctx.Input;
            so.FindProperty("reticle").objectReferenceValue = ctx.Reticle.transform;
            so.FindProperty("manager").objectReferenceValue = ctx.Manager;
            so.FindProperty("floors").objectReferenceValue = ctx.FloorTool;
            so.FindProperty("planMaterial").objectReferenceValue = ctx.FloorMat;
            so.ApplyModifiedProperties();

            // the floor tool reads the plan placement from here
            var fso = new SerializedObject(ctx.FloorTool);
            fso.FindProperty("blueprint").objectReferenceValue = ctx.Blueprint;
            fso.ApplyModifiedProperties();
        }
    }
}
#endif
