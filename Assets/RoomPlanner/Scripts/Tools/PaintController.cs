using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Measure;
using RoomPlanner.Walls;

namespace RoomPlanner.Tools
{
    /// <summary>Apply a finish (solid color OR texture) to the object's body — the
    /// whole object, or ONE side of a wall (issue #34). Undo restores BOTH sides'
    /// previous finishes, so mixed states survive the round-trip. Before/after
    /// snapshots, not deltas (coding rule: no clamp drift).</summary>
    public class PaintCommand : ICommand, ISelectableCommand
    {
        private readonly Selectable _target;
        private readonly SurfaceFinish _after;
        private readonly Texture2D _afterTex;
        private readonly bool _sideOnly;
        private readonly WallSide _side;
        private readonly SurfaceFinish _beforeInner, _beforeOuter;
        private readonly Texture2D _beforeInnerTex, _beforeOuterTex;

        public PaintCommand(Selectable target, Color color)
            : this(target, SurfaceFinish.OfColor(color), null) { }

        public PaintCommand(Selectable target, SurfaceFinish after, Texture2D afterTex)
            : this(target, after, afterTex, sideOnly: false, WallSide.Inner) { }

        /// <summary>Paint one wall side (Apply: Side, the default).</summary>
        public PaintCommand(Selectable target, SurfaceFinish after, Texture2D afterTex, WallSide side)
            : this(target, after, afterTex, sideOnly: true, side) { }

        private PaintCommand(Selectable target, SurfaceFinish after, Texture2D afterTex,
            bool sideOnly, WallSide side)
        {
            _target = target;
            _after = after;
            _afterTex = afterTex;
            _sideOnly = sideOnly;
            _side = side;
            if (target != null)
            {
                _beforeInner = target.FinishOf(WallSide.Inner);
                _beforeInnerTex = target.FinishTextureOf(WallSide.Inner);
                _beforeOuter = target.FinishOf(WallSide.Outer);
                _beforeOuterTex = target.FinishTextureOf(WallSide.Outer);
            }
        }

        public ISelectable Target => _target;

        private bool Alive => _target != null && _target.IsAlive;

        public string Name => "Paint";

        public void Do()
        {
            if (!Alive) return;
            if (_sideOnly) _target.SetFinishSide(_side, _after, _afterTex);
            else _target.SetFinish(_after, _afterTex);
        }

        public void Undo()
        {
            if (!Alive) return;
            _target.SetFinishSide(WallSide.Inner, _beforeInner, _beforeInnerTex);
            _target.SetFinishSide(WallSide.Outer, _beforeOuter, _beforeOuterTex);
        }
    }

    /// <summary>
    /// Paint tool ("Pnt", design/04 v1): pick a preset color in the inspector, pull the
    /// trigger on a wall / floor / stair — one undoable PaintCommand per click. B = back
    /// to Select. Zones and textures come later; solid per-object color is the v1.
    /// </summary>
    public class PaintController : MonoBehaviour, ITool
    {
        [SerializeField] private PointerProvider pointer;
        [SerializeField] private MeasureInput input;
        [SerializeField] private ToolManager manager;
        [SerializeField] private SceneModel sceneModel;
        [SerializeField] private Transform reticle;   // visible aim point (device feedback 2026-08-10)
        [SerializeField] private FinishLibrary library;   // texture catalog (design/04 «Текстуры v1»)

        private static readonly (string Name, Color Color)[] Presets =
        {
            ("White", new Color(0.95f, 0.94f, 0.91f)),
            ("Sand", new Color(0.89f, 0.84f, 0.72f)),
            ("Terracotta", new Color(0.77f, 0.39f, 0.23f)),
            ("Sage", new Color(0.61f, 0.69f, 0.53f)),
            ("Sky", new Color(0.50f, 0.66f, 0.79f)),
            ("Graphite", new Color(0.29f, 0.31f, 0.33f)),
            ("Brick", new Color(0.62f, 0.29f, 0.24f)),
            ("Mint", new Color(0.66f, 0.81f, 0.75f)),
        };

        /// <summary>Texture tabs 1..N map onto these catalog categories (v1.2 «много
        /// материалов»): Tiles suit walls AND floors, Ceiling paints a slab whose
        /// underside is the storey-below ceiling.</summary>
        private static readonly (string cat, string swatchLabel, string hint)[] TexTabs =
        {
            ("Walls", "Material", "aim a wall · Trigger = apply"),
            ("Floors", "Material", "aim a floor · Trigger = apply"),
            ("Tiles", "Tile", "walls & floors · Trigger = apply"),
            ("Ceiling", "Material", "aim a slab · Trigger = apply"),
        };

