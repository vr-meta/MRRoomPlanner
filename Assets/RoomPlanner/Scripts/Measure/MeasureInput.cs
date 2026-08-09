using UnityEngine;

namespace RoomPlanner.Measure
{
    /// <summary>
    /// Input for the tools (OVRInput). Keyboard fallback in the Editor.
    /// Button map: docs/design/10-controls.md.
    /// </summary>
    public class MeasureInput : MonoBehaviour
    {
        /// <summary>Place / select / start drag — index TRIGGER (either hand; you point with the right).</summary>
        public bool ConfirmPressed()
        {
#if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.Space)) return true;
#endif
            return OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger)
                || OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger);
        }

        /// <summary>Trigger held — for dragging a point.</summary>
        public bool ConfirmHeld()
        {
#if UNITY_EDITOR
            if (Input.GetKey(KeyCode.Space)) return true;
#endif
            return OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger)
                || OVRInput.Get(OVRInput.Button.SecondaryIndexTrigger);
        }

        /// <summary>Cancel / delete / finish chain — B button.</summary>
        public bool ClearPressed()
        {
#if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.Backspace)) return true;
#endif
            return OVRInput.GetDown(OVRInput.Button.Two);
        }

        /// <summary>Axis snap (vertical / horizontal) — GRIP held (either hand).</summary>
        public bool SnapHeld()
        {
#if UNITY_EDITOR
            if (Input.GetKey(KeyCode.LeftShift)) return true;
#endif
            return OVRInput.Get(OVRInput.Button.PrimaryHandTrigger)
                || OVRInput.Get(OVRInput.Button.SecondaryHandTrigger);
        }

        /// <summary>Thumbstick vector (either controller, larger magnitude wins).</summary>
        public Vector2 Thumbstick()
        {
#if UNITY_EDITOR
            var k = new Vector2(
                (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f),
                (Input.GetKey(KeyCode.PageUp) ? 1f : 0f) - (Input.GetKey(KeyCode.PageDown) ? 1f : 0f));
            if (k.sqrMagnitude > 0f) return k;
#endif
            Vector2 l = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
            Vector2 r = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
            return r.sqrMagnitude >= l.sqrMagnitude ? r : l;
        }

        /// <summary>Air-cursor depth — thumbstick up/down (either controller).</summary>
        public float DepthAdjust() => Thumbstick().y;

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
