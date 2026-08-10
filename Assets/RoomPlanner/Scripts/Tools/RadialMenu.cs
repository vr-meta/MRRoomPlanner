using System.Collections.Generic;
using TMPro;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tools
{
    /// <summary>One radial slot: a tool or a reserved placeholder (design/20 §1.6).</summary>
    public struct RadialSlotDef
    {
        public string IconId;     // IconPaths id (ignored when Reserved)
        public string Label;      // hub name while highlighted
        public Color Tint;        // layer hue of the tool (sector icon tint)
        public int ToolIndex;     // ToolManager registry index; −1 = reserved
        public bool Reserved => ToolIndex < 0;
    }

    /// <summary>
    /// The Rust-style tool wheel (design/20 §1): gaze-anchored at open then world-locked,
    /// 12 fixed compass slots, stick flick OR ray+trigger to pick, name lives in the hub.
    /// All geometry is generated at runtime from UiMeshes/IconRenderer — nothing serialized
    /// beyond materials. ToolManager owns input and calls <see cref="Tick"/> every frame
    /// while open.
    /// </summary>
    public class RadialMenu : MonoBehaviour
    {
        [SerializeField] private Material sectorMaterial;   // opaque, MPB-tinted per state
        [SerializeField] private Material scrimMaterial;    // unlit transparent vertex color
        [SerializeField] private Material iconMaterial;
        [SerializeField] private TMP_FontAsset font;        // optional; TMP default otherwise

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly RadialTracker _tracker = new();
        private RadialSlotDef[] _slots;
        private MeshRenderer[] _sectorRenderers;
        private Transform[] _sectors;
        private IconRenderer[] _icons;
        private GameObject _activeStripe;
        private GameObject _hoverStripe;
        private IconRenderer _hubIcon;
        private TMP_Text _hubLabel;
        private TMP_Text _hubHint;
        private MaterialPropertyBlock _mpb;

        private Mesh _sectorMesh;
        private Mesh _sectorHoverMesh;
        private Mesh _stripeMesh;
        private readonly List<Mesh> _ownedMeshes = new();   // per-instance meshes freed in OnDestroy

        private bool _built;
        private bool _open;
        private float _openedAt;
        private float _closingUntil;
        private int _lastRaySlot = -1;
        private int _currentToolSlot = -1;
        private Vector3 _spawnHeadPos;
        private Vector3 _spawnHeadFwd;

        public bool IsOpen => _open;

        /// <summary>Fired with the picked slot's ToolIndex; ToolManager subscribes.</summary>
        public System.Action<int> OnPicked;

        public void Configure(RadialSlotDef[] slots)
        {
            _slots = slots;
            if (_built) RefreshStatic();
        }

        // ---- lifecycle ----

        public void Open(Transform head, int currentToolIndex)
        {
            if (_slots == null || head == null) return;
            if (!_built) Build();

            _currentToolSlot = SlotOfTool(currentToolIndex);
            transform.position = head.position + head.forward * UiTokens.RadialSpawnDistance;
            transform.rotation = Quaternion.LookRotation(transform.position - head.position);
            _spawnHeadPos = head.position;
            _spawnHeadFwd = head.forward;

            _tracker.Reset();
            _lastRaySlot = -1;
            _open = true;
            _openedAt = Time.time;
            _closingUntil = 0f;
            gameObject.SetActive(true);
            RefreshVisuals();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _closingUntil = Time.time + UiTokens.RadialCloseSeconds;
        }

        /// <summary>
        /// Per-frame while open (and during the close animation). Returns true while the
        /// wheel captures input (scene tools must stay blocked).
        /// </summary>
        public bool Tick(Vector2 leftStick, Ray pointerRay, bool confirmPressed,
            bool cancelPressed, Transform head, Measure.MeasureInput haptics)
        {
            // closing animation tail
            if (!_open)
            {
                if (_closingUntil > 0f)
                {
                    float k = 1f - Mathf.Clamp01((_closingUntil - Time.time) / UiTokens.RadialCloseSeconds);
                    transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.92f, k);
                    if (Time.time >= _closingUntil)
                    {
                        _closingUntil = 0f;
                        gameObject.SetActive(false);
                    }
                }
                return false;
            }

            // open animation
            float openK = Mathf.Clamp01((Time.time - _openedAt) / UiTokens.RadialOpenSeconds);
            transform.localScale = Vector3.one * Mathf.Lerp(0.90f, 1f, 1f - (1f - openK) * (1f - openK));

            // abandoned intent: walked away or turned away (design/20 §1.3)
            if (head != null &&
                (Vector3.Distance(head.position, _spawnHeadPos) > 1f
                 || Vector3.Angle(head.forward, _spawnHeadFwd) > 60f))
            {
                Close();
                return true;
            }

            if (cancelPressed)
            {
                Close();
                return true;
            }

            int before = _tracker.HighlightedSlot;

            // ray path: hover a sector, trigger confirms (last mover wins via SetHighlight)
            int raySlot = RaySlot(pointerRay, out bool overHub);
            if (raySlot != _lastRaySlot)
            {
                _lastRaySlot = raySlot;
                if (raySlot >= 0 && !_slots[raySlot].Reserved)
                {
                    _tracker.SetHighlight(raySlot, Time.time);
                    haptics?.PulseLeft(0.25f, 0.008f);
                }
            }
            if (confirmPressed)
            {
                if (overHub) { Close(); return true; }               // hub = cancel (§1.7)
                if (raySlot >= 0 && !_slots[raySlot].Reserved) { Pick(raySlot, haptics); return true; }
            }

            // stick path
            var ev = _tracker.Step(leftStick, Time.time);
            switch (ev)
            {
                case RadialEvent.SlotChanged:
                    if (_tracker.HighlightedSlot >= 0 && _slots[_tracker.HighlightedSlot].Reserved)
                        break;                                        // reserved: highlight shows nothing
                    haptics?.PulseLeft(0.25f, 0.008f);
                    break;
                case RadialEvent.Confirmed:
                    int slot = _tracker.HighlightedSlot;
                    if (slot >= 0 && !_slots[slot].Reserved) { Pick(slot, haptics); return true; }
                    break;
                case RadialEvent.BrowseCancelled:
                    break;                                            // stay open, highlight cleared
            }

            if (_tracker.HighlightedSlot != before) RefreshVisuals();
            else if (ev != RadialEvent.None) RefreshVisuals();
            return true;
        }

        private void Pick(int slot, Measure.MeasureInput haptics)
        {
            haptics?.PulseLeft(0.6f, 0.02f);
            _currentToolSlot = slot;
            RefreshVisuals();
            Close();
            OnPicked?.Invoke(_slots[slot].ToolIndex);
        }

        // ---- geometry ----

        private int SlotOfTool(int toolIndex)
        {
            if (_slots == null) return -1;
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].ToolIndex == toolIndex) return i;
            return -1;
        }

        /// <summary>Sector under the pointer ray, −1 if none. Hub reported separately.</summary>
        private int RaySlot(Ray ray, out bool overHub)
        {
            overHub = false;
            var plane = new Plane(-transform.forward, transform.position);
            if (!plane.Raycast(ray, out float dist)) return -1;
            Vector3 local = transform.InverseTransformPoint(ray.GetPoint(dist));
            float r = new Vector2(local.x, local.y).magnitude;
            if (r <= UiTokens.RadialHubRadius) { overHub = true; return -1; }
            if (r < UiTokens.RadialInnerRadius || r > UiTokens.RadialOuterRadius * 1.1f) return -1;
            float angle = RadialMath.AngleDeg(new Vector2(local.x, local.y));
            return RadialMath.SlotAt(angle);
        }

        private void Build()
        {
            _built = true;
            _mpb = new MaterialPropertyBlock();
            int n = RadialMath.Slots;
            _sectors = new Transform[n];
            _sectorRenderers = new MeshRenderer[n];
            _icons = new IconRenderer[n];

            float half = RadialMath.SlotSpanDeg * 0.5f - UiTokens.RadialSectorGapDeg * 0.5f;
            _sectorMesh = IconRenderer.ToMesh(
                UiMeshes.Sector(UiTokens.RadialInnerRadius, UiTokens.RadialOuterRadius, -half, half),
                "RadialSector");
            _sectorHoverMesh = IconRenderer.ToMesh(
                UiMeshes.Sector(UiTokens.RadialInnerRadius, UiTokens.RadialOuterRadius * 1.06f, -half, half),
                "RadialSectorHover");
            _stripeMesh = IconRenderer.ToMesh(
                UiMeshes.Sector(UiTokens.RadialOuterRadius + 0.004f, UiTokens.RadialOuterRadius + 0.007f,
                    -half + 2f, half - 2f),
                "RadialActiveStripe");

            // scrim: the one sanctioned large transparency (§6.3) — a quad with a
            // procedural radial-falloff texture (vertex-color shaders strip on device)
            var scrim = GameObject.CreatePrimitive(PrimitiveType.Quad);
            scrim.name = "Scrim";
            scrim.layer = gameObject.layer;
            var scrimCol = scrim.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(scrimCol);
            else DestroyImmediate(scrimCol);   // editor screenshot pipeline
            scrim.transform.SetParent(transform, false);
            scrim.transform.localPosition = new Vector3(0f, 0f, 0.003f);
            scrim.transform.localScale =
                new Vector3(UiTokens.RadialScrimRadius * 2f, UiTokens.RadialScrimRadius * 2f, 1f);
            var scrimR = scrim.GetComponent<MeshRenderer>();
            scrimR.sharedMaterial = scrimMaterial;
            scrimR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            for (int i = 0; i < n; i++)
            {
                var s = new GameObject($"Sector{i}") { layer = gameObject.layer };
                s.transform.SetParent(transform, false);
                // compass slot i sits at i·30° clockwise on screen = −i·30° around Unity Z
                s.transform.localRotation = Quaternion.Euler(0f, 0f, -i * RadialMath.SlotSpanDeg);
                var mf = s.AddComponent<MeshFilter>();
                mf.sharedMesh = _sectorMesh;
                var mr = s.AddComponent<MeshRenderer>();
                mr.sharedMaterial = sectorMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _sectors[i] = s.transform;
                _sectorRenderers[i] = mr;

                Vector2 dir = RadialMath.SlotDirection(i);
                if (_slots != null && i < _slots.Length && _slots[i].Reserved)
                {
                    // reserved: a faint small dot — must NOT read as a button (design/20 §1.6)
                    var dot = new GameObject($"Dot{i}") { layer = gameObject.layer };
                    dot.transform.SetParent(transform, false);
                    dot.transform.localPosition = new Vector3(
                        dir.x * UiTokens.RadialIconRadius, dir.y * UiTokens.RadialIconRadius, -0.002f);
                    var dotMesh = IconRenderer.ToMesh(UiMeshes.Disc(0.004f, 20), "ReservedDot");
                    _ownedMeshes.Add(dotMesh);
                    dot.AddComponent<MeshFilter>().sharedMesh = dotMesh;
                    var dr = dot.AddComponent<MeshRenderer>();
                    dr.sharedMaterial = iconMaterial;
                    SetTint(dr, UiTokens.LabelDim);
                }
                else
                {
                    var icon = new GameObject($"Icon{i}") { layer = gameObject.layer };
                    icon.transform.SetParent(transform, false);
                    icon.transform.localPosition =
                        new Vector3(dir.x, dir.y, 0f) * UiTokens.RadialIconRadius
                        + new Vector3(0f, 0f, -0.002f);
                    var ir = icon.AddComponent<IconRenderer>();
                    ir.Init(_slots != null && i < _slots.Length ? _slots[i].IconId : null,
                        iconMaterial, 0.024f);
                    _icons[i] = ir;
                }
            }

            _activeStripe = new GameObject("ActiveStripe") { layer = gameObject.layer };
            _activeStripe.transform.SetParent(transform, false);
            _activeStripe.AddComponent<MeshFilter>().sharedMesh = _stripeMesh;
            var stripeR = _activeStripe.AddComponent<MeshRenderer>();
            stripeR.sharedMaterial = iconMaterial;
            SetTint(stripeR, UiTokens.Selected);
            _activeStripe.SetActive(false);

            // hover rim: the cyan outer-edge arc from the preview (design/20 §6.5)
            _hoverStripe = new GameObject("HoverStripe") { layer = gameObject.layer };
            _hoverStripe.transform.SetParent(transform, false);
            _hoverStripe.AddComponent<MeshFilter>().sharedMesh = _stripeMesh;
            var hoverR = _hoverStripe.AddComponent<MeshRenderer>();
            hoverR.sharedMaterial = iconMaterial;
            SetTint(hoverR, UiTokens.Hover);
            _hoverStripe.SetActive(false);

            // hub
            var hub = new GameObject("Hub") { layer = gameObject.layer };
            hub.transform.SetParent(transform, false);
            hub.transform.localPosition = new Vector3(0f, 0f, -0.001f);
            var hubMesh = IconRenderer.ToMesh(UiMeshes.Disc(UiTokens.RadialHubRadius, 48), "RadialHub");
            _ownedMeshes.Add(hubMesh);
            hub.AddComponent<MeshFilter>().sharedMesh = hubMesh;
            var hubR = hub.AddComponent<MeshRenderer>();
            hubR.sharedMaterial = sectorMaterial;
            SetTint(hubR, UiTokens.PanelBg);

            var hubIconGo = new GameObject("HubIcon") { layer = gameObject.layer };
            hubIconGo.transform.SetParent(transform, false);
            hubIconGo.transform.localPosition = new Vector3(0f, 0.013f, -0.003f);
            _hubIcon = hubIconGo.AddComponent<IconRenderer>();
            _hubIcon.Init("select-cursor", iconMaterial, 0.026f);

            _hubLabel = MakeHubText("HubLabel", new Vector3(0f, -0.014f, -0.003f), 0.016f);
            _hubHint = MakeHubText("HubHint", new Vector3(0f, -0.031f, -0.003f), 0.009f);
            _hubHint.color = UiTokens.LabelDim;

            RefreshStatic();
            gameObject.SetActive(false);
        }

        private TMP_Text MakeHubText(string name, Vector3 lp, float glyphSize)
        {
            var go = new GameObject(name) { layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            go.transform.localPosition = lp;
            var tmp = go.AddComponent<TextMeshPro>();
            if (font != null) tmp.font = font;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.rectTransform.sizeDelta = new Vector2(UiTokens.RadialHubRadius * 2.2f, glyphSize * 1.4f);
            tmp.enableAutoSizing = false;
            tmp.fontSize = glyphSize * 6.5f;   // TMP world glyph ≈ fontSize × 0.15
            tmp.color = UiTokens.LabelLight;
            return tmp;
        }

        private void RefreshStatic()
        {
            if (_icons == null || _slots == null) return;
            for (int i = 0; i < _icons.Length && i < _slots.Length; i++)
                if (_icons[i] != null && !_slots[i].Reserved)
                    _icons[i].SetTint(_slots[i].Tint);
        }

        private void RefreshVisuals()
        {
            if (!_built || _slots == null) return;
            int hot = _tracker.HighlightedSlot;

            for (int i = 0; i < _sectors.Length; i++)
            {
                bool hovered = i == hot;
                bool active = i == _currentToolSlot;
                bool reserved = i < _slots.Length && _slots[i].Reserved;

                _sectors[i].GetComponent<MeshFilter>().sharedMesh =
                    hovered && !reserved ? _sectorHoverMesh : _sectorMesh;
                Color bg = reserved ? UiTokens.ButtonDisabledBg
                    : hovered ? UiTokens.ButtonHoverBg
                    : UiTokens.ButtonBg;
                SetTint(_sectorRenderers[i], bg);

                if (_icons[i] != null && i < _slots.Length)
                    _icons[i].SetTint(hovered ? UiTokens.LabelLight
                        : active ? UiTokens.Selected
                        : _slots[i].Tint);
            }

            bool showStripe = _currentToolSlot >= 0;
            _activeStripe.SetActive(showStripe);
            if (showStripe)
                _activeStripe.transform.localRotation =
                    Quaternion.Euler(0f, 0f, -_currentToolSlot * RadialMath.SlotSpanDeg);

            bool showHover = hot >= 0 && hot < _slots.Length && !_slots[hot].Reserved;
            _hoverStripe.SetActive(showHover);
            if (showHover)
            {
                _hoverStripe.transform.localRotation =
                    Quaternion.Euler(0f, 0f, -hot * RadialMath.SlotSpanDeg);
                // ride the hovered sector's ×1.06 outward growth
                _hoverStripe.transform.localScale = Vector3.one * 1.06f;
            }

            // hub: highlighted tool wins, else the current tool (§1.7)
            int show = hot >= 0 && hot < _slots.Length && !_slots[hot].Reserved ? hot : _currentToolSlot;
            if (show >= 0 && show < _slots.Length)
            {
                _hubIcon.SetIcon(_slots[show].IconId);
                _hubIcon.SetTint(UiTokens.LabelLight);
                _hubLabel.text = _slots[show].Label;
            }
            _hubHint.text = hot >= 0 ? "release to select" : "flick to pick · B to cancel";
        }

        private void SetTint(Renderer r, Color c)
        {
            _mpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(_mpb);
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        private void OnDestroy()
        {
            // shared icon meshes live in IconRenderer's cache; these are ours to free
            if (_sectorMesh != null) Destroy(_sectorMesh);
            if (_sectorHoverMesh != null) Destroy(_sectorHoverMesh);
            if (_stripeMesh != null) Destroy(_stripeMesh);
            foreach (var m in _ownedMeshes)
                if (m != null) Destroy(m);
            _ownedMeshes.Clear();
        }
    }
}
