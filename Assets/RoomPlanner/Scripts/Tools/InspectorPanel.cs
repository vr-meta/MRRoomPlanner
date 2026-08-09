using System.Collections.Generic;
using TMPro;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// Floating, world-anchored settings panel. Rows are generated AT RUNTIME from the
    /// active tool's <see cref="SettingsSchema"/> (design/14-modularity.md) — the setup
    /// builds only the shell (background, title bar, selection group). Buttons are ordinary
    /// MenuButtons with a runtime OnClick, routed through ToolManager.Execute. Grabbable by
    /// the title bar (grip); faces the camera (yaw billboard).
    /// </summary>
    public class InspectorPanel : MonoBehaviour
    {
        private const int MenuLayer = 2;

        [SerializeField] private GameObject panelRoot;      // toggled visible/hidden
        [SerializeField] private Transform rowsRoot;        // parent for generated rows
        [SerializeField] private GameObject selectionGroup; // shown when the Select tool has a selection
        [SerializeField] private Material buttonMaterial;   // background for generated buttons

        [Header("Selection labels")]
        [SerializeField] private TMP_Text selTitleLabel;
        [SerializeField] private TMP_Text selInfoLabel;

        // row layout (local units of the un-scaled panel; the whole panel is scaled in setup)
        private const float RowTop = 0.13f;
        private const float RowStep = 0.055f;
        private static readonly Vector2 BtnSize = new Vector2(0.045f, 0.045f);

        private SettingsSchema _bound;
        private readonly List<(SettingField field, TMP_Text valueLabel)> _rows = new();

        private Transform _cam;

        public bool IsShown => panelRoot != null && panelRoot.activeSelf;
        public Transform Panel => panelRoot != null ? panelRoot.transform : transform;

        /// <summary>Show rows for the schema (null/empty → none) and/or the selection group.</summary>
        public void ShowFor(SettingsSchema schema, bool showSelection)
        {
            if (panelRoot == null) return;
            bool showSettings = schema != null && schema.Fields.Count > 0;
            bool show = showSettings || showSelection;

            if (show && !panelRoot.activeSelf) PlaceInFront();
            panelRoot.SetActive(show);
            if (selectionGroup != null) selectionGroup.SetActive(showSelection);
            if (rowsRoot != null) rowsRoot.gameObject.SetActive(showSettings);
            if (showSettings) Bind(schema);
            RefreshValues();
        }

        /// <summary>Fill the selection group labels (title + one-line description).</summary>
        public void SetSelection(string title, string info)
        {
            if (selTitleLabel != null) selTitleLabel.text = title;
            if (selInfoLabel != null) selInfoLabel.text = info;
        }

        /// <summary>Update value labels from the bound schema's delegates.</summary>
        public void RefreshValues()
        {
            foreach (var (field, label) in _rows)
                if (label != null && field.Value != null) label.text = field.Value();
        }

        // ---- runtime row generation ----

        private void Bind(SettingsSchema schema)
        {
            if (ReferenceEquals(_bound, schema)) return;
            _bound = schema;

            foreach (Transform child in rowsRoot) Destroy(child.gameObject);
            _rows.Clear();

            float y = RowTop;
            foreach (var f in schema.Fields)
            {
                BuildRow(f, y);
                y -= RowStep;
            }
        }

        private void BuildRow(SettingField f, float y)
        {
            MakeLabel($"{f.Id}Cap", f.Caption, new Vector3(-0.115f, y, 0f), new Vector2(0.13f, 0.04f));

            TMP_Text value;
            if (f.Kind == SettingKind.Stepper)
            {
                MakeButton($"{f.Id}-", "−", new Vector3(-0.01f, y, 0f), f.Decrease);
                value = MakeLabel($"{f.Id}Val", "—", new Vector3(0.06f, y, 0f), new Vector2(0.09f, 0.04f));
                MakeButton($"{f.Id}+", "+", new Vector3(0.14f, y, 0f), f.Increase);
            }
            else // Cycle
            {
                value = MakeLabel($"{f.Id}Val", "—", new Vector3(0.03f, y, 0f), new Vector2(0.11f, 0.04f));
                MakeButton($"{f.Id}>", ">", new Vector3(0.14f, y, 0f), f.Increase);
            }
            _rows.Add((f, value));
        }

        private void MakeButton(string name, string label, Vector3 lp, System.Action onClick)
        {
            var root = new GameObject(name) { layer = MenuLayer };
            root.transform.SetParent(rowsRoot, false);
            root.transform.localPosition = lp;
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(BtnSize.x, BtnSize.y, 0.04f);
            var mb = root.AddComponent<MenuButton>();
            mb.OnClick = onClick;

            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "Bg";
            bg.layer = MenuLayer;
            Destroy(bg.GetComponent<Collider>());   // the BoxCollider above is the hit target
            bg.transform.SetParent(root.transform, false);
            bg.transform.localScale = new Vector3(BtnSize.x, BtnSize.y, 1f);
            if (buttonMaterial != null) bg.GetComponent<Renderer>().sharedMaterial = buttonMaterial;

            MakeText(root.transform, "Label", label, BtnSize);
        }

        private TMP_Text MakeLabel(string name, string text, Vector3 lp, Vector2 size)
        {
            var root = new GameObject(name) { layer = MenuLayer };
            root.transform.SetParent(rowsRoot, false);
            root.transform.localPosition = lp;
            return MakeText(root.transform, "Text", text, size);
        }

        private static TMP_Text MakeText(Transform parent, string name, string text, Vector2 size)
        {
            var t = new GameObject(name) { layer = MenuLayer };
            t.transform.SetParent(parent, false);
            t.transform.localPosition = new Vector3(0f, 0f, -0.006f);
            var tmp = t.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.enableWordWrapping = false;
            // same sizing rules as the setup-built labels: full cell width, readable floor
            tmp.rectTransform.sizeDelta = new Vector2(size.x * 1.6f, size.y * 0.95f);
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = size.y * 0.5f;
            tmp.fontSizeMax = size.y * 0.9f;
            return tmp;
        }

        // ---- placement ----

        /// <summary>Move the panel so the grabbed point tracks the pointer (called while dragging).</summary>
        public void MovePanel(Vector3 worldPos)
        {
            if (panelRoot != null) panelRoot.transform.position = worldPos;
        }

        private void PlaceInFront()
        {
            EnsureCam();
            if (_cam == null) return;
            panelRoot.transform.position =
                _cam.position + _cam.forward * 0.55f + _cam.right * 0.12f - _cam.up * 0.12f;
        }

        private void LateUpdate()
        {
            if (panelRoot == null || !panelRoot.activeSelf) return;
            EnsureCam();
            if (_cam == null) return;
            Vector3 dir = panelRoot.transform.position - _cam.position;
            if (dir.sqrMagnitude > 0.0001f)
                panelRoot.transform.rotation = Quaternion.LookRotation(dir);
        }

        private void EnsureCam()
        {
            if (_cam == null && Camera.main != null) _cam = Camera.main.transform;
        }
    }
}
