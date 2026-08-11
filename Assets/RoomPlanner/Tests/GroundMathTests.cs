using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Core;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Ground datum, support lookup and falling (design/25-ground.md, issue #59).
    /// </summary>
    public class GroundMathTests
    {
        private static List<Vector3> Square(float half, float x = 0f, float z = 0f) => new()
        {
            new Vector3(x - half, 0f, z - half),
            new Vector3(x + half, 0f, z - half),
            new Vector3(x + half, 0f, z + half),
            new Vector3(x - half, 0f, z + half),
        };

        // ---- support ----

        [Test]
        public void Support_FallsBackToTheGroundOutsideEverySlab()
        {
            var slabs = new List<WalkSlab> { new(0f, Square(2f)) };
            Assert.AreEqual(-1.5f, GroundMath.Support(slabs, null, -1.5f, 10f, 10f, 0f), 1e-5f);
        }

        [Test]
        public void Support_GroundIsInfinite_NoVoidPastThePlaneEdge()
        {
            // 500 m from anything — still ground, never a fall into nothing
            Assert.AreEqual(0f, GroundMath.Support(null, null, 0f, 500f, -500f, 0f), 1e-5f);
        }

        [Test]
        public void Support_PicksTheSlabTopWhenStandingOnIt()
        {
            var slabs = new List<WalkSlab> { new(3f, Square(2f)), new(0f, Square(2f)) };
            Assert.AreEqual(3f, GroundMath.Support(slabs, null, 0f, 0.5f, 0.5f, 3f), 1e-5f);
        }

        [Test]
        public void Support_IgnoresSlabsAboveTheStepUpLimit()
        {
            // standing at ground level under an upper storey: its slab is a ceiling, not a floor
            var slabs = new List<WalkSlab> { new(3f, Square(2f)) };
            Assert.AreEqual(0f, GroundMath.Support(slabs, null, 0f, 0f, 0f, 0f), 1e-5f);
        }

        [Test]
        public void Support_StepsOntoALedgeWithinTheStepUpLimit()
        {
            var slabs = new List<WalkSlab> { new(0.2f, Square(2f)) };
            Assert.AreEqual(0.2f, GroundMath.Support(slabs, null, 0f, 0f, 0f, 0f), 1e-5f);
        }

        [Test]
        public void Support_DropsThroughAStairwellHole()
        {
            var slabs = new List<WalkSlab>
            {
                new(3f, Square(5f), new List<IReadOnlyList<Vector3>> { Square(1f) }),
                new(0f, Square(5f)),
            };
            // inside the hole the upper slab does not carry you — the lower one does
            Assert.AreEqual(0f, GroundMath.Support(slabs, null, 0f, 0.2f, 0.2f, 3f), 1e-5f);
            Assert.AreEqual(3f, GroundMath.Support(slabs, null, 0f, 3f, 3f, 3f), 1e-5f);
        }

        [Test]
        public void Support_StandsStillWhenEvenTheGroundIsOverhead()
        {
            // the model's only slab is a mezzanine at 1.5 → the derived ground is 1.5, but
            // yanking the walker up to it would be worse than leaving them where they are
            var slabs = new List<WalkSlab> { new(1.5f, Square(2f)) };
            Assert.AreEqual(0f, GroundMath.Support(slabs, null, 1.5f, 0f, 0f, 0f), 1e-5f);
        }

        [Test]
        public void Support_TakesTheHighestOfOverlappingSlabs()
        {
            var slabs = new List<WalkSlab> { new(0f, Square(5f)), new(0.3f, Square(1f)) };
            Assert.AreEqual(0.3f, GroundMath.Support(slabs, null, 0f, 0f, 0f, 0.3f), 1e-5f);
        }

        // ---- stairs ----

        [Test]
        public void TreadTopY_RisesOneRiserPerTread()
        {
            // 3 risers × 0.18, tread 0.3 → run 0.6
            Assert.AreEqual(0.18f, GroundMath.TreadTopY(0.15f, 3, 0.18f, 0.3f), 1e-5f);
            Assert.AreEqual(0.36f, GroundMath.TreadTopY(0.45f, 3, 0.18f, 0.3f), 1e-5f);
            Assert.AreEqual(0.54f, GroundMath.TreadTopY(0.6f, 3, 0.18f, 0.3f), 1e-5f);
        }

        [Test]
        public void TreadTopY_ClampsBelowTheFirstAndAboveTheLastStep()
        {
            Assert.AreEqual(0.18f, GroundMath.TreadTopY(0f, 3, 0.18f, 0.3f), 1e-5f);
            Assert.AreEqual(0.54f, GroundMath.TreadTopY(99f, 3, 0.18f, 0.3f), 1e-5f);
        }

        [Test]
        public void TryFlightTop_UsesTheFlightsOwnFrame()
        {
            // yaw 90° → the run points along +X
            var f = new WalkFlight(new Vector3(1f, 0f, 0f), 90f, 1f, 10, 0.18f, 0.3f);
            Assert.IsTrue(GroundMath.TryFlightTop(f, 1.15f, 0f, out float y));
            Assert.AreEqual(0.18f, y, 1e-4f);            // 0.15 m along the run = first tread
            Assert.IsTrue(GroundMath.TryFlightTop(f, 1.45f, 0.4f, out _));   // within half width
            Assert.IsFalse(GroundMath.TryFlightTop(f, 1.45f, 0.9f, out _));  // past the side rail
            Assert.IsFalse(GroundMath.TryFlightTop(f, 0.5f, 0f, out _));     // behind the base
        }

        [Test]
        public void Support_WalksUpAFlightOneStepAtATime()
        {
            var flights = new List<WalkFlight> { new(Vector3.zero, 0f, 1f, 10, 0.18f, 0.3f) };
            Assert.AreEqual(0.18f, GroundMath.Support(null, flights, 0f, 0f, 0.1f, 0f), 1e-4f);
            Assert.AreEqual(0.36f, GroundMath.Support(null, flights, 0f, 0f, 0.4f, 0.18f), 1e-4f);
            // three treads up in one go is above the step-up limit — the ground stays
            Assert.AreEqual(0f, GroundMath.Support(null, flights, 0f, 0f, 2.5f, 0f), 1e-4f);
        }

        // ---- falling ----

        [Test]
        public void Fall_AcceleratesDownwardsAndLandsExactlyOnTheSupport()
        {
            float v = 0f, y = 3f;
            for (int i = 0; i < 200 && y > 0f; i++) y = GroundMath.Fall(y, 0f, ref v, 1f / 72f);
            Assert.AreEqual(0f, y, 1e-5f);
            Assert.AreEqual(0f, v, 1e-5f);       // contact zeroes the fall speed
        }

        [Test]
        public void Fall_FirstFrameIsSmall_NoTeleportDownwards()
        {
            float v = 0f;
            float y = GroundMath.Fall(3f, 0f, ref v, 1f / 72f);
            Assert.Less(3f - y, 0.01f);
            Assert.Greater(3f - y, 0f);
        }

        [Test]
        public void Fall_StepsUpInstantlyWithinTheLimit()
        {
            float v = 1f;
            Assert.AreEqual(0.2f, GroundMath.Fall(0f, 0.2f, ref v, 1f / 72f), 1e-5f);
            Assert.AreEqual(0f, v, 1e-5f);
        }

        [Test]
        public void Fall_RefusesToClimbTallerThanTheStepUpLimit()
        {
            float v = 0f;
            Assert.AreEqual(0f, GroundMath.Fall(0f, 3f, ref v, 1f / 72f), 1e-5f);
        }

        // ---- the visible plane ----

        [Test]
        public void PlaneLevel_SitsJustUnderTheDatum()
        {
            Assert.AreEqual(-3.02f, GroundMath.PlaneLevel(-3f, 0f, 0.02f), 1e-5f);
        }

        [Test]
        public void PlaneLevel_StaysUnderTheFeetWhenTheDatumIsOverhead()
        {
            Assert.AreEqual(-0.02f, GroundMath.PlaneLevel(1.5f, 0f, 0.02f), 1e-5f);
        }

        [Test]
        public void PlaneLevel_KeepsTheDatumWhileFalling()
        {
            // mid-fall the feet are ABOVE the ground — the plane must not follow them up
            Assert.AreEqual(-0.02f, GroundMath.PlaneLevel(0f, 2.4f, 0.02f), 1e-5f);
        }

        [Test]
        public void Fall_TerminalSpeedIsCapped()
        {
            float v = 0f, y = 1000f;
            for (int i = 0; i < 1000; i++) y = GroundMath.Fall(y, 0f, ref v, 1f / 72f);
            Assert.LessOrEqual(v, GroundMath.FallSpeedMax + 1e-4f);
        }
    }
}
