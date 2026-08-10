using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>What a tracker step produced (design/20 §1).</summary>
    public enum RadialEvent
    {
        None,
        SlotChanged,      // highlight moved to HighlightedSlot
        Confirmed,        // fast flick released — HighlightedSlot is the pick
        BrowseCancelled   // slow return to center — keep the menu open, clear highlight
    }

    /// <summary>
    /// Pure geometry of the 12-slot tool radial (design/20-ui-design-system.md §1).
    /// Angle convention: 0° = 12 o'clock, clockwise positive — matches the fixed compass
    /// layout of tools. Stick vectors come in as (x right, y up).
    /// </summary>
    public static class RadialMath
    {
        public const int Slots = 12;
        public const float SlotSpanDeg = 360f / Slots;

        /// <summary>Stick deflection that enters sector tracking…</summary>
        public const float EnterDeflection = 0.55f;
        /// <summary>…and the release level that ends it (hysteresis band between them).</summary>
        public const float ExitDeflection = 0.45f;

        /// <summary>Extra angle past a sector edge before the highlight switches —
        /// 12 sectors without boundary flicker (the ergonomics condition for 12 over 8).</summary>
        public const float BoundaryHysteresisDeg = 4f;

        /// <summary>Sector-entry → center-return faster than this = intent (confirm);
        /// slower = browsing (keep open). §1.5.</summary>
        public const float FlickConfirmSeconds = 0.4f;

        /// <summary>Direction → compass angle [0, 360): up = 0, right = 90.</summary>
        public static float AngleDeg(Vector2 dir)
        {
            float a = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            return a < 0f ? a + 360f : a;
        }

        /// <summary>Nearest slot for an angle, no hysteresis. Slot 0 spans −15°…+15°.</summary>
        public static int SlotAt(float angleDeg)
        {
            int slot = Mathf.RoundToInt(Mathf.Repeat(angleDeg, 360f) / SlotSpanDeg);
            return slot % Slots;
        }

        /// <summary>
        /// Slot tracking with the ±4° boundary hysteresis: keep <paramref name="currentSlot"/>
        /// until the angle is clearly past its edge. −1 current = no slot yet, no hysteresis.
        /// </summary>
        public static int Track(int currentSlot, float angleDeg)
        {
            if (currentSlot < 0) return SlotAt(angleDeg);
            float delta = Mathf.DeltaAngle(SlotCenterDeg(currentSlot), Mathf.Repeat(angleDeg, 360f));
            if (Mathf.Abs(delta) <= SlotSpanDeg * 0.5f + BoundaryHysteresisDeg) return currentSlot;
            return SlotAt(angleDeg);
        }

        public static float SlotCenterDeg(int slot) => slot * SlotSpanDeg;

        /// <summary>Unit direction (x right, y up) to a slot's center — icon placement.</summary>
        public static Vector2 SlotDirection(int slot)
        {
            float rad = SlotCenterDeg(slot) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        }
    }

    /// <summary>
    /// The stick path of the radial: deflect → track sectors → release. Confirms on a
    /// fast flick, keeps the menu open on a slow browse-return. Pure C# — the caller
    /// feeds stick vectors and its own clock, so the gesture logic is unit-testable.
    /// The ray path (hover + trigger) lives in the menu component; last mover wins there.
    /// </summary>
    public sealed class RadialTracker
    {
        /// <summary>Currently highlighted slot, −1 = none. Valid after Confirmed too.</summary>
        public int HighlightedSlot { get; private set; } = -1;

        private bool _deflected;
        private float _slotEnteredAt;

        public void Reset()
        {
            HighlightedSlot = -1;
            _deflected = false;
        }

        /// <summary>The ray path stole the highlight — adopt it so a follow-up flick
        /// release confirms what the user actually sees.</summary>
        public void SetHighlight(int slot, float now)
        {
            HighlightedSlot = slot;
            _slotEnteredAt = now;
        }

        public RadialEvent Step(Vector2 stick, float now)
        {
            float mag = stick.magnitude;

            if (!_deflected)
            {
                if (mag < RadialMath.EnterDeflection) return RadialEvent.None;
                _deflected = true;
                HighlightedSlot = RadialMath.SlotAt(RadialMath.AngleDeg(stick));
                _slotEnteredAt = now;
                return RadialEvent.SlotChanged;
            }

            // hysteresis band: still "deflected" until clearly released
            if (mag > RadialMath.ExitDeflection)
            {
                int slot = RadialMath.Track(HighlightedSlot, RadialMath.AngleDeg(stick));
                if (slot == HighlightedSlot) return RadialEvent.None;
                HighlightedSlot = slot;
                _slotEnteredAt = now;
                return RadialEvent.SlotChanged;
            }

            // released: fast flick = intent, slow return = browsing
            _deflected = false;
            if (HighlightedSlot >= 0 && now - _slotEnteredAt < RadialMath.FlickConfirmSeconds)
                return RadialEvent.Confirmed;
            HighlightedSlot = -1;
            return RadialEvent.BrowseCancelled;
        }
    }
}
