using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Measure;
using RoomPlanner.Tools;

namespace RoomPlanner.Editing
{
    /// <summary>
    /// The Select/Edit tool (roadmap Phase A). Owns hover, selection, whole-object move and
    /// delete for any <see cref="ISelectable"/>. Move is applied live during the drag and
    /// recorded as a single MoveCommand on release; delete goes through the command stack too,
    /// so both are undoable (X/Y, handled by ToolManager). The default active tool.
    /// </summary>
    public class SelectController : MonoBehaviour, ITool
    {
        [SerializeField] private PointerProvider pointer;
        [SerializeField] private MeasureInput input;
        [SerializeField] private SceneModel sceneModel;
        [SerializeField] private Transform reticle;
        [SerializeField] private ToolManager manager;

        private ISelectable _selected;
        private ISelectable _hovered;
        private ISelectable _dragging;
        private Vector3 _lastCursor;
        private Vector3 _dragTotal;
        private float _dragPlaneY;
        private bool _draggedFar;

        public bool HasSelection => _selected != null && !_selected.IsHidden;
        public string SelectionTitle { get; private set; } = "Nothing selected";
        public string SelectionInfo { get; private set; } = "";

        public void OnActivate() { }

        public void OnDeactivate()
        {
            EndDrag(record: false);
            Deselect();
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

            // --- Dragging the selected object across the ground plane at its own height ---
            if (_dragging != null)
            {
                if (input.ConfirmHeld())
                {
                    if (MeasureMath.RayPlaneY(ray, _dragPlaneY, out var cur))
                    {
                        Vector3 delta = cur - _lastCursor; delta.y = 0f;
                        if (delta.sqrMagnitude > 0f)
                        {
                            _dragging.MoveBy(delta);
                            _dragTotal += delta;
                            _lastCursor = cur;
                            if (_dragTotal.sqrMagnitude > 0.0004f) _draggedFar = true; // >2 cm total
                        }
                        if (reticle != null) { reticle.gameObject.SetActive(true); reticle.position = cur; }
                    }
                    RefreshSelectionText();
                    return;
                }
                EndDrag(record: true);
            }

            // --- Hover ---
            bool over = sceneModel.TryPick(ray, out var picked, out var hitPoint);
            SetHover(over ? picked : null);
            if (reticle != null)
            {
                reticle.gameObject.SetActive(over);
                if (over) reticle.position = hitPoint;
            }

            // --- Delete (B) ---
            if (input.ClearPressed() && HasSelection)
            {
                sceneModel.History.Execute(new DeleteCommand(_selected));
                Deselect();
                return;
            }

            // --- Select / begin drag (trigger) ---
            if (input.ConfirmPressed())
            {
                if (over)
                {
                    Select(picked);
                    BeginDrag(picked, ray);
                }
                else
                {
                    Deselect();
                }
            }
        }

        // ---- selection ----

        private void Select(ISelectable s)
        {
            if (_selected == s) return;
            if (_selected != null) _selected.SetHighlight(HighlightState.None);
            _selected = s;
            if (_selected != null) _selected.SetHighlight(HighlightState.Selected);
            RefreshSelectionText();
            Notify();
        }

        private void Deselect()
        {
            if (_selected != null) _selected.SetHighlight(HighlightState.None);
            _selected = null;
            SelectionTitle = "Nothing selected";
            SelectionInfo = "";
            Notify();
        }

        private void SetHover(ISelectable s)
        {
            if (_hovered == s) return;
            // don't override the selected object's highlight
            if (_hovered != null && _hovered != _selected) _hovered.SetHighlight(HighlightState.None);
            _hovered = s;
            if (_hovered != null && _hovered != _selected)
            {
                _hovered.SetHighlight(HighlightState.Hover);
                if (input != null) input.Pulse(0.3f, 0.04f);
            }
        }

        // ---- drag ----

        private void BeginDrag(ISelectable s, Ray ray)
        {
            _dragging = s;
            _dragTotal = Vector3.zero;
            _draggedFar = false;
            _dragPlaneY = s.WorldBounds.center.y;
            if (!MeasureMath.RayPlaneY(ray, _dragPlaneY, out _lastCursor))
                _lastCursor = ray.origin + ray.direction * 2f;
        }

        private void EndDrag(bool record)
        {
            if (_dragging == null) return;
            var moved = _dragging;
            var total = _dragTotal;
            _dragging = null;
            if (record && _draggedFar && total.sqrMagnitude > 0f)
                sceneModel.History.Record(new MoveCommand(moved, total));
            _draggedFar = false;
        }

        // ---- inspector text ----

        private void RefreshSelectionText()
        {
            if (_selected == null) { SelectionTitle = "Nothing selected"; SelectionInfo = ""; return; }
            SelectionTitle = $"{_selected.Kind} #{_selected.Id}";
            SelectionInfo = _selected.Describe();
        }

        private void Notify()
        {
            if (manager != null) manager.RefreshInspector();
        }
    }
}
