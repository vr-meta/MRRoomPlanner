using NUnit.Framework;
using RoomPlanner.Core;
using UnityEngine;

namespace RoomPlanner.Tests
{
    public class BuildingNavTests
    {
        [Test]
        public void TeleportDelta_BringsAimedPointToHeadXZ()
        {
            var d = BuildingNav.TeleportDelta(new Vector3(10f, 0f, 4f), new Vector3(1f, 1.7f, 2f));
            Assert.AreEqual(new Vector3(-9f, 0f, -2f), d);
        }

        [Test]
        public void TeleportDelta_BringsUpperStoreyDownToTheRealFloor()
        {
            // aiming at an L2 slab (top at 3.15 m) must lower the model by 3.15
            var d = BuildingNav.TeleportDelta(new Vector3(5f, 3.15f, 5f), new Vector3(5f, 1.7f, 5f));
            Assert.AreEqual(0f, d.x, 1e-5f);
            Assert.AreEqual(-3.15f, d.y, 1e-5f);
            Assert.AreEqual(0f, d.z, 1e-5f);
        }

        [Test]
        public void TeleportDelta_RespectsCustomFloorLevel()
        {
            var d = BuildingNav.TeleportDelta(new Vector3(0f, 3f, 0f), new Vector3(0f, 1.7f, 0f), floorY: 0.4f);
            Assert.AreEqual(-2.6f, d.y, 1e-5f);
        }
    }
}
