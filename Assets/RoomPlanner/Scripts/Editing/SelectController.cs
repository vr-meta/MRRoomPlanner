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
        /// <summary>
        /// The inspector shows the SELECTED object's own parameters — the Select tool has none
        /// of its own. Returning a different schema instance per selection is what makes the
        /// panel rebind to the new object (design/13-phase-b-wallgraph.md, B4).
        /// </summary>
        public SettingsSchema GetSettings() => HasSelection ? _selected.GetSettings() : null;

        public bool HasSelection => _selected != null && _selected.IsAlive && !_selected.IsHidden;

        /// <summary>True while a drag is in progress (ToolManager suppresses undo/redo then).</summary>
        public bool IsDragging => _dragging != null || _draggingHandle != null;

        public string SelectionTitle { get; private set; } = "Nothing selected";
        public string SelectionInfo { get; private set; } = "";

        public void OnActivate() { }

        public void OnDeactivate()
        {
            // Record (not discard): the live movement stays applied, so a lost record would
            // desync undo from the visible scene.
            EndHandleDrag();          // settle physics + record, never leave a half-applied drag
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
                EndHandleDrag();
                EndDrag(record: true);
                SetHover(null);
                UpdateHandles();
                if (reticle != null) reticle.gameObject.SetActive(false);
                return;
            }

            Ray ray = pointer.GetRay();

            // --- Dragging a vertex handle (takes priority: it sits on top of its object) ---
            if (_draggingHandle != null)
            {
                if (input.ConfirmHeld())
                {
                    if (MeasureMath.RayPlaneY(ray, _handlePlaneY, out var hp))
                    {
                        _handleTo = hp;
                        _draggingHandle.Provider?.PreviewHandle(_draggingHandle.Index, hp);
                        if (reticle != null) { reticle.gameObject.SetActive(true); reticle.position = hp; }
                    }
                    UpdateHandles();
                    return;
                }
                EndHandleDrag();
            }

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
                // A handle sits ON its object, so it must win the click — otherwise you could
                // never grab a vertex, you would always move the whole wall instead.
                var handle = PickHandle(ray);
                if (handle != null) BeginHandleDrag(handle, ray);
                else if (over)
                {
                    Select(picked);
                    BeginDrag(picked, ray);
                }
                else
                {
                    Deselect();
                }
            }

            UpdateHandles();
        }

        // ---- vertex handles (B5) ----

        /// <summary>Layer 6 "Selectable" — same as walls/floors, so handles never hit the menu.</summary>
        private const int SelectableLayer = 6;

        [SerializeField] private float handlePickRadius = 0.06f;

        private readonly System.Collections.Generic.List<EditHandle> _handles = new();
        private EditHandle _draggingHandle;
        private Vector3 _handleFrom, _handleTo;
        private float _handlePlaneY;
        private Transform _cam;
        private Material _handleMat;

        private IHandleProvider SelectedProvider =>
            HasSelection && _selected.Transform != null
                ? _selected.Transform.GetComponent<IHandleProvider>()
                : null;

        /// <summary>Show one handle per point of the selection; hide them otherwise.</summary>
        private void UpdateHandles()
        {
            var provider = SelectedProvider;
            int want = provider?.HandleCount ?? 0;

            while (_handles.Count < want) _handles.Add(CreateHandle());
            for (int i = 0; i < _handles.Count; i++)
            {
                var h = _handles[i];
                if (h == null) continue;
                bool used = i < want;
                if (h.gameObject.activeSelf != used) h.gameObject.SetActive(used);
                if (!used) continue;
                h.Bind(provider, i);
                h.Follow(Cam());
            }
        }

        private Transform Cam()
        {
            if (_cam == null && Camera.main != null) _cam = Camera.main.transform;
            return _cam;
        }

        private EditHandle CreateHandle()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "EditHandle";
            go.layer = SelectableLayer;
            go.transform.SetParent(transform, false);

            if (_handleMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                _handleMat = new Material(shader) { name = "EditHandleMat" };
                if (_handleMat.HasProperty("_BaseColor")) _handleMat.SetColor("_BaseColor", new Color(1f, 0.75f, 0.2f));
                _handleMat.color = new Color(1f, 0.75f, 0.2f);
            }
            go.GetComponent<Renderer>().sharedMaterial = _handleMat;
            return go.AddComponent<EditHandle>();
        }

        /// <summary>Nearest visible handle under the ray, or null.</summary>
        private EditHandle PickHandle(Ray ray)
        {
            EditHandle best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < _handles.Count; i++)
            {
                var h = _handles[i];
                if (h == null || !h.gameObject.activeSelf) continue;
                Vector3 p = h.transform.position;
                float along = Vector3.Dot(p - ray.origin, ray.direction);
                if (along <= 0f) continue;
                float miss = Vector3.Distance(p, ray.origin + ray.direction * along);
                float radius = Mathf.Max(handlePickRadius, h.transform.localScale.x);
                if (miss <= radius && along < bestDist) { bestDist = along; best = h; }
            }
            return best;
        }

        private void BeginHandleDrag(EditHandle h, Ray ray)
        {
            _draggingHandle = h;
            _handleFrom = h.Provider != null ? h.Provider.GetHandlePosition(h.Index) : h.transform.position;
            _handleTo = _handleFrom;
            _handlePlaneY = _handleFrom.y;
            if (input != null) input.Pulse(0.4f, 0.05f);
        }

        private void EndHandleDrag()
        {
            var h = _draggingHandle;
            _draggingHandle = null;
            if (h == null || h.Provider == null) return;

            // one command for the whole gesture; the provider settles physics at the same time
            var cmd = h.Provider.CommitHandle(h.Index, _handleFrom, _handleTo);
            if (cmd != null && sceneModel != null) sceneModel.History.Record(cmd);
            RefreshSelectionText();
        }

        private void OnDestroy()
        {
            // a runtime-created material is ours to free (coding rule 1.5)
            if (_handleMat != null) Destroy(_handleMat);
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
                if (input != null) input.Pulse(0.2f, 0.01f);   // hover tick
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
