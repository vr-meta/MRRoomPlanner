using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Tools;
using RoomPlanner.Editing;

namespace RoomPlanner.Measure
{
    /// <summary>
    /// Рулетка.
    /// • Триггер по поверхности: точка A → точка B (фиксация).
    /// • Наведение на ЛЮБУЮ точку (стартовую/конечную любой линии): рядом появляется «+»
    ///   (продолжить цепочку от этой точки), а сама точка — grab для редактирования.
    /// • Наведение на точку + удержание триггера: тащить её. Наведение на точку + B: удалить измерение.
    /// • Зажатый грип во время ведения/таскания: привязка к оси (вертикаль/горизонталь).
    /// • Магнит: точка примагничивается к концам существующих измерений.
    /// • Совпавшие точки нескольких линий показываются одним маркером.
    /// • В режиме перетаскивания «+» скрыт (чтобы не мешал магнититься).
    /// </summary>
    public class MeasureController : MonoBehaviour, ITool
    {
        [SerializeField] private PointerProvider pointer;
        [SerializeField] private MeasureInput input;
        [SerializeField] private SceneRaycaster raycaster;
        [SerializeField] private Transform reticle;
        [SerializeField] private Measurement measurementPrefab;
        [SerializeField] private MeasureContinueButton continueButtonPrefab;
        [SerializeField] private ToolManager manager;   // snap toggles (optional; on by default if null)
        [SerializeField] private SceneModel sceneModel; // central registry for Select tool
        [Tooltip("Радиус магнита к существующим концам, м.")]
        [SerializeField] private float snapDistance = 0.1f;
        [SerializeField] private int maxMeasurements = 40;

        private const float PlusGrace = 0.25f; // сколько «+» держится после ухода луча (чтобы успеть навестись)
        private const float DepthSpeed = 1.5f; // м/с изменения глубины курсора в воздухе
        private const float MinDepth = 0.2f;
        private const float MaxDepth = 10f;

        private readonly List<Measurement> _measurements = new();
        private readonly List<Vector3> _keptPts = new();
        private Vector3? _pendingStart;
        private Measurement _preview;
        private MeasurePointHandle _dragging;
        private MeasureContinueButton _plus;   // единственная кнопка «+», всплывает у наведённой точки
        private Vector3 _plusAnchor;
        private float _plusVisibleUntil;
        private Vector3 _currentHit;
        private Vector3 _currentNormal;
        private bool _hasHit;
        private Vector3 _cursorPoint;           // точка под прицелом: на поверхности или в воздухе
        private float _cursorDistance = 2f;     // глубина курсора в воздухе, м
        private Component _hoverTarget;
        private MeasureContinueButton _hoverBtn;

        private void Start()
        {
            Debug.Log($"[Measure] v10 started. prefab={(measurementPrefab != null)} contBtn={(continueButtonPrefab != null)} scene={(sceneModel != null)}");
            if (continueButtonPrefab != null)
            {
                _plus = Instantiate(continueButtonPrefab, transform);
                _plus.gameObject.SetActive(false);
            }
        }

        public string Id => "measure";
        public string PaletteLabel => "Meas";
        public string IconId => "tape-measure";

        private int _mode;   // 0 Hands (Layout-style tape at the controller) · 1 Ray (aim far)
        private SettingsSchema _settings;

        public SettingsSchema GetSettings()
        {
            _settings ??= new SettingsSchema()
                .Segmented("mmode", "Mode", new[] { "Hands", "Ray" },
                    () => _mode, i => _mode = Mathf.Clamp(i, 0, 1))
                .Readout("mhint", "How to", () => _mode == 0
                    ? "tip at your hand · Trigger = pin"
                    : "aim · Trigger = place · stick = depth");
            return _settings;
        }

        public void OnActivate() { }

        public void OnDeactivate()
        {
            if (_preview != null) { Destroy(_preview.gameObject); _preview = null; _pendingStart = null; }
            // Re-enable the dragged measurement's endpoint colliders — otherwise a tool switch
            // mid-drag leaves the measurement permanently unpickable.
            if (_dragging != null && _dragging.Owner != null) _dragging.Owner.SetInteractable(true);
            _dragging = null;
            HidePlus();
            if (reticle != null) reticle.gameObject.SetActive(false);
        }

