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
            SetupElectricTool.Build(ctx);
            SetupPaintTool.Build(ctx);

            ToolMenuAndInspector(ctx);
            SetupCoreRig.TryEnableEffectMeshColliders();

            Selection.activeGameObject = ctx.Rig;
            EditorSceneManager.MarkSceneDirty(ctx.Rig.scene);

            EditorUtility.DisplayDialog("Tools Rig",
                "Rebuilt (UI v2, design/20):\n" +
                "  • RADIAL tool menu on LEFT STICK CLICK (flick to pick, ray+trigger works too).\n" +
                "  • Left-hand strip = snap toggles (icons) + current-tool chip.\n" +
                "  • Floating INSPECTOR v2: sliders, segmented rows, selects, numpad, swatches;\n" +
                "    grip its title bar to move it. Right stick click = previous tool.\n" +
                "  • X = undo, Y = redo; B = delete/cancel.\n\n" +
                "Next: Ctrl+S → Ctrl+B.",
                "OK");
        }

        private static void ToolMenuAndInspector(RigContext ctx)
        {
            var menu = SetupPalette.Build(ctx);
            var inspector = SetupInspector.Build(ctx);
            var radial = SetupRadial.Build(ctx);
            // Keep them under the rig so a re-run of Setup cleans them up (no duplicates).
            menu.transform.SetParent(ctx.Rig.transform, true);
            inspector.transform.SetParent(ctx.Rig.transform, true);
            radial.transform.SetParent(ctx.Rig.transform, true);

            var so = new SerializedObject(ctx.Manager);
            so.FindProperty("pointer").objectReferenceValue = ctx.Pointer;
            so.FindProperty("input").objectReferenceValue = ctx.Input;
            so.FindProperty("reticle").objectReferenceValue = ctx.Reticle.transform;
            so.FindProperty("menu").objectReferenceValue = menu;
            so.FindProperty("radial").objectReferenceValue = radial;
            so.FindProperty("inspector").objectReferenceValue = inspector;
            so.FindProperty("sceneModel").objectReferenceValue = ctx.SceneModel;
            so.FindProperty("select").objectReferenceValue = ctx.Select;
            so.FindProperty("measure").objectReferenceValue = ctx.Measure;
            so.FindProperty("wall").objectReferenceValue = ctx.WallTool;
            so.FindProperty("floor").objectReferenceValue = ctx.FloorTool;
            so.FindProperty("blueprint").objectReferenceValue = ctx.Blueprint;
            so.FindProperty("importTool").objectReferenceValue = ctx.Import;
            so.FindProperty("electric").objectReferenceValue = ctx.Electric;
            so.FindProperty("paint").objectReferenceValue = ctx.Paint;
            so.FindProperty("groundMat").objectReferenceValue = ctx.GroundMat;
            so.FindProperty("skyMat").objectReferenceValue = ctx.SkyMat;
            so.ApplyModifiedProperties();
        }
    }
}
#endif
