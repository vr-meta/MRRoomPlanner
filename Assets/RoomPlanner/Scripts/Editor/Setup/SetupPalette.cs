#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using RoomPlanner.Tools;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Snap strip v3: a SMALL floating panel (device feedback 2026-08-11 — off the hand,
    /// both hands belong to the tape). Row 1: five snap toggles + the rendering gear;
    /// row 2: the current-tool chip + hint. Grip anywhere on the panel parks it
    /// (InspectorGrab); ToolMenu handles gaze placement and the yaw billboard. v4 (#85):
    /// two tabs — frequently used tools first, snapping on its own.
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

            // ---- tabs (#85): file-folder tabs sitting ON TOP of the strip, not inside it.
            // The first build put them at y=0.048, overlapping the button row at 0.026
            // (headset feedback 2026-08-15 — "они наезжают на кнопки").
            const float panelTop = 0.115f * 0.5f;          // 0.0575
            var tabSize = new Vector2(0.072f, 0.022f);
            float tabY = panelTop + tabSize.y * 0.5f - 0.004f;   // overlap 4 mm = attached, not floating

            MenuButton Tab(string name, string label, int index, float x, string tip)
            {
                var b = SetupAssets.MakeMenuButton(root.transform, name, label,
                    MenuAction.SelectStripTab, new Vector3(x, tabY, 0.001f), tabSize,
                    ctx.BtnMat, ctx.ActiveMat, withActiveMark: false, kind: MenuButtonKind.Radio);
                b.Tooltip = tip;
                var bso = new SerializedObject(b);
                bso.FindProperty("toolIndex").intValue = index;   // which tab this is
                bso.ApplyModifiedProperties();
                return b;
            }
            var tabToolsBtn = Tab("TabTools", "Tools", 0, -0.117f, "Frequently used tools");
            var tabSnapBtn = Tab("TabSnap", "Snap", 1, -0.041f, "Snapping (walls, openings)");

            var toolsRow = new GameObject("ToolsRow");
            toolsRow.transform.SetParent(root.transform, false);
            var snapRow = new GameObject("SnapRow");
            snapRow.transform.SetParent(root.transform, false);

            // ---- tab 1: shortcuts to the tools reached constantly ----
            var snapSize = new Vector2(0.042f, 0.042f);
            const float stepX = 0.047f;
            float x0 = -0.1175f;

            // Radial slot order is permanent, so these indices are stable labels, not magic:
            // the strip mirrors the tools a session actually cycles through.
            var shortcuts = new List<MenuButton>();
            var shortcutDefs = new (string id, string icon, string tip)[]
            {
                ("select", "select-cursor", "Select"),
                ("furniture", "furniture", "Furniture"),
                ("wall", "wall", "Walls"),
                ("paint", "paint-roller", "Paint"),
                ("measure", "tape-measure", "Measure"),
                ("projects", "folder", "Projects"),
            };
            for (int i = 0; i < shortcutDefs.Length; i++)
            {
                var def = shortcutDefs[i];
                var b = SetupAssets.MakeMenuButton(toolsRow.transform, "Btn_" + def.id, null,
                    MenuAction.SelectTool, new Vector3(x0 + i * stepX, 0.026f, 0f), snapSize,
                    ctx.BtnMat, ctx.ActiveMat, withActiveMark: false, kind: MenuButtonKind.Radio,
                    iconId: def.icon, iconMat: ctx.IconMat);
                b.Tooltip = def.tip;
                var bso = new SerializedObject(b);
                bso.FindProperty("toolIndex").intValue = ToolManager.RegistryIndexOf(def.id);
                bso.ApplyModifiedProperties();
                shortcuts.Add(b);
            }

            // ---- tab 2: snap toggles + the rendering gear ----
            MenuButton Btn(int i, string name, MenuAction action, string icon, string tooltip,
                MenuButtonKind kind)
            {
                var b = SetupAssets.MakeMenuButton(snapRow.transform, name, null, action,
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
                "Tools: press A", new Vector2(0.15f, 0.016f));
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
            so.FindProperty("toolsRow").objectReferenceValue = toolsRow;
            so.FindProperty("snapRow").objectReferenceValue = snapRow;
            so.FindProperty("tabToolsBtn").objectReferenceValue = tabToolsBtn;
            so.FindProperty("tabSnapBtn").objectReferenceValue = tabSnapBtn;
            var shortcutsProp = so.FindProperty("toolShortcuts");
            shortcutsProp.arraySize = shortcuts.Count;
            for (int i = 0; i < shortcuts.Count; i++)
                shortcutsProp.GetArrayElementAtIndex(i).objectReferenceValue = shortcuts[i];
            so.ApplyModifiedProperties();
            menu.SetTab(0);   // the strip opens on the shortcuts, not on snapping

            // real meters, no compensating scale — the strip is deliberately SMALL now
            root.transform.localScale = Vector3.one;
            SetupAssets.SetLayerRecursively(root, SetupCoreRig.MenuLayer);
            return menu;
        }
    }
}
#endif
