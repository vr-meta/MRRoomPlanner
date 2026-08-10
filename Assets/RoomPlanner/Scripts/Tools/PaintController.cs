using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Measure;

namespace RoomPlanner.Tools
{
    /// <summary>Apply a finish (solid color OR texture) to the object's body — undo
    /// restores the previous finish, or the material's own look if it never had one.
    /// Before/after snapshots, not deltas (coding rule: no clamp drift).</summary>
    public class PaintCommand : ICommand, ISelectableCommand
    {
        private readonly Selectable _target;
        private readonly SurfaceFinish _after;
        private readonly Texture2D _afterTex;
        private readonly SurfaceFinish _before;
        private readonly Texture2D _beforeTex;

        public PaintCommand(Selectable target, Color color)
            : this(target, SurfaceFinish.OfColor(color), null) { }

        public PaintCommand(Selectable target, SurfaceFinish after, Texture2D afterTex)
        {
            _target = target;
            _after = after;
            _afterTex = afterTex;
            _before = target != null ? target.Finish : SurfaceFinish.None;
            _beforeTex = target != null ? target.FinishTexture : null;
        }

        public ISelectable Target => _target;

        private bool Alive => _target != null && _target.IsAlive;

        public string Name => "Paint";
        public void Do() { if (Alive) _target.SetFinish(_after, _afterTex); }
        public void Undo() { if (Alive) _target.SetFinish(_before, _beforeTex); }
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

        private int _preset;
        private int _tab;          // 0 Color · 1 Walls · 2 Floors
        private int _wallTex;      // index into the Walls texture ids
        private int _floorTex;     // index into the Floors texture ids
        private string[] _wallIds = System.Array.Empty<string>();
        private string[] _floorIds = System.Array.Empty<string>();
        private Selectable _hover;
        private SettingsSchema _settings;

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
                _wallIds = library != null ? library.IdsOf("Walls").ToArray()
                    : System.Array.Empty<string>();
                _floorIds = library != null ? library.IdsOf("Floors").ToArray()
                    : System.Array.Empty<string>();

                var colorPage = new SettingsSchema()
                    .Readout("how", "How to", () => "aim wall/floor · Trigger = paint")
                    .Swatch("color", "Color", _palette, () => _preset,
                        i => _preset = Mathf.Clamp(i, 0, Presets.Length - 1))
                    .Readout("cname", "Preset", () => Presets[_preset].Name)
                    .Action("clear", "Original look (unpaint aimed)", "eraser", ClearHovered);
                // NOTE: the Shading toggles moved to the Rendering page (snap-strip gear).

                var wallsPage = new SettingsSchema()
                    .Readout("howw", "How to", () => "aim a wall · Trigger = apply");
                if (_wallIds.Length > 0)
                    wallsPage.TextureSwatch("wtex", "Wallpaper", _wallIds,
                        () => _wallTex, i => _wallTex = Mathf.Clamp(i, 0, _wallIds.Length - 1));
                else
                    wallsPage.Readout("nonew", "Textures", () => "run Download Textures");
                wallsPage.Action("clearw", "Original look (unpaint aimed)", "eraser", ClearHovered);

                var floorsPage = new SettingsSchema()
                    .Readout("howf", "How to", () => "aim a floor · Trigger = apply");
                if (_floorIds.Length > 0)
                    floorsPage.TextureSwatch("ftex", "Wood", _floorIds,
                        () => _floorTex, i => _floorTex = Mathf.Clamp(i, 0, _floorIds.Length - 1));
                else
                    floorsPage.Readout("nonef", "Textures", () => "run Download Textures");
                floorsPage.Action("clearf", "Original look (unpaint aimed)", "eraser", ClearHovered);

                _settings = SettingsSchema.Tabbed(
                    new[] { "Color", "Walls", "Floors" },
                    () => _tab, i => _tab = Mathf.Clamp(i, 0, 2),
                    colorPage, wallsPage, floorsPage);
            }
            return _settings;
        }

        /// <summary>The finish the trigger applies, decided by the active tab; false if
        /// the tab has no usable pick (textures not downloaded).</summary>
        private bool TryCurrentFinish(out SurfaceFinish finish, out Texture2D texture)
        {
            finish = SurfaceFinish.None;
            texture = null;
            string[] ids = _tab == 1 ? _wallIds : _tab == 2 ? _floorIds : null;
            if (_tab == 0)
            {
                finish = SurfaceFinish.OfColor(CurrentColor);
                return true;
            }
            if (ids == null || ids.Length == 0 || library == null) return false;
            int pick = Mathf.Clamp(_tab == 1 ? _wallTex : _floorTex, 0, ids.Length - 1);
            if (!library.TryGet(ids[pick], out texture, out float tile)) return false;
            finish = SurfaceFinish.OfTexture(ids[pick], tile);
            return true;
        }

        public void OnActivate() { }

        public void OnDeactivate()
        {
            SetHover(null);
            if (reticle != null) reticle.gameObject.SetActive(false);
        }

        public void Tick(bool blocked)
        {
            if (pointer == null || input == null || sceneModel == null) return;
            if (blocked)
            {
                SetHover(null);
                if (reticle != null) reticle.gameObject.SetActive(false);
                return;
            }

            sceneModel.TryPick(pointer.GetRay(), out var picked, out var point);
            var sel = picked as Selectable;
            if (sel != null && sel.Kind == SelectableKind.Measurement) sel = null;  // not paintable
            SetHover(sel);

            // the CURSOR is the aiming feedback — hover tint alone read as "куда я
            // вообще целюсь?" on device (feedback 2026-08-10)
            if (reticle != null)
            {
                reticle.gameObject.SetActive(sel != null);
                if (sel != null) reticle.position = point;
            }

            if (input.ConfirmPressed() && _hover != null
                && TryCurrentFinish(out var finish, out var texture))
            {
                sceneModel.History.Execute(new PaintCommand(_hover, finish, texture));
                input.Pulse(0.5f, 0.02f);
            }

            if (input.ClearPressed() && manager != null)
                manager.ActivateTool("select");
        }

        /// <summary>Inspector row: strip the finish from whatever the ray is on —
        /// itself an undoable paint action (finish → None).</summary>
        private void ClearHovered()
        {
            if (_hover == null || !_hover.IsPainted || sceneModel == null) return;
            sceneModel.History.Execute(new PaintCommand(_hover, SurfaceFinish.None, null));
        }

        private void SetHover(Selectable sel)
        {
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
