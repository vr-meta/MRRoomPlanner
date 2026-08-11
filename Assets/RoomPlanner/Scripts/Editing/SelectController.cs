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

        // Armed-but-not-dragging state (#46): the selecting tap arms; the drag engages
        // on hold or travel. Constants mirror the measure tap-vs-drag window.
        private const float DragHoldSeconds = 0.25f;
        private const float DragEngageDistance = 0.03f;
        private ISelectable _armed;
        private float _armedAt;
        private Vector3 _armedStart;
        private bool _armedStartValid;
        private float _armedPlaneY;

        // Door gesture (issue #50 + headset feedback): tap on a selected door toggles
        // it, hold/travel slides the OPENING along its wall (the arming pattern again —
        // a toggle must never fire when the user meant to drag).
        private Selectable _armedDoor;
        private bool _armedDoorToggle;   // it was already selected → a tap means toggle
        private RoomPlanner.Walls.OpeningParameters _doorDrag;
        private float _doorPlaneY;

        public string Id => "select";
        public string PaletteLabel => "Sel";
        public string IconId => "select-cursor";

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
            EndDoorDrag();
            _armed = null;            // an armed (not yet engaged) drag simply dissolves
            _armedDoor = null;
            Deselect();
            SetHover(null);
            UpdateHandles();          // hide handle spheres — stale colliders on layer 6 would
                                      // read as surfaces to other tools (rule 5.3)
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
                EndDoorDrag();
                _armedDoor = null;
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

            // --- Door drag in progress: slide the opening along its wall ---
            if (_doorDrag != null)
            {
                if (input.ConfirmHeld())
                {
                    if (MeasureMath.RayPlaneY(ray, _doorPlaneY, out var cur))
                    {
                        _doorDrag.PreviewMove(cur);
                        if (reticle != null) { reticle.gameObject.SetActive(true); reticle.position = cur; }
                    }
                    RefreshSelectionText();
                    return;
                }
                EndDoorDrag();
                return;
            }

            // --- Armed door (#50 feedback): tap = toggle (when already selected),
            // hold/travel = drag the opening along its wall ---
            if (_armedDoor != null)
            {
                if (!_armedDoor.IsAlive) _armedDoor = null;
                else if (input.ConfirmHeld())
                {
                    bool engage = Time.time - _armedAt >= DragHoldSeconds;
                    if (MeasureMath.RayPlaneY(ray, _armedPlaneY, out var cur))
                    {
                        if (!_armedStartValid) { _armedStart = cur; _armedStartValid = true; }
                        else if ((cur - _armedStart).sqrMagnitude
                                 >= DragEngageDistance * DragEngageDistance) engage = true;
                    }
                    if (engage)
                    {
                        _doorDrag = _armedDoor.GetComponent<RoomPlanner.Walls.OpeningParameters>();
                        _doorPlaneY = _armedPlaneY;
                        _armedDoor = null;
                        if (_doorDrag != null) _doorDrag.BeginMove();
                        if (input != null) input.Pulse(0.3f, 0.01f);
                    }
                    return;
                }
                else
                {
                    // early release — a tap; toggles only a door that was already selected
                    if (_armedDoorToggle && _armedDoor.LeafView != null)
                    {
                        _armedDoor.LeafView.Toggle();
                        input.Pulse(0.4f, 0.02f);
                    }
                    _armedDoor = null;
                }
            }

            // --- Armed drag (#46): the selecting tap must never nudge the object. The drag
            // engages only after a real hold or real hand travel; releasing earlier leaves
            // a pure selection and nothing moved.
            if (_armed != null)
            {
                if (!_armed.IsAlive) _armed = null;
                else if (input.ConfirmHeld())
                {
                    bool engage = Time.time - _armedAt >= DragHoldSeconds;
                    if (MeasureMath.RayPlaneY(ray, _armedPlaneY, out var cur))
                    {
                        if (!_armedStartValid) { _armedStart = cur; _armedStartValid = true; }
                        else if ((cur - _armedStart).sqrMagnitude
                                 >= DragEngageDistance * DragEngageDistance) engage = true;
                    }
                    if (engage)
                    {
                        var target = _armed;
                        _armed = null;
                        BeginDrag(target, ray);   // latches from the CURRENT cursor — no jump
                    }
                    return;
                }
                else _armed = null;               // early release — tap = select only
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
                // A selected door deletes its OPENING (undo restores it in the wall);
                // hiding just the leaf child would orphan the doorway (issue #50).
                ICommand del = null;
                if (_selected is Selectable dsel && dsel.Kind == SelectableKind.Door)
                    del = dsel.GetComponent<RoomPlanner.Walls.OpeningParameters>()?.BuildDeleteCommand();
                sceneModel.History.Execute(del ?? new DeleteCommand(_selected));
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
                    bool again = _selected == picked;
                    Select(picked);
                    if (picked is Selectable ps && ps.Kind == SelectableKind.Door)
                    {
                        // Doors arm their own gesture (#50 + feedback): a TAP on an
                        // already-selected door toggles it, hold/travel drags the
                        // opening along its wall.
                        _armedDoor = ps;
                        _armedDoorToggle = again;
                        _armedAt = Time.time;
                        _armedPlaneY = hitPoint.y;
                        _armedStartValid = MeasureMath.RayPlaneY(ray, _armedPlaneY, out _armedStart);
                    }
                    else
                    {
                        // Arm instead of dragging (#46): jitter during the click used to
                        // move the object every single time and cost an undo.
                        _armed = picked;
                        _armedAt = Time.time;
                        _armedPlaneY = picked.WorldBounds.center.y;
                        _armedStartValid = MeasureMath.RayPlaneY(ray, _armedPlaneY, out _armedStart);
                    }
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

        private void EndDoorDrag()
        {
            if (_doorDrag == null) return;
            var d = _doorDrag;
            _doorDrag = null;
            d.EndMove();   // reverts when invalid/unmoved, else one OpeningMoveCommand
            RefreshSelectionText();
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
