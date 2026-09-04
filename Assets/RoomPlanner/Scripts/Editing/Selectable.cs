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
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly int HasBumpId = Shader.PropertyToID("_HasBump");
        private static readonly int UvRotId = Shader.PropertyToID("_UvRot");

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
        private RoomPlanner.Plumbing.PlumbFixture _plumbFixture;
        private RoomPlanner.Plumbing.PipeRoute _pipe;
        private OpeningLeafView _leafView;   // door/garage leaf child (issue #50)
        private RoomPlanner.Furniture.FurnitureItemView _furniture;
        private RoomPlanner.Import.MepView _mep;
        private ISettingsProvider _settingsProvider;
        private Renderer[] _renderers;
        private Color[] _ownColors;   // each renderer's material color, cached for lerp-tinting
        private MaterialPropertyBlock _mpb;
        private HighlightState _state = HighlightState.None;
        private int _bomVersion = int.MinValue;
        private string _bomDescription;

        public string Id { get; set; }
        public Transform Transform => transform;
        public bool IsHidden => !gameObject.activeSelf;

        /// <summary>Paint and highlight cover EVERY submesh, not just the body (material
        /// 0). Set by the IFC importer for elements split into per-material parts
        /// (design/29 §2): their submeshes are all «body», there is no glass slot to
        /// protect. Walls, doors and furniture leave it false.</summary>
        public bool PaintAllSubmeshes { get; set; }

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
            _plumbFixture = GetComponent<RoomPlanner.Plumbing.PlumbFixture>();
            _pipe = GetComponent<RoomPlanner.Plumbing.PipeRoute>();
            _leafView = GetComponent<OpeningLeafView>();
            _furniture = GetComponent<RoomPlanner.Furniture.FurnitureItemView>();
            _mep = GetComponent<RoomPlanner.Import.MepView>();
            // Every material slot of a baked IFC product is a physical part of the same
            // paintable object. Do not rely on one specific importer call to opt it in.
            if (_mep != null) PaintAllSubmeshes = true;
            if (_furniture != null) _kind = SelectableKind.Furniture;
            // baked IFC elements: their own kind since issue #135, so they can be picked,
            // deleted and re-dressed instead of silently reading as «Measurement»
            else if (_mep != null) _kind = SelectableKind.Mep;
            else if (_wall != null) _kind = SelectableKind.Wall;
            else if (_leafView != null) _kind = SelectableKind.Door;
            else if (_floor != null) _kind = SelectableKind.Floor;
            else if (_stair != null) _kind = SelectableKind.Stair;
            else if (_fixture != null) _kind = SelectableKind.Fixture;
            else if (_route != null) _kind = SelectableKind.Wire;
            else if (_plumbFixture != null) _kind = SelectableKind.PlumbFixture;
            else if (_pipe != null) _kind = SelectableKind.Pipe;
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
        // Walls carry a PAIR of finishes (issue #34): _finish = inner side (and the
        // whole object for every other kind), _finishOuter = outer side; the mesh
        // splits the body into submeshes 0 (inner) / 3 (outer) / 4 (rims → outer).

        private SurfaceFinish _finish = SurfaceFinish.None;
        private Texture2D _finishTexture;   // resolved by the caller (FinishLibrary)
        private Texture2D _finishNormal;    // optional relief — null for most finishes (design/22)
        private SurfaceFinish _finishOuter = SurfaceFinish.None;
        private Texture2D _finishTextureOuter;
        private Texture2D _finishNormalOuter;

        public bool IsPainted { get { Resolve(); return !_finish.IsNone || !_finishOuter.IsNone; } }

        /// <summary>Legacy color view of the finish (persisted colors, tint of a texture).</summary>
        public Color Paint { get { Resolve(); return _finish.Color; } }

        public SurfaceFinish Finish { get { Resolve(); return _finish; } }
        public Texture2D FinishTexture { get { Resolve(); return _finishTexture; } }
        public Texture2D FinishNormal { get { Resolve(); return _finishNormal; } }

        /// <summary>Per-side view: non-walls answer with their single finish.</summary>
        public SurfaceFinish FinishOf(WallSide side)
        {
            Resolve();
            return side == WallSide.Outer && _wall != null ? _finishOuter : _finish;
        }

        public Texture2D FinishTextureOf(WallSide side)
        {
            Resolve();
            return side == WallSide.Outer && _wall != null ? _finishTextureOuter : _finishTexture;
        }

        public Texture2D FinishNormalOf(WallSide side)
        {
            Resolve();
            return side == WallSide.Outer && _wall != null ? _finishNormalOuter : _finishNormal;
        }

        /// <summary>The color the body shows when not highlighted (paint, or the material's own).</summary>
        public Color BaseBodyColor
        {
            get
            {
                Resolve();
                return _finish.Kind == FinishKind.Color ? _finish.Color
                    : _fixture != null ? _fixture.PlasticColor
                    : _ownColors[0];
            }
        }

        /// <summary>Paint the object's body a solid color (submesh 0 of the first
        /// renderer — glass and joinery keep their look). Undo via PaintCommand.</summary>
        public void SetPaint(Color color) => SetFinish(SurfaceFinish.OfColor(color), null);

        /// <summary>Apply a finish to the WHOLE object (both wall sides); for Texture
        /// kind the caller resolves the Texture2D (and the optional normal map) through
        /// FinishLibrary — the model itself never touches assets.</summary>
        public void SetFinish(SurfaceFinish finish, Texture2D texture, Texture2D normal = null)
        {
            Resolve();
            _finish = finish;
            _finishTexture = finish.Kind == FinishKind.Texture ? texture : null;
            _finishNormal = finish.Kind == FinishKind.Texture ? normal : null;
            _finishOuter = _wall != null ? finish : SurfaceFinish.None;
            _finishTextureOuter = _wall != null ? _finishTexture : null;
            _finishNormalOuter = _wall != null ? _finishNormal : null;
            ApplyVisual();
        }

        /// <summary>Apply a finish to ONE wall side (issue #34); anything that is not
        /// a wall degrades to the whole-object path.</summary>
        public void SetFinishSide(WallSide side, SurfaceFinish finish, Texture2D texture,
            Texture2D normal = null)
        {
            Resolve();
            if (_wall == null) { SetFinish(finish, texture, normal); return; }
            // Rectangular IFC columns reuse Wall's five-submesh mesh, but have no
            // semantic inside/outside. A finish chosen on any face covers the object.
            if (_wall.Segment != null && _wall.Segment.IsColumn)
            {
                SetFinish(finish, texture, normal);
                return;
            }
            var tex = finish.Kind == FinishKind.Texture ? texture : null;
            var nrm = finish.Kind == FinishKind.Texture ? normal : null;
            if (side == WallSide.Outer)
            {
                _finishOuter = finish; _finishTextureOuter = tex; _finishNormalOuter = nrm;
            }
            else
            {
                _finish = finish; _finishTexture = tex; _finishNormal = nrm;
            }
            ApplyVisual();
        }

        /// <summary>Back to the material's own look.</summary>
        public void ClearPaint()
        {
            Resolve();
            _finish = SurfaceFinish.None;
            _finishTexture = null;
            _finishNormal = null;
            _finishOuter = SurfaceFinish.None;
            _finishTextureOuter = null;
            _finishNormalOuter = null;
            ApplyVisual();
        }

        public void SetHighlight(HighlightState state)
        {
            Resolve();
            if (_state == state) return;
            _state = state;
            ApplyVisual();
        }

        /// <summary>Re-apply finish and highlight after an owner changes its native
        /// material state, for example an electrical fixture colour variant.</summary>
        public void RefreshVisual()
        {
            Resolve();
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
            Color stateColor = _state == HighlightState.Selected ? UiTokens.Selected
                                                                 : UiTokens.Hover;
            float t = _state == HighlightState.Selected ? SelectTint : HoverTint;
            bool hasFinish = !_finish.IsNone;

            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;

                // Wall body with per-side submeshes (issue #34): inner (0) takes its
                // own finish, outer (3) and rims (4) take the outer one — rims follow
                // the outer look, as real decorating leaves reveals unpapered.
                if (i == 0 && _wall != null && r.sharedMaterials.Length >= 5)
                {
                    BodyBlock(r, 0, _finish, _finishTexture, _finishNormal,
                        tint, stateColor, t, _ownColors[i]);
                    BodyBlock(r, 3, _finishOuter, _finishTextureOuter, _finishNormalOuter,
                        tint, stateColor, t, _ownColors[i]);
                    BodyBlock(r, 4, _finishOuter, _finishTextureOuter, _finishNormalOuter,
                        tint, stateColor, t, _ownColors[i]);
                    continue;
                }

                // A closed panel has no plastic triangles, so highlighting only slot 0
                // makes selection invisible. Tint all three fixture surfaces while
                // preserving their deliberately different plastic/metal responses.
                if (i == 0 && _fixture != null
                    && r.sharedMaterials.Length >= RoomPlanner.Electrical.ElectricFixture.SubmeshCount)
                {
                    FixtureBlock(r, RoomPlanner.Electrical.ElectricFixture.PlasticSubmesh,
                        _finish, _finishTexture, _finishNormal, _fixture.PlasticColor,
                        0.55f, 0f, tint, stateColor, t);
                    FixtureBlock(r, RoomPlanner.Electrical.ElectricFixture.AccentSubmesh,
                        SurfaceFinish.None, null, null,
                        RoomPlanner.Electrical.ElectricFixture.DarkAccent,
                        0.75f, 0.45f, tint, stateColor, t);
                    FixtureBlock(r, RoomPlanner.Electrical.ElectricFixture.MetalSubmesh,
                        SurfaceFinish.None, null, null, _fixture.PanelMetalColor,
                        0.38f, 0.65f, tint, stateColor, t);
                    continue;
                }

                // a leaf view's "body" is every panel renderer, not just the first
                bool body = i == 0 || _leafView != null;
                bool needBlock = tint || (body && hasFinish);
                // Imported elements wear one material PER PART (design/29 §2), so paint
                // and highlight must cover every submesh; walls/doors keep the body-only
                // rule that protects their glass and joinery.
                bool bodyOnly = i == 0 && r.sharedMaterials.Length > 1 && !PaintAllSubmeshes;
                bool everySubmesh = i == 0 && PaintAllSubmeshes
                    && r.sharedMaterials.Length > 1;

                if (needBlock)
                {
                    var finish = body ? _finish : SurfaceFinish.None;
                    FillBlock(finish, body ? _finishTexture : null, body ? _finishNormal : null,
                        tint, stateColor, t, _ownColors[i]);
                    if (everySubmesh)
                    {
                        r.SetPropertyBlock(null);
                        for (int slot = 0; slot < r.sharedMaterials.Length; slot++)
                            r.SetPropertyBlock(_mpb, slot);
                    }
                    else if (bodyOnly) r.SetPropertyBlock(_mpb, 0);
                    else r.SetPropertyBlock(_mpb);
                }
                else
                {
                    if (everySubmesh)
                    {
                        r.SetPropertyBlock(null);
                        for (int slot = 0; slot < r.sharedMaterials.Length; slot++)
                            r.SetPropertyBlock(null, slot);
                    }
                    else if (bodyOnly) r.SetPropertyBlock(null, 0);
                    else r.SetPropertyBlock(null);   // restore the material's own color
                }
            }
        }

        private void FixtureBlock(Renderer r, int index, SurfaceFinish finish, Texture2D tex,
            Texture2D normal, Color ownColor, float smoothness, float metallic,
            bool tint, Color stateColor, float t)
        {
            FillBlock(finish, tex, normal, tint, stateColor, t, ownColor);
            if (finish.IsNone) _mpb.SetFloat(SmoothnessId, smoothness);
            _mpb.SetFloat(MetallicId, metallic);
            r.SetPropertyBlock(_mpb, index);
        }

        /// <summary>One body submesh of a per-side wall: block when painted or
        /// highlighted, cleared otherwise.</summary>
        private void BodyBlock(Renderer r, int index, SurfaceFinish finish, Texture2D tex,
            Texture2D normal, bool tint, Color stateColor, float t, Color ownColor)
        {
            if (!tint && finish.IsNone) { r.SetPropertyBlock(null, index); return; }
            FillBlock(finish, tex, normal, tint, stateColor, t, ownColor);
            r.SetPropertyBlock(_mpb, index);
        }

        /// <summary>Fill _mpb for one surface: paint/texture/tint — the single place
        /// deciding what a finish looks like.</summary>
        private void FillBlock(SurfaceFinish finish, Texture2D tex, Texture2D normal,
            bool tint, Color stateColor, float t, Color ownColor)
        {
            bool hasFinish = !finish.IsNone;
            bool textured = finish.Kind == FinishKind.Texture && tex != null;
            // body color: solid paint, texture tint (usually white), or the material's own
            Color own = hasFinish ? finish.Color : ownColor;
            Color c = tint ? Color.Lerp(own, stateColor, t) : own;
            _mpb.Clear();
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(ColorId, c);
            _mpb.SetColor(FaceColorId, c);   // TMP text (measurement badge) uses _FaceColor
            // finish gloss applies to color paint AND textures (design/04 v1.2)
            if (hasFinish) _mpb.SetFloat(SmoothnessId, finish.Smoothness);
            if (textured)
            {
                // wallpaper/wood over the metric UVs: swap the texture and set the
                // metric tiling (design/04) — the material itself is never touched
                _mpb.SetTexture(BaseMapId, tex);
                _mpb.SetTexture(MainTexId, tex);
                _mpb.SetVector(BaseMapStId, finish.UvScaleOffset());
                _mpb.SetVector(MainTexStId, finish.UvScaleOffset());
                // turn the texture in the metric plane (rotate laminate etc.);
                // (cos,sin), default (1,0) = no rotation
                _mpb.SetVector(UvRotId, finish.UvRotation());
                // floors keep the blueprint projection in uv0; finish textures
                // tile over the metric uv1 channel (design/04, T4)
                if (_floor != null) _mpb.SetFloat(UseUv1Id, 1f);
                // optional relief (design/22): the shader derives the TBN from
                // derivatives, so no mesh tangents are needed
                if (normal != null)
                {
                    _mpb.SetTexture(BumpMapId, normal);
                    _mpb.SetFloat(HasBumpId, 1f);
                }
            }
        }

        /// <summary>Owners that must react to hide/show (e.g. WallGraphRenderer pulls a
        /// hidden wall out of its neighbours' joints — audit 02 §Б3).</summary>
        public event System.Action<bool> HiddenChanged;

        public void SetHidden(bool hidden)
        {
            if (hidden == !gameObject.activeSelf) return;
            gameObject.SetActive(!hidden);
            HiddenChanged?.Invoke(hidden);
        }

        public void MoveBy(Vector3 delta)
        {
            Resolve();
            if (_leafView != null) return;   // doors move with the Openings tool, not Select
            if (_furniture != null) _furniture.MoveBy(delta);
            else if (_wall != null) _wall.MoveBy(delta);
            else if (_floor != null) _floor.MoveBy(delta);
            else if (_stair != null) _stair.MoveBy(delta);
            else if (_fixture != null) MoveFixture(delta);
            else if (_route != null) _route.MoveBy(delta);
            else if (_plumbFixture != null) MovePlumb(delta);
            else if (_pipe != null) MovePipe(delta);
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

        /// <summary>Moving a plumb fixture drags the pipe ends logically attached to it
        /// (the electrical id-link pattern).</summary>
        private void MovePlumb(Vector3 delta)
        {
            _plumbFixture.MoveBy(delta);
            DragAttachedPipeEnds(delta);
        }

        /// <summary>Moving a pipe (a riser above all) drags the ends of OTHER pipes teed
        /// into it by id.</summary>
        private void MovePipe(Vector3 delta)
        {
            _pipe.MoveBy(delta);
            DragAttachedPipeEnds(delta);
        }

        private void DragAttachedPipeEnds(Vector3 delta)
        {
            var model = SceneModel.Instance;
            if (model == null || string.IsNullOrEmpty(Id)) return;
            var items = model.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || !item.IsAlive) continue;
                if (item is Selectable s && s.Kind == SelectableKind.Pipe && s._pipe != null
                    && s._pipe != _pipe)
                    s._pipe.TryMoveAttachedEnd(Id, delta);
            }
        }

        /// <summary>Typed accessors for electrical tooling (terminal snap, BOM) — cached in
        /// Resolve so per-frame loops never call GetComponent.</summary>
        internal RoomPlanner.Electrical.ElectricFixture Fixture { get { Resolve(); return _fixture; } }
        internal RoomPlanner.Electrical.WireRoute Route { get { Resolve(); return _route; } }

        /// <summary>Typed accessors for plumbing tooling (terminal/riser snap, BOM).</summary>
        internal RoomPlanner.Plumbing.PlumbFixture Plumb { get { Resolve(); return _plumbFixture; } }
        internal RoomPlanner.Plumbing.PipeRoute Pipe { get { Resolve(); return _pipe; } }

        /// <summary>The door/garage leaf this Selectable wraps (trigger toggle, issue #50).</summary>
        internal OpeningLeafView LeafView { get { Resolve(); return _leafView; } }

        /// <summary>Per-instance rows come from a provider component, if one is present.</summary>
        public RoomPlanner.Core.SettingsSchema GetSettings()
        {
            Resolve();
            return _settingsProvider?.GetSettings();
        }

        /// <summary>
        /// Endpoints for the selection-context Quick Measure action. Parametric lines expose
        /// their real endpoints; volume-like objects fall back to their longest bounds axis.
        /// </summary>
        public bool TryGetMeasurementSpan(out Vector3 a, out Vector3 b)
        {
            Resolve();
            if (_wall != null && _wall.Points.Count >= 2)
            {
                a = _wall.Points[0];
                b = _wall.Points[_wall.Points.Count - 1];
                return (b - a).sqrMagnitude > 1e-8f;
            }
            if (_measurement != null)
            {
                a = _measurement.PointA;
                b = _measurement.PointB;
                return (b - a).sqrMagnitude > 1e-8f;
            }

            Bounds bounds = WorldBounds;
            Vector3 size = bounds.size;
            int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            Vector3 half = axis == 0 ? Vector3.right * size.x * 0.5f
                : axis == 1 ? Vector3.up * size.y * 0.5f
                : Vector3.forward * size.z * 0.5f;
            a = bounds.center - half;
            b = bounds.center + half;
            return half.sqrMagnitude > 1e-8f;
        }

        /// <summary>Short hover badge; deliberately smaller than the inspector description.</summary>
        public string CompactDimensions()
        {
            Resolve();
            if (_wall != null) return $"L {WallLength():0.00} m";
            if (_measurement != null) return $"{_measurement.Distance:0.00} m";
            if (_leafView != null && _leafView.Opening != null)
                return $"{_leafView.Opening.Width * 100f:0}×{_leafView.Opening.Height * 100f:0} cm";
            if (_floor != null) return $"{_floor.Area:0.0} m²";
            if (_route != null) return $"L {_route.Length:0.00} m";

            Bounds bounds = WorldBounds;
            float horizontalMax = Mathf.Max(bounds.size.x, bounds.size.z);
            if (bounds.size.y > horizontalMax * 1.25f)
                return $"H {bounds.size.y:0.00} m";
            return $"{bounds.size.x:0.00}×{bounds.size.z:0.00} m";
        }

        public string Describe()
        {
            Resolve();
            switch (_kind)
            {
                case SelectableKind.Wall:
                    return $"Length {WallLength() * 100f:0} cm";
                case SelectableKind.Door:
                {
                    var op = _leafView != null ? _leafView.Opening : null;
                    if (op == null) return "Door";
                    string kind = op.Kind == RoomPlanner.Walls.OpeningKind.Garage ? "Garage door" : "Door";
                    return $"{kind} {op.Width * 100f:0}×{op.Height * 100f:0} cm · open {op.OpenFraction * 100f:0}%";
                }
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
                case SelectableKind.Furniture:
                    return _furniture != null ? _furniture.Describe() : "Furniture";
                case SelectableKind.Fixture:
                    return DescribeFixture();
                case SelectableKind.Wire:
                    return _route != null
                        ? $"{RoomPlanner.Electrical.Cable.Label(_route.Cable)} · {RoomPlanner.Electrical.ElectricalBom.FormatMeters(_route.Length)}"
                        : "Wire";
                case SelectableKind.PlumbFixture:
                    return DescribePlumbFixture();
                case SelectableKind.Pipe:
                    return DescribePipe();
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
                    int version = model != null ? model.Version : -1;
                    if (_bomVersion == version && _bomDescription != null)
                        return _bomDescription;
                    var entries = new System.Collections.Generic.List<RoomPlanner.Electrical.RouteBomEntry>();
                    int routed = 0, total = 0;
                    if (model != null)
                    {
                        var items = model.Items;
                        // An end attached to a DELETED fixture must not bill a connection
                        // allowance (audit 08 §Б3): resolve ids against live fixtures
                        // instead of trusting string non-emptiness (WireRoute.Connections).
                        var liveFixtures = new System.Collections.Generic.HashSet<string>();
                        for (int i = 0; i < items.Count; i++)
                        {
                            var item = items[i];
                            if (item == null || !item.IsAlive || item.IsHidden) continue;
                            if (item is Selectable f && f.Kind == SelectableKind.Fixture
                                && !string.IsNullOrEmpty(f.Id))
                                liveFixtures.Add(f.Id);
                        }
                        for (int i = 0; i < items.Count; i++)
                        {
                            var item = items[i];
                            if (item == null || !item.IsAlive || item.IsHidden) continue;
                            if (item is not Selectable s || s.Kind != SelectableKind.Wire || s._route == null) continue;
                            var r = s._route;
                            int liveEnds =
                                (!string.IsNullOrEmpty(r.StartFixtureId) && liveFixtures.Contains(r.StartFixtureId) ? 1 : 0)
                                + (!string.IsNullOrEmpty(r.EndFixtureId) && liveFixtures.Contains(r.EndFixtureId) ? 1 : 0);
                            entries.Add(new RoomPlanner.Electrical.RouteBomEntry(r.Cable, r.Length, liveEnds));
                            total++;
                            if (!string.IsNullOrEmpty(Id) && (r.StartFixtureId == Id || r.EndFixtureId == Id)) routed++;
                        }
                    }
                    _bomDescription = RoomPlanner.Electrical.ElectricalBom.Describe(
                        entries, _fixture.ReservePercent, unrouted: total - routed);
                    _bomVersion = version;
                    return _bomDescription;
                }
            }
        }

        private string DescribePlumbFixture()
        {
            if (_plumbFixture == null) return "Plumbing";
            string angle = _plumbFixture.Angle switch
            {
                RoomPlanner.Plumbing.OutletAngle.Deg45 => "45°↓",
                RoomPlanner.Plumbing.OutletAngle.Deg45Up => "45°↑",
                _ => "90°",
            };
            return _plumbFixture.Kind switch
            {
                RoomPlanner.Plumbing.PlumbFixtureKind.ToiletOutlet =>
                    $"Toilet outlet D110 {angle}, h {_plumbFixture.HeightAboveLevel * 100f:0} cm",
                RoomPlanner.Plumbing.PlumbFixtureKind.SinkOutlet =>
                    $"Sink outlet D50 {angle}, h {_plumbFixture.HeightAboveLevel * 100f:0} cm",
                _ => $"Floor drain {PlumbingDrainLabel()}",
            };
        }

        private static string PlumbingDrainLabel() =>
            $"{RoomPlanner.Plumbing.PlumbingDefaults.DrainSize * 100f:0}×{RoomPlanner.Plumbing.PlumbingDefaults.DrainSize * 100f:0} cm";

        /// <summary>A riser's description IS the plumbing BOM (docs/design/30-plumbing.md),
        /// the electrical-panel precedent; an ordinary pipe reports its own size and length.
        /// Selection-time only — the allocation here never runs per frame.</summary>
        private string DescribePipe()
        {
            if (_pipe == null) return "Pipe";
            if (!_pipe.IsRiser)
                return $"D{RoomPlanner.Plumbing.PipeSpec.Label(_pipe.Diameter)} · {RoomPlanner.Plumbing.PlumbingBom.FormatMeters(_pipe.Length)}";

            var model = SceneModel.Instance;
            var entries = new System.Collections.Generic.List<RoomPlanner.Plumbing.PipeBomEntry>();
            if (model != null)
            {
                var items = model.Items;
                // an end attached to a DELETED peer must not bill a connection allowance
                // (the audit 08 §Б3 lesson from electrical): resolve ids against live objects
                var liveIds = new System.Collections.Generic.HashSet<string>();
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (item == null || !item.IsAlive || item.IsHidden) continue;
                    if (item is Selectable p && !string.IsNullOrEmpty(p.Id)
                        && (p.Kind == SelectableKind.PlumbFixture || p.Kind == SelectableKind.Pipe))
                        liveIds.Add(p.Id);
                }
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (item == null || !item.IsAlive || item.IsHidden) continue;
                    if (item is not Selectable s || s.Kind != SelectableKind.Pipe || s._pipe == null) continue;
                    var r = s._pipe;
                    int liveEnds =
                        (!string.IsNullOrEmpty(r.StartFixtureId) && liveIds.Contains(r.StartFixtureId) ? 1 : 0)
                        + (!string.IsNullOrEmpty(r.EndFixtureId) && liveIds.Contains(r.EndFixtureId) ? 1 : 0);
                    RoomPlanner.Plumbing.PipeMath.CountElbows(r.Points, out int e90, out int e45);
                    entries.Add(new RoomPlanner.Plumbing.PipeBomEntry(r.Diameter, r.Length, liveEnds, e90, e45));
                }
            }
            return "Riser · " + RoomPlanner.Plumbing.PlumbingBom.Describe(entries, _pipe.ReservePercent);
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
