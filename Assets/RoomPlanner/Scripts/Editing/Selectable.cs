using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Walls;
using RoomPlanner.Floors;
using RoomPlanner.Measure;

namespace RoomPlanner.Editing
{
    /// <summary>
    /// Selection adapter placed on wall/floor/measurement roots. Auto-detects which kind it
    /// wraps, exposes bounds/highlight/move/hide, and delegates move to the underlying
    /// procedural object so geometry stays parametric (no transform drift).
    /// Highlight is a non-destructive tint via MaterialPropertyBlock; hide = SetActive(false)
    /// so a deleted object is invisible AND unpickable, and Undo simply re-activates it.
    /// </summary>
    public class Selectable : MonoBehaviour, ISelectable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int FaceColorId = Shader.PropertyToID("_FaceColor"); // TMP labels

        // Tint strength: enough to read the state, weak enough to keep the object's own color
        // visible — a full repaint would erase wall paint, the core future feature (UX v2 P1.4).
        private const float HoverTint = 0.30f;
        private const float SelectTint = 0.45f;

        private SelectableKind _kind;
        private bool _resolved;
        private Wall _wall;
        private Floor _floor;
        private Measurement _measurement;
        private RoomPlanner.Stairs.Stair _stair;
        private ISettingsProvider _settingsProvider;
        private Renderer[] _renderers;
        private Color[] _ownColors;   // each renderer's material color, cached for lerp-tinting
        private MaterialPropertyBlock _mpb;
        private HighlightState _state = HighlightState.None;

        public string Id { get; set; }
        public Transform Transform => transform;
        public bool IsHidden => !gameObject.activeSelf;

        // `this` is compile-time typed as a UnityEngine.Object here, so the overloaded
        // null-check correctly reports a destroyed component (unlike interface-typed refs).
        public bool IsAlive => this != null;

        public SelectableKind Kind { get { Resolve(); return _kind; } }

        private void Awake() => Resolve();

        private void OnDestroy()
        {
            // Safety net: drop this object (and its history commands) from the model even if
            // the destroying code forgot the explicit Unregister.
            var model = SceneModel.Instance;
            if (model != null) model.Unregister(this);
        }

        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            _wall = GetComponent<Wall>();
            _floor = GetComponent<Floor>();
            _measurement = GetComponent<Measurement>();
            _stair = GetComponent<RoomPlanner.Stairs.Stair>();
            if (_wall != null) _kind = SelectableKind.Wall;
            else if (_floor != null) _kind = SelectableKind.Floor;
            else if (_stair != null) _kind = SelectableKind.Stair;
            else _kind = SelectableKind.Measurement;
            _settingsProvider = GetComponent<ISettingsProvider>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _mpb = new MaterialPropertyBlock();

