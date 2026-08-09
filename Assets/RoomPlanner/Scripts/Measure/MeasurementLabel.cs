using TMPro;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Measure
{
    /// <summary>
    /// Подпись расстояния как бейдж: текст на тёмной плашке-подложке, по центру линии,
    /// всегда повёрнута к камере (billboard). Плашка подгоняется под размер текста.
    /// </summary>
    public class MeasurementLabel : MonoBehaviour
    {
        [SerializeField] private TMP_Text text;
        [SerializeField] private Transform background;
        [Tooltip("Сдвиг подписи к камере от линии, м (чтобы не тонула в стене).")]
        [SerializeField] private float forwardOffset = 0.02f;

        private Transform _cam;
        private Vector3 _anchor;

        /// <summary>Опорная точка подписи (в мире). Реальную позицию смещаем к камере в LateUpdate.</summary>
        public void SetAnchor(Vector3 world)
        {
            _anchor = world;
            transform.position = world;
        }

        public void SetDistance(float meters)
        {
            if (text == null) return;
            string s = FormatDistance(meters);
            if (s == text.text) return;   // same rounded cm — skip the TMP rebuild + string churn
            text.text = s;

            if (background != null)
            {
                text.ForceMeshUpdate();
                Bounds b = text.textBounds;
                float w = Mathf.Max(b.size.x, 0.1f) + 0.6f;
                float h = Mathf.Max(b.size.y, 0.1f) + 0.3f;
                background.localScale = new Vector3(w, h, background.localScale.z);
                background.localPosition = new Vector3(b.center.x, b.center.y, background.localPosition.z);
            }
        }

        public static string FormatDistance(float meters) => MeasureMath.FormatDistanceCm(meters);

        private void LateUpdate()
        {
            if (_cam == null)
            {
                if (Camera.main == null) return;
                _cam = Camera.main.transform;
            }
            Vector3 toCam = _cam.position - _anchor;
            if (toCam.sqrMagnitude > 0.0001f)
            {
                transform.position = _anchor + toCam.normalized * forwardOffset; // чуть впереди линии
                transform.rotation = Quaternion.LookRotation(-toCam);            // лицом к камере
            }
        }
    }
}
