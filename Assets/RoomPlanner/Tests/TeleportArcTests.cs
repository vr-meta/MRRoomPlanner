using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Core;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>Ballistic aim curve of the portal teleport (docs/design/21-locomotion.md).</summary>
    public class TeleportArcTests
    {
        private readonly List<Vector3> _pts = new();

        [Test]
        public void Sample_StartsAtOrigin_AndCountsMatch()
        {
            var origin = new Vector3(1f, 1.5f, -2f);
            int n = TeleportArc.Sample(origin, Vector3.forward, TeleportArc.Speed,
                TeleportArc.Gravity, TeleportArc.TimeStep, TeleportArc.MaxSamples, _pts);
            Assert.AreEqual(TeleportArc.MaxSamples, n);
            Assert.AreEqual(n, _pts.Count);
            Assert.AreEqual(origin, _pts[0]);
        }

        [Test]
        public void Sample_LevelAim_RisesNeverAndFallsMonotonically()
        {
            TeleportArc.Sample(Vector3.up * 1.5f, Vector3.forward, 7f, 9.81f, 0.05f, 40, _pts);
            for (int i = 1; i < _pts.Count; i++)
            {
                Assert.LessOrEqual(_pts[i].y, _pts[i - 1].y + 1e-5f, "a level shot only falls");
                Assert.Greater(_pts[i].z, _pts[i - 1].z, "and always advances forward");
            }
        }

        [Test]
        public void Sample_UpwardAim_HasAnApexThenDescends()
        {
            TeleportArc.Sample(Vector3.zero, new Vector3(0f, 1f, 1f).normalized, 7f, 9.81f, 0.05f, 40, _pts);
            int apex = 0;
            for (int i = 1; i < _pts.Count; i++)
                if (_pts[i].y > _pts[apex].y) apex = i;
            Assert.Greater(apex, 0, "the arc climbs first");
            Assert.Less(apex, _pts.Count - 1, "and comes back down within the sample budget");
            Assert.Less(_pts[_pts.Count - 1].y, 0f, "the tail dives below the launch height");
        }

        [Test]
        public void Sample_MatchesClosedFormPhysics()
        {
            TeleportArc.Sample(Vector3.zero, Vector3.forward, 7f, 9.81f, 0.05f, 40, _pts);
            // p(t) at sample 10: t = 0.5 s → z = 3.5 m, y = −9.81·0.25/2
            Assert.AreEqual(3.5f, _pts[10].z, 1e-4);
            Assert.AreEqual(-9.81f * 0.25f * 0.5f, _pts[10].y, 1e-4);
        }

        [Test]
        public void Sample_DegenerateInputs_ProduceNothing()
        {
            Assert.AreEqual(0, TeleportArc.Sample(Vector3.zero, Vector3.zero, 7f, 9.81f, 0.05f, 40, _pts));
            Assert.AreEqual(0, TeleportArc.Sample(Vector3.zero, Vector3.forward, 7f, 9.81f, 0f, 40, _pts));
            Assert.AreEqual(0, TeleportArc.Sample(Vector3.zero, Vector3.forward, 7f, 9.81f, 0.05f, 1, _pts));
        }
    }
}
