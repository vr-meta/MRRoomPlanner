using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Measure;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// Left-hand navigation (docs/design/21-locomotion.md): hold the LEFT trigger to aim
    /// a ballistic arc from the left controller, release over a slab or stair tread to
    /// teleport — the MODEL comes to your feet (the usual TeleportCommand, undoable),
    /// the camera never moves in passthrough. The left stick walks the OVRCameraRig
    /// smoothly, but only with the scan OFF (virtual background): sliding the camera
    /// against the real room breaks the passthrough illusion and reads as nausea.
    /// Ticked by ToolManager so the radial's input capture is respected.
    /// </summary>
    public class TeleportLocomotion : MonoBehaviour
    {
        [SerializeField] private MeasureInput input;
        [SerializeField] private SceneModel sceneModel;
        [SerializeField] private ToolManager manager;
        [SerializeField] private GroundService ground;
        [SerializeField] private LineRenderer arcLine;
        [SerializeField] private LineRenderer portalRing;

        public const float MoveSpeed = 2f;        // m/s of smooth locomotion
        public const float RingRadius = 0.25f;    // portal marker on the landing spot
        private const int RingPoints = 24;
        private const float StickDeadZone = 0.15f;

        private static readonly Color ValidColor = new(0.91f, 0.93f, 0.96f, 0.9f);
        private static readonly Color InvalidColor = new(0.55f, 0.55f, 0.55f, 0.35f);

        private readonly List<Vector3> _arc = new();
        private OVRCameraRig _rig;
        private Transform _leftAnchor;
        private bool _aiming;
        private bool _hasTarget;
        private Vector3 _target;

        /// <summary>Whether the portal aim is live this frame (ToolManager keeps the A-tap
        /// teleport away while the left hand is already aiming one).</summary>
        public bool IsAiming => _aiming;

        /// <summary>Called by ToolManager every frame. uiCaptured = the radial owns the
        /// left hand (stick flick + wheel); scanOn gates smooth locomotion.</summary>
        public void Tick(bool uiCaptured, bool scanOn)
        {
            // #123 diagnostics: walking reads dead on device — log the whole gate chain
            // once per 2 s so logcat says which link breaks (remove once diagnosed)
            if (Time.unscaledTime >= _nextDiagLog)
            {
                _nextDiagLog = Time.unscaledTime + 2f;
                Vector2 st = input != null ? input.LeftThumbstick() : Vector2.zero;
                object mrukDiag = MrukInstance();
                bool locked = mrukDiag != null && _mrukLockActiveProp != null
                    && (bool)_mrukLockActiveProp.GetValue(mrukDiag);
                Debug.Log($"[Loco] input={(input != null)} ui={uiCaptured} scan={scanOn} "
                    + $"stick=({st.x:0.00},{st.y:0.00}) rig={(ResolveRig() != null)} cam={(Camera.main != null)} wl={locked}");
            }
            if (input == null) { CancelAim(); return; }
            if (uiCaptured) { CancelAim(); return; }
            TickAim();
            TickMove(scanOn);
        }

        private float _nextDiagLog;

        // ---- portal teleport (hold left trigger → arc, release → jump) ----

        private void TickAim()
        {
            if (!input.TeleportAimHeld())
            {
                if (_aiming)
                {
                    bool jump = _hasTarget;
                    Vector3 target = _target;
                    CancelAim();
                    // release on a valid ring = go; release in the void does nothing
                    if (jump && manager != null) manager.TeleportModelTo(target);
                }
                return;
            }

            _aiming = true;
            Transform a = ResolveLeftAnchor();
            if (a == null) { HideAim(); return; }

            TeleportArc.Sample(a.position, a.forward, TeleportArc.Speed, TeleportArc.Gravity,
                TeleportArc.TimeStep, TeleportArc.MaxSamples, _arc);
            if (_arc.Count < 2) { HideAim(); return; }

            // walk the parabola segment by segment; the first surface hit ends the arc, and
            // only a slab, a stair tread or the GROUND makes it a valid landing (design/26 —
            // the ground is a first-class surface, the same rule as A-tap otherwise)
            _hasTarget = false;
            int last = _arc.Count - 1;
            for (int i = 1; i < _arc.Count; i++)
            {
                Vector3 p0 = _arc[i - 1];
                Vector3 p1 = _arc[i];
                Vector3 d = p1 - p0;
                float len = d.magnitude;
                if (len < 1e-5f) continue;

                Vector3 hit = Vector3.zero;
                float hitDist = float.PositiveInfinity;
                bool valid = false;

                if (sceneModel != null
                    && sceneModel.TryPick(new Ray(p0, d / len), out var picked, out var pt)
                    && Vector3.Distance(p0, pt) <= len)          // a hit beyond this segment is not ours
                {
                    hit = pt;
                    hitDist = Vector3.Distance(p0, pt);
                    valid = picked is Selectable sel
                        && (sel.Kind == SelectableKind.Floor || sel.Kind == SelectableKind.Stair);
                }

                // The ground carries no Selectable (it is a derived datum, not an object),
                // so the falling arc is intersected with its plane directly.
                if (ground != null && d.y < 0f)
                {
                    float gy = ground.GroundY;
                    if (p0.y >= gy && p1.y <= gy)
                    {
                        Vector3 gp = p0 + d * ((p0.y - gy) / (p0.y - p1.y));
                        float dist = Vector3.Distance(p0, gp);
                        if (dist < hitDist) { hit = gp; hitDist = dist; valid = true; }
                    }
                }

                if (float.IsPositiveInfinity(hitDist)) continue;
                _arc[i] = hit;                                   // trim the arc at the surface
                last = i;
                if (valid) { _hasTarget = true; _target = hit; }
                break;
            }
            DrawAim(last);
        }

        private void DrawAim(int lastIndex)
        {
            if (arcLine != null)
            {
                arcLine.enabled = true;
                int n = lastIndex + 1;
                arcLine.positionCount = n;
                for (int i = 0; i < n; i++) arcLine.SetPosition(i, _arc[i]);
                Color c = _hasTarget ? ValidColor : InvalidColor;
                arcLine.startColor = c;
                arcLine.endColor = c;
            }
            if (portalRing != null)
            {
                portalRing.enabled = _hasTarget;
                if (_hasTarget)
                {
                    portalRing.positionCount = RingPoints;
                    for (int i = 0; i < RingPoints; i++)
                    {
                        float ang = i * 2f * Mathf.PI / RingPoints;
                        portalRing.SetPosition(i, _target + new Vector3(
                            Mathf.Cos(ang) * RingRadius, 0.01f, Mathf.Sin(ang) * RingRadius));
                    }
                }
            }
        }

        private void CancelAim()
        {
            _aiming = false;
            _hasTarget = false;
            HideAim();
        }

        private void HideAim()
        {
            if (arcLine != null) arcLine.enabled = false;
            if (portalRing != null) portalRing.enabled = false;
        }

        // ---- smooth locomotion (left stick, scan-off virtual mode only) ----

        // ←/→ snap turn (feedback 2026-08-15): a discrete pivot beats the unused
        // strafe — turning the whole body in the chair got old fast
        private const float SnapTurnDeg = 45f;
        private const float SnapTurnThreshold = 0.6f;
        private const float SnapTurnRearm = 0.3f;
        private bool _snapArmed = true;

        private void TickMove(bool scanOn)
        {
            // The Scan-ON gate is gone (headset feedback 2026-08-15: "повороты и ходьба
            // не работают") — the user drives the stick deliberately and owns the
            // comfort trade-off; passthrough desync self-corrects on the next teleport.
            _ = scanOn;
            Vector2 stick = input.LeftThumbstick();

            var cam = Camera.main;
            Transform rig = ResolveRig();
            if (cam == null || rig == null) return;

            // ←/→ = snap turn around the head, once per push, re-armed at the center
            if (Mathf.Abs(stick.x) > SnapTurnThreshold)
            {
                if (_snapArmed)
                {
                    _snapArmed = false;
                    ApplyLocomotion(Vector3.zero, Mathf.Sign(stick.x) * SnapTurnDeg,
                        cam.transform.position, rig);
                    input.PulseLeft(0.3f, 0.02f);
                }
            }
            else if (Mathf.Abs(stick.x) < SnapTurnRearm)
            {
                _snapArmed = true;
            }

            if (Mathf.Abs(stick.y) < StickDeadZone) return;

            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) return;   // looking straight down — no heading
            fwd.Normalize();
            Vector3 step = fwd * (stick.y * MoveSpeed * Time.deltaTime);

            // Horizontal only — the vertical is gravity's job (GroundService settles the
            // MODEL to the feet). The one thing refused here is a ledge too tall to step
            // onto: that is what keeps the walker out of the solid body of a stair flight
            // instead of gliding through it (design/26 §4).
            if (ground != null
                && !ground.CanWalkTo(cam.transform.position, cam.transform.position + step))
                return;   // a genuine ledge — relative check, see GroundService (#123)

            // Bodies block the walk (feedback: "проходим сквозь лестницы") — smooth
            // locomotion had no horizontal collision at all, hidden until walking
            // itself worked. Two short whiskers along the step: chest and knee height;
            // stair flights, walls and furniture all live on the selectable layer.
            Vector3 head = cam.transform.position;
            Vector3 dir = step.normalized;
            const float Whisker = 0.35f;
            const int SelectableLayer = 6;
            if (Physics.Raycast(head, dir, Whisker, 1 << SelectableLayer,
                    QueryTriggerInteraction.Ignore)
                || Physics.Raycast(head + Vector3.down * 0.9f, dir, Whisker,
                    1 << SelectableLayer, QueryTriggerInteraction.Ignore))
                return;

            ApplyLocomotion(step, 0f, default, rig);
        }

        // ---- MRUK world lock (#123 root cause): with EnableWorldLock on, MRUK rewrites
        // the TrackingSpace pose EVERY frame, eating any manual rig motion — walking
        // "moved a couple of centimeters" (one frame of lag) and snap turns died.
        // TrackingSpaceOffset is MRUK's official locomotion accumulator, so all stick
        // motion goes through it whenever the lock is live; the plain rig transform
        // stays the fallback (editor, no scan, MRUK absent). Reflection keeps the app's
        // no-hard-Meta-dependency rule (the EffectMesh/MRUKAnchor precedent).

        private static System.Type _mrukType;
        private static System.Reflection.PropertyInfo _mrukInstanceProp;
        private static System.Reflection.PropertyInfo _mrukLockActiveProp;
        private static System.Reflection.FieldInfo _mrukOffsetField;
        private static bool _mrukProbed;

        private static object MrukInstance()
        {
            if (!_mrukProbed)
            {
                _mrukProbed = true;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    _mrukType = asm.GetType("Meta.XR.MRUtilityKit.MRUK");
                    if (_mrukType != null) break;
                }
                if (_mrukType != null)
                {
                    const System.Reflection.BindingFlags S =
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public;
                    const System.Reflection.BindingFlags I =
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
                    _mrukInstanceProp = _mrukType.GetProperty("Instance", S);
                    _mrukLockActiveProp = _mrukType.GetProperty("IsWorldLockActive", I);
                    _mrukOffsetField = _mrukType.GetField("TrackingSpaceOffset", I);
                }
            }
            return _mrukInstanceProp != null ? _mrukInstanceProp.GetValue(null) : null;
        }

        /// <summary>One entry point for every stick-driven pose change: a world-space
        /// translation and/or a yaw around a pivot, routed through the world-lock
        /// offset when MRUK owns the tracking space, or the rig root otherwise.</summary>
        private void ApplyLocomotion(Vector3 step, float turnDeg, Vector3 pivot, Transform rig)
        {
            object mruk = MrukInstance();
            if (mruk != null && _mrukLockActiveProp != null && _mrukOffsetField != null
                && (bool)_mrukLockActiveProp.GetValue(mruk))
            {
                var m = (Matrix4x4)_mrukOffsetField.GetValue(mruk);
                if (turnDeg != 0f)
                    m = Matrix4x4.Translate(pivot)
                        * Matrix4x4.Rotate(Quaternion.AngleAxis(turnDeg, Vector3.up))
                        * Matrix4x4.Translate(-pivot) * m;
                if (step != Vector3.zero)
                    m = Matrix4x4.Translate(step) * m;
                _mrukOffsetField.SetValue(mruk, m);
                return;
            }
            if (rig == null) return;
            if (turnDeg != 0f) rig.RotateAround(pivot, Vector3.up, turnDeg);
            if (step != Vector3.zero) rig.position += step;
        }

        // ---- anchors (resolved lazily, PointerProvider-style — no hard rig wiring) ----

        private Transform ResolveLeftAnchor()
        {
            if (_leftAnchor != null) return _leftAnchor;
            if (_rig == null) _rig = Object.FindFirstObjectByType<OVRCameraRig>();
            if (_rig != null && _rig.leftControllerAnchor != null)
                return _leftAnchor = _rig.leftControllerAnchor;
            foreach (var n in new[] { "LeftControllerAnchor", "LeftHandAnchor" })
            {
                var go = GameObject.Find(n);
                if (go != null) return _leftAnchor = go.transform;
            }
#if UNITY_EDITOR
            // editor smoke path: aim from the head so G-key testing shows the arc
            return Camera.main != null ? Camera.main.transform : null;
#else
            return null;
#endif
        }

        private Transform ResolveRig()
        {
            if (_rig == null) _rig = Object.FindFirstObjectByType<OVRCameraRig>();
            return _rig != null ? _rig.transform : null;
        }
    }
}
