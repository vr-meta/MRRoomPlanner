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
    }
}
