using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Measure;

namespace RoomPlanner.Tools
{
    /// <summary>Paint the object's body a solid color — undo restores the previous paint,
    /// or the material's own look if it was never painted.</summary>
    public class PaintCommand : ICommand, ISelectableCommand
    {
        private readonly Selectable _target;
        private readonly Color _color;
        private readonly Color _previous;
        private readonly bool _wasPainted;

        public PaintCommand(Selectable target, Color color)
        {
            _target = target;
            _color = color;
            _wasPainted = target != null && target.IsPainted;
            _previous = _wasPainted ? target.Paint : Color.clear;
        }

        public ISelectable Target => _target;

        private bool Alive => _target != null && _target.IsAlive;

        public string Name => "Paint";
        public void Do() { if (Alive) _target.SetPaint(_color); }
        public void Undo()
        {
            if (!Alive) return;
            if (_wasPainted) _target.SetPaint(_previous);
            else _target.ClearPaint();
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
        [SerializeField] private RoomPlanner.Walls.WallGraphRenderer walls;
        [SerializeField] private UnityEngine.Rendering.Universal.ScriptableRendererFeature ssaoFeature;

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
        private Selectable _hover;
        private SettingsSchema _settings;

        public string Id => "paint";
        public string PaletteLabel => "Pnt";
        public string IconId => "paint-roller";

        public Color CurrentColor => Presets[_preset].Color;

        private static Color[] _palette;

        public SettingsSchema GetSettings()
        {
            // v2 (design/20 §2.7): the color is a real swatch grid — hue here IS the data;
            // booleans became toggles; "Original look" is an eraser action.
            if (_palette == null)
            {
                _palette = new Color[Presets.Length];
                for (int i = 0; i < Presets.Length; i++) _palette[i] = Presets[i].Color;
            }
            _settings ??= new SettingsSchema()
                .Swatch("color", "Color", _palette, () => _preset,
                    i => _preset = Mathf.Clamp(i, 0, Presets.Length - 1))
                .Readout("cname", "Preset", () => Presets[_preset].Name)
                .Action("clear", "Original look", "eraser", ClearHovered)
                .Header("shade", "Shading")
                .Toggle("vao", "Vertex AO", () => Core.MeshShading.VertexAO, _ => ToggleVertexAO())
                .Toggle("ssao", "SSAO",
                    () => ssaoFeature != null && ssaoFeature.isActive, _ => ToggleSsao());
            return _settings;
        }

        /// <summary>Baked-AO switch: flips the global flag and rebuilds every mesh once.</summary>
        private void ToggleVertexAO()
        {
            Core.MeshShading.VertexAO = !Core.MeshShading.VertexAO;
            if (walls != null && walls.Graph != null)
                foreach (var s in walls.Graph.Segments) walls.RebuildSegment(s);
            foreach (var f in TeleportCommand.CollectFloors()) f.Rebuild();
            foreach (var st in TeleportCommand.CollectStairs()) st.Rebuild();
        }

        private void ToggleSsao()
        {
            if (ssaoFeature != null) ssaoFeature.SetActive(!ssaoFeature.isActive);
        }

        public void OnActivate() { }

        public void OnDeactivate() => SetHover(null);

        public void Tick(bool blocked)
        {
            if (pointer == null || input == null || sceneModel == null) return;
            if (blocked) { SetHover(null); return; }

            sceneModel.TryPick(pointer.GetRay(), out var picked, out _);
            var sel = picked as Selectable;
            if (sel != null && sel.Kind == SelectableKind.Measurement) sel = null;  // not paintable
            SetHover(sel);

            if (input.ConfirmPressed() && _hover != null)
            {
                sceneModel.History.Execute(new PaintCommand(_hover, CurrentColor));
                input.Pulse(0.5f, 0.02f);
            }

            if (input.ClearPressed() && manager != null)
                manager.ActivateTool("select");
        }

        /// <summary>Inspector row: strip the paint from whatever the ray is on.</summary>
        private void ClearHovered()
        {
            if (_hover == null || !_hover.IsPainted || sceneModel == null) return;
            // going back to the material is itself an undoable paint action
            var cmd = new PaintCommand(_hover, _hover.Paint);   // captures previous state
            _hover.ClearPaint();
            sceneModel.History.Record(new UnpaintRecord(_hover, cmd));
        }

        /// <summary>Undo unit for "original look": re-applies / re-clears the paint.</summary>
        private class UnpaintRecord : ICommand
        {
            private readonly Selectable _target;
            private readonly PaintCommand _restore;

            public UnpaintRecord(Selectable target, PaintCommand restore)
            {
                _target = target;
                _restore = restore;
            }

            public string Name => "Unpaint";
            public void Do() { if (_target != null && _target.IsAlive) _target.ClearPaint(); }
            public void Undo() => _restore.Do();
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
