using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tests
{
    public class BlueprintMathTests
    {
        private static BlueprintPlacement P(float scale, float rot, float ox, float oz) =>
            new BlueprintPlacement { Scale = scale, RotationDeg = rot, OriginX = ox, OriginZ = oz };

        [Test]
        public void WorldToPlanUV_ZeroRotation_MatchesLegacyFormula()
        {
            var p = P(2f, 0f, 1f, 3f);
            var uv = BlueprintMath.WorldToPlanUV(new Vector3(5f, 0f, 7f), p);
            Assert.AreEqual((5f - 1f) / 2f, uv.x, 1e-5f);
            Assert.AreEqual((7f - 3f) / 2f, uv.y, 1e-5f);
        }

        [Test]
        public void RoundTrip_UVToWorldToUV()
        {
            var p = P(3.5f, 37f, -2f, 4.2f);
            var uv0 = new Vector2(0.31f, -0.77f);
            var world = BlueprintMath.PlanUVToWorld(uv0, 1.5f, p);
            var uv1 = BlueprintMath.WorldToPlanUV(world, p);
            Assert.AreEqual(uv0.x, uv1.x, 1e-4f);
            Assert.AreEqual(uv0.y, uv1.y, 1e-4f);
            Assert.AreEqual(1.5f, world.y, 1e-5f, "height passes through");
        }

        [Test]
        public void PlanUVToWorld_Rotation90_TurnsUAxisTowardZ()
        {
            var p = P(2f, 90f, 1f, 3f);
            var w = BlueprintMath.PlanUVToWorld(new Vector2(1f, 0f), 0f, p);
            Assert.AreEqual(1f, w.x, 1e-4f, "u-axis maps to +Z after a 90° turn");
            Assert.AreEqual(5f, w.z, 1e-4f);
        }

        [Test]
        public void WorldToPlanUV_NearZeroScale_FallsBackToOne()
        {
            var p = P(0f, 0f, 0f, 0f);
            var uv = BlueprintMath.WorldToPlanUV(new Vector3(2f, 0f, 3f), p);
            Assert.AreEqual(2f, uv.x, 1e-5f);
            Assert.AreEqual(3f, uv.y, 1e-5f);
        }

        // ---- two-point calibration ----

        [Test]
        public void FromPointPairs_RecoversKnownPlacement()
        {
            var current = P(1f, 0f, 0f, 0f);
            var target = P(2f, 30f, 1f, -2f);
            var uvA = new Vector2(0.2f, 0.3f);
            var uvB = new Vector2(0.8f, 0.5f);

            // where those plan points sit NOW, and where they SHOULD be
            Vector3 fromA = BlueprintMath.PlanUVToWorld(uvA, 0f, current);
            Vector3 fromB = BlueprintMath.PlanUVToWorld(uvB, 0f, current);
            Vector3 toA = BlueprintMath.PlanUVToWorld(uvA, 0f, target);
            Vector3 toB = BlueprintMath.PlanUVToWorld(uvB, 0f, target);

            var solved = BlueprintMath.FromPointPairs(fromA, toA, fromB, toB, current);

            Assert.AreEqual(target.Scale, solved.Scale, 1e-4f);
            Assert.AreEqual(target.RotationDeg, Mathf.Repeat(solved.RotationDeg, 360f), 1e-3f);
            Assert.AreEqual(target.OriginX, solved.OriginX, 1e-4f);
            Assert.AreEqual(target.OriginZ, solved.OriginZ, 1e-4f);
        }

        [Test]
        public void FromPointPairs_BothPairsLandExactly()
        {
            var current = P(5f, 12f, 3f, -1f);
            Vector3 fromA = new Vector3(1f, 0f, 1f), toA = new Vector3(4f, 0f, 0f);
            Vector3 fromB = new Vector3(2f, 0f, 1.5f), toB = new Vector3(4f, 0f, 2f);

            var solved = BlueprintMath.FromPointPairs(fromA, toA, fromB, toB, current);

            // the plan features under fromA/fromB must now appear at toA/toB
            Vector2 uvA = BlueprintMath.WorldToPlanUV(fromA, current);
            Vector2 uvB = BlueprintMath.WorldToPlanUV(fromB, current);
            Vector3 a = BlueprintMath.PlanUVToWorld(uvA, 0f, solved);
            Vector3 b = BlueprintMath.PlanUVToWorld(uvB, 0f, solved);
            Assert.AreEqual(toA.x, a.x, 1e-4f); Assert.AreEqual(toA.z, a.z, 1e-4f);
            Assert.AreEqual(toB.x, b.x, 1e-4f); Assert.AreEqual(toB.z, b.z, 1e-4f);
        }

        [Test]
        public void FromPointPairs_DegenerateSegment_ReturnsCurrent()
        {
            var current = P(5f, 0f, 1f, 2f);
            var same = new Vector3(1f, 0f, 1f);
            var solved = BlueprintMath.FromPointPairs(same, same, same, same, current);
            Assert.AreEqual(current.Scale, solved.Scale);
            Assert.AreEqual(current.OriginX, solved.OriginX);
        }
    }
}
