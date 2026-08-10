#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Tools;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Inspector SHELL only, v2 (design/20 §3): built at scale 1 in real meters on the
    /// 6 mm grid — background (collider blocks scene tools, auto-height at runtime),
    /// grabbable title bar, tab-strip root, rows root, selection group and the modal
    /// popups component. All parameter rows are generated at runtime by InspectorPanel.
    /// Panel origin = TOP-CENTER (rows grow downward from the title).
    /// </summary>
    internal static class SetupInspector
    {
        public static InspectorPanel Build(RigContext ctx)
        {
            var root = new GameObject("Inspector");
            root.AddComponent<RigMarker>();
            var inspector = root.AddComponent<InspectorPanel>();

            var panelRoot = new GameObject("Panel");
            panelRoot.transform.SetParent(root.transform, false);

            // rounded panel plate; a BoxCollider blocks scene tools; the rim plate behind
            // is resized together with the background by InspectorPanel.FitBackground
            var bg = SetupAssets.MakePlateGo(panelRoot.transform, "Bg",
                new Vector3(0f, -0.10f, 0.006f), PanelLayout.Width, 0.20f, UiTokens.RadiusL,
                ctx.PanelMat);
            bg.AddComponent<BoxCollider>().size = new Vector3(PanelLayout.Width, 0.20f, 0.01f);
            SetupAssets.MakePlateGo(bg.transform, "Rim", new Vector3(0f, 0f, 0.002f),
                PanelLayout.Width + 0.006f, 0.206f, UiTokens.RadiusL + 0.003f, ctx.RimMat);

            // title bar = grab handle
            var title = new GameObject("Title");
            title.transform.SetParent(panelRoot.transform, false);
            title.transform.localPosition = new Vector3(0f,
                -(UiTokens.PanelPadding + UiTokens.TitleBarHeight * 0.5f), 0f);
            title.AddComponent<BoxCollider>().size =
                new Vector3(PanelLayout.Width, UiTokens.TitleBarHeight, 0.04f);
            title.AddComponent<InspectorGrab>();
            SetupAssets.MakePlateGo(title.transform, "TitleBg", Vector3.zero,
                PanelLayout.Width - 0.008f, UiTokens.TitleBarHeight, UiTokens.RadiusM, ctx.BtnMat);
            var titleText = SetupAssets.MakeTextChild(title.transform, "TitleText",
                "Settings (grip to move)", new Vector2(0.24f, 0.020f));
            titleText.rectTransform.localPosition = new Vector3(0f, 0f, -0.006f);

            // runtime roots
            var tabsRoot = new GameObject("Tabs");
            tabsRoot.transform.SetParent(panelRoot.transform, false);
            var rowsRoot = new GameObject("Rows");
            rowsRoot.transform.SetParent(panelRoot.transform, false);

            // Selection group (shown by the Select tool when something is picked) — occupies
            // the SelectionRows band right under the title.
            var selectionGroup = new GameObject("SelectionGroup");
            selectionGroup.transform.SetParent(panelRoot.transform, false);
            float selTop = UiTokens.PanelPadding + UiTokens.TitleBarHeight;
            var selTitleLabel = SetupAssets.MakeValueLabel(selectionGroup.transform, "SelTitle",
                "Nothing selected", new Vector3(0f, -(selTop + 0.022f), -0.006f),
                new Vector2(0.25f, 0.024f));
            var selInfoLabel = SetupAssets.MakeValueLabel(selectionGroup.transform, "SelInfo", "",
                new Vector3(0f, -(selTop + 0.055f), -0.006f), new Vector2(0.25f, 0.020f));
            var hint1 = SetupAssets.MakeValueLabel(selectionGroup.transform, "SelHint",
                "Trigger: select / drag", new Vector3(0f, -(selTop + 0.088f), -0.006f),
                new Vector2(0.25f, 0.015f));
            hint1.color = UiTokens.LabelDim;
            var hint2 = SetupAssets.MakeValueLabel(selectionGroup.transform, "SelHint2",
                "B: delete    X/Y: undo/redo", new Vector3(0f, -(selTop + 0.112f), -0.006f),
                new Vector2(0.25f, 0.015f));
            hint2.color = UiTokens.LabelDim;

            // modal popup layer (design/20 §3.8)
            var popups = panelRoot.AddComponent<UiPopups>();
            var pso = new SerializedObject(popups);
            pso.FindProperty("anchor").objectReferenceValue = panelRoot.transform;
            pso.FindProperty("buttonMaterial").objectReferenceValue = ctx.BtnMat;
            pso.FindProperty("insetMaterial").objectReferenceValue = ctx.InsetMat;
            pso.FindProperty("panelMaterial").objectReferenceValue = ctx.PanelMat;
            pso.FindProperty("iconMaterial").objectReferenceValue = ctx.IconMat;
            pso.ApplyModifiedProperties();

            var so = new SerializedObject(inspector);
            so.FindProperty("panelRoot").objectReferenceValue = panelRoot;
            so.FindProperty("rowsRoot").objectReferenceValue = rowsRoot.transform;
            so.FindProperty("tabsRoot").objectReferenceValue = tabsRoot.transform;
            so.FindProperty("selectionGroup").objectReferenceValue = selectionGroup;
            so.FindProperty("background").objectReferenceValue = bg.transform;
            so.FindProperty("selTitleLabel").objectReferenceValue = selTitleLabel;
            so.FindProperty("selInfoLabel").objectReferenceValue = selInfoLabel;
            so.FindProperty("buttonMaterial").objectReferenceValue = ctx.BtnMat;
            so.FindProperty("insetMaterial").objectReferenceValue = ctx.InsetMat;
            so.FindProperty("iconMaterial").objectReferenceValue = ctx.IconMat;
            so.FindProperty("popups").objectReferenceValue = popups;
            so.ApplyModifiedProperties();

            // v2 builds in real meters on the 6 mm grid; ×1.55 on top per device feedback
            // 2026-08-10 («шрифт минимум ×3») — with the ×3.3 font recalibration this puts
            // row text at ~×5 of the first build, the regime the user asked for
            panelRoot.transform.localScale = Vector3.one * 1.55f;
            SetupAssets.SetLayerRecursively(root, SetupCoreRig.MenuLayer);
            panelRoot.SetActive(false); // shown when a tool with settings becomes active
            return inspector;
        }
    }
}
#endif