        private int _preset;
        private int _tab;          // 0 Color · 1.. = TexTabs categories
        // 0 = one wall side (the default — headset feedback 2026-08-10), 1 = whole object
        private int _scope;
        private static readonly string[] ApplyOptions = { "Side", "Whole" };
        private readonly int[] _pick = new int[TexTabs.Length];
        // tile-size overrides in metres, both axes; 0 = the texture's own catalog tile
        // (headset feedback 2026-08-11: repeats need to be adjustable per axis)
        private readonly float[] _tileW = new float[TexTabs.Length];
        private readonly float[] _tileH = new float[TexTabs.Length];
        // gloss override per TAB (index 0 = Color); -1 = auto (catalog / matte paint)
        private readonly float[] _gloss = { -1f, -1f, -1f, -1f, -1f };
        private readonly string[][] _catIds = new string[TexTabs.Length][];
        private Selectable _hover;
        private SettingsSchema _settings;

        private const float ColorAutoGloss = 0.10f;   // satin paint default

        public string Id => "paint";
        public string PaletteLabel => "Pnt";
        public string IconId => "paint-roller";

        public Color CurrentColor => Presets[_preset].Color;

        private static Color[] _palette;

        public SettingsSchema GetSettings()
        {
            // Tabs (design/04 «Текстуры v1»): Color = paint grid; Walls/Floors = CC0
            // texture swatches. The active tab decides what the trigger applies.
            if (_palette == null)
            {
                _palette = new Color[Presets.Length];
                for (int i = 0; i < Presets.Length; i++) _palette[i] = Presets[i].Color;
            }
            if (_settings == null)
            {
                var colorPage = new SettingsSchema()
                    .Readout("how", "How to", () => "aim wall/floor · Trigger = paint")
                    .Swatch("color", "Color", _palette, () => _preset,
                        i => _preset = Mathf.Clamp(i, 0, Presets.Length - 1))
                    .Readout("cname", "Preset", () => Presets[_preset].Name);
                AddGlossStepper(colorPage, "cg", 0);
                AddApplyScope(colorPage, "ap-color");
                colorPage.Action("clear", "Original look (unpaint aimed)", "eraser", ClearHovered);
                // NOTE: the Shading toggles moved to the Rendering page (snap-strip gear).

                var tabNames = new string[TexTabs.Length + 1];
                var pages = new SettingsSchema[TexTabs.Length + 1];
                tabNames[0] = "Color";
                pages[0] = colorPage;
                for (int c = 0; c < TexTabs.Length; c++)
                {
                    _catIds[c] = library != null ? library.IdsOf(TexTabs[c].cat).ToArray()
                        : System.Array.Empty<string>();
                    tabNames[c + 1] = TexTabs[c].cat;
                    pages[c + 1] = BuildTexturePage(c);
                }

                _settings = SettingsSchema.Tabbed(tabNames,
                    () => _tab, i => _tab = Mathf.Clamp(i, 0, TexTabs.Length), pages);
            }
            return _settings;
        }

        /// <summary>One texture tab: swatch grid + tile W/H steppers + gloss stepper.</summary>
        private SettingsSchema BuildTexturePage(int c)
        {
            string k = TexTabs[c].cat.ToLowerInvariant();
            var page = new SettingsSchema()
                .Readout($"how-{k}", "How to", () => TexTabs[c].hint);
            if (_catIds[c].Length > 0)
            {
                page.TextureSwatch($"tex-{k}", TexTabs[c].swatchLabel, _catIds[c],
                        () => _pick[c], i => _pick[c] = Mathf.Clamp(i, 0, _catIds[c].Length - 1))
                    .Stepper($"tw-{k}", "Tile W",
                        () => TileLabel(_tileW[c], LibraryTile(c)),
                        () => _tileW[c] = StepTile(_tileW[c], LibraryTile(c), -1),
                        () => _tileW[c] = StepTile(_tileW[c], LibraryTile(c), +1))
                    .Stepper($"th-{k}", "Tile H",
                        () => _tileH[c] > 0f ? $"{_tileH[c] * 100f:0} cm" : "= W",
                        () => _tileH[c] = StepTile(_tileH[c], EffectiveTileW(c), -1),
                        () => _tileH[c] = StepTile(_tileH[c], EffectiveTileW(c), +1));
                AddGlossStepper(page, $"gl-{k}", c + 1);
                // Apply scope only where a wall can be the target (Walls, Tiles)
                if (CategoryAccepts(TexTabs[c].cat, SelectableKind.Wall))
                    AddApplyScope(page, $"ap-{k}");
            }
            else
                page.Readout($"none-{k}", "Textures", () => "run Download Textures");
            page.Action($"clear-{k}", "Original look (unpaint aimed)", "eraser", ClearHovered);
            return page;
        }

