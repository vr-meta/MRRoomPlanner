using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Measure;
using RoomPlanner.Tools;
using RoomPlanner.Editing;
using RoomPlanner.Walls;

namespace RoomPlanner.Paint
{
    /// <summary>
    /// Paint tool (design/04, v1): point at one of YOUR walls and pull the trigger — the
    /// wall takes the current preset color (whole wall; per-side faces and regions are the
    /// documented next steps). The color is a named cycle row, not RGB sliders — cycling a
    /// catalog beats precision input in VR. Clear mode washes a wall back to bare concrete.
    /// Every stroke is one undoable command; repainting with the same color records nothing.
    /// </summary>
    public class PaintController : MonoBehaviour, ITool
    {
        [SerializeField] private PointerProvider pointer;
        [SerializeField] private MeasureInput input;
        [SerializeField] private SceneModel sceneModel;
        [SerializeField] private Transform reticle;
        [SerializeField] private ToolManager manager;

        private int _colorIndex;
        private bool _clearMode;
        private ISelectable _hovered;
        private SettingsSchema _settings;

        public string Id => "paint";
        public string PaletteLabel => "Paint";

        public SettingsSchema GetSettings()
        {
            _settings ??= new SettingsSchema()
                .Cycle("pcolor", "Color", () => PaintPalette.NameAt(_colorIndex),
                    () => _colorIndex = (_colorIndex + 1) % PaintPalette.Count)
                .Cycle("pact", "Action", () => _clearMode ? "Clear" : "Paint",
                    () => _clearMode = !_clearMode);
            return _settings;
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

            Ray ray = pointer.GetRay();
            bool over = sceneModel.TryPick(ray, out var picked, out var hitPoint)
                        && picked.Kind == SelectableKind.Wall;
            SetHover(over ? picked : null);
            if (reticle != null)
            {
                reticle.gameObject.SetActive(over);
                if (over) reticle.position = hitPoint;
            }

            if (over && input.ConfirmPressed()) Stroke(picked);
            else if (!over && input.ConfirmPressed()) input.Pulse(0.2f, 0.01f);   // only walls take paint

            // Esc semantics: B returns to Select (nothing here to cancel or delete)
            if (input.ClearPressed() && manager != null) manager.ActivateTool("select");
        }

        private void Stroke(ISelectable picked)
        {
            var wall = picked.Transform != null ? picked.Transform.GetComponent<Wall>() : null;
            if (wall == null) return;

            bool afterHas = !_clearMode;
            Color after = PaintPalette.ColorAt(_colorIndex);
            if (wall.HasPaint == afterHas && (!afterHas || wall.PaintColor == after))
            {
                input.Pulse(0.2f, 0.01f);   // no-op stroke — keep history clean
                return;
            }

            sceneModel.History.Execute(new PaintCommand(picked, wall, afterHas, after));
            // the stroke overwrote the hover-tinted block with plain paint while the state
            // stayed Hover — re-apply so the wall under the ray keeps its hover feedback
            if (picked is Selectable sel) sel.ReapplyHighlight();
            input.Pulse(0.6f, 0.02f);
        }

        private void SetHover(ISelectable s)
        {
            if (_hovered == s) return;
            if (_hovered != null && _hovered.IsAlive) _hovered.SetHighlight(HighlightState.None);
            _hovered = s;
            if (_hovered != null && _hovered.IsAlive)
            {
                _hovered.SetHighlight(HighlightState.Hover);
                if (input != null) input.Pulse(0.2f, 0.01f);
            }
        }
    }

    /// <summary>One paint stroke on one wall — undo restores the exact previous state,
    /// including "never painted" (back to bare concrete).</summary>
    public sealed class PaintCommand : ICommand, ISelectableCommand
    {
        private readonly ISelectable _target;
        private readonly Wall _wall;
        private readonly bool _beforeHas, _afterHas;
        private readonly Color _before, _after;

        public PaintCommand(ISelectable target, Wall wall, bool afterHas, Color after)
        {
            _target = target;
            _wall = wall;
            _beforeHas = wall != null && wall.HasPaint;
            _before = wall != null ? wall.PaintColor : Color.white;
            _afterHas = afterHas;
            _after = after;
        }

        public string Name => _afterHas ? "Paint wall" : "Clear paint";
        public ISelectable Target => _target;

        public void Do() => Set(_afterHas, _after);
        public void Undo() => Set(_beforeHas, _before);

        private void Set(bool has, Color color)
        {
            if (_wall == null) return;   // destroyed since the command was recorded
            if (has) _wall.SetPaint(color);
            else _wall.ClearPaint();
        }
    }
}
