#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RoomPlanner.Tools;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Left-hand palette v2 = the Snap &amp; Layers STRIP (design/20 §1.9). Tool buttons are
    /// GONE — tool switching lives in the radial (L3). The strip keeps the five snap
    /// toggles (now icon buttons) and a passive current-tool chip with the 4 mm layer
    /// edge stripe. Palm-gating / lazy-follow wiring unchanged.
    /// </summary>
    internal static class SetupPalette
    {
        public static ToolMenu Build(RigContext ctx)
        {
            var root = new GameObject("ToolMenu");
            root.AddComponent<RigMarker>();
            var menu = root.AddComponent<ToolMenu>();

            // Rounded panel plate (design/20 §6.3) + a BoxCollider so the whole strip
            // blocks scene tools — a trigger pull between buttons must not place a point
            // on the wall BEHIND the menu. Rim = a slightly larger plate just behind.
            var panel = SetupAssets.MakePlateGo(root.transform, "Panel",
                new Vector3(0f, 0f, 0.006f), 0.34f, 0.085f, RoomPlanner.Core.UiTokens.RadiusL,
                ctx.PanelMat);
            panel.AddComponent<BoxCollider>().size = new Vector3(0.34f, 0.085f, 0.01f);
            SetupAssets.MakePlateGo(panel.transform, "Rim", new Vector3(0f, 0f, 0.002f),
                0.346f, 0.091f, RoomPlanner.Core.UiTokens.RadiusL + 0.003f, ctx.RimMat);

            // ---- snap toggles: icon buttons with the LED (design/20 §2.5); the LED alone
            // is the ON indicator — no active-mark strip (it read as "active tool") ----
            var snapSize = new Vector2(0.040f, 0.040f);
            const float stepX = 0.0445f;
            float x0 = -0.128f;
            MenuButton Snap(int i, string name, MenuAction action, string icon, string tooltip)
            {
                var b = SetupAssets.MakeMenuButton(root.transform, name, null, action,
                    new Vector3(x0 + i * stepX, 0.008f, 0f), snapSize, ctx.BtnMat, ctx.ActiveMat,
                    withActiveMark: false, kind: MenuButtonKind.Toggle,
                    iconId: icon, iconMat: ctx.IconMat);
                b.Tooltip = tooltip;
                return b;
            }
            var snapCornerBtn = Snap(0, "BtnSnapCorner", MenuAction.ToggleSnapCorner, "corner-snap", "Snap to corners");
            var snapEdgeBtn = Snap(1, "BtnSnapEdge", MenuAction.ToggleSnapEdge, "edge-snap", "Snap to wall edges");
            var snapGridBtn = Snap(2, "BtnSnapGrid", MenuAction.ToggleSnapGrid, "grid-snap", "Snap to 5 cm grid");
            var snapAngleBtn = Snap(3, "BtnSnapAngle", MenuAction.ToggleSnapAngle, "angle-snap", "Angle snap for walls");
            var scanBtn = Snap(4, "BtnScan", MenuAction.ToggleScan, "scan", "Room scan on / virtual world off");

            // ---- passive current-tool chip: icon + name + 4 mm layer stripe ----
            var chip = new GameObject("ToolChip");
            chip.transform.SetParent(root.transform, false);
            chip.transform.localPosition = new Vector3(0.117f, 0.008f, 0f);
            SetupAssets.MakePlateGo(chip.transform, "Bg", Vector3.zero,
                0.088f, 0.040f, RoomPlanner.Core.UiTokens.RadiusM, ctx.BtnMat);
            var stripe = SetupAssets.MakePlateGo(chip.transform, "Stripe",
                new Vector3(-0.042f, 0f, -0.001f), 0.004f, 0.034f, 0.002f, ctx.ActiveMat);

            var chipIconGo = new GameObject("Icon");
            chipIconGo.transform.SetParent(chip.transform, false);
            chipIconGo.transform.localPosition = new Vector3(-0.028f, 0f, -0.004f);
            var chipIcon = chipIconGo.AddComponent<IconRenderer>();
            var iso = new SerializedObject(chipIcon);
            iso.FindProperty("iconId").stringValue = "select-cursor";
            iso.FindProperty("material").objectReferenceValue = ctx.IconMat;
            iso.FindProperty("size").floatValue = 0.020f;
            iso.ApplyModifiedProperties();

            var chipLabel = SetupAssets.MakeTextChild(chip.transform, "Name", "Select",
                new Vector2(0.056f, 0.020f));
            chipLabel.rectTransform.localPosition = new Vector3(0.012f, 0f, -0.004f);

            // hint strip: tooltip for hovered buttons + radial discoverability line
            var tooltip = SetupAssets.MakeTextChild(root.transform, "Tooltip",
                "Tools: click left stick", new Vector2(0.24f, 0.020f));
            tooltip.rectTransform.localPosition = new Vector3(0f, -0.030f, 0f);

            var so = new SerializedObject(menu);
            Transform leftAnchor = SetupCoreRig.FindLeftControllerAnchor();
            if (leftAnchor != null) so.FindProperty("follow").objectReferenceValue = leftAnchor;
            so.FindProperty("snapCornerBtn").objectReferenceValue = snapCornerBtn;
            so.FindProperty("snapEdgeBtn").objectReferenceValue = snapEdgeBtn;
            so.FindProperty("snapGridBtn").objectReferenceValue = snapGridBtn;
            so.FindProperty("snapAngleBtn").objectReferenceValue = snapAngleBtn;
            so.FindProperty("scanBtn").objectReferenceValue = scanBtn;
            so.FindProperty("tooltipLabel").objectReferenceValue = tooltip;
            so.FindProperty("chipIcon").objectReferenceValue = chipIcon;
            so.FindProperty("chipLabel").objectReferenceValue = chipLabel;
            so.FindProperty("chipStripe").objectReferenceValue = stripe.GetComponent<Renderer>();
            so.ApplyModifiedProperties();

            root.transform.localScale = Vector3.one * 1.55f; // readable at wrist distance
            SetupAssets.SetLayerRecursively(root, SetupCoreRig.MenuLayer);
            return menu;
        }
    }
}
#endif
