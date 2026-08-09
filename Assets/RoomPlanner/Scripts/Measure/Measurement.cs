using UnityEngine;

namespace RoomPlanner.Measure
{
    /// <summary>
    /// Одно измерение: линия между двумя точками, маркеры на концах и подпись
    /// расстояния в середине. Управляется из MeasureController.
    /// </summary>
    public class Measurement : MonoBehaviour
    {
        [SerializeField] private LineRenderer line;
        [SerializeField] private MeasurementLabel label;
        [SerializeField] private Transform markerA;
        [SerializeField] private Transform markerB;

        public float Distance { get; private set; }
        public Vector3 PointA { get; private set; }
        public Vector3 PointB { get; private set; }

        /// <summary>Вкл/выкл коллайдеры концов. У предпросмотра и таскаемого — выкл,
        /// чтобы луч не попадал в собственные маркеры и шёл в стену.</summary>
        public void SetInteractable(bool on)
        {
            ToggleCollider(markerA, on);
            ToggleCollider(markerB, on);
        }

        private static void ToggleCollider(Transform t, bool on)
        {
            if (t == null) return;
            var c = t.GetComponent<Collider>();
            if (c != null) c.enabled = on;
        }

        /// <summary>Показать/скрыть визуал маркера конца (для схлопывания совпавших точек).</summary>
        public void SetMarkerVisible(bool isEndA, bool visible)
        {
            var t = isEndA ? markerA : markerB;
            if (t == null) return;
            var r = t.GetComponent<Renderer>();
            if (r != null) r.enabled = visible;
        }

        /// <summary>Ручка конца (для выделения/таскания по магнит-близости, не только по коллайдеру).</summary>
        public MeasurePointHandle GetHandle(bool isEndA)
        {
            var t = isEndA ? markerA : markerB;
            return t != null ? t.GetComponent<MeasurePointHandle>() : null;
        }

        public void Set(Vector3 a, Vector3 b)
        {
            PointA = a;
            PointB = b;
            if (markerA != null) markerA.position = a;
            if (markerB != null) markerB.position = b;

            if (line != null)
            {
                line.positionCount = 2;
                line.SetPosition(0, a);
                line.SetPosition(1, b);
            }

            Distance = Vector3.Distance(a, b);

            if (label != null)
            {
                // чуть над линией: смещаем по перпендикуляру, направленному «вверх»
                Vector3 mid = (a + b) * 0.5f;
                Vector3 lineDir = (b - a).sqrMagnitude > 0.0001f ? (b - a).normalized : Vector3.right;
                Vector3 up = Vector3.up - Vector3.Dot(Vector3.up, lineDir) * lineDir;
                up = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up; // вертикальная линия → фолбэк
                label.transform.position = mid + up * 0.06f;
                label.SetDistance(Distance);
            }
        }
    }
}
