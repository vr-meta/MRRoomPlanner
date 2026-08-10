using System.Collections.Generic;
using TMPro;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// Floating, world-anchored settings panel, v2 (design/20 §2–§3): renders the full
    /// widget vocabulary from the active tool's <see cref="SettingsSchema"/> on the 6 mm
    /// grid — tabs, steppers, sliders, segmented, selects, toggles, numeric fields,
    /// swatches, action/destructive buttons, readouts, progress and headers. The setup
    /// builds only the shell; every row is generated at runtime. Grabbable by the title
    /// bar (grip); yaw billboard; grip-parked position is sacred.
    /// </summary>
    public class InspectorPanel : MonoBehaviour
    {
        private const int MenuLayer = 2;

        [SerializeField] private GameObject panelRoot;      // toggled visible/hidden
        [SerializeField] private Transform rowsRoot;        // parent for generated rows
        [SerializeField] private Transform tabsRoot;        // parent for the tab strip
        [SerializeField] private GameObject selectionGroup; // shown when Select has a selection
        [SerializeField] private Transform background;      // bg quad, auto-sized to content
        [SerializeField] private Material buttonMaterial;
        [SerializeField] private Material insetMaterial;    // slider tracks, fields, wells
        [SerializeField] private Material iconMaterial;
        [SerializeField] private UiPopups popups;

        [Header("Selection labels")]
        [SerializeField] private TMP_Text selTitleLabel;
        [SerializeField] private TMP_Text selInfoLabel;

        // selection block occupies this many row slots above the schema rows
        private const int SelectionRows = 4;

        private SettingsSchema _bound;
        private SettingsSchema _boundPage;
        private int _boundSelRows = -1;
        private readonly List<System.Action> _refreshers = new();
        private readonly List<SliderWidget> _sliders = new();
        private readonly List<(SettingField field, Transform fill, float width)> _progressBars = new();
        private readonly Dictionary<(float w, float h, float r), Mesh> _plateCache = new();

        private Transform _cam;
        private bool _everPlaced;   // grip-parked position is sacred — never auto-recenter

        public bool IsShown => panelRoot != null && panelRoot.activeSelf;
        public Transform Panel => panelRoot != null ? panelRoot.transform : transform;
        public UiPopups Popups => popups;

        /// <summary>Show rows for the schema (null/empty → none) and/or the selection group.</summary>
        public void ShowFor(SettingsSchema schema, bool showSelection)
        {
            if (panelRoot == null) return;
            var page = schema?.ActivePage();
            bool showSettings = schema != null
                && ((page != null && page.Fields.Count > 0) || schema.HasTabs);
            bool show = showSettings || showSelection;

            // Re-place ONLY when the parked position is useless (never shown, behind the
            // user, or far away) — a grip-parked panel survives tool switches (UX v2 P0.4).
            if (show && !panelRoot.activeSelf && NeedsPlacement()) PlaceInFront();
            panelRoot.SetActive(show);
            if (!show)
            {
                popups?.CloseAll();
                return;
            }

            if (selectionGroup != null) selectionGroup.SetActive(showSelection);
            int selRows = showSelection ? SelectionRows : 0;

            if (showSettings) Bind(schema, selRows);
            else Clear();

            if (rowsRoot != null) rowsRoot.gameObject.SetActive(showSettings);
            if (tabsRoot != null) tabsRoot.gameObject.SetActive(showSettings && schema.HasTabs);

            int rows = showSettings && page != null ? VisibleRowCount(page) : 0;
            FitBackground(rows + selRows, showSettings && schema.HasTabs);
            RefreshValues();
        }

        private static int VisibleRowCount(SettingsSchema page) => page.Fields.Count;

        /// <summary>Fill the selection group labels (title + one-line description).</summary>
        public void SetSelection(string title, string info)
        {
            if (selTitleLabel != null) selTitleLabel.text = title;
            if (selInfoLabel != null) selInfoLabel.text = info;
        }

        /// <summary>Update every row's visuals from the bound delegates.</summary>
        public void RefreshValues()
        {
            foreach (var r in _refreshers) r?.Invoke();
            foreach (var s in _sliders) if (s != null) s.Refresh();
        }

        // ---- runtime generation ----

        private void Clear()
        {
            popups?.CloseAll();
            if (rowsRoot != null)
                foreach (Transform child in rowsRoot) Destroy(child.gameObject);
            if (tabsRoot != null)
                foreach (Transform child in tabsRoot) Destroy(child.gameObject);
            _refreshers.Clear();
            _sliders.Clear();
            _progressBars.Clear();
            _bound = null;
            _boundPage = null;
            _boundSelRows = -1;
        }

        private void Bind(SettingsSchema schema, int selRows)
        {
            var page = schema.ActivePage();
            if (ReferenceEquals(_bound, schema) && ReferenceEquals(_boundPage, page)
                && _boundSelRows == selRows) return;
            Clear();
            _bound = schema;
            _boundPage = page;
            _boundSelRows = selRows;

            if (schema.HasTabs) BuildTabStrip(schema, selRows);

            float topOffset = selRows * UiTokens.RowStep;
            int row = 0;
            foreach (var f in page.Fields)
            {
                float y = -(PanelLayout.RowY(row, schema.HasTabs) + topOffset);
                BuildRow(f, y);
                row++;
            }
        }

        private void BuildTabStrip(SettingsSchema schema, int selRows)
        {
            int n = schema.Tabs.Length;
            float y = -(UiTokens.PanelPadding + UiTokens.TitleBarHeight
                        + PanelLayout.TabStripHeight * 0.5f + selRows * UiTokens.RowStep);
            float w = PanelLayout.ContentWidth / n;
            float x0 = -PanelLayout.ContentWidth * 0.5f + w * 0.5f;
            for (int i = 0; i < n; i++)
            {
                int tab = i;
                var mb = MakeButton(tabsRoot, $"Tab{i}", schema.Tabs[i],
                    new Vector3(x0 + i * w, y, 0f), new Vector2(w - 0.003f, UiTokens.RowHeight),
                    () => { schema.SelectTab(tab); Bind(schema, selRows); RefreshValues(); },
                    kind: MenuButtonKind.Radio);
                // page icons: Electric tabs carry the sub-mode icons (design/20 §6.1)
                var pageIcon = TabIconFor(schema.Tabs[i]);
                if (pageIcon != null)
                    AddRowIcon(mb.transform, pageIcon, new Vector3(-w * 0.5f + 0.014f, 0f, -0.004f), 0.012f);
                int captured = i;
                _refreshers.Add(() => mb.SetActiveTool(schema.ActiveTab() == captured));
            }
        }

        private static string TabIconFor(string tabName) => tabName switch
        {
            "Outlet" => "outlet",
            "Switch" => "switch",
            "Wire" => "wire",
            "Panel" => "breaker-panel",
            "Stairs" => "stairs",
            _ => null,
        };

        private void BuildRow(SettingField f, float y)
        {
            switch (f.Kind)
            {
                case SettingKind.Header: BuildHeader(f, y); return;
                case SettingKind.Action: BuildAction(f, y); return;
                case SettingKind.Readout: BuildReadout(f, y); return;
            }

            MakeCaption(f.Id + "Cap", f.Caption, y);
            switch (f.Kind)
            {
                case SettingKind.Stepper: BuildStepper(f, y); break;
                case SettingKind.Cycle: BuildCycle(f, y); break;
                case SettingKind.Slider: BuildSlider(f, y); break;
                case SettingKind.Segmented: BuildSegmented(f, y); break;
                case SettingKind.Select: BuildSelect(f, y); break;
                case SettingKind.Toggle: BuildToggle(f, y); break;
                case SettingKind.Numeric: BuildNumeric(f, y); break;
                case SettingKind.Swatch: BuildSwatch(f, y); break;
                case SettingKind.Progress: BuildProgress(f, y); break;
            }
        }

        // ---- widgets ----

        private void BuildStepper(SettingField f, float y)
        {
            float btn = 0.024f;
            float right = PanelLayout.ControlRight;
            MakeButton(rowsRoot, f.Id + "+", null, new Vector3(right - btn * 0.5f, y, 0f),
                new Vector2(btn, btn), f.Increase, repeatable: true, iconId: "plus");
            var value = MakeValue(f.Id + "Val", "—",
                new Vector3(right - btn - 0.036f, y, 0f), new Vector2(0.062f, UiTokens.RowHeight));
            MakeButton(rowsRoot, f.Id + "-", null,
                new Vector3(right - btn * 1.5f - 0.072f, y, 0f),
                new Vector2(btn, btn), f.Decrease, repeatable: true, iconId: "minus");
            _refreshers.Add(() => { if (f.Value != null) value.text = f.Value(); });
        }

        private void BuildCycle(SettingField f, float y)
        {
            float right = PanelLayout.ControlRight;
            var value = MakeValue(f.Id + "Val", "—",
                new Vector3(right - 0.024f - 0.045f, y, 0f), new Vector2(0.08f, UiTokens.RowHeight));
            MakeButton(rowsRoot, f.Id + ">", null, new Vector3(right - 0.012f, y, 0f),
                new Vector2(0.024f, 0.024f), f.Increase, iconId: "chevron-right");
            _refreshers.Add(() => { if (f.Value != null) value.text = f.Value(); });
        }

        private void BuildSlider(SettingField f, float y)
        {
            const float valueW = 0.052f;
            float trackW = PanelLayout.ControlWidth - valueW - 0.008f;
            float trackLeft = PanelLayout.ControlLeft;

            var root = new GameObject(f.Id + "Slider") { layer = MenuLayer };
            root.transform.SetParent(rowsRoot, false);
            root.transform.localPosition = new Vector3(trackLeft, y, 0f);
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(trackW * 0.5f, 0f, 0f);
            col.size = new Vector3(trackW, UiTokens.MinTarget, 0.02f);

            var track = MakePlate(root.transform, "Track",
                new Vector3(trackW * 0.5f, 0f, 0f), trackW, 0.006f, 0.003f, insetMaterial);
            _ = track;

            var fill = MakePlate(root.transform, "Fill",
                new Vector3(0f, 0f, -0.001f), 0.01f, 0.006f, 0.003f, buttonMaterial);
            var knob = MakePlate(root.transform, "Knob",
                new Vector3(0f, 0f, -0.003f), 0.016f, 0.016f, 0.008f, buttonMaterial);
            Tint(knob.GetComponent<Renderer>(), UiTokens.Knob);

            var value = MakeValue(f.Id + "Val", "—",
                new Vector3(PanelLayout.ControlRight - valueW * 0.5f, y, 0f),
                new Vector2(valueW, UiTokens.RowHeight));

            var widget = root.AddComponent<SliderWidget>();
            widget.Init(f, fill.transform, fill.GetComponent<Renderer>(),
                knob.transform, value, trackW);
            _sliders.Add(widget);
        }

        private void BuildSegmented(SettingField f, float y)
        {
            var options = f.ResolveOptions() ?? new string[0];
            int n = Mathf.Max(1, options.Length);
            float w = PanelLayout.ControlWidth / n;
            var buttons = new MenuButton[n];
            for (int i = 0; i < n && i < options.Length; i++)
            {
                int idx = i;
                buttons[i] = MakeButton(rowsRoot, $"{f.Id}Seg{i}", options[i],
                    new Vector3(PanelLayout.ControlLeft + w * (i + 0.5f), y, 0f),
                    new Vector2(w - 0.002f, UiTokens.RowHeight - 0.004f),
                    () => f.SetIndex?.Invoke(idx), kind: MenuButtonKind.Radio);
            }
            _refreshers.Add(() =>
            {
                int cur = f.GetIndex?.Invoke() ?? -1;
                for (int i = 0; i < buttons.Length; i++)
                    if (buttons[i] != null) buttons[i].SetActiveTool(i == cur);
            });
        }

        private void BuildSelect(SettingField f, float y)
        {
            var mb = MakeButton(rowsRoot, f.Id + "Sel", "—",
                new Vector3(PanelLayout.ControlLeft + PanelLayout.ControlWidth * 0.5f, y, 0f),
                new Vector2(PanelLayout.ControlWidth, UiTokens.RowHeight - 0.004f),
                () => popups?.OpenSelect(f, () => RefreshValues()));
            AddRowIcon(mb.transform, "chevron-down",
                new Vector3(PanelLayout.ControlWidth * 0.5f - 0.012f, 0f, -0.004f), 0.010f);
            var label = mb.GetComponentInChildren<TMP_Text>();
            _refreshers.Add(() =>
            {
                var opts = f.ResolveOptions();
                int cur = f.GetIndex?.Invoke() ?? -1;
                if (label != null)
                    label.text = opts != null && cur >= 0 && cur < opts.Length ? opts[cur]
                        : f.Value != null ? f.Value() : "—";
            });
        }

        private void BuildToggle(SettingField f, float y)
        {
            const float pillW = 0.030f, pillH = 0.016f;
            float cx = PanelLayout.ControlRight - pillW * 0.5f;

            var mb = MakeButton(rowsRoot, f.Id + "Tgl", null, new Vector3(cx, y, 0f),
                new Vector2(UiTokens.MinTarget * 1.6f, UiTokens.MinTarget),
                () => f.SetOn?.Invoke(!(f.GetOn?.Invoke() ?? false)), background: false);

            var track = MakePlate(mb.transform, "Pill", Vector3.zero, pillW, pillH, pillH * 0.5f,
                insetMaterial);
            var trackR = track.GetComponent<Renderer>();
            var knob = MakePlate(mb.transform, "Knob", new Vector3(0f, 0f, -0.002f),
                pillH - 0.004f, pillH - 0.004f, (pillH - 0.004f) * 0.5f, buttonMaterial);
            Tint(knob.GetComponent<Renderer>(), UiTokens.Knob);

            _refreshers.Add(() =>
            {
                bool on = f.GetOn?.Invoke() ?? false;
                Tint(trackR, on ? UiTokens.ToggleOnBg : UiTokens.InsetBg);
                float kx = (pillW - pillH) * 0.5f;
                knob.transform.localPosition = new Vector3(on ? kx : -kx, 0f, -0.002f);
            });
        }

        private void BuildNumeric(SettingField f, float y)
        {
            const float w = 0.075f;
            var mb = MakeButton(rowsRoot, f.Id + "Num", null,
                new Vector3(PanelLayout.ControlRight - w * 0.5f, y, 0f),
                new Vector2(w, UiTokens.RowHeight - 0.004f),
                () => popups?.OpenNumpad(f, () => RefreshValues()), background: false);
            var plate = MakePlate(mb.transform, "Bg", new Vector3(0f, 0f, 0.001f),
                w, UiTokens.RowHeight - 0.006f, UiTokens.RadiusM, insetMaterial);
            _ = plate;
            var value = MakeText(mb.transform, "Val", "—",
                new Vector2(w - 0.008f, UiTokens.RowHeight - 0.012f));
            _refreshers.Add(() => { if (f.Value != null) value.text = f.Value(); });
        }

        private void BuildSwatch(SettingField f, float y)
        {
            const float chip = 0.020f;
            var mb = MakeButton(rowsRoot, f.Id + "Chip", null,
                new Vector3(PanelLayout.ControlRight - chip * 0.5f - 0.002f, y, 0f),
                new Vector2(UiTokens.MinTarget, UiTokens.MinTarget),
                () => popups?.OpenSwatch(f, () => RefreshValues()), background: false);
            var plate = MakePlate(mb.transform, "Chip", Vector3.zero, chip, chip, UiTokens.RadiusS,
                buttonMaterial);
            var r = plate.GetComponent<Renderer>();
            _refreshers.Add(() =>
            {
                int cur = f.GetIndex?.Invoke() ?? 0;
                if (f.Palette != null && f.Palette.Length > 0)
                    Tint(r, f.Palette[Mathf.Clamp(cur, 0, f.Palette.Length - 1)]);
            });
        }

        private void BuildAction(SettingField f, float y)
        {
            var mb = MakeButton(rowsRoot, f.Id + "Act", f.Caption,
                new Vector3(0f, y, 0f),
                new Vector2(PanelLayout.ContentWidth, UiTokens.RowHeight),
                () => f.Increase?.Invoke());
            mb.Destructive = f.Destructive;
            if (!string.IsNullOrEmpty(f.IconId))
                AddRowIcon(mb.transform, f.IconId,
                    new Vector3(-PanelLayout.ContentWidth * 0.5f + 0.016f, 0f, -0.004f), 0.013f);
        }

        private void BuildReadout(SettingField f, float y)
        {
            var cap = MakeCaption(f.Id + "Cap", f.Caption, y);
            cap.color = UiTokens.LabelDim;
            var value = MakeValue(f.Id + "Val", "—",
                new Vector3(PanelLayout.ControlLeft + PanelLayout.ControlWidth * 0.5f, y, 0f),
                new Vector2(PanelLayout.ControlWidth, UiTokens.RowHeight));
            _refreshers.Add(() => { if (f.Value != null) value.text = f.Value(); });
        }

        private void BuildProgress(SettingField f, float y)
        {
            float w = PanelLayout.ControlWidth;
            var root = new GameObject(f.Id + "Prog") { layer = MenuLayer };
            root.transform.SetParent(rowsRoot, false);
            root.transform.localPosition = new Vector3(PanelLayout.ControlLeft, y, 0f);
            MakePlate(root.transform, "Track", new Vector3(w * 0.5f, 0f, 0f),
                w, 0.006f, 0.003f, insetMaterial);
            var fill = MakePlate(root.transform, "Fill", new Vector3(0f, 0f, -0.001f),
                0.01f, 0.006f, 0.003f, buttonMaterial);
            Tint(fill.GetComponent<Renderer>(), UiTokens.TrackFillActive);
            _progressBars.Add((f, fill.transform, w));
        }

        private void BuildHeader(SettingField f, float y)
        {
            var cap = MakeCaption(f.Id + "Hdr", f.Caption.ToUpperInvariant(), y);
            cap.color = UiTokens.LabelDim;
            cap.fontSizeMax = UiTokens.RowHeight * 0.55f;
            var line = MakePlate(rowsRoot, f.Id + "Rule",
                new Vector3(PanelLayout.ContentWidth * 0.20f, y, 0f),
                PanelLayout.ContentWidth * 0.55f, 0.001f, 0.0005f, insetMaterial);
            Tint(line.GetComponent<Renderer>(), UiTokens.Divider);
        }

        // ---- factories ----

        private TMP_Text MakeCaption(string name, string text, float y)
        {
            const float w = 0.10f;
            var t = MakeLabel(name, text,
                new Vector3(PanelLayout.CaptionX + w * 0.5f, y, 0f),
                new Vector2(w, UiTokens.RowHeight * 0.85f));
            t.alignment = TextAlignmentOptions.MidlineLeft;
            return t;
        }

        private TMP_Text MakeValue(string name, string text, Vector3 lp, Vector2 size)
        {
            var t = MakeLabel(name, text, lp, size * 0.85f);
            return t;
        }

        private MenuButton MakeButton(Transform parent, string name, string label, Vector3 lp,
            Vector2 size, System.Action onClick, bool repeatable = false,
            MenuButtonKind kind = MenuButtonKind.Momentary, string iconId = null,
            bool background = true)
        {
            var root = new GameObject(name) { layer = MenuLayer };
            root.transform.SetParent(parent, false);
            root.transform.localPosition = lp;
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(Mathf.Max(size.x, UiTokens.MinTarget),
                Mathf.Max(size.y, UiTokens.MinTarget), 0.02f);
            var mb = root.AddComponent<MenuButton>();
            mb.OnClick = onClick;
            mb.Repeatable = repeatable;

            Renderer bgRenderer = null;
            if (background)
            {
                var bg = MakePlate(root.transform, "Bg", Vector3.zero,
                    size.x, size.y, UiTokens.RadiusM, buttonMaterial);
                bgRenderer = bg.GetComponent<Renderer>();
            }

            TMP_Text text = null;
            if (!string.IsNullOrEmpty(label))
                text = MakeText(root.transform, "Label", label,
                    new Vector2(size.x * 0.92f, size.y * 0.8f));

            mb.InitRuntime(kind, bgRenderer, text);
            if (!string.IsNullOrEmpty(iconId) && label == null)
                AddRowIcon(root.transform, iconId, new Vector3(0f, 0f, -0.004f),
                    Mathf.Min(size.y * 0.55f, 0.014f));
            return mb;
        }

        private void AddRowIcon(Transform parent, string iconId, Vector3 lp, float size)
        {
            var go = new GameObject("Icon") { layer = MenuLayer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = lp;
            var ir = go.AddComponent<IconRenderer>();
            ir.Init(iconId, iconMaterial, size);
            ir.SetTint(UiTokens.LabelLight);
        }

        /// <summary>Rounded-rect plate (§6.3) — the v2 standard replacing flat quads.
        /// Meshes are cached per size and freed in OnDestroy.</summary>
        private GameObject MakePlate(Transform parent, string name, Vector3 lp,
            float w, float h, float radius, Material mat)
        {
            var key = (Mathf.Round(w * 2000f) / 2000f, Mathf.Round(h * 2000f) / 2000f,
                Mathf.Round(radius * 2000f) / 2000f);
            if (!_plateCache.TryGetValue(key, out var mesh) || mesh == null)
            {
                mesh = IconRenderer.ToMesh(UiMeshes.RoundedRect(w, h, radius), $"Plate_{key}");
                _plateCache[key] = mesh;
            }
            var go = new GameObject(name) { layer = MenuLayer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = lp;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
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
            tmp.rectTransform.sizeDelta = new Vector2(size.x, size.y);
            tmp.enableAutoSizing = true;
            // readability floor ≥ ~0.9° at 65 cm (UX v2 P1.5) — auto-sizing is only a clamp
            tmp.fontSizeMin = size.y * 0.55f;
            tmp.fontSizeMax = size.y * 0.72f;
            return tmp;
        }

        private MaterialPropertyBlock _tintMpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            _tintMpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(_tintMpb);
            _tintMpb.SetColor(BaseColorId, c);
            _tintMpb.SetColor(ColorId, c);
            r.SetPropertyBlock(_tintMpb);
        }

        // ---- background / placement ----

        /// <summary>Stretch the background to the content — an oversized empty quad both
        /// looks broken and BLOCKS scene tools with its collider (UX v2 P0.4).</summary>
        private void FitBackground(int rows, bool hasTabs)
        {
            if (background == null) return;
            float h = PanelLayout.PanelHeight(rows, hasTabs);
            var ls = background.localScale;
            background.localScale = new Vector3(ls.x, h, ls.z);
            var lp = background.localPosition;
            background.localPosition = new Vector3(lp.x, -h * 0.5f, lp.z);
        }

        private bool NeedsPlacement()
        {
            if (!_everPlaced) return true;
            EnsureCam();
            if (_cam == null) return false;
            Vector3 to = panelRoot.transform.position - _cam.position;
            if (to.magnitude > 2.5f) return true;
            return Vector3.Angle(_cam.forward, to) > 45f;   // parked behind the user's back
        }

        /// <summary>Move the panel so the grabbed point tracks the pointer (drag).</summary>
        public void MovePanel(Vector3 worldPos)
        {
            if (panelRoot != null) panelRoot.transform.position = worldPos;
        }

        private void PlaceInFront()
        {
            EnsureCam();
            if (_cam == null) return;
            // 0.65 m: closer causes vergence strain over an hour-long session (UX v2 P0.4)
            panelRoot.transform.position =
                _cam.position + _cam.forward * 0.65f + _cam.right * 0.16f + _cam.up * 0.10f;
            _everPlaced = true;
        }

        private void LateUpdate()
        {
            if (panelRoot == null || !panelRoot.activeSelf) return;
            EnsureCam();
            if (_cam == null) return;
            // yaw-only billboard: full LookRotation pitches a low-parked panel
            Vector3 dir = panelRoot.transform.position - _cam.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                panelRoot.transform.rotation = Quaternion.LookRotation(dir);

            // progress bars animate every frame while visible
            foreach (var (f, fill, w) in _progressBars)
            {
                if (fill == null || f.GetProgress == null) continue;
                float p = f.GetProgress();
                float t = p < 0f ? Mathf.PingPong(Time.time * 0.8f, 1f) : Mathf.Clamp01(p);
                fill.localScale = new Vector3(Mathf.Max(1e-4f, t * w), fill.localScale.y, 1f);
                fill.localPosition = new Vector3(t * w * 0.5f, 0f, -0.001f);
            }
        }

        private void EnsureCam()
        {
            if (_cam == null && Camera.main != null) _cam = Camera.main.transform;
        }

        private void OnDestroy()
        {
            foreach (var mesh in _plateCache.Values)
                if (mesh != null) Destroy(mesh);
            _plateCache.Clear();
        }
    }
}
