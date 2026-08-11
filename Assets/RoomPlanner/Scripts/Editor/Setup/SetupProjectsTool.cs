#if UNITY_EDITOR
using UnityEditor;
using RoomPlanner.Import;

namespace RoomPlanner.EditorTools
{
    /// <summary>Projects tool ("Proj", #58, design/06 «Проекты v1»): panel-only tool,
    /// no scene visuals — wiring only. Runs after SetupImportTool: it drives the
    /// ImportController (scene clear) and the ProjectAutosave (save/load).</summary>
    internal static class SetupProjectsTool
    {
        public static void Build(RigContext ctx)
        {
            var tool = ctx.Rig.AddComponent<ProjectsController>();

            var so = new SerializedObject(tool);
            so.FindProperty("manager").objectReferenceValue = ctx.Manager;
            so.FindProperty("input").objectReferenceValue = ctx.Input;
            so.FindProperty("import").objectReferenceValue = ctx.Import;
            so.FindProperty("autosave").objectReferenceValue =
                ctx.Rig.GetComponent<ProjectAutosave>();
            so.ApplyModifiedProperties();

            var mso = new SerializedObject(ctx.Manager);
            mso.FindProperty("projects").objectReferenceValue = tool;
            mso.ApplyModifiedProperties();
        }
    }
}
#endif
