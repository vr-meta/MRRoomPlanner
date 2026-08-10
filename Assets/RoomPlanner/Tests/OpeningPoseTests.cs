using NUnit.Framework;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests
{
    /// <summary>Openable-leaf kinematics (issue #50, design/03 §«Открывающиеся»).</summary>
    public class OpeningPoseTests
    {
        [Test]
        public void DoorYaw_ScalesWithFraction_AndClamps()
        {
            Assert.AreEqual(0f, OpeningPose.DoorYawDeg(0f), 1e-4f);
            Assert.AreEqual(OpeningPose.MaxDoorYawDeg, OpeningPose.DoorYawDeg(1f), 1e-4f);
            Assert.AreEqual(75f, OpeningPose.DoorYawDeg(0.75f), 1e-4f,
                "imported IFC doors keep their historical 75° stance");
            Assert.AreEqual(OpeningPose.MaxDoorYawDeg, OpeningPose.DoorYawDeg(2f), 1e-4f, "clamped");
        }

        [Test]
        public void GaragePanels_Closed_AllVerticalStacked()
        {
            const float h = 2f;
            for (int i = 0; i < 4; i++)
            {
                OpeningPose.GaragePanel(h, 4, i, 0f, out float y, out float z, out float tilt);
                Assert.AreEqual(i * 0.5f, y, 1e-4f, $"panel {i} bottom");
                Assert.AreEqual(0f, z, 1e-4f);
                Assert.AreEqual(0f, tilt, 1e-4f, $"panel {i} vertical when closed");
            }
        }

        [Test]
        public void GaragePanels_Open_AllHorizontalUnderTheCeiling()
        {
            const float h = 2f;
            for (int i = 0; i < 4; i++)
            {
                OpeningPose.GaragePanel(h, 4, i, 1f, out float y, out float z, out float tilt);
                Assert.AreEqual(h, y, 1e-4f, "parked at the header height");
                Assert.AreEqual(i * 0.5f, z, 1e-4f, "stacked inward along the ceiling");
                Assert.AreEqual(90f, tilt, 1e-4f);
            }
        }

        [Test]
        public void GaragePanels_MidLift_TopPanelTiltsThroughTheBend()
        {
            const float h = 2f;
            // half lift: panel 3 bottom at 1.5 + 1.0 = 2.5 > h → horizontal
            OpeningPose.GaragePanel(h, 4, 3, 0.5f, out float y3, out float z3, out float t3);
            Assert.AreEqual(h, y3, 1e-4f);
            Assert.AreEqual(0.5f, z3, 1e-4f);
            Assert.AreEqual(90f, t3, 1e-4f);
            // panel 2 bottom at 1.0 + 1.0 = 2.0 → exactly at the bend, first horizontal
            OpeningPose.GaragePanel(h, 4, 2, 0.5f, out float y2, out float z2, out float t2);
            Assert.AreEqual(h, y2, 1e-4f);
            Assert.AreEqual(0f, z2, 1e-4f);
            Assert.AreEqual(90f, t2, 1e-4f);
            // panel 1 bottom at 0.5 + 1.0 = 1.5, top touching the bend → still vertical
            OpeningPose.GaragePanel(h, 4, 1, 0.5f, out float y1, out float z1, out float t1);
            Assert.AreEqual(1.5f, y1, 1e-4f);
            Assert.AreEqual(0f, z1, 1e-4f);
            Assert.AreEqual(0f, t1, 1e-4f, "top edge only touches the bend — no tilt yet");
            // panel 0 bottom at 1.0, top 1.5 < 2.0 → still vertical
            OpeningPose.GaragePanel(h, 4, 0, 0.5f, out float y0, out _, out float t0);
            Assert.AreEqual(1f, y0, 1e-4f);
            Assert.AreEqual(0f, t0, 1e-4f);
        }

        [Test]
        public void GaragePanels_TiltIsMonotonicWithLift()
        {
            const float h = 2f;
            float prev = -1f;
            for (float f = 0f; f <= 1.001f; f += 0.1f)
            {
                OpeningPose.GaragePanel(h, 4, 2, f, out _, out _, out float tilt);
                Assert.GreaterOrEqual(tilt, prev, $"tilt never regresses (f={f:0.0})");
                prev = tilt;
            }
        }
    }
}