        public void Tick(bool blocked)
        {
            if (pointer == null || raycaster == null || input == null) return;

            // A teleport (or its undo) shifts every Measurement including the live preview —
            // the already-pinned first point must stay glued to the model, not to the user
            // (headset feedback 2026-08-11). Re-adopt the start the command moved.
            if (_preview != null && _pendingStart.HasValue)
                _pendingStart = _preview.PointA;

            if (blocked)
            {
                HidePlus();
                UpdateHover(null, null);   // clear hover scale/highlight so "+" doesn't reappear enlarged
                if (reticle != null) reticle.gameObject.SetActive(false);
                return;
            }
            DedupeMarkers();

            Ray ray = pointer.GetRay();

            // hands mode (device feedback 2026-08-10, Layout-style): the tape lives at
            // your fingertips — points are pinned by the CONTROLLER position, not the ray
            if (_mode == 0)
            {
                TickHands(ray);
                return;
            }

            _hasHit = raycaster.TryRaycastSurface(ray, out _currentHit, out _currentNormal, out var hitObj);
            var plusHit = (_hasHit && hitObj != null) ? hitObj.GetComponentInParent<MeasureContinueButton>() : null;
            var handleHit = (_hasHit && hitObj != null) ? hitObj.GetComponentInParent<MeasurePointHandle>() : null;

            // Курсор: на поверхности — точка попадания; в воздухе — вдоль луча на регулируемой глубине
            // (стик вверх/вниз). При уходе с поверхности глубина продолжается от неё — без скачка.
            if (_hasHit)
                _cursorDistance = Vector3.Distance(ray.origin, _currentHit);
            else
                _cursorDistance = Mathf.Clamp(_cursorDistance + input.DepthAdjust() * DepthSpeed * Time.deltaTime, MinDepth, MaxDepth);
            _cursorPoint = _hasHit ? _currentHit : ray.origin + ray.direction * _cursorDistance;

            // --- Перетаскивание точки: «+» скрыт ---
            if (_dragging != null)
            {
                HidePlus();
                UpdateHover(null, null);
                DoDrag();
                return;
            }

            // «Точка под прицелом» = попадание в коллайдер ИЛИ магнит-близость (как у ретикла),
            // чтобы клик рядом с точкой не начинал новую линию, а редактировал её.
            MeasurePointHandle effHandle = handleHit;
            if (effHandle == null && !_pendingStart.HasValue)
                effHandle = FindEndpointHandle(_cursorPoint);

            UpdateHover(plusHit, effHandle);

            // --- Кнопка «+» у наведённой точки (не во время постановки) ---
            if (_pendingStart.HasValue)
            {
                HidePlus();
            }
            else
            {
                if (effHandle != null) SetPlusFor(effHandle);
                else if (plusHit != null && plusHit == _plus) _plusVisibleUntil = Time.time + PlusGrace;
                MaybeHidePlus();
            }

            // --- Destination: the ONE snap policy shared by every mode (audit 01 §Б3) ---
            Vector3 target = ResolveTarget(_cursorPoint, allowSurface: false,
                exclude: null, axisAnchor: _pendingStart);

            if (reticle != null)
            {
                reticle.gameObject.SetActive(true);
                reticle.position = target;
            }

            // --- Удаление ---
            if (input.ClearPressed())
            {
                if (effHandle != null && effHandle.Owner != null) DeleteMeasurement(effHandle.Owner);
                else ClearLast();
                return;
            }

            // --- Постановка / старт цепочки / старт таскания (триггер — всегда, в т.ч. в воздухе) ---
            if (input.ConfirmPressed())
            {
                if (_pendingStart.HasValue)
                    PlacePoint(target);                                  // фиксация второй точки
                else if (plusHit != null && plusHit == _plus)
                    StartChainFrom(_plusAnchor);                         // продолжить от наведённой точки
                else if (effHandle != null)
                {
                    _dragging = effHandle;                               // тащить точку (по коллайдеру или магниту)
                    if (effHandle.Owner != null)
                    {
                        _dragOrigin = effHandle.IsEndA
                            ? effHandle.Owner.PointA : effHandle.Owner.PointB;
                        effHandle.Owner.SetInteractable(false);
                    }
                }
                else
                    PlacePoint(target);                                  // первая точка нового измерения
            }

            if (_pendingStart.HasValue && _preview != null)
                _preview.Set(_pendingStart.Value, target);
        }

        // ---- hands mode: tape at the fingertips (design/01 v2) ----

        private const float TipOffset = 0.07f;        // "tape tip" just ahead of the controller
        private const float SurfaceMagnet = 0.06f;    // pull the tip onto a nearby surface/corner
        private readonly Collider[] _nearby = new Collider[8];

