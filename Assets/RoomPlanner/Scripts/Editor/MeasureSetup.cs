#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Одно-кнопочная сборка рига: RoomPlanner → Setup Measure Rig. Тонкий оркестратор —
    /// вся работа в модулях Editor/Setup/* (design/14-modularity.md §4): ассеты, ядро рига,
    /// по модулю на инструмент, палитра, каркас инспектора.
    /// Новый инструмент: свой SetupXxxTool.Build(ctx) + строка в реестре ToolManager.Start
    /// + позиция в списке SetupPalette. Идемпотентно; GUID ассетов не меняются.
    /// </summary>
    public static class MeasureSetup
    {
        [MenuItem("RoomPlanner/Setup Measure Rig")]
        public static void SetupMeasureRig()
        {
            SetupCoreRig.DestroyPrevious();
            SetupCoreRig.EnsureLayerName(SetupCoreRig.SelectableLayer, "Selectable");

            var ctx = new RigContext();
            SetupAssets.CreateAll(ctx);
            SetupCoreRig.Build(ctx);

            // Tool modules — order defines the registry/palette order (ToolManager.Start).
            SetupSelectTool.Build(ctx);
            SetupMeasureTool.Build(ctx);
            SetupWallTool.Build(ctx);
            SetupFloorTool.Build(ctx);
            SetupBlueprintTool.Build(ctx);
            SetupImportTool.Build(ctx);
            SetupPaintTool.Build(ctx);

            ToolMenuAndInspector(ctx);
            SetupCoreRig.TryEnableEffectMeshColliders();

            Selection.activeGameObject = ctx.Rig;
            EditorSceneManager.MarkSceneDirty(ctx.Rig.scene);

            EditorUtility.DisplayDialog("Tools Rig",
                "Rebuilt (modular setup): SELECT + measure + walls + floor.\n" +
                "  • Left-hand PALETTE = tool buttons (from registry) + snap toggles.\n" +
                "  • Floating INSPECTOR renders rows from each tool's SettingsSchema at runtime;\n" +
                "    grip its title bar to move it. Select shows the picked object.\n" +
                "  • X = undo, Y = redo; B = delete/cancel.\n\n" +
                "Next: Ctrl+S → Ctrl+B.",
                "OK");
        }

        private static void ToolMenuAndInspector(RigContext ctx)
        {
            var menu = SetupPalette.Build(ctx);
            var inspector = SetupInspector.Build(ctx);
            // Keep them under the rig so a re-run of Setup cleans them up (no duplicates).
            menu.transform.SetParent(ctx.Rig.transform, true);
            inspector.transform.SetParent(ctx.Rig.transform, true);

            var so = new SerializedObject(ctx.Manager);
            so.FindProperty("pointer").objectReferenceValue = ctx.Pointer;
            so.FindProperty("input").objectReferenceValue = ctx.Input;
            so.FindProperty("reticle").objectReferenceValue = ctx.Reticle.transform;
            so.FindProperty("menu").objectReferenceValue = menu;
            so.FindProperty("inspector").objectReferenceValue = inspector;
            so.FindProperty("sceneModel").objectReferenceValue = ctx.SceneModel;
            so.FindProperty("select").objectReferenceValue = ctx.Select;
            so.FindProperty("measure").objectReferenceValue = ctx.Measure;
            so.FindProperty("wall").objectReferenceValue = ctx.WallTool;
            so.FindProperty("floor").objectReferenceValue = ctx.FloorTool;
            so.FindProperty("blueprint").objectReferenceValue = ctx.Blueprint;
            so.FindProperty("importTool").objectReferenceValue = ctx.Import;
            so.FindProperty("paint").objectReferenceValue = ctx.Paint;
            so.FindProperty("groundMat").objectReferenceValue = ctx.GroundMat;
            so.ApplyModifiedProperties();
        }
    }
}
#endif
