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
        private bool _cursorValid;
        private Vector3 _dragTotal;
        private float _dragPlaneY;

        public string Id => "select";
        public string PaletteLabel => "Sel";

        /// <summary>No parameter rows — the selection group is a dedicated panel section.</summary>
        public SettingsSchema GetSettings() => null;

        public bool HasSelection => _selected != null && _selected.IsAlive && !_selected.IsHidden;

        /// <summary>True while a drag is in progress (ToolManager suppresses undo/redo then).</summary>
        public bool IsDragging => _dragging != null;

        public string SelectionTitle { get; private set; } = "Nothing selected";
        public string SelectionInfo { get; private set; } = "";

        public void OnActivate() { }

        public void OnDeactivate()
        {
            // Record (not discard): the live movement stays applied, so a lost record would
            // desync undo from the visible scene.
            EndDrag(record: true);
            Deselect();
            SetHover(null);
            if (reticle != null) reticle.gameObject.SetActive(false);
        }

        public void Tick(bool blocked)
        {
            if (pointer == null || input == null || sceneModel == null) return;
            if (blocked)
            {
                // Finish (and record) an in-flight drag instead of freezing it — otherwise the
                // object teleports to the cursor when the ray comes back from the menu.
                EndDrag(record: true);
                SetHover(null);
                if (reticle != null) reticle.gameObject.SetActive(false);
                return;
            }

            Ray ray = pointer.GetRay();

            // --- Dragging the selected object across the ground plane at its own height ---
            if (_dragging != null)
            {
                if (!_dragging.IsAlive) { _dragging = null; }
                else if (input.ConfirmHeld())
                {
                    if (MeasureMath.RayPlaneY(ray, _dragPlaneY, out var cur))
                    {
                        if (!_cursorValid)
                        {
                            // First valid plane hit — just latch the cursor; applying a delta
                            // against a made-up fallback point would teleport the object.
                            _lastCursor = cur;
                            _cursorValid = true;
                        }
                        Vector3 delta = cur - _lastCursor; delta.y = 0f;
                        if (delta.sqrMagnitude > 0f)
                        {
                            // Cheap preview: shift the transform only. The parametric geometry
                            // (and its MeshCollider re-cook) is applied ONCE in EndDrag.
                            _dragging.Transform.position += delta;
                            _dragTotal += delta;
                            _lastCursor = cur;
                        }
                        if (reticle != null) { reticle.gameObject.SetActive(true); reticle.position = cur; }
                    }
                    RefreshSelectionText();
                    return;
                }
                else EndDrag(record: true);
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

        private static bool Alive(ISelectable s) => s != null && s.IsAlive;

        private void Select(ISelectable s)
        {
            if (_selected == s) return;
            if (Alive(_selected)) _selected.SetHighlight(HighlightState.None);
            _selected = s;
            if (Alive(_selected)) _selected.SetHighlight(HighlightState.Selected);
            RefreshSelectionText();
            Notify();
        }

        private void Deselect()
        {
            if (Alive(_selected)) _selected.SetHighlight(HighlightState.None);
            _selected = null;
            SelectionTitle = "Nothing selected";
            SelectionInfo = "";
            Notify();
        }

        private void SetHover(ISelectable s)
        {
            if (_hovered == s) return;
            // don't override the selected object's highlight
            if (Alive(_hovered) && _hovered != _selected) _hovered.SetHighlight(HighlightState.None);
            _hovered = s;
            if (Alive(_hovered) && _hovered != _selected)
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
            _dragPlaneY = s.WorldBounds.center.y;
            _cursorValid = MeasureMath.RayPlaneY(ray, _dragPlaneY, out _lastCursor);
        }

        private void EndDrag(bool record)
        {
            if (_dragging == null) return;
            var moved = _dragging;
            var total = _dragTotal;
            _dragging = null;
            _dragTotal = Vector3.zero;
            _cursorValid = false;
            if (!moved.IsAlive) return;

            // Revert the transform preview, then commit the move parametrically in one rebuild.
            moved.Transform.position -= total;
            bool far = total.sqrMagnitude > 0.0004f;   // ≤2 cm = accidental jiggle → revert
            if (record && far)
            {
                moved.MoveBy(total);
                sceneModel.History.Record(new MoveCommand(moved, total));
            }
        }

        // ---- inspector text ----

        private void RefreshSelectionText()
        {
            if (_selected == null || !_selected.IsAlive)
            {
                SelectionTitle = "Nothing selected";
                SelectionInfo = "";
                return;
            }
            SelectionTitle = $"{_selected.Kind} #{_selected.Id}";
            SelectionInfo = _selected.Describe();
        }

        private void Notify()
        {
            if (manager != null) manager.RefreshInspector();
        }
    }
}
