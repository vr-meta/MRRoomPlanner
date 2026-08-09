using TMPro;
using UnityEngine;

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

        private Transform _cam;

        public void SetDistance(float meters)
        {
            if (text == null) return;
            text.text = FormatDistance(meters);

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

        public static string FormatDistance(float meters)
        {
            float cm = meters * 100f;
            return $"{cm:0} см"; // всегда в сантиметрах
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