        private void TickHands(Ray ray)
        {
            HidePlus();
            UpdateHover(null, null);

            Vector3 tip = ray.origin + ray.direction * TipOffset;

            // vertex drag in progress: hold = keep dragging, release decides tap vs move
            if (_dragging != null)
            {
                DoDragHands(tip);
                return;
            }

            // same snap policy as ray mode — the tip only adds the surface magnet
            Vector3 target = ResolveTarget(tip, allowSurface: true,
                exclude: null, axisAnchor: _pendingStart);

            // the reticle IS the tape tip — always visible in hands mode
            if (reticle != null)
            {
                reticle.gameObject.SetActive(true);
                reticle.position = target;
            }

            if (input.ClearPressed())
            {
                ClearLast();   // cancel the stretched tape, or Esc back to Select
                return;
            }

            if (input.ConfirmPressed())
            {
                // Tip on an existing vertex (not while stretching): HOLD drags it, a quick
                // TAP starts a new tape from that vertex (headset feedback 2026-08-11).
                var handle = _pendingStart.HasValue ? null : FindEndpointHandle(target);
                if (handle != null && handle.Owner != null)
                {
                    _dragging = handle;
                    _dragPressedAt = Time.time;
                    _dragOrigin = handle.IsEndA ? handle.Owner.PointA : handle.Owner.PointB;
                    handle.Owner.SetInteractable(false);
                }
                else
                    PlacePoint(target);   // pin A, walk, pin B — same chain machinery
            }

            if (_pendingStart.HasValue && _preview != null)
                _preview.Set(_pendingStart.Value, target);   // live tape with the size badge
        }

        private const float TapSeconds = 0.25f;   // shorter press on a vertex = tap, not drag
        private float _dragPressedAt;
        private Vector3 _dragOrigin;

        /// <summary>Hands-mode vertex drag: the tape tip carries the point (same magnets as
        /// placing). Releasing within the tap window undoes the micro-move and starts a new
        /// chain from the vertex instead — "press and release = place a point".</summary>
        private void DoDragHands(Vector3 tip)
        {
            if (input.ConfirmHeld())
            {
                Vector3 t = ResolveTarget(tip, allowSurface: true,
                    exclude: _dragging.Owner, axisAnchor: OtherEnd(_dragging));
                ApplyDrag(_dragging, t);
                if (reticle != null) { reticle.gameObject.SetActive(true); reticle.position = t; }
            }
            else
            {
                var h = _dragging;
                _dragging = null;
                if (h.Owner != null) h.Owner.SetInteractable(true);
                if (Time.time - _dragPressedAt < TapSeconds)
                {
                    ApplyDrag(h, _dragOrigin);        // barely moved — put it back
                    StartChainFrom(_dragOrigin);      // tap on a vertex = continue from it
                }
                else RecordDragCommand(h);            // one undo entry per gesture (01 §Б2)
            }
        }

        /// <summary>Closest point on any nearby surface (scan or own geometry) within the
        /// magnet radius — the tip clicks onto walls and corners instead of hovering in
        /// the air 3 cm away. NonAlloc per frame (coding rule 4).</summary>
        private bool TrySnapToNearbySurface(Vector3 tip, out Vector3 snapped)
        {
            snapped = tip;
            int mask = ~(1 << 2);   // everything except the menu layer
            int n = Physics.OverlapSphereNonAlloc(tip, SurfaceMagnet, _nearby, mask,
                QueryTriggerInteraction.Ignore);
            float best = SurfaceMagnet;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var col = _nearby[i];
                if (col == null) continue;
                var sel = col.GetComponentInParent<RoomPlanner.Editing.Selectable>();
                if (sel != null && sel.Kind == RoomPlanner.Editing.SelectableKind.Measurement)
                    continue;   // measurement markers have their own endpoint magnet
                Vector3 p = col.ClosestPoint(tip);
                float d = Vector3.Distance(tip, p);
                if (d < best)
                {
                    best = d;
                    snapped = p;
                    found = true;
                }
            }
            return found;
        }

        private void DoDrag()
        {
            if (input.ConfirmHeld())
            {
                Vector3 t = ResolveTarget(_cursorPoint, allowSurface: false,
                    exclude: _dragging.Owner, axisAnchor: OtherEnd(_dragging));
                ApplyDrag(_dragging, t);
                if (reticle != null) { reticle.gameObject.SetActive(true); reticle.position = t; }
            }
            else
            {
                var h = _dragging;
                _dragging = null;
                if (h.Owner != null) h.Owner.SetInteractable(true);
                RecordDragCommand(h);                 // one undo entry per gesture (01 §Б2)
            }
        }

