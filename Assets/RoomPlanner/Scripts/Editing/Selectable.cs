using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
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
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");     // URP
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");     // legacy
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
        private static readonly int UseUv1Id = Shader.PropertyToID("_UseUV1");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly int HasBumpId = Shader.PropertyToID("_HasBump");

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

        // ---- finish (design/04 «Текстуры v1»): color OR texture on the BODY ----

        private SurfaceFinish _finish = SurfaceFinish.None;
        private Texture2D _finishTexture;   // resolved by the caller (FinishLibrary)
        private Texture2D _finishNormal;    // optional relief — null for most finishes (design/22)

        public bool IsPainted { get { Resolve(); return !_finish.IsNone; } }

        /// <summary>Legacy color view of the finish (persisted colors, tint of a texture).</summary>
        public Color Paint { get { Resolve(); return _finish.Color; } }

        public SurfaceFinish Finish { get { Resolve(); return _finish; } }
        public Texture2D FinishTexture { get { Resolve(); return _finishTexture; } }
        public Texture2D FinishNormal { get { Resolve(); return _finishNormal; } }

        /// <summary>The color the body shows when not highlighted (paint, or the material's own).</summary>
        public Color BaseBodyColor
        {
            get
            {
                Resolve();
                return _finish.Kind == FinishKind.Color ? _finish.Color : _ownColors[0];
            }
        }

        /// <summary>Paint the object's body a solid color (submesh 0 of the first
        /// renderer — glass and joinery keep their look). Undo via PaintCommand.</summary>
        public void SetPaint(Color color) => SetFinish(SurfaceFinish.OfColor(color), null);

        /// <summary>Apply a finish; for Texture kind the caller resolves the Texture2D
        /// (and the optional normal map) through FinishLibrary — the model itself never
        /// touches assets.</summary>
        public void SetFinish(SurfaceFinish finish, Texture2D texture, Texture2D normal = null)
        {
            Resolve();
            _finish = finish;
            _finishTexture = finish.Kind == FinishKind.Texture ? texture : null;
            _finishNormal = finish.Kind == FinishKind.Texture ? normal : null;
            ApplyVisual();
        }

        /// <summary>Back to the material's own look.</summary>
        public void ClearPaint()
        {
            Resolve();
            _finish = SurfaceFinish.None;
            _finishTexture = null;
            _finishNormal = null;
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
            bool hasFinish = !_finish.IsNone;
            bool textured = _finish.Kind == FinishKind.Texture && _finishTexture != null;

            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                // body color: solid paint, texture tint (usually white), or the material's own
                Color own = i == 0 && hasFinish ? _finish.Color : _ownColors[i];
                bool needBlock = tint || (i == 0 && hasFinish);
                bool bodyOnly = i == 0 && r.sharedMaterials.Length > 1;

                if (needBlock)
                {
                    Color c = tint ? Color.Lerp(own, stateColor, t) : own;
                    _mpb.Clear();
                    _mpb.SetColor(BaseColorId, c);
                    _mpb.SetColor(ColorId, c);
                    _mpb.SetColor(FaceColorId, c);   // TMP text (measurement badge) uses _FaceColor
                    // finish gloss applies to color paint AND textures (design/04 v1.2)
                    if (i == 0 && hasFinish) _mpb.SetFloat(SmoothnessId, _finish.Smoothness);
                    if (i == 0 && textured)
                    {
                        // wallpaper/wood over the metric UVs: swap the texture and set the
                        // metric tiling (design/04) — the material itself is never touched
                        _mpb.SetTexture(BaseMapId, _finishTexture);
                        _mpb.SetTexture(MainTexId, _finishTexture);
                        _mpb.SetVector(BaseMapStId, _finish.UvScaleOffset());
                        _mpb.SetVector(MainTexStId, _finish.UvScaleOffset());
                        // floors keep the blueprint projection in uv0; finish textures
                        // tile over the metric uv1 channel (design/04, T4)
                        if (_floor != null) _mpb.SetFloat(UseUv1Id, 1f);
                        // optional relief (design/22): the shader derives the TBN from
                        // derivatives, so no mesh tangents are needed
                        if (_finishNormal != null)
                        {
                            _mpb.SetTexture(BumpMapId, _finishNormal);
                            _mpb.SetFloat(HasBumpId, 1f);
                        }
                    }
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
                case RoomPlanner.Electrical.FixtureKind.Junction:
                {
                    // how many runs branch through this box — the number the electrician wants
                    int wires = 0;
                    var m = SceneModel.Instance;
                    if (m != null && !string.IsNullOrEmpty(Id))
                    {
                        var items = m.Items;
                        for (int i = 0; i < items.Count; i++)
                        {
                            var item = items[i];
                            if (item == null || !item.IsAlive || item.IsHidden) continue;
                            if (item is not Selectable s || s.Kind != SelectableKind.Wire || s._route == null) continue;
                            if (s._route.StartFixtureId == Id || s._route.EndFixtureId == Id) wires++;
                        }
                    }
                    return $"Junction box · {wires} wires";
                }
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