            _ownColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                var m = _renderers[i] != null ? _renderers[i].sharedMaterial : null;
                _ownColors[i] = m == null ? Color.white
                    : m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId)
                    : m.HasProperty(ColorId) ? m.GetColor(ColorId)
                    : Color.white;
            }
        }

        public Bounds WorldBounds
        {
            get
            {
                Resolve();
                bool has = false;
                Bounds b = new Bounds(transform.position, Vector3.zero);
                if (_renderers != null)
                {
                    foreach (var r in _renderers)
                    {
                        if (r == null || !r.enabled) continue;
                        if (!has) { b = r.bounds; has = true; }
                        else b.Encapsulate(r.bounds);
                    }
                }
                if (!has) b = new Bounds(transform.position, Vector3.one * 0.1f);
                return b;
            }
        }

        // ---- paint (design/04 v1): the user's own color for the BODY of the object ----

        private bool _painted;
        private Color _paint;

        public bool IsPainted { get { Resolve(); return _painted; } }
        public Color Paint { get { Resolve(); return _paint; } }

        /// <summary>The color the body shows when not highlighted (paint, or the material's own).</summary>
        public Color BaseBodyColor { get { Resolve(); return _painted ? _paint : _ownColors[0]; } }

        /// <summary>Paint the object's body (submesh 0 of the first renderer — glass and
        /// joinery keep their look). Callers own undo via PaintCommand.</summary>
        public void SetPaint(Color color)
        {
            Resolve();
            _painted = true;
            _paint = color;
            ApplyVisual();
        }

        /// <summary>Back to the material's own color.</summary>
        public void ClearPaint()
        {
            Resolve();
            _painted = false;
            ApplyVisual();
        }

        public void SetHighlight(HighlightState state)
        {
            Resolve();
            if (_state == state) return;
            _state = state;
            ApplyVisual();
        }

        /// <summary>
        /// One writer for the renderer property blocks: base color = paint (if any),
        /// lerped toward the state color while highlighted. The first renderer is the
        /// BODY — on multi-material bodies (walls: wall/glass/joinery) only material 0
        /// takes the block, so glass stays glass whatever the paint or highlight.
        /// </summary>
        private void ApplyVisual()
        {
            if (_renderers == null) return;
            bool tint = _state != HighlightState.None;
            Color stateColor = _state == HighlightState.Selected ? RoomPlanner.Tools.UiColors.Selected
                                                                 : RoomPlanner.Tools.UiColors.Hover;
            float t = _state == HighlightState.Selected ? SelectTint : HoverTint;

            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                Color own = i == 0 && _painted ? _paint : _ownColors[i];
                bool needBlock = tint || (i == 0 && _painted);
                bool bodyOnly = i == 0 && r.sharedMaterials.Length > 1;

                if (needBlock)
                {
                    Color c = tint ? Color.Lerp(own, stateColor, t) : own;
                    _mpb.Clear();
                    _mpb.SetColor(BaseColorId, c);
                    _mpb.SetColor(ColorId, c);
                    _mpb.SetColor(FaceColorId, c);   // TMP text (measurement badge) uses _FaceColor
                    if (bodyOnly) r.SetPropertyBlock(_mpb, 0);
                    else r.SetPropertyBlock(_mpb);
                }
                else
                {
                    if (bodyOnly) r.SetPropertyBlock(null, 0);
                    else r.SetPropertyBlock(null);   // restore the material's own color
                }
            }
        }

        public void SetHidden(bool hidden)
        {
            if (hidden == !gameObject.activeSelf) return;
            gameObject.SetActive(!hidden);
        }

        public void MoveBy(Vector3 delta)
        {
            Resolve();
            if (_wall != null) _wall.MoveBy(delta);
            else if (_floor != null) _floor.MoveBy(delta);
            else if (_stair != null) _stair.MoveBy(delta);
            else if (_measurement != null) _measurement.MoveBy(delta);
        }

        /// <summary>Per-instance rows come from a provider component, if one is present.</summary>
        public RoomPlanner.Core.SettingsSchema GetSettings()
        {
            Resolve();
            return _settingsProvider?.GetSettings();
        }

        public string Describe()
        {
            Resolve();
            switch (_kind)
            {
                case SelectableKind.Wall:
                    return $"Length {WallLength() * 100f:0} cm";
                case SelectableKind.Stair:
                    return _stair != null
                        ? $"{_stair.Risers} steps, {_stair.TotalHeight * 100f:0} cm up"
                        : "Stair";
                case SelectableKind.Floor:
                    if (_floor != null)
                    {
                        // Area, not the bounding box: slabs are outlines now, and "4 x 5 m" for
                        // an L-shaped room states a size the room does not have.
                        return $"{_floor.Area:0.0} m², {_floor.Outline.Count} corners";
                    }
                    return "Floor";
                default:
                    return _measurement != null ? $"{_measurement.Distance * 100f:0} cm" : "Measurement";
            }
        }

        private float WallLength()
        {
            if (_wall == null) return 0f;
            var p = _wall.Points;
            float len = 0f;
            for (int i = 1; i < p.Count; i++) len += Vector3.Distance(p[i - 1], p[i]);
            return len;
        }
    }
}
