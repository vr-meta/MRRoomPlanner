using System.Collections.Generic;
using UnityEngine;

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
    public class MeasureController : MonoBehaviour
    {
        [SerializeField] private PointerProvider pointer;
        [SerializeField] private MeasureInput input;
        [SerializeField] private SceneRaycaster raycaster;
        [SerializeField] private Transform reticle;
        [SerializeField] private Measurement measurementPrefab;
        [SerializeField] private MeasureContinueButton continueButtonPrefab;
        [Tooltip("Радиус магнита к существующим концам, м.")]
        [SerializeField] private float snapDistance = 0.1f;
        [SerializeField] private int maxMeasurements = 40;

        private const float PlusGrace = 0.25f; // сколько «+» держится после ухода луча (чтобы успеть навестись)

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
        private Component _hoverTarget;
        private MeasureContinueButton _hoverBtn;

        private void Start()
        {
            Debug.Log($"[Measure] v7 started. prefab={(measurementPrefab != null)} contBtn={(continueButtonPrefab != null)}");
            if (continueButtonPrefab != null)
            {
                _plus = Instantiate(continueButtonPrefab, transform);
                _plus.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (pointer == null || raycaster == null || input == null) return;

            DedupeMarkers();

            Ray ray = pointer.GetRay();
            _hasHit = raycaster.TryRaycast(ray, out _currentHit, out _currentNormal, out var hitObj);
            var plusHit = (_hasHit && hitObj != null) ? hitObj.GetComponentInParent<MeasureContinueButton>() : null;
            var handleHit = (_hasHit && hitObj != null) ? hitObj.GetComponentInParent<MeasurePointHandle>() : null;

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
            if (effHandle == null && !_pendingStart.HasValue && _hasHit)
                effHandle = FindEndpointHandle(_currentHit);

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

            // --- Точка назначения: ось (грип) + магнит к концам ---
            Vector3 target = _currentHit;
            if (_pendingStart.HasValue && input.SnapHeld())
                target = SnapToAxis(_pendingStart.Value, _currentHit);
            if (TrySnapToEndpoint(_currentHit, out var snapPt, _dragging != null ? _dragging.Owner : null))
                target = snapPt;

            if (reticle != null)
            {
                reticle.gameObject.SetActive(true);
                reticle.position = _hasHit ? target : ray.origin + ray.direction * 2f;
            }

            // --- Удаление ---
            if (input.ClearPressed())
            {
                if (effHandle != null && effHandle.Owner != null) DeleteMeasurement(effHandle.Owner);
                else ClearLast();
                return;
            }

            // --- Постановка / старт цепочки / старт таскания ---
            if (input.ConfirmPressed())
            {
                if (_pendingStart.HasValue && _hasHit)
                    PlacePoint(target);                                  // фиксация второй точки
                else if (plusHit != null && plusHit == _plus)
                    StartChainFrom(_plusAnchor);                         // продолжить от наведённой точки
                else if (effHandle != null)
                {
                    _dragging = effHandle;                               // тащить точку (по коллайдеру или магниту)
                    if (effHandle.Owner != null) effHandle.Owner.SetInteractable(false);
                }
                else if (_hasHit)
                    PlacePoint(target);                                  // первая точка нового измерения
            }

            if (_pendingStart.HasValue && _preview != null)
                _preview.Set(_pendingStart.Value, _hasHit ? target : _pendingStart.Value);
        }

        private void DoDrag()
        {
            if (input.ConfirmHeld())
            {
                Vector3 t = _hasHit ? _currentHit : _dragging.transform.position;
                if (input.SnapHeld() && _dragging.Owner != null)
                {
                    Vector3 other = _dragging.IsEndA ? _dragging.Owner.PointB : _dragging.Owner.PointA;
                    t = SnapToAxis(other, t);
                }
                if (TrySnapToEndpoint(_currentHit, out var sp, _dragging.Owner)) t = sp;
                ApplyDrag(_dragging, t);
                if (reticle != null) { reticle.gameObject.SetActive(true); reticle.position = t; }
            }
            else
            {
                if (_dragging.Owner != null) _dragging.Owner.SetInteractable(true);
                _dragging = null;
            }
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
                if (hover != null && input != null) input.Pulse();
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
                _preview.Set(_pendingStart.Value, p);
                var done = _preview;
                _measurements.Add(done);
                _preview = null;
                _pendingStart = null;
                done.SetInteractable(true);
                TrimMeasurements();
            }
        }

        private void TrimMeasurements()
        {
            if (_measurements.Count > maxMeasurements)
            {
                Destroy(_measurements[0].gameObject);
                _measurements.RemoveAt(0);
            }
        }

        private void DeleteMeasurement(Measurement m)
        {
            _measurements.Remove(m);
            if (m != null) Destroy(m.gameObject);
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
            if (_measurements.Count > 0)
            {
                var m = _measurements[_measurements.Count - 1];
                _measurements.RemoveAt(_measurements.Count - 1);
                Destroy(m.gameObject);
            }
        }

        /// <summary>Схлопывание совпавших точек: маркер рисуется только у первого владельца.</summary>
        private void DedupeMarkers()
        {
            _keptPts.Clear();
            foreach (var m in _measurements)
            {
                if (m == null) continue;
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
                if (m == null) continue;
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
                if (m == null || m == exclude) continue;
                float da = Vector3.Distance(p, m.PointA);
                if (da < best) { best = da; result = m.PointA; found = true; }
                float db = Vector3.Distance(p, m.PointB);
                if (db < best) { best = db; result = m.PointB; found = true; }
            }
            return found;
        }

        /// <summary>Привязка точки b к ближайшей мировой оси относительно a (вертикаль/горизонталь).</summary>
        private static Vector3 SnapToAxis(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
            if (ay >= ax && ay >= az) return a + new Vector3(0f, d.y, 0f);   // вертикаль
            if (ax >= az) return a + new Vector3(d.x, 0f, 0f);               // горизонталь X
            return a + new Vector3(0f, 0f, d.z);                             // горизонталь Z
        }
    }
}
