#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RoomPlanner.Tools;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Inspector SHELL only: background (with collider — blocks scene tools), grabbable
    /// title bar, rows root and the selection group. Parameter rows are generated at
    /// runtime by InspectorPanel from the active tool's SettingsSchema.
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

            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "Bg";   // collider kept — the panel background must block scene tools
            bg.transform.SetParent(panelRoot.transform, false);
            bg.transform.localScale = new Vector3(0.36f, 0.44f, 1f);
            bg.transform.localPosition = new Vector3(0f, 0f, 0.006f);
            bg.GetComponent<Renderer>().sharedMaterial = ctx.PanelMat;
            SetupAssets.AddRim(bg, ctx.RimMat);   // child of bg → follows runtime auto-height

            // title bar = grab handle
            var title = new GameObject("Title");
            title.transform.SetParent(panelRoot.transform, false);
            title.transform.localPosition = new Vector3(0f, 0.19f, 0f);
            title.AddComponent<BoxCollider>().size = new Vector3(0.36f, 0.05f, 0.04f);
            title.AddComponent<InspectorGrab>();
            var titleBg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            titleBg.name = "TitleBg"; SetupAssets.RemoveCollider(titleBg);
            titleBg.transform.SetParent(title.transform, false);
            titleBg.transform.localScale = new Vector3(0.36f, 0.05f, 1f);
            titleBg.GetComponent<Renderer>().sharedMaterial = ctx.BtnMat;
            SetupAssets.MakeValueLabel(title.transform, "TitleText", "Settings (grip to move)",
                new Vector3(0f, 0f, 0f), new Vector2(0.34f, 0.045f));

            // rows are generated at runtime under this root
            var rowsRoot = new GameObject("Rows");
            rowsRoot.transform.SetParent(panelRoot.transform, false);

            // Selection group (shown by the Select tool when something is picked).
            var selectionGroup = new GameObject("SelectionGroup");
            selectionGroup.transform.SetParent(panelRoot.transform, false);
            var selTitleLabel = SetupAssets.MakeValueLabel(selectionGroup.transform, "SelTitle", "Nothing selected",
                new Vector3(0f, 0.11f, 0f), new Vector2(0.32f, 0.055f));
            var selInfoLabel = SetupAssets.MakeValueLabel(selectionGroup.transform, "SelInfo", "",
                new Vector3(0f, 0.03f, 0f), new Vector2(0.32f, 0.05f));
            SetupAssets.MakeValueLabel(selectionGroup.transform, "SelHint", "Trigger: select / drag",
                new Vector3(0f, -0.06f, 0f), new Vector2(0.32f, 0.04f));
            SetupAssets.MakeValueLabel(selectionGroup.transform, "SelHint2", "B: delete    X/Y: undo/redo",
                new Vector3(0f, -0.12f, 0f), new Vector2(0.32f, 0.04f));

            var so = new SerializedObject(inspector);
            so.FindProperty("panelRoot").objectReferenceValue = panelRoot;
            so.FindProperty("rowsRoot").objectReferenceValue = rowsRoot.transform;
            so.FindProperty("selectionGroup").objectReferenceValue = selectionGroup;
            so.FindProperty("background").objectReferenceValue = bg.transform;
            so.FindProperty("selTitleLabel").objectReferenceValue = selTitleLabel;
            so.FindProperty("selInfoLabel").objectReferenceValue = selInfoLabel;
            so.FindProperty("buttonMaterial").objectReferenceValue = ctx.BtnMat;
            so.ApplyModifiedProperties();

            // 1.3 (was 1.8): with the raised font floor the panel stops dominating the FOV
            // (UX v2 P0.4) while text stays ≥ ~0.7° at 65 cm.
            panelRoot.transform.localScale = Vector3.one * 1.3f;
            SetupAssets.SetLayerRecursively(root, SetupCoreRig.MenuLayer);
            panelRoot.SetActive(false); // shown when a tool with settings becomes active
            return inspector;
        }
    }
}
#endif