        /// <summary>One shared paint scope (issue #34): Side = the wall face the ray
        /// hit, Whole = the entire object. Non-walls always paint whole.</summary>
        private void AddApplyScope(SettingsSchema page, string id) =>
            page.Segmented(id, "Apply", ApplyOptions,
                () => _scope, i => _scope = Mathf.Clamp(i, 0, 1));

        /// <summary>The texture category the active tab paints with; null = Color tab.</summary>
        private string ActiveCategory => _tab == 0 ? null : TexTabs[_tab - 1].cat;

        /// <summary>Tab-target filter (audit 06 §Б5): a Floors texture must refuse a
        /// wall and vice versa. Colors apply to anything paintable. Static + public
        /// so the rule is unit-testable.</summary>
        public static bool CategoryAccepts(string category, SelectableKind kind)
        {
            switch (category)
            {
                case "Walls": return kind == SelectableKind.Wall;
                case "Floors": return kind == SelectableKind.Floor || kind == SelectableKind.Stair;
                case "Tiles": return kind == SelectableKind.Wall || kind == SelectableKind.Floor;
                case "Ceiling": return kind == SelectableKind.Floor;
                default: return true;   // Color tab (null) — anything paintable
            }
        }

        /// <summary>Gloss stepper on every tab (design/04 v1.2): auto = catalog gloss
        /// (satin for paint), steps of 10 %.</summary>
        private void AddGlossStepper(SettingsSchema page, string id, int tab)
        {
            page.Stepper(id, "Gloss",
                () => _gloss[tab] >= 0f ? $"{_gloss[tab] * 100f:0}%"
                                        : $"auto ({AutoGloss(tab) * 100f:0}%)",
                () => _gloss[tab] = Mathf.Clamp01((_gloss[tab] >= 0f ? _gloss[tab] : AutoGloss(tab)) - 0.1f),
                () => _gloss[tab] = Mathf.Clamp01((_gloss[tab] >= 0f ? _gloss[tab] : AutoGloss(tab)) + 0.1f));
        }

        /// <summary>The finish the trigger applies, decided by the active tab; false if
        /// the tab has no usable pick (textures not downloaded).</summary>
        private bool TryCurrentFinish(out SurfaceFinish finish, out Texture2D texture)
        {
            finish = SurfaceFinish.None;
            texture = null;
            if (_tab == 0)
            {
                finish = SurfaceFinish.OfColor(CurrentColor, GlossFor(0));
                return true;
            }
            int c = _tab - 1;
            var ids = _catIds[c];
            if (ids == null || ids.Length == 0 || library == null) return false;
            int pick = Mathf.Clamp(_pick[c], 0, ids.Length - 1);
            if (!library.TryGet(ids[pick], out texture, out float tile)) return false;
            // per-axis overrides: 0 = catalog tile; H unset follows W (square)
            float w = _tileW[c] > 0f ? _tileW[c] : tile;
            finish = SurfaceFinish.OfTexture(ids[pick], w, _tileH[c], null, GlossFor(_tab));
            return true;
        }

        /// <summary>Catalog gloss of the picked texture (tab ≥ 1) or the satin paint default.</summary>
        private float AutoGloss(int tab)
        {
            if (tab == 0) return ColorAutoGloss;
            int c = tab - 1;
            var ids = _catIds[c];
            if (ids == null || ids.Length == 0 || library == null) return 0f;
            int pick = Mathf.Clamp(_pick[c], 0, ids.Length - 1);
            return library.TryGet(ids[pick], out _, out _, out float g) ? g : 0f;
        }

        private float GlossFor(int tab) => _gloss[tab] >= 0f ? _gloss[tab] : AutoGloss(tab);

        /// <summary>The catalog tile of the texture picked on category page c.</summary>
        private float LibraryTile(int c)
        {
            var ids = _catIds[c];
            if (ids == null || ids.Length == 0 || library == null) return 1f;
            int pick = Mathf.Clamp(_pick[c], 0, ids.Length - 1);
            return library.TryGet(ids[pick], out _, out float tile) ? tile : 1f;
        }

        private float EffectiveTileW(int c) => _tileW[c] > 0f ? _tileW[c] : LibraryTile(c);

        /// <summary>±25 cm per step from the current (or fallback) size, 25–800 cm.</summary>
        private static float StepTile(float current, float fallback, int dir)
        {
            float v = (current > 0f ? current : fallback) + dir * 0.25f;
            return Mathf.Clamp(v, 0.25f, 8f);
        }

