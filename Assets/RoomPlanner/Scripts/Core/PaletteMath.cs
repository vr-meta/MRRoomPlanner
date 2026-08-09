using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>
    /// Pure math for the hand palette's motion (design/16 P1.2): lazy-follow with a dead zone
    /// (a raw 1:1 follow makes the ray-target jitter with the hand — Fitts' law tax on every
    /// click), and a hysteresis gate for palm-facing visibility.
    /// </summary>
    public static class PaletteMath
    {
        public const float DefaultTau = 0.12f;       // smoothing time constant, s
        public const float DefaultDeadZone = 0.015f; // m — inside it the palette holds still
        public const float ShowAbove = 0.6f;         // palm-dot thresholds (hysteresis)
        public const float HideBelow = 0.45f;

        /// <summary>
        /// Exponential lazy-follow: no motion inside the dead zone; outside it the palette
        /// converges toward the dead-zone boundary (never chases the exact target, so small
        /// hand tremor never moves it).
        /// </summary>
        public static Vector3 SmoothFollow(Vector3 current, Vector3 target, float dt,
            float tau = DefaultTau, float deadZone = DefaultDeadZone)
        {
            Vector3 delta = target - current;
            float dist = delta.magnitude;
            if (dist <= deadZone || dist < 1e-6f) return current;
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, tau));
            return current + delta / dist * ((dist - deadZone) * k);
        }

        /// <summary>Palm-gating with hysteresis: show when the controller faces the head
        /// (dot &gt; 0.6), hide only when it clearly turns away (dot &lt; 0.45) — no flicker
        /// at the boundary.</summary>
        public static bool GateVisible(bool wasVisible, float palmDot,
            float showAbove = ShowAbove, float hideBelow = HideBelow)
        {
            return wasVisible ? palmDot >= hideBelow : palmDot > showAbove;
        }
    }
}
