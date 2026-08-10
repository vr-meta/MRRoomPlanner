using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests
{
    /// <summary>Placement rules for the Openings tool (design/03, audit F1).</summary>
    public class OpeningMathTests
    {
        private static WallSegment Seg(float length = 4f, float height = 2.7f)
        {
            var g = new WallGraph();
            var s = g.AddSegment(
                g.SnapOrCreateNode(Vector3.zero),
                g.SnapOrCreateNode(new Vector3(length, 0f, 0f)));
            s.Height = height;
            return s;
        }

        private static void AddOpening(WallSegment s, float centerMeters, float width)
        {
            s.Openings.Add(new WallOpening
            {
                AlongFraction = centerMeters / s.Length,
                Width = width,
            });
        }

        [Test]
        public void CanPlace_CentredDoor_OnAnEmptyWall() =>
            Assert.IsTrue(OpeningMath.CanPlace(Seg(), 2f, 0.85f, 2.1f));

        [Test]
        public void CanPlace_RespectsThePiersToTheWallEnds()
        {
            var s = Seg();
            Assert.IsFalse(OpeningMath.CanPlace(s, 0.4f, 0.85f, 2.1f), "left pier < 5 cm");
            Assert.IsTrue(OpeningMath.CanPlace(s, 0.5f, 0.85f, 2.1f), "5 cm pier is enough");
        }

        [Test]
        public void CanPlace_RespectsTheHeader()
        {
            var s = Seg(height: 2.2f);
            Assert.IsFalse(OpeningMath.CanPlace(s, 2f, 0.85f, 2.2f), "no header left");
            Assert.IsTrue(OpeningMath.CanPlace(s, 2f, 0.85f, 2.1f));
        }

        [Test]
        public void CanPlace_RefusesOverlapWithAnExistingOpening()
        {
            var s = Seg();
            AddOpening(s, 2f, 1f);   // occupies 1.5..2.5
            Assert.IsFalse(OpeningMath.CanPlace(s, 2.9f, 0.85f, 2.1f), "pier to the window < 5 cm");
            Assert.IsTrue(OpeningMath.CanPlace(s, 3.1f, 0.85f, 2.1f), "clear of the window");
        }

        [Test]
        public void CanPlace_RefusesSilliness()
        {
            Assert.IsFalse(OpeningMath.CanPlace(null, 2f, 0.85f, 2.1f));
            Assert.IsFalse(OpeningMath.CanPlace(Seg(), 2f, 0.2f, 2.1f), "width < 30 cm");
            Assert.IsFalse(OpeningMath.CanPlace(Seg(length: 0.6f), 0.3f, 0.85f, 2.1f),
                "the wall is shorter than the opening plus piers");
        }

        [Test]
        public void CanPlace_IgnoresTheDraggedOpeningItself()
        {
            // Drag validation (#47 follow-up): an opening must not collide with itself.
            var s = Seg();
            AddOpening(s, 2f, 1f);
            var dragged = s.Openings[0];
            Assert.IsFalse(OpeningMath.CanPlace(s, 2.1f, 1f, 2.1f),
                "as a NEW opening the spot is blocked by the existing one");
            Assert.IsTrue(OpeningMath.CanPlace(s, 2.1f, 1f, 2.1f, ignore: dragged),
                "as the dragged opening itself the spot is free");
        }

        [Test]
        public void NearestOpening_PicksTheClosest_WithinReach()
        {
            var s = Seg();
            AddOpening(s, 1f, 0.8f);
            AddOpening(s, 3f, 0.8f);
            Assert.AreEqual(0, OpeningMath.NearestOpening(s, 1.2f, 0.25f));
            Assert.AreEqual(1, OpeningMath.NearestOpening(s, 2.9f, 0.25f));
            Assert.AreEqual(-1, OpeningMath.NearestOpening(s, 2f, 0.25f), "nothing within 25 cm");
        }
    }
}
