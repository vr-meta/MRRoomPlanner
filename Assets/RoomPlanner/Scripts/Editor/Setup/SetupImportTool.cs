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
            so.FindProperty("plumbingMat").objectReferenceValue = ctx.PlumbingMat;
            so.FindProperty("furnitureMat").objectReferenceValue = ctx.FurnitureMat;
            so.FindProperty("proxyMat").objectReferenceValue = ctx.ProxyMat;
            so.FindProperty("railingMat").objectReferenceValue = ctx.RailingMat;
            so.FindProperty("mepGlassMat").objectReferenceValue = ctx.GlassMat;
            so.ApplyModifiedProperties();

            // persistence v0: autosave on pause/quit, restore on launch (design/06)
            var autosave = ctx.Rig.AddComponent<ProjectAutosave>();
            var aso = new SerializedObject(autosave);
            aso.FindProperty("walls").objectReferenceValue = ctx.Rig.GetComponent<WallGraphRenderer>();
            aso.FindProperty("import").objectReferenceValue = ctx.Import;
            aso.FindProperty("blueprint").objectReferenceValue = ctx.Blueprint;
            aso.ApplyModifiedProperties();
        }
    }
}
#endif
