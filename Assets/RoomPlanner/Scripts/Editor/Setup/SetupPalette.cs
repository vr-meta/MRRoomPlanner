#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RoomPlanner.Tools;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Snap strip v3: a SMALL floating panel (device feedback 2026-08-11 — off the hand,
    /// both hands belong to the tape). Row 1: five snap toggles + the rendering gear;
    /// row 2: the current-tool chip + hint. Grip anywhere on the panel parks it
    /// (InspectorGrab); ToolMenu handles gaze placement and the yaw billboard.
    /// </summary>
    internal static class SetupPalette
    {
        public static ToolMenu Build(RigContext ctx)
        {
            var root = new GameObject("ToolMenu");
            root.AddComponent<RigMarker>();
            var menu = root.AddComponent<ToolMenu>();

            var panel = SetupAssets.MakePlateGo(root.transform, "Panel",
                new Vector3(0f, 0f, 0.006f), 0.31f, 0.115f, RoomPlanner.Core.UiTokens.RadiusL,
                ctx.PanelMat);
            var panelCol = panel.AddComponent<BoxCollider>();
            panelCol.size = new Vector3(0.31f, 0.115f, 0.01f);
            var grab = panel.AddComponent<InspectorGrab>();
            grab.MoveTarget = root.transform;   // grip anywhere on the strip = park it
            SetupAssets.MakePlateGo(panel.transform, "Rim", new Vector3(0f, 0f, 0.002f),
                0.316f, 0.121f, RoomPlanner.Core.UiTokens.RadiusL + 0.003f, ctx.RimMat);

            // ---- row 1: snap toggles + the rendering gear ----
            var snapSize = new Vector2(0.042f, 0.042f);
            const float stepX = 0.047f;
            float x0 = -0.1175f;
            MenuButton Btn(int i, string name, MenuAction action, string icon, string tooltip,
                MenuButtonKind kind)
            {
                var b = SetupAssets.MakeMenuButton(root.transform, name, null, action,
                    new Vector3(x0 + i * stepX, 0.026f, 0f), snapSize, ctx.BtnMat, ctx.ActiveMat,
                    withActiveMark: false, kind: kind, iconId: icon, iconMat: ctx.IconMat);
                b.Tooltip = tooltip;
                return b;
            }
            var snapCornerBtn = Btn(0, "BtnSnapCorner", MenuAction.ToggleSnapCorner, "corner-snap",
                "Snap to corners", MenuButtonKind.Toggle);
            var snapEdgeBtn = Btn(1, "BtnSnapEdge", MenuAction.ToggleSnapEdge, "edge-snap",
                "Snap to wall edges", MenuButtonKind.Toggle);
            var snapGridBtn = Btn(2, "BtnSnapGrid", MenuAction.ToggleSnapGrid, "grid-snap",
                "Snap to 5 cm grid", MenuButtonKind.Toggle);
            var snapAngleBtn = Btn(3, "BtnSnapAngle", MenuAction.ToggleSnapAngle, "angle-snap",
                "Angle snap for walls", MenuButtonKind.Toggle);
            var scanBtn = Btn(4, "BtnScan", MenuAction.ToggleScan, "scan",
                "Room scan on / virtual world off", MenuButtonKind.Toggle);
            var gearBtn = Btn(5, "BtnRender", MenuAction.ToggleRenderSettings, "gear",
                "Rendering settings", MenuButtonKind.Momentary);
            _ = gearBtn;

            // ---- row 2: passive current-tool chip + hint ----
            var chip = new GameObject("ToolChip");
            chip.transform.SetParent(root.transform, false);
            chip.transform.localPosition = new Vector3(-0.078f, -0.028f, 0f);
            SetupAssets.MakePlateGo(chip.transform, "Bg", Vector3.zero,
                0.135f, 0.036f, RoomPlanner.Core.UiTokens.RadiusM, ctx.BtnMat);
            var stripe = SetupAssets.MakePlateGo(chip.transform, "Stripe",
                new Vector3(-0.0655f, 0f, -0.001f), 0.004f, 0.030f, 0.002f, ctx.ActiveMat);

            var chipIconGo = new GameObject("Icon");
            chipIconGo.transform.SetParent(chip.transform, false);
            chipIconGo.transform.localPosition = new Vector3(-0.048f, 0f, -0.004f);
            var chipIcon = chipIconGo.AddComponent<IconRenderer>();
            var iso = new SerializedObject(chipIcon);
            iso.FindProperty("iconId").stringValue = "select-cursor";
            iso.FindProperty("material").objectReferenceValue = ctx.IconMat;
            iso.FindProperty("size").floatValue = 0.022f;
            iso.ApplyModifiedProperties();

            var chipLabel = SetupAssets.MakeTextChild(chip.transform, "Name", "Select",
                new Vector2(0.09f, 0.020f));
            chipLabel.rectTransform.localPosition = new Vector3(0.014f, 0f, -0.004f);

            var tooltip = SetupAssets.MakeTextChild(root.transform, "Tooltip",
                "Tools: hold A", new Vector2(0.15f, 0.016f));
            tooltip.rectTransform.localPosition = new Vector3(0.078f, -0.028f, -0.004f);

            var so = new SerializedObject(menu);
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

            // real meters, no compensating scale — the strip is deliberately SMALL now
            root.transform.localScale = Vector3.one;
            SetupAssets.SetLayerRecursively(root, SetupCoreRig.MenuLayer);
            return menu;
        }
    }
}
#endif
