using UnityEngine;

namespace RoomPlanner.Measure
{
    /// <summary>
    /// Кнопка-«плюс». Единственный экземпляр всплывает рядом с наведённой точкой (любой
    /// стартовой/конечной). Наведение лучом + триггер продолжают цепочку от точки Anchor.
    /// Всегда повёрнута к камере (billboard). Коллайдер нужен для попадания луча.
    /// </summary>
    public class MeasureContinueButton : MonoBehaviour
    {
        public Vector3 Anchor;

        private Transform _cam;
        private Vector3 _baseScale;
        private bool _hovered;

        private void Awake()
        {
            _baseScale = transform.localScale;
        }

        /// <summary>Наведён ли на кнопку луч (визуально подрастает).</summary>
        public void SetHovered(bool on)
        {
            if (_hovered == on) return;
            _hovered = on;
            transform.localScale = on ? _baseScale * 1.4f : _baseScale;
        }

        private void LateUpdate()
        {
            if (_cam == null)
            {
                if (Camera.main == null) return;
                _cam = Camera.main.transform;
            }
            Vector3 dir = transform.position - _cam.position;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir);
        }
    }
}
