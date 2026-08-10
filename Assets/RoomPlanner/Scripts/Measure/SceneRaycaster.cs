using UnityEngine;

namespace RoomPlanner.Measure
{
    /// <summary>
    /// Луч по геометрии комнаты (Physics.Raycast по коллайдерам EffectMesh) и по
    /// интерактивным элементам (кнопки «+»). Возвращает объект попадания, чтобы
    /// контроллер мог отличить поверхность от кнопки.
    /// </summary>
    public class SceneRaycaster : MonoBehaviour
    {
        [Tooltip("Слои для луча поверхности (всё, кроме меню=2 и выбираемой геометрии=6). " +
                 "Стены/полы выбираются инструментом Select, а не служат поверхностью для рисования.")]
        [SerializeField] private LayerMask sceneMask = ~((1 << 2) | (1 << 6));

        [Tooltip("Максимальная дальность луча, м.")]
        [SerializeField] private float maxDistance = 10f;

        public bool TryRaycast(Ray ray, out Vector3 point, out Vector3 normal, out GameObject hitObject)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, sceneMask, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                normal = hit.normal;
                hitObject = hit.collider.gameObject;
                return true;
            }

            point = default;
            normal = default;
            hitObject = null;
            return false;
        }

        /// <summary>
        /// Scan AND the user's OWN geometry (layer 6), closest hit wins. Own walls,
        /// slabs and stairs ARE surfaces for point placement — without them, aiming at
        /// an own wall sailed past it into the air («точка проваливается за стену»,
        /// device feedback 2026-08-10; в план-режиме скана нет вовсе). Measurement
        /// markers and thin wires stay non-surfaces. TryRaycast remains scan-only for
        /// callers that need the old semantics.
        /// </summary>
        public bool TryRaycastSurface(Ray ray, out Vector3 point, out Vector3 normal,
            out GameObject hitObject)
        {
            bool has = TryRaycast(ray, out point, out normal, out hitObject);

            if (Physics.Raycast(ray, out RaycastHit own, maxDistance, 1 << 6,
                    QueryTriggerInteraction.Ignore))
            {
                bool closer = !has || own.distance < Vector3.Distance(ray.origin, point);
                if (closer)
                {
                    var sel = own.collider.GetComponentInParent<RoomPlanner.Editing.Selectable>();
                    if (sel != null
                        && sel.Kind != RoomPlanner.Editing.SelectableKind.Measurement
                        && sel.Kind != RoomPlanner.Editing.SelectableKind.Wire)
                    {
                        point = own.point;
                        normal = own.normal;
                        hitObject = own.collider.gameObject;
                        has = true;
                    }
                }
            }
            return has;
        }
    }
}