        private static string TileLabel(float over, float fallback) =>
            over > 0f ? $"{over * 100f:0} cm" : $"auto ({fallback * 100f:0} cm)";

        public void OnActivate() { }

        public void OnDeactivate()
        {
            SetHover(null);
            _lastAimed = null;   // a stale target must not survive a tool switch
            SetReticleDanger(false);
            if (reticle != null) reticle.gameObject.SetActive(false);
        }

        public void Tick(bool blocked)
        {
            if (pointer == null || input == null || sceneModel == null) return;
            if (blocked)
            {
                SetHover(null);
                SetReticleDanger(false);
                if (reticle != null) reticle.gameObject.SetActive(false);
                return;
            }

            sceneModel.TryPick(pointer.GetRay(), out var picked, out var point);
            var sel = picked as Selectable;
            if (sel != null && sel.Kind == SelectableKind.Measurement) sel = null;  // not paintable
            SetHover(sel);

            // Tab-target filter (audit 06 §Б5): wrong-category target reads as a
            // Danger cursor and the trigger refuses with a weak tick.
            bool mismatch = sel != null && !CategoryAccepts(ActiveCategory, sel.Kind);

            // the CURSOR is the aiming feedback — hover tint alone read as "куда я
            // вообще целюсь?" on device (feedback 2026-08-10)
            if (reticle != null)
            {
                reticle.gameObject.SetActive(sel != null);
                if (sel != null) reticle.position = point;
            }
            SetReticleDanger(mismatch);

            if (input.ConfirmPressed() && _hover != null)
            {
                if (mismatch || !TryCurrentFinish(out var finish, out var texture))
                {
                    input.Pulse(0.2f, 0.01f);   // refusal — the cursor is already red
                }
                else
                {
                    // Apply: Side (default) paints the wall face the ray hit; Whole
                    // and non-walls keep the object-wide behaviour (issue #34).
                    var wallView = _scope == 0 && _hover.Kind == SelectableKind.Wall
                        ? _hover.GetComponent<Wall>() : null;
                    var cmd = wallView != null
                        ? new PaintCommand(_hover, finish, texture, wallView.SideOf(point))
                        : new PaintCommand(_hover, finish, texture);
                    sceneModel.History.Execute(cmd);
                    input.Pulse(0.5f, 0.02f);
                }
            }

            if (input.ClearPressed() && manager != null)
                manager.ActivateTool("select");
        }

        // ---- Danger cursor for the tab-target filter (audit 06 §Б5) ----

        private static readonly int ReticleBaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ReticleColorId = Shader.PropertyToID("_Color");
        private Renderer _reticleRenderer;
        private MaterialPropertyBlock _reticleMpb;
        private bool _reticleDanger;

        private void SetReticleDanger(bool danger)
        {
            if (danger == _reticleDanger) return;
            _reticleDanger = danger;
            if (_reticleRenderer == null && reticle != null)
                _reticleRenderer = reticle.GetComponentInChildren<Renderer>(true);
            if (_reticleRenderer == null) return;
            if (danger)
            {
                if (_reticleMpb == null) _reticleMpb = new MaterialPropertyBlock();
                _reticleMpb.SetColor(ReticleBaseColorId, UiTokens.Danger);
                _reticleMpb.SetColor(ReticleColorId, UiTokens.Danger);
                _reticleRenderer.SetPropertyBlock(_reticleMpb);
            }
            else
            {
                _reticleRenderer.SetPropertyBlock(null);
            }
        }

        /// <summary>The last object the tool ray actually touched. Clicking the "Original
        /// look" row necessarily happens with the cursor ON the panel (blocked → _hover
        /// already null), so the row acts on this — the reason it never fired before
        /// (audit 06 §Б2).</summary>
        private Selectable _lastAimed;

        /// <summary>Inspector row: strip the finish from the last aimed object —
        /// itself an undoable paint action (finish → None).</summary>
        private void ClearHovered()
        {
            var target = _hover != null ? _hover : _lastAimed;
            if (target == null || !target.IsAlive || target.IsHidden
                || !target.IsPainted || sceneModel == null) return;
            sceneModel.History.Execute(new PaintCommand(target, SurfaceFinish.None, null));
        }

        private void SetHover(Selectable sel)
        {
            if (sel != null) _lastAimed = sel;
            if (ReferenceEquals(sel, _hover)) return;
            if (_hover != null && _hover.IsAlive) _hover.SetHighlight(HighlightState.None);
            _hover = sel;
            if (_hover != null && _hover.IsAlive)
            {
                _hover.SetHighlight(HighlightState.Hover);
                if (input != null) input.Pulse(0.2f, 0.01f);
            }
        }
    }
}