        /// <summary>The fixed end of the dragged measurement — the axis modifier's anchor.</summary>
        private static Vector3? OtherEnd(MeasurePointHandle h)
        {
            if (h == null || h.Owner == null) return null;
            return h.IsEndA ? h.Owner.PointB : h.Owner.PointA;
        }

        /// <summary>
        /// Probe the endpoint/surface magnets, then resolve the shared snap-priority
        /// policy (MeasureMath.ApplySnapPolicy). Every gesture in both modes goes
        /// through here — the three hand-rolled orderings disagreed (audit 01 §Б3).
        /// </summary>
        private Vector3 ResolveTarget(Vector3 raw, bool allowSurface, Measurement exclude, Vector3? axisAnchor)
        {
            Vector3? endpoint = null;
            if ((manager == null || manager.SnapCorner) && TrySnapToEndpoint(raw, out var sp, exclude))
                endpoint = sp;
            Vector3? surface = null;
            if (!endpoint.HasValue && allowSurface && TrySnapToNearbySurface(raw, out var sfc))
                surface = sfc;
            return MeasureMath.ApplySnapPolicy(raw, endpoint, surface,
                axisAnchor, input.SnapHeld(),
                manager != null && manager.SnapGrid, manager != null ? manager.GridSize : 0f);
        }

        /// <summary>Record the finished drag as one undo entry; a sub-mm wiggle is not an edit.</summary>
        private void RecordDragCommand(MeasurePointHandle h)
        {
            if (sceneModel == null || h == null || h.Owner == null) return;
            Vector3 after = h.IsEndA ? h.Owner.PointA : h.Owner.PointB;
            if ((after - _dragOrigin).sqrMagnitude < 1e-6f) return;
            var sel = h.Owner.GetComponent<Selectable>();
            if (sel == null) return;
            sceneModel.History.Record(new MeasurePointMoveCommand(sel, h.Owner, h.IsEndA, _dragOrigin, after));
        }

        private void ApplyDrag(MeasurePointHandle h, Vector3 pos)
        {
            var m = h.Owner;
            if (m == null) return;
            if (h.IsEndA) m.Set(pos, m.PointB);
            else m.Set(m.PointA, pos);
        }

        // --- Кнопка «+» ---

        private void SetPlusFor(MeasurePointHandle h)
        {
            if (_plus == null || h == null || h.Owner == null) return;
            Vector3 p = h.IsEndA ? h.Owner.PointA : h.Owner.PointB;
            Vector3 line = h.Owner.PointB - h.Owner.PointA;
            Vector3 dir = h.IsEndA ? -line : line;                       // «наружу» от линии
            Vector3 side = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.right;

            _plusAnchor = p;
            _plus.Anchor = p;                                            // цепочка продолжается от самой точки
            _plus.transform.position = p + side * 0.09f;
            if (!_plus.gameObject.activeSelf) _plus.gameObject.SetActive(true);
            _plusVisibleUntil = Time.time + PlusGrace;
        }

        private void MaybeHidePlus()
        {
            if (_plus != null && _plus.gameObject.activeSelf && Time.time > _plusVisibleUntil)
                _plus.gameObject.SetActive(false);
        }

        private void HidePlus()
        {
            if (_plus != null && _plus.gameObject.activeSelf) _plus.gameObject.SetActive(false);
        }

        /// <summary>Отслеживание наведения: вибро-отклик при заходе на «+»/шарик, подсветка «+».</summary>
        private void UpdateHover(MeasureContinueButton btn, MeasurePointHandle handle)
        {
            Component hover = (Component)btn ?? handle;
            if (hover != _hoverTarget)
            {
                _hoverTarget = hover;
                if (hover != null && input != null) input.Pulse(0.2f, 0.01f);   // hover tick
            }
            if (_hoverBtn != btn)
            {
                if (_hoverBtn != null) _hoverBtn.SetHovered(false);
                _hoverBtn = btn;
                if (_hoverBtn != null) _hoverBtn.SetHovered(true);
            }
        }

        // --- Постановка / цепочка ---

        private void StartChainFrom(Vector3 anchor)
        {
            _pendingStart = anchor;
            _preview = Instantiate(measurementPrefab, transform);
            _preview.Set(anchor, anchor);
            _preview.SetInteractable(false);
            HidePlus();
        }

