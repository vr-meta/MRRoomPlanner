using UnityEngine;

namespace RoomPlanner.Measure
{
    /// <summary>
    /// Ввод для рулетки (OVRInput). В редакторе — клавиатура.
    /// </summary>
    public class MeasureInput : MonoBehaviour
    {
        /// <summary>Поставить точку — правый триггер (или A).</summary>
        public bool ConfirmPressed()
        {
#if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.Space)) return true;
#endif
            return OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger)
                || OVRInput.GetDown(OVRInput.Button.One);
        }

        /// <summary>Удержание триггера — для перетаскивания точки.</summary>
        public bool ConfirmHeld()
        {
#if UNITY_EDITOR
            if (Input.GetKey(KeyCode.Space)) return true;
#endif
            return OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger)
                || OVRInput.Get(OVRInput.Button.One);
        }

        /// <summary>Отменить/удалить — кнопка B.</summary>
        public bool ClearPressed()
        {
#if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.Backspace)) return true;
#endif
            return OVRInput.GetDown(OVRInput.Button.Two);
        }

        /// <summary>Привязка к оси (вертикаль/горизонталь) — зажатый боковой триггер (грип).</summary>
        public bool SnapHeld()
        {
#if UNITY_EDITOR
            if (Input.GetKey(KeyCode.LeftShift)) return true;
#endif
            return OVRInput.Get(OVRInput.Button.PrimaryHandTrigger);
        }

        private float _hapticUntil;

        /// <summary>Короткий вибро-отклик правого контроллера (например, при наведении на кнопку).</summary>
        public void Pulse(float amplitude = 0.5f, float duration = 0.06f)
        {
            OVRInput.SetControllerVibration(1f, amplitude, OVRInput.Controller.RTouch);
            _hapticUntil = Time.time + duration;
        }

        private void Update()
        {
            if (_hapticUntil > 0f && Time.time >= _hapticUntil)
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
                _hapticUntil = 0f;
            }
        }
    }
}
