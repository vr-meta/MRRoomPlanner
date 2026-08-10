using NUnit.Framework;
using RoomPlanner.Core;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>Sector geometry of the tool radial (design/20 §1).</summary>
    public class RadialMathTests
    {
        [Test]
        public void CompassAngles_UpIsZero_ClockwisePositive()
        {
            Assert.AreEqual(0f, RadialMath.AngleDeg(Vector2.up), 1e-3f);
            Assert.AreEqual(90f, RadialMath.AngleDeg(Vector2.right), 1e-3f);
            Assert.AreEqual(180f, RadialMath.AngleDeg(Vector2.down), 1e-3f);
            Assert.AreEqual(270f, RadialMath.AngleDeg(Vector2.left), 1e-3f);
        }

        [Test]
        public void SlotAt_TwelveSlots_CenteredOnCompassPoints()
        {
            Assert.AreEqual(0, RadialMath.SlotAt(0f));
            Assert.AreEqual(0, RadialMath.SlotAt(14.9f));
            Assert.AreEqual(1, RadialMath.SlotAt(15.1f));
            Assert.AreEqual(3, RadialMath.SlotAt(90f));
            Assert.AreEqual(6, RadialMath.SlotAt(180f));
            Assert.AreEqual(11, RadialMath.SlotAt(330f));
            Assert.AreEqual(0, RadialMath.SlotAt(345.1f));   // wraps back to slot 0
            Assert.AreEqual(0, RadialMath.SlotAt(360f));
        }

        [Test]
        public void Track_KeepsSlot_InsideHysteresisBand()
        {
            // slot 0 spans ±15°; +4° hysteresis holds it until 19°
            Assert.AreEqual(0, RadialMath.Track(0, 17f));
            Assert.AreEqual(0, RadialMath.Track(0, 18.9f));
            Assert.AreEqual(1, RadialMath.Track(0, 19.5f));
            // and symmetric on the way back
            Assert.AreEqual(1, RadialMath.Track(1, 12f));
            Assert.AreEqual(0, RadialMath.Track(1, 10.5f));
        }

        [Test]
        public void Track_NoCurrent_SnapsImmediately()
        {
            Assert.AreEqual(1, RadialMath.Track(-1, 17f));
        }

        [Test]
        public void Track_FarJump_LandsOnCorrectSlot()
        {
            Assert.AreEqual(6, RadialMath.Track(0, 180f));
        }

        [Test]
        public void SlotDirection_MatchesCenterAngle()
        {
            var up = RadialMath.SlotDirection(0);
            Assert.AreEqual(0f, up.x, 1e-4f);
            Assert.AreEqual(1f, up.y, 1e-4f);
            var right = RadialMath.SlotDirection(3);
            Assert.AreEqual(1f, right.x, 1e-4f);
            Assert.AreEqual(0f, right.y, 1e-4f);
            for (int s = 0; s < RadialMath.Slots; s++)
                Assert.AreEqual(1f, RadialMath.SlotDirection(s).magnitude, 1e-4f);
        }
    }

    /// <summary>The stick gesture: deflect → track → flick-confirm or browse (design/20 §1.5).</summary>
    public class RadialTrackerTests
    {
        private static Vector2 Dir(float deg, float mag = 1f) =>
            new Vector2(Mathf.Sin(deg * Mathf.Deg2Rad), Mathf.Cos(deg * Mathf.Deg2Rad)) * mag;

        [Test]
        public void BelowEnterDeflection_NothingHappens()
        {
            var t = new RadialTracker();
            Assert.AreEqual(RadialEvent.None, t.Step(Dir(0f, 0.5f), 0f));
            Assert.AreEqual(-1, t.HighlightedSlot);
        }

        [Test]
        public void Deflect_HighlightsSlot()
        {
            var t = new RadialTracker();
            Assert.AreEqual(RadialEvent.SlotChanged, t.Step(Dir(60f, 0.9f), 0f));
            Assert.AreEqual(2, t.HighlightedSlot);
        }

        [Test]
        public void FastFlick_Confirms()
        {
            var t = new RadialTracker();
            t.Step(Dir(60f, 0.9f), 0f);
            Assert.AreEqual(RadialEvent.Confirmed, t.Step(Dir(60f, 0.1f), 0.2f));
            Assert.AreEqual(2, t.HighlightedSlot);   // the pick survives the release
        }

        [Test]
        public void SlowReturn_IsBrowse_NotConfirm()
        {
            var t = new RadialTracker();
            t.Step(Dir(60f, 0.9f), 0f);
            Assert.AreEqual(RadialEvent.None, t.Step(Dir(60f, 0.9f), 0.3f));
            Assert.AreEqual(RadialEvent.BrowseCancelled, t.Step(Dir(60f, 0.1f), 0.5f));
            Assert.AreEqual(-1, t.HighlightedSlot);
        }

        [Test]
        public void SlotChange_RestartsFlickWindow()
        {
            var t = new RadialTracker();
            t.Step(Dir(0f, 0.9f), 0f);
            // dwell on slot 0 past the window, then hop to slot 3 and release fast
            t.Step(Dir(0f, 0.9f), 0.5f);
            Assert.AreEqual(RadialEvent.SlotChanged, t.Step(Dir(90f, 0.9f), 0.6f));
            Assert.AreEqual(RadialEvent.Confirmed, t.Step(Dir(90f, 0.1f), 0.8f));
            Assert.AreEqual(3, t.HighlightedSlot);
        }

        [Test]
        public void HysteresisBand_KeepsDeflection()
        {
            var t = new RadialTracker();
            t.Step(Dir(0f, 0.9f), 0f);
            // 0.5 is between exit (0.45) and enter (0.55): still deflected, no event
            Assert.AreEqual(RadialEvent.None, t.Step(Dir(0f, 0.5f), 0.1f));
            Assert.AreEqual(0, t.HighlightedSlot);
            Assert.AreEqual(RadialEvent.Confirmed, t.Step(Dir(0f, 0.2f), 0.2f));
        }

        [Test]
        public void RayHighlight_AdoptedByFlickRelease()
        {
            var t = new RadialTracker();
            t.Step(Dir(0f, 0.9f), 0f);
            t.SetHighlight(7, 0.1f);                 // ray path stole the highlight
            Assert.AreEqual(RadialEvent.Confirmed, t.Step(Dir(0f, 0.1f), 0.2f));
            Assert.AreEqual(7, t.HighlightedSlot);   // confirms what the user saw
        }

        [Test]
        public void Reset_ClearsState()
        {
            var t = new RadialTracker();
            t.Step(Dir(0f, 0.9f), 0f);
            t.Reset();
            Assert.AreEqual(-1, t.HighlightedSlot);
            Assert.AreEqual(RadialEvent.None, t.Step(Dir(0f, 0.5f), 1f));
        }
    }
}
