using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tests
{
    public class PaletteMathTests
    {
        [Test]
        public void SmoothFollow_InsideDeadZone_DoesNotMove()
        {
            var cur = Vector3.zero;
            var target = new Vector3(0.01f, 0f, 0f);   // 1 cm < 1.5 cm dead zone
            Assert.AreEqual(cur, PaletteMath.SmoothFollow(cur, target, 0.016f),
                "hand tremor inside the dead zone must not move the palette");
        }

        [Test]
        public void SmoothFollow_OutsideDeadZone_ConvergesTowardBoundary()
        {
            var cur = Vector3.zero;
            var target = new Vector3(0.5f, 0f, 0f);

            var p = cur;
            for (int i = 0; i < 300; i++) p = PaletteMath.SmoothFollow(p, target, 0.016f);

            // settles at the dead-zone boundary, not at the exact target
            Assert.AreEqual(0.5f - PaletteMath.DefaultDeadZone, p.x, 0.002f);
            Assert.Greater(p.x, 0.4f, "big moves are followed");
        }

        [Test]
        public void SmoothFollow_IsGradual_NotTeleport()
        {
            var p = PaletteMath.SmoothFollow(Vector3.zero, new Vector3(1f, 0f, 0f), 0.016f);
            Assert.Less(p.x, 0.2f, "one frame covers only a fraction of the distance");
            Assert.Greater(p.x, 0f);
        }

        [Test]
        public void GateVisible_Hysteresis_NoFlickerAtBoundary()
        {
            // visible stays visible until clearly turned away
            Assert.IsTrue(PaletteMath.GateVisible(true, 0.5f), "0.5 is between thresholds → keep shown");
            Assert.IsFalse(PaletteMath.GateVisible(true, 0.4f), "below 0.45 → hide");
            // hidden stays hidden until clearly facing
            Assert.IsFalse(PaletteMath.GateVisible(false, 0.5f), "0.5 is between thresholds → keep hidden");
            Assert.IsTrue(PaletteMath.GateVisible(false, 0.7f), "above 0.6 → show");
        }
    }
}
