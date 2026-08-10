using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// The single modal layer (design/20 §3.8): Select option list, numpad and swatch
    /// grid. One popup at a time; while open, the parent panel's colliders are disabled,
    /// B or click-outside cancels, commit closes. Popups spawn 10 mm toward the viewer
    /// over a scrim plate. ToolManager routes input here first while IsOpen.
    /// </summary>
    public class UiPopups : MonoBehaviour
    {
        private const int MenuLayer = 2;

        [SerializeField] private Transform anchor;          // the inspector panelRoot
        [SerializeField] private Material buttonMaterial;
        [SerializeField] private Material insetMaterial;
        [SerializeField] private Material panelMaterial;
        [SerializeField] private Material iconMaterial;

        private GameObject _root;
        private System.Action _onChanged;
        private SettingField _field;
        private readonly List<Collider> _disabledColliders = new();
        private readonly List<Mesh> _ownedMeshes = new();

        // select list scrolling
        private string[] _options;
        private int _firstVisible;
        private Transform _listRoot;
        private readonly List<MenuButton> _listButtons = new();
        private float _scrollAccum;

        // numpad editing
        private string _entry = "";
        private TMP_Text _entryLabel;

        public bool IsOpen => _root != null;

        /// <summary>True when the collider belongs to this popup — clicks on anything else
        /// close it (click-outside).</summary>
        public bool Owns(Collider c) => _root != null && c != null && c.transform.IsChildOf(_root.transform);

        // ---- open/close ----

        public void OpenSelect(SettingField field, System.Action onChanged)
        {
            CloseAll();
            _field = field;
            _onChanged = onChanged;
            _scrollAccum = 0f;   // leftover fractional scroll must not jump the new list
            _options = field.ResolveOptions() ?? new string[0];
            int cur = field.GetIndex?.Invoke() ?? 0;
            _firstVisible = PanelLayout.ScrollTo(cur, _options.Length);

            float rowH = UiTokens.RowHeight;
            int visible = Mathf.Min(_options.Length, PanelLayout.MaxVisibleOptions);
            float w = PanelLayout.ControlWidth + 0.02f;
            float h = visible * rowH + 0.016f;
            _root = MakeRoot("SelectPopup", w, h);
            _listRoot = new GameObject("List") { layer = MenuLayer }.transform;
            _listRoot.SetParent(_root.transform, false);
            RebuildList(w, rowH, visible, h);
        }

        private void RebuildList(float w, float rowH, int visible, float h)
        {
            foreach (Transform c in _listRoot) Destroy(c.gameObject);
            _listButtons.Clear();
            int cur = _field.GetIndex?.Invoke() ?? -1;
            for (int v = 0; v < visible; v++)
            {
                int idx = _firstVisible + v;
                if (idx >= _options.Length) break;
                float y = h * 0.5f - 0.008f - rowH * (v + 0.5f);
                int captured = idx;
                var mb = MakeButton(_listRoot, $"Opt{idx}", _options[idx],
                    new Vector3(0f, y, 0f), new Vector2(w - 0.012f, rowH - 0.003f),
                    () =>
                    {
                        _field.SetIndex?.Invoke(captured);
                        CloseAll();
                        _onChanged?.Invoke();
                    });
                if (idx == cur)
                    AddIcon(mb.transform, "check",
                        new Vector3(-(w - 0.012f) * 0.5f + 0.012f, 0f, -0.004f), 0.010f);
                _listButtons.Add(mb);
            }
        }

        public void OpenNumpad(SettingField field, System.Action onChanged)
        {
            CloseAll();
            _field = field;
            _onChanged = onChanged;
            _entry = "";

            const float key = 0.032f, gap = 0.006f;
            float w = key * 3f + gap * 4f;
            float h = 0.030f + (key + gap) * 4f + 0.030f + 0.016f;
            _root = MakeRoot("Numpad", w + 0.012f, h);

            float top = h * 0.5f - 0.008f;
            _entryLabel = MakeText(_root.transform, "Entry",
                CurrentDisplay(), new Vector3(0f, top - 0.015f, -0.002f),
                new Vector2(w, 0.026f));

            string[] keys = { "7", "8", "9", "4", "5", "6", "1", "2", "3", "±", "0", "⌫" };
            for (int i = 0; i < keys.Length; i++)
            {
                int r = i / 3, c = i % 3;
                string k = keys[i];
                MakeButton(_root.transform, $"Key{k}", k,
                    new Vector3((c - 1) * (key + gap), top - 0.036f - r * (key + gap), 0f),
                    new Vector2(key, key), () => PressKey(k));
            }
            float by = top - 0.036f - 4f * (key + gap) + 0.004f;
            MakeButton(_root.transform, "Cancel", "Cancel",
                new Vector3(-w * 0.25f, by, 0f), new Vector2(w * 0.46f, 0.024f), CloseAll);
            MakeButton(_root.transform, "OK", "OK",
                new Vector3(w * 0.25f, by, 0f), new Vector2(w * 0.46f, 0.024f), CommitNumpad);
        }

        public void OpenSwatch(SettingField field, System.Action onChanged)
        {
            CloseAll();
            _field = field;
            _onChanged = onChanged;
            var palette = field.Palette ?? new Color[0];

            const int cols = 4;
            const float chip = 0.028f, gutter = 0.006f;
            int rows = Mathf.CeilToInt(palette.Length / (float)cols);
            float w = cols * (chip + gutter) + gutter + 0.008f;
            float h = rows * (chip + gutter) + gutter + 0.008f;
            _root = MakeRoot("SwatchPopup", w, h);

            int cur = field.GetIndex?.Invoke() ?? -1;
            for (int i = 0; i < palette.Length; i++)
            {
                int r = i / cols, c = i % cols;
                float x = -w * 0.5f + gutter + chip * 0.5f + 0.004f + c * (chip + gutter);
                float y = h * 0.5f - gutter - chip * 0.5f - 0.004f - r * (chip + gutter);
                int captured = i;
                var mb = MakeButton(_root.transform, $"Chip{i}", null,
                    new Vector3(x, y, 0f), new Vector2(chip, chip),
                    () =>
                    {
                        _field.SetIndex?.Invoke(captured);
                        CloseAll();
                        _onChanged?.Invoke();
                    }, background: false);
                var plate = MakePlate(mb.transform, "Color", new Vector3(0f, 0f, 0.001f),
                    chip, chip, UiTokens.RadiusS, buttonMaterial);
                Tint(plate.GetComponent<Renderer>(), palette[i]);
                if (i == cur)
                {
                    var rim = MakePlate(mb.transform, "Rim", new Vector3(0f, 0f, 0.002f),
                        chip + 0.004f, chip + 0.004f, UiTokens.RadiusS + 0.002f, buttonMaterial);
                    Tint(rim.GetComponent<Renderer>(), UiTokens.Hover);
                }
            }
        }

        public void CloseAll()
        {
            if (_root != null) Destroy(_root);
            _root = null;
            _field = null;
            _listRoot = null;
            _entryLabel = null;
            _scrollAccum = 0f;
            _listButtons.Clear();
            foreach (var c in _disabledColliders)
                if (c != null) c.enabled = true;
            _disabledColliders.Clear();
            foreach (var m in _ownedMeshes)
                if (m != null) Destroy(m);
            _ownedMeshes.Clear();
        }

        /// <summary>Per-frame while open: stick scrolls the select list; B closes.</summary>
        public void Tick(Vector2 stick, bool cancelPressed)
        {
            if (!IsOpen) return;
            if (cancelPressed)
            {
                CloseAll();
                return;
            }
            if (_listRoot != null && _options != null
                && _options.Length > PanelLayout.MaxVisibleOptions)
            {
                _scrollAccum += -stick.y * Time.deltaTime * 3f;   // 3 rows/s (§3.7)
                int step = (int)_scrollAccum;
                if (step != 0)
                {
                    _scrollAccum -= step;
                    int next = PanelLayout.ClampScroll(_firstVisible + step, _options.Length);
                    if (next != _firstVisible)
                    {
                        _firstVisible = next;
                        float rowH = UiTokens.RowHeight;
                        int visible = Mathf.Min(_options.Length, PanelLayout.MaxVisibleOptions);
                        float w = PanelLayout.ControlWidth + 0.02f;
                        float h = visible * rowH + 0.016f;
                        RebuildList(w, rowH, visible, h);
                    }
                }
            }
        }

        // ---- numpad logic ----

        private string CurrentDisplay()
        {
            if (_entry.Length > 0) return _entry;
            return _field?.Value != null ? _field.Value() : "";
        }

        private void PressKey(string k)
        {
            switch (k)
            {
                case "⌫":
                    if (_entry.Length > 0) _entry = _entry.Substring(0, _entry.Length - 1);
                    break;
                case "±":
                    _entry = _entry.StartsWith("-") ? _entry.Substring(1) : "-" + _entry;
                    break;
                default:
                    if (_entry.Length < 7) _entry += k;   // first digit replaces the old value
                    break;
            }
            if (_entryLabel != null) _entryLabel.text = CurrentDisplay();
        }

        private void CommitNumpad()
        {
            if (_field != null && _entry.Length > 0
                && float.TryParse(_entry, NumberStyles.Float, CultureInfo.InvariantCulture, out float typed))
            {
                float before = _field.GetNumber?.Invoke() ?? 0f;
                // typed in display units (cm) → back to native (m) via DisplayScale (§2.6)
                float scale = _field.DisplayScale > 0f ? _field.DisplayScale : 1f;
                float after = Mathf.Clamp(typed / scale, _field.Min, _field.Max);
                _field.CommitNumber?.Invoke(before, after);
            }
            CloseAll();
            _onChanged?.Invoke();
        }

        // ---- construction ----

        private GameObject MakeRoot(string name, float w, float h)
        {
            _root = new GameObject(name) { layer = MenuLayer };
            var t = _root.transform;
            t.SetParent(anchor != null ? anchor : transform, false);
            t.localPosition = new Vector3(0f, -0.10f, UiTokens.ZPopoverPlate);
            t.localScale = Vector3.one;

            var bg = MakePlate(t, "Bg", new Vector3(0f, 0f, 0.004f), w, h, UiTokens.RadiusL,
                panelMaterial);
            var col = bg.AddComponent<BoxCollider>();
            col.size = new Vector3(w, h, 0.01f);

            // modal: parent panel stops receiving rays while the popup lives (§3.8)
            if (anchor != null)
                foreach (var c in anchor.GetComponentsInChildren<Collider>())
                    if (c.enabled && !c.transform.IsChildOf(t))
                    {
                        c.enabled = false;
                        _disabledColliders.Add(c);
                    }
            return _root;
        }

        private MenuButton MakeButton(Transform parent, string name, string label, Vector3 lp,
            Vector2 size, System.Action onClick, bool background = true)
        {
            var root = new GameObject(name) { layer = MenuLayer };
            root.transform.SetParent(parent, false);
            root.transform.localPosition = lp;
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(Mathf.Max(size.x, UiTokens.MinTarget),
                Mathf.Max(size.y, UiTokens.MinTarget), 0.015f);
            var mb = root.AddComponent<MenuButton>();
            mb.OnClick = onClick;

            Renderer bgRenderer = null;
            if (background)
            {
                var bg = MakePlate(root.transform, "Bg", Vector3.zero,
                    size.x, size.y, UiTokens.RadiusM, buttonMaterial);
                bgRenderer = bg.GetComponent<Renderer>();
            }
            TMP_Text text = null;
            if (!string.IsNullOrEmpty(label))
                text = MakeText(root.transform, "Label", label, new Vector3(0f, 0f, -0.006f),
                    new Vector2(size.x * 0.92f, size.y * 0.8f));
            mb.InitRuntime(MenuButtonKind.Momentary, bgRenderer, text);
            return mb;
        }

        private GameObject MakePlate(Transform parent, string name, Vector3 lp,
            float w, float h, float radius, Material mat)
        {
            var mesh = IconRenderer.ToMesh(UiMeshes.RoundedRect(w, h, radius), $"Popup_{name}");
            _ownedMeshes.Add(mesh);
            var go = new GameObject(name) { layer = MenuLayer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = lp;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        private void AddIcon(Transform parent, string iconId, Vector3 lp, float size)
        {
            var go = new GameObject("Icon") { layer = MenuLayer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = lp;
            var ir = go.AddComponent<IconRenderer>();
            ir.Init(iconId, iconMaterial, size);
            ir.SetTint(UiTokens.Selected);
        }

        private static TMP_Text MakeText(Transform parent, string name, string text,
            Vector3 lp, Vector2 size)
        {
            var t = new GameObject(name) { layer = MenuLayer };
            t.transform.SetParent(parent, false);
            t.transform.localPosition = lp;
            var tmp = t.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.enableWordWrapping = false;
            tmp.rectTransform.sizeDelta = size;
            tmp.enableAutoSizing = true;
            // same factors as InspectorPanel.MakeText (TMP world glyph ≈ fontSize × 0.15)
            tmp.fontSizeMin = size.y * 2.6f;
            tmp.fontSizeMax = size.y * 3.3f;
            return tmp;
        }

        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            _mpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(_mpb);
        }

        private void OnDestroy() => CloseAll();
    }
}
