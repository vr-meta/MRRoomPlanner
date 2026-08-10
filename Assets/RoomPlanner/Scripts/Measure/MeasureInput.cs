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

        /// <summary>Undo — X (Button.Three, left hand). Editor: Ctrl+Z.</summary>
        public bool UndoPressed()
        {
#if UNITY_EDITOR
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                && Input.GetKeyDown(KeyCode.Z)) return true;
#endif
            return OVRInput.GetDown(OVRInput.Button.Three);
        }

        /// <summary>Redo — Y (Button.Four, left hand). Editor: Ctrl+Y.</summary>
        public bool RedoPressed()
        {
#if UNITY_EDITOR
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                && Input.GetKeyDown(KeyCode.Y)) return true;
#endif
            return OVRInput.GetDown(OVRInput.Button.Four);
        }

        /// <summary>Teleport to the aimed floor spot — A (Button.One, right hand). Editor: T.</summary>
        public bool TeleportPressed()
        {
#if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.T)) return true;
#endif
            return OVRInput.GetDown(OVRInput.Button.One);
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

        /// <summary>Grip just pressed this frame — starts the inspector grab (held grip elsewhere = snap).</summary>
        public bool SnapPressed()
        {
#if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.LeftShift)) return true;
#endif
            return OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger)
                || OVRInput.GetDown(OVRInput.Button.SecondaryHandTrigger);
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

        /// <summary>LEFT stick click — opens/closes the tool radial (design/20 §1). Editor: Tab.</summary>
        public bool RadialPressed()
        {
#if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.Tab)) return true;
#endif
            return OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch);
        }

        /// <summary>RIGHT stick click — previous tool (16 P2.3). Editor: Q.</summary>
        public bool PrevToolPressed()
        {
#if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.Q)) return true;
#endif
            return OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch);
        }

        /// <summary>The LEFT stick specifically — drives the radial's flick gesture
        /// (the generic Thumbstick() is "larger magnitude wins", wrong for the radial).</summary>
        public Vector2 LeftThumbstick()
        {
#if UNITY_EDITOR
            var k = new Vector2(
                (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f),
                (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f));
            if (k.sqrMagnitude > 0f) return k.normalized;
#endif
            return OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
        }

        private float _hapticUntil;
        private float _leftHapticUntil;

        /// <summary>Left-controller haptics — radial and palette events (design/20 §4.2).</summary>
        public void PulseLeft(float amplitude = 0.5f, float duration = 0.02f)
        {
            OVRInput.SetControllerVibration(1f, amplitude, OVRInput.Controller.LTouch);
            _leftHapticUntil = Time.time + duration;
        }

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
            if (_leftHapticUntil > 0f && Time.time >= _leftHapticUntil)
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
                _leftHapticUntil = 0f;
            }
        }

        private void OnDisable()
        {
            // Don't leave a vibration running for the OS's ~2 s auto-timeout.
            if (_hapticUntil > 0f)
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
                _hapticUntil = 0f;
            }
            if (_leftHapticUntil > 0f)
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
                _leftHapticUntil = 0f;
            }
        }
    }
}
