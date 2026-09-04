using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tests
{
    public class ServicePlacementTests
    {
        private readonly Vector3[] _scratch = new Vector3[4];

        private static MountSurface Wall()
        {
            var s = new MountSurface { Kind = MountSurfaceKind.Wall, Origin = new Vector3(10f, 3f, 2f),
                U = Vector3.right, V = Vector3.up, Normal = Vector3.forward, BaseLevel = 3f };
            s.Boundary.AddRange(new[] { Vector3.zero, new Vector3(4, 0, 0), new Vector3(4, 0, 3), new Vector3(0, 0, 3) });
            return s;
        }

        [Test]
        public void WholeBlockMustFitAfterHeightAndGapAreApplied()
        {
            var s = Wall();
            Assert.AreEqual(PlacementFailure.None, ServicePlacement.Validate(s, s.Point(1, .3f), Quaternion.identity, new Vector2(.4f, .08f), _scratch));
            Assert.AreEqual(PlacementFailure.OutsideSurface, ServicePlacement.Validate(s, s.Point(.1f, .3f), Quaternion.identity, new Vector2(.4f, .08f), _scratch));
            Assert.AreEqual(PlacementFailure.OutsideSurface, ServicePlacement.Validate(s, s.Point(1, 3.1f), Quaternion.identity, new Vector2(.08f, .08f), _scratch));
        }

        [Test]
        public void TinyOpeningInsideBlockIsDetectedEvenWhenAllCornersHaveSupport()
        {
            var s = Wall();
            s.NextHole().AddRange(new[] { new Vector3(.99f, 0, .99f), new Vector3(1.01f, 0, .99f),
                new Vector3(1.01f, 0, 1.01f), new Vector3(.99f, 0, 1.01f) });
            Assert.AreEqual(PlacementFailure.Opening,
                ServicePlacement.Validate(s, s.Point(1, 1), Quaternion.identity, Vector2.one * .4f, _scratch));
        }

        [Test]
        public void ExactGapUsesBlockEdgeAndSelectedSideOnRotatedWall()
        {
            var s = Wall();
            var rotation = Quaternion.Euler(0, 37, 0);
            s.U = rotation * Vector3.right; s.Normal = rotation * Vector3.forward;
            var target = ServicePlacement.WithEdgeGap(s, s.Point(2, .9f), .4f, .15f, true);
            Assert.That(ServicePlacement.EdgeGap(s, target, .4f, true), Is.EqualTo(.15f).Within(1e-5));
            Assert.That(s.Project(target).z, Is.EqualTo(.9f).Within(1e-5));
            Assert.AreEqual(PlacementFailure.None, ServicePlacement.Validate(s, target, rotation, new Vector2(.4f, .08f), _scratch));
        }

        [Test]
        public void WideBlocksAboveEachOtherDoNotUseWidthAsVerticalClearance()
        {
            Assert.IsFalse(ServicePlacement.PlatesOverlap(Vector3.zero, Quaternion.identity, new Vector2(.4f, .08f),
                new Vector3(0, .14f, 0), Quaternion.identity, new Vector2(.4f, .08f), .05f));
            Assert.IsTrue(ServicePlacement.PlatesOverlap(Vector3.zero, Quaternion.identity, new Vector2(.4f, .08f),
                new Vector3(0, .1f, 0), Quaternion.identity, new Vector2(.4f, .08f), .05f));
            Assert.IsFalse(ServicePlacement.PlatesOverlap(Vector3.zero, Quaternion.identity, Vector2.one,
                Vector3.zero, Quaternion.Euler(0, 180, 0), Vector2.one, .05f));
        }

        [Test]
        public void ConcaveFloorDoesNotSupportABlockAcrossItsNotch()
        {
            var s = Wall(); s.Boundary.Clear();
            s.Boundary.AddRange(new[] { new Vector3(0,0,0), new Vector3(4,0,0), new Vector3(4,0,4),
                new Vector3(2.1f,0,4), new Vector3(2.1f,0,1), new Vector3(1.9f,0,1),
                new Vector3(1.9f,0,4), new Vector3(0,0,4) });
            Assert.AreEqual(PlacementFailure.OutsideSurface,
                ServicePlacement.Validate(s, s.Point(2,2), Quaternion.identity, new Vector2(2,2), _scratch));
        }

        [Test]
        public void InvalidInputsAndOffPlanePlacementAreRejected()
        {
            var s = Wall();
            Assert.AreEqual(PlacementFailure.InvalidSize,
                ServicePlacement.Validate(s, s.Point(1,1), Quaternion.identity, new Vector2(float.NaN,1), _scratch));
            Assert.AreEqual(PlacementFailure.OffSurface,
                ServicePlacement.Validate(s, s.Point(1,1) + Vector3.forward * .1f, Quaternion.identity, Vector2.one, _scratch));
        }

        [Test]
        public void PipeEnvelopeAndInnerSizeDoNotUseNominalMarking()
        {
            var p = new PipeDimensions { Nominal = "DN 15", OuterDiameter = .020f, WallThickness = .002f, InsulationThickness = .01f };
            Assert.IsTrue(p.IsValid);
            Assert.That(p.InnerDiameter, Is.EqualTo(.016f).Within(1e-6));
            Assert.That(p.EnvelopeDiameter, Is.EqualTo(.040f).Within(1e-6));
            Assert.That(p.BottomAt(1f), Is.EqualTo(.98f).Within(1e-6));
            Assert.That(p.GapTo(p, .1f), Is.EqualTo(.06f).Within(1e-6));
            p.WallThickness = .011f; Assert.IsFalse(p.IsValid);
        }

        [Test]
        public void DrainUsesCumulativeHorizontalLengthAndDoesNotMoveFixedPort()
        {
            var plan = new List<Vector3> { Vector3.zero, new Vector3(3,0,0), new Vector3(3,0,4) };
            var result = new List<Vector3>();
            Assert.IsTrue(DrainSlope.TryApply(plan, 1, 2, .86f, result, out _));
            Assert.That(result[1].y, Is.EqualTo(.94f).Within(1e-6));
            Assert.That(result[2].y, Is.EqualTo(.86f).Within(1e-6));
            var before = result.ToArray();
            Assert.IsFalse(DrainSlope.TryApply(plan, 1, 2, .9f, result, out var mismatch));
            Assert.That(mismatch, Is.EqualTo(-.04f).Within(1e-6));
            CollectionAssert.AreEqual(before, result, "failure must preserve the user's existing route");
            Assert.IsTrue(DrainSlope.TryApply(plan, 1, 2, null, plan, out _));
            Assert.That(plan[2].y, Is.EqualTo(.86f).Within(1e-6), "aliased input must survive");
        }
    }
}
