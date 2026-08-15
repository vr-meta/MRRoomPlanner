using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Core;
using UnityEngine;

namespace RoomPlanner.Tests
{
    public class PlacementGuidesTests
    {
        private readonly WallGuide[] _guides = new WallGuide[2];

        // an L-corner: one wall along X at z=0, one along Z at x=0
        private static List<Vector3> Corner() => new()
        {
            new Vector3(0f, 0f, 0f), new Vector3(5f, 0f, 0f),
            new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 5f),
        };

        [Test]
        public void CornerPoint_GetsTwoPerpendicularGuides()
        {
            int n = PlacementGuides.FindGuides(new Vector3(0.4f, 1.2f, 0.7f), Corner(), _guides);
            Assert.AreEqual(2, n);
            // nearest wall is the X-axis one (0.7 away... actually 0.7 vs 0.4 — the Z wall at 0.4 wins)
            Assert.AreEqual(0.4f, _guides[0].Distance, 1e-4, "nearest wall first");
            Assert.AreEqual(0.7f, _guides[1].Distance, 1e-4, "the perpendicular partner second");
            Assert.AreEqual(1.2f, _guides[0].Closest.y, 1e-4, "guide drawn at the element height");
        }

        [Test]
        public void ParallelWalls_YieldOnlyOneGuide()
        {
            var walls = new List<Vector3>
            {
                new(0f, 0f, 0f), new(5f, 0f, 0f),
                new(0f, 0f, 1f), new(5f, 0f, 1f),
            };
            int n = PlacementGuides.FindGuides(new Vector3(2f, 0f, 0.3f), walls, _guides);
            Assert.AreEqual(1, n, "a parallel twin adds no second dimension");
            Assert.AreEqual(0.3f, _guides[0].Distance, 1e-4);
        }

        [Test]
        public void FarWalls_GiveNoGuides()
        {
            int n = PlacementGuides.FindGuides(
                new Vector3(50f, 0f, 50f), Corner(), _guides);
            Assert.AreEqual(0, n);
            Assert.IsFalse(_guides[0].Valid);
        }

        [Test]
        public void Quantize_SnapsBothCornerDistancesToTheStep()
        {
            var p = new Vector3(0.43f, 1f, 0.68f);
            int n = PlacementGuides.FindGuides(p, Corner(), _guides);
            var q = PlacementGuides.Quantize(p, _guides, n, 0.05f);
            Assert.AreEqual(0.45f, q.x, 1e-4, "distance to the Z wall lands on 5 cm");
            Assert.AreEqual(0.70f, q.z, 1e-4, "distance to the X wall lands on 5 cm");
            Assert.AreEqual(1f, q.y, 1e-4, "height untouched");
        }

        [Test]
        public void Quantize_NeverPullsIntoTheWall()
        {
            var p = new Vector3(0.012f, 0f, 2f);   // 1.2 cm from the Z wall — rounds to 0
            int n = PlacementGuides.FindGuides(p, Corner(), _guides);
            var q = PlacementGuides.Quantize(p, _guides, n, 0.05f);
            Assert.AreEqual(0.05f, q.x, 1e-4, "clamps to one step instead of zero");
        }

        [Test]
        public void DegenerateAndNullInputs_AreSafe()
        {
            Assert.AreEqual(0, PlacementGuides.FindGuides(Vector3.zero, null, _guides));
            var degenerate = new List<Vector3> { Vector3.one, Vector3.one };
            Assert.AreEqual(0, PlacementGuides.FindGuides(Vector3.zero, degenerate, _guides));
            var p = new Vector3(1f, 0f, 1f);
            Assert.AreEqual(p, PlacementGuides.Quantize(p, null, 0, 0.05f));
        }
    }
}
