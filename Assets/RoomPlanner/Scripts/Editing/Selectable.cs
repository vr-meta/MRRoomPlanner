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
        private RoomPlanner.Electrical.ElectricFixture _fixture;
        private RoomPlanner.Electrical.WireRoute _route;
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
            _fixture = GetComponent<RoomPlanner.Electrical.ElectricFixture>();
            _route = GetComponent<RoomPlanner.Electrical.WireRoute>();
            if (_wall != null) _kind = SelectableKind.Wall;
            else if (_floor != null) _kind = SelectableKind.Floor;
            else if (_stair != null) _kind = SelectableKind.Stair;
            else if (_fixture != null) _kind = SelectableKind.Fixture;
            else if (_route != null) _kind = SelectableKind.Wire;
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

        public void SetHighlight(HighlightState state)
        {
            Resolve();
            if (_state == state) return;
            _state = state;
            if (_renderers == null) return;

            bool tint = state != HighlightState.None;
            Color stateColor = state == HighlightState.Selected ? RoomPlanner.Tools.UiColors.Selected
                                                                : RoomPlanner.Tools.UiColors.Hover;
            float t = state == HighlightState.Selected ? SelectTint : HoverTint;
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                if (tint)
                {
                    // Lerp toward the state color instead of repainting — the object keeps
                    // its identity (own material/paint stays visible under the tint).
                    Color c = Color.Lerp(_ownColors[i], stateColor, t);
                    r.GetPropertyBlock(_mpb);
                    _mpb.SetColor(BaseColorId, c);
                    _mpb.SetColor(ColorId, c);
                    _mpb.SetColor(FaceColorId, c);   // TMP text (measurement badge) uses _FaceColor
                    r.SetPropertyBlock(_mpb);
                }
                else
                {
                    r.SetPropertyBlock(null);   // restore the material's own color
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
            else if (_fixture != null) MoveFixture(delta);
            else if (_route != null) _route.MoveBy(delta);
            else if (_measurement != null) _measurement.MoveBy(delta);
        }

        /// <summary>Moving a fixture drags the wire ends logically attached to it — the
        /// route↔fixture link is by SceneModel id, so we resolve peers through the registry
        /// (the same no-wiring pattern walls use to find floor slabs).</summary>
        private void MoveFixture(Vector3 delta)
        {
            _fixture.MoveBy(delta);
            var model = SceneModel.Instance;
            if (model == null || string.IsNullOrEmpty(Id)) return;
            var items = model.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || !item.IsAlive) continue;
                if (item is Selectable s && s.Kind == SelectableKind.Wire && s._route != null)
                    s._route.TryMoveAttachedEnd(Id, delta);
            }
        }

        /// <summary>Typed accessors for electrical tooling (terminal snap, BOM) — cached in
        /// Resolve so per-frame loops never call GetComponent.</summary>
        internal RoomPlanner.Electrical.ElectricFixture Fixture { get { Resolve(); return _fixture; } }
        internal RoomPlanner.Electrical.WireRoute Route { get { Resolve(); return _route; } }

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
                case SelectableKind.Fixture:
                    return DescribeFixture();
                case SelectableKind.Wire:
                    return _route != null
                        ? $"{RoomPlanner.Electrical.Cable.Label(_route.Cable)} · {RoomPlanner.Electrical.ElectricalBom.FormatMeters(_route.Length)}"
                        : "Wire";
                default:
                    return _measurement != null ? $"{_measurement.Distance * 100f:0} cm" : "Measurement";
            }
        }

        /// <summary>The panel's description IS the cable BOM (docs/design/19-electrical.md):
        /// live routes are collected from the registry, so the summary is always current.
        /// Selection-time only — the allocation here never runs per frame.</summary>
        private string DescribeFixture()
        {
            if (_fixture == null) return "Fixture";
            switch (_fixture.Kind)
            {
                case RoomPlanner.Electrical.FixtureKind.Outlet:
                    return $"Outlet ×{_fixture.Posts}, h {_fixture.HeightAboveLevel * 100f:0} cm";
                case RoomPlanner.Electrical.FixtureKind.Switch:
                    return $"Switch ×{_fixture.Keys}, h {_fixture.HeightAboveLevel * 100f:0} cm";
                default:
                {
                    var model = SceneModel.Instance;
                    var entries = new System.Collections.Generic.List<RoomPlanner.Electrical.RouteBomEntry>();
                    int routed = 0, total = 0;
                    if (model != null)
                    {
                        var items = model.Items;
                        for (int i = 0; i < items.Count; i++)
                        {
                            var item = items[i];
                            if (item == null || !item.IsAlive || item.IsHidden) continue;
                            if (item is not Selectable s || s.Kind != SelectableKind.Wire || s._route == null) continue;
                            var r = s._route;
                            entries.Add(new RoomPlanner.Electrical.RouteBomEntry(r.Cable, r.Length, r.Connections));
                            total++;
                            if (!string.IsNullOrEmpty(Id) && (r.StartFixtureId == Id || r.EndFixtureId == Id)) routed++;
                        }
                    }
                    return RoomPlanner.Electrical.ElectricalBom.Describe(
                        entries, _fixture.ReservePercent, unrouted: total - routed);
                }
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