        private void PlacePoint(Vector3 p)
        {
            if (!_pendingStart.HasValue)
            {
                _pendingStart = p;
                _preview = Instantiate(measurementPrefab, transform);
                _preview.Set(p, p);
                _preview.SetInteractable(false);
            }
            else
            {
                var done = _preview;
                var start = _pendingStart.Value;
                _preview = null;
                _pendingStart = null;
                if (done == null) return;   // preview destroyed externally — abandon gracefully
                done.Set(start, p);
                _measurements.Add(done);
                if (sceneModel != null)
                {
                    var sel = done.GetComponent<Selectable>();
                    sceneModel.Register(sel);
                    // Creation is history too: X takes back a misplaced point (01 §Б2).
                    if (sel != null) sceneModel.History.Record(new Editing.CreateCommand(sel));
                }
                done.SetInteractable(true);
                TrimMeasurements();
            }
        }

        /// <summary>Alive AND visible (not hidden by a DeleteCommand) — hidden objects must not
        /// snap, dedupe or count toward the cap as if they were still on the scene.</summary>
        private static bool IsLive(Measurement m) => m != null && m.gameObject.activeSelf;

        private void TrimMeasurements()
        {
            // Hard cap on total entries (live + hidden) so memory stays bounded. Destroying is
            // safe for undo: SceneModel.Unregister purges the victim's commands from history.
            while (_measurements.Count > maxMeasurements)
            {
                var m = _measurements[0];
                _measurements.RemoveAt(0);
                if (m == null) continue;
                if (sceneModel != null) sceneModel.Unregister(m.GetComponent<Selectable>());
                Destroy(m.gameObject);
            }
        }

        private void DeleteMeasurement(Measurement m)
        {
            if (m == null) { _measurements.Remove(m); return; }
            var sel = m.GetComponent<Selectable>();
            if (sceneModel != null && sel != null)
            {
                // Route through the command stack: delete = hide, undoable with X.
                sceneModel.History.Execute(new DeleteCommand(sel));
            }
            else
            {
                _measurements.Remove(m);
                Destroy(m.gameObject);
            }
        }

        private void ClearLast()
        {
            if (_preview != null)
            {
                Destroy(_preview.gameObject);
                _preview = null;
                _pendingStart = null;
                return;
            }
            // No blind LIFO deletes (UX v2 P0.3): destructive B needs a hovered target —
            // otherwise B on empty space is the Esc gesture, back to the Select tool.
            if (manager != null) manager.ActivateTool("select");
        }

        /// <summary>Схлопывание совпавших точек: маркер рисуется только у первого владельца.</summary>
        private void DedupeMarkers()
        {
            _keptPts.Clear();
            foreach (var m in _measurements)
            {
                if (!IsLive(m)) continue;
                m.SetMarkerVisible(true, KeepOrDuplicate(m.PointA));
                m.SetMarkerVisible(false, KeepOrDuplicate(m.PointB));
            }
        }

        /// <summary>true — эту точку показываем (первая в кластере); false — совпала с уже показанной.</summary>
        private bool KeepOrDuplicate(Vector3 p)
        {
            const float epsSqr = 0.0004f; // ~2 см
            for (int i = 0; i < _keptPts.Count; i++)
                if ((_keptPts[i] - p).sqrMagnitude < epsSqr) return false;
            _keptPts.Add(p);
            return true;
        }

        /// <summary>Ручка ближайшего конца в радиусе магнита (для «клик рядом с точкой = редактирование»).</summary>
        private MeasurePointHandle FindEndpointHandle(Vector3 p)
        {
            float best = snapDistance;
            MeasurePointHandle found = null;
            foreach (var m in _measurements)
            {
                if (!IsLive(m)) continue;
                float da = Vector3.Distance(p, m.PointA);
                if (da < best) { best = da; found = m.GetHandle(true); }
                float db = Vector3.Distance(p, m.PointB);
                if (db < best) { best = db; found = m.GetHandle(false); }
            }
            return found;
        }

        /// <summary>Магнит: если p близко к концу существующего измерения (кроме exclude) — вернуть этот конец.</summary>
        private bool TrySnapToEndpoint(Vector3 p, out Vector3 result, Measurement exclude)
        {
            float best = snapDistance;
            result = default;
            bool found = false;
            foreach (var m in _measurements)
            {
                if (!IsLive(m) || m == exclude) continue;
                float da = Vector3.Distance(p, m.PointA);
                if (da < best) { best = da; result = m.PointA; found = true; }
                float db = Vector3.Distance(p, m.PointB);
                if (db < best) { best = db; result = m.PointB; found = true; }
            }
            return found;
        }

    }
}
