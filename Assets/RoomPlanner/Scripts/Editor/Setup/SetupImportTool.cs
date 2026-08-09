#if UNITY_EDITOR
using UnityEditor;
using RoomPlanner.Import;
using RoomPlanner.Walls;

namespace RoomPlanner.EditorTools
{
    /// <summary>IFC import tool ("Imp", design/18-ifc-import.md): controller wiring —
    /// it feeds the wall graph and the floor tool, so both must be built before it.</summary>
    internal static class SetupImportTool
    {
        public static void Build(RigContext ctx)
        {
            ctx.Import = ctx.Rig.AddComponent<ImportController>();

            var so = new SerializedObject(ctx.Import);
            so.FindProperty("manager").objectReferenceValue = ctx.Manager;
            so.FindProperty("input").objectReferenceValue = ctx.Input;
            so.FindProperty("walls").objectReferenceValue = ctx.Rig.GetComponent<WallGraphRenderer>();
            so.FindProperty("floors").objectReferenceValue = ctx.FloorTool;
            so.FindProperty("sceneModel").objectReferenceValue = ctx.SceneModel;
            so.FindProperty("stairMat").objectReferenceValue = ctx.StairMat;
            so.ApplyModifiedProperties();
        }
    }
}
#endif
