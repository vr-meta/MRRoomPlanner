#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RoomPlanner.Tools;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Left-hand palette: tool buttons GENERATED from the registry order (labels come from
    /// each tool's PaletteLabel — adding a tool needs no palette edits) + global snap toggles.
    /// </summary>
    internal static class SetupPalette
    {
        public static ToolMenu Build(RigContext ctx)
        {
            var root = new GameObject("ToolMenu");
            root.AddComponent<RigMarker>();
            var menu = root.AddComponent<ToolMenu>();

            var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = "Panel";
            // Keep the quad's collider: the whole panel must block scene tools, or a trigger
            // pull between buttons places a point on the wall BEHIND the menu.
            panel.transform.SetParent(root.transform, false);
            panel.transform.localScale = new Vector3(0.335f, 0.16f, 1f);  // fits 7 tool buttons
            panel.transform.localPosition = new Vector3(0f, 0f, 0.006f);
            panel.GetComponent<Renderer>().sharedMaterial = ctx.PanelMat;
            SetupAssets.AddRim(panel, ctx.RimMat);

            // ---- tool buttons from the registry order (must match ToolManager's list) ----
            ITool[] tools = { ctx.Select, ctx.Measure, ctx.WallTool, ctx.FloorTool, ctx.Blueprint, ctx.Import, ctx.Paint };
            var toolButtons = new MenuButton[tools.Length];
            var toolSize = new Vector2(0.045f, 0.05f);
            float stepX = 0.0485f;                                  // centered row
            float startX = -stepX * (tools.Length - 1) * 0.5f;
            // Full-word hints for the abbreviated labels (hover tooltip, feedback 2026-08-10).
            string TooltipFor(ITool t) => t == null ? "" : t.Id switch
            {
                "select" => "Select & move objects",
                "measure" => "Tape measure",
                "wall" => "Draw walls",
                "floor" => "Draw floor slabs",
                "blueprint" => "Floor plan underlay",
                "import" => "Import IFC (Revit)",
                "paint" => "Paint surfaces",
                _ => t.PaletteLabel,
            };

            for (int i = 0; i < tools.Length; i++)
            {
                string label = tools[i] != null ? tools[i].PaletteLabel : $"T{i}";
                toolButtons[i] = SetupAssets.MakeMenuButton(root.transform, $"BtnTool{i}", label,
                    MenuAction.SelectTool, new Vector3(startX + i * stepX, 0.045f, 0f), toolSize,
                    ctx.BtnMat, ctx.ActiveMat, withActiveMark: true, toolIndex: i,
                    kind: MenuButtonKind.Radio);
                toolButtons[i].Tooltip = TooltipFor(tools[i]);
            }

            // ---- global snap toggles (Toggle kind: strip + LED, not the radio inversion) ----
            var snapSize = new Vector2(0.042f, 0.04f);
            var snapCornerBtn = SetupAssets.MakeMenuButton(root.transform, "BtnSnapCorner", "Cor", MenuAction.ToggleSnapCorner,
                new Vector3(-0.092f, -0.03f, 0f), snapSize, ctx.BtnMat, ctx.ActiveMat, true, kind: MenuButtonKind.Toggle);
            var snapEdgeBtn = SetupAssets.MakeMenuButton(root.transform, "BtnSnapEdge", "Edg", MenuAction.ToggleSnapEdge,
                new Vector3(-0.046f, -0.03f, 0f), snapSize, ctx.BtnMat, ctx.ActiveMat, true, kind: MenuButtonKind.Toggle);
            var snapGridBtn = SetupAssets.MakeMenuButton(root.transform, "BtnSnapGrid", "Grd", MenuAction.ToggleSnapGrid,
                new Vector3(0f, -0.03f, 0f), snapSize, ctx.BtnMat, ctx.ActiveMat, true, kind: MenuButtonKind.Toggle);
            var snapAngleBtn = SetupAssets.MakeMenuButton(root.transform, "BtnSnapAngle", "Ang", MenuAction.ToggleSnapAngle,
                new Vector3(0.046f, -0.03f, 0f), snapSize, ctx.BtnMat, ctx.ActiveMat, true, kind: MenuButtonKind.Toggle);
            var scanBtn = SetupAssets.MakeMenuButton(root.transform, "BtnScan", "Scan", MenuAction.ToggleScan,
                new Vector3(0.092f, -0.03f, 0f), snapSize, ctx.BtnMat, ctx.ActiveMat, true, kind: MenuButtonKind.Toggle);
            snapCornerBtn.Tooltip = "Snap to corners";
            snapEdgeBtn.Tooltip = "Snap to wall edges";
            snapGridBtn.Tooltip = "Snap to 5 cm grid";
            snapAngleBtn.Tooltip = "Angle snap for walls";
            scanBtn.Tooltip = "Room scan on / virtual world off";

            // hover tooltip strip under the buttons
            var tooltip = SetupAssets.MakeTextChild(root.transform, "Tooltip", "",
                new Vector2(0.20f, 0.024f));
            tooltip.rectTransform.localPosition = new Vector3(0f, -0.066f, 0f);
            tooltip.gameObject.SetActive(false);

            var so = new SerializedObject(menu);
            Transform leftAnchor = SetupCoreRig.FindLeftControllerAnchor();
            if (leftAnchor != null) so.FindProperty("follow").objectReferenceValue = leftAnchor;
            var arr = so.FindProperty("toolButtons");
            arr.arraySize = toolButtons.Length;
            for (int i = 0; i < toolButtons.Length; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = toolButtons[i];
            so.FindProperty("snapCornerBtn").objectReferenceValue = snapCornerBtn;
            so.FindProperty("snapEdgeBtn").objectReferenceValue = snapEdgeBtn;
            so.FindProperty("snapGridBtn").objectReferenceValue = snapGridBtn;
            so.FindProperty("snapAngleBtn").objectReferenceValue = snapAngleBtn;
            so.FindProperty("scanBtn").objectReferenceValue = scanBtn;
            so.FindProperty("tooltipLabel").objectReferenceValue = tooltip;
            so.ApplyModifiedProperties();

            root.transform.localScale = Vector3.one * 1.55f; // bigger, readable text (feedback 2026-08-10)
            SetupAssets.SetLayerRecursively(root, SetupCoreRig.MenuLayer);
            return menu;
        }
    }
}
#endif
