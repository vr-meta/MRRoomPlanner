using NUnit.Framework;
using RoomPlanner.Core;
using RoomPlanner.Tools;
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

        [Test]
        public void Hint_MatchesTheLastInputPath()
        {
            Assert.AreEqual("trigger to select", RadialMenu.SelectionHint(true, true));
            Assert.AreEqual("release to select", RadialMenu.SelectionHint(true, false));
            Assert.AreEqual("coming soon", RadialMenu.SelectionHint(true, true, reserved: true));
            Assert.AreEqual("walls only", RadialMenu.SelectionHint(true, true,
                disabled: true, disabledHint: "walls only"));
            Assert.AreEqual("flick to pick · B to cancel", RadialMenu.SelectionHint(false, false));
        }

        [Test]
        public void SelectionContext_KeepsActionsCardinal_AndExplainsUnsupportedOnes()
        {
            var definitions = ToolManager.CreateSelectionContextDefinitions(
                canDuplicate: false, canQuickMeasure: true, canOffset: false);

            Assert.AreEqual(RadialMath.Slots, definitions.Length);
            Assert.AreEqual((int)SelectionAction.Duplicate, definitions[0].ToolIndex);
            Assert.IsTrue(definitions[0].Disabled);
            Assert.AreEqual((int)SelectionAction.QuickMeasure, definitions[3].ToolIndex);
            Assert.IsTrue(definitions[3].Available);
            Assert.AreEqual((int)SelectionAction.OffsetWall, definitions[6].ToolIndex);
            Assert.AreEqual("walls only", definitions[6].DisabledHint);
            Assert.AreEqual((int)SelectionAction.Delete, definitions[9].ToolIndex);
            Assert.IsTrue(definitions[9].Available);
            Assert.IsTrue(definitions[1].Reserved, "unused sectors remain non-actions");
        }

        [Test]
        public void Reconfigure_SwapsAvailableIconAndReservedDot()
        {
            var radialGo = new GameObject("Radial");
            var headGo = new GameObject("Head");
            try
            {
                var radial = radialGo.AddComponent<RadialMenu>();
                radial.Configure(ToolManager.CreateRadialDefinitions(ToolManager.DefaultToolIndex));
                radial.Open(headGo.transform, ToolManager.DefaultToolIndex("select"));

                var icon = radialGo.transform.Find("Icon1");
                var dot = radialGo.transform.Find("Dot1");
                Assert.IsNotNull(icon);
                Assert.IsNotNull(dot);
                Assert.IsTrue(icon.gameObject.activeSelf, "Measure is an available tool slot");
                Assert.IsFalse(dot.gameObject.activeSelf);

                radial.Configure(ToolManager.CreateSelectionContextDefinitions(true, true, true));
                Assert.IsFalse(icon.gameObject.activeSelf,
                    "unused context sectors must not look like disabled plus buttons");
                Assert.IsTrue(dot.gameObject.activeSelf);

                radial.Configure(ToolManager.CreateRadialDefinitions(ToolManager.DefaultToolIndex));
                Assert.IsTrue(icon.gameObject.activeSelf);
                Assert.IsFalse(dot.gameObject.activeSelf);
            }
            finally
            {
                Object.DestroyImmediate(radialGo);
                Object.DestroyImmediate(headGo);
            }
        }

        [Test]
        public void ReticleSnapKinds_HaveDistinctSilhouettes()
        {
            Assert.AreEqual(16, ReticleVisual.Shape(ReticleSnapKind.None).Length);
            Assert.AreEqual(4, ReticleVisual.Shape(ReticleSnapKind.Corner).Length);
            Assert.AreEqual(2, ReticleVisual.Shape(ReticleSnapKind.Edge).Length);
            Assert.AreEqual(4, ReticleVisual.Shape(ReticleSnapKind.Grid).Length);
            Assert.AreEqual(3, ReticleVisual.Shape(ReticleSnapKind.Angle).Length);
            Assert.AreNotEqual(ReticleVisual.Shape(ReticleSnapKind.Corner)[0],
                ReticleVisual.Shape(ReticleSnapKind.Grid)[0]);
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
