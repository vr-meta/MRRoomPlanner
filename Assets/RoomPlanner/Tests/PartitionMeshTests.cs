using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core.Furniture;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Slat partitions (design/27 §3c, #86). Generated geometry gets the same scrutiny as
    /// walls and slabs: outward winding (rules 12 §1.1 — an inverted face moves the pick
    /// point), honest overall size, and safe degradation on nonsense input (§1.3).
    /// </summary>
    public class PartitionMeshTests
    {
        private static (List<Vector3> v, List<int> t) Build(
            float w = 1.2f, float h = 2.0f, float slat = 0.04f, float gap = 0.04f, float d = 0.04f)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            PartitionMesh.Build(w, h, slat, gap, d, v, t);
            return (v, t);
        }

        [Test]
        public void SlatCount_FollowsWidthAndPitch()
        {
            // 1.2 m at a 0.08 m pitch: 15 slats leave 14 gaps and fit exactly.
            Assert.AreEqual(15, PartitionMesh.SlatCount(1.2f, 0.04f, 0.04f));
            Assert.AreEqual(1, PartitionMesh.SlatCount(0.04f, 0.04f, 0.04f), "one slat is the floor");
            Assert.AreEqual(1, PartitionMesh.SlatCount(0.5f, 0.04f, 5f), "a gap wider than the screen");
        }

        [Test]
        public void Build_ProducesOneBoxPerSlat()
        {
            var (v, t) = Build();
            int slats = PartitionMesh.SlatCount(1.2f, 0.04f, 0.04f);
            Assert.AreEqual(slats * 24, v.Count, "6 faces × 4 verts per slat");
            Assert.AreEqual(slats * 36, t.Count, "6 faces × 2 triangles × 3 indices");
        }

        [Test]
        public void Build_MeasuresWhatTheInspectorSays()
        {
            var (v, _) = Build(1.2f, 2.0f, 0.04f, 0.04f, 0.05f);
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var p in v) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }

            Assert.AreEqual(1.2f, max.x - min.x, 1e-4f, "outermost slats touch the edges");
            Assert.AreEqual(2.0f, max.y - min.y, 1e-4f);
            Assert.AreEqual(0.05f, max.z - min.z, 1e-4f);
            Assert.AreEqual(0f, min.y, 1e-4f, "it stands ON its origin, like every placed piece");
        }

        [Test]
        public void Build_EveryTriangleFacesOutwards()
        {
            // Judge each triangle against the centre of ITS OWN slat: the screen's centre
            // says nothing useful about a 4 cm batten two metres to the left.
            const float w = 1.2f, h = 2.0f, s = 0.04f, g = 0.04f;
            var (v, t) = Build(w, h, s, g, 0.05f);
            int slats = PartitionMesh.SlatCount(w, s, g);
            float span = slats > 1 ? (w - s) / (slats - 1) : 0f;
            float x0 = slats > 1 ? -w * 0.5f + s * 0.5f : 0f;

            for (int i = 0; i < t.Count; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                var centroid = (a + b + c) / 3f;

                int slat = slats > 1 ? Mathf.Clamp(Mathf.RoundToInt((centroid.x - x0) / span), 0, slats - 1) : 0;
                var centre = new Vector3(x0 + slat * span, h * 0.5f, 0f);

                var normal = Vector3.Cross(b - a, c - a);
                Assert.Greater(Vector3.Dot(normal, centroid - centre), 0f,
                    $"triangle {i / 3} winds inwards");
            }
        }

        [Test]
        public void Build_DegenerateInput_StillYieldsGeometry()
        {
            foreach (var (w, h, s, g, d) in new[]
            {
                (0f, 2f, 0.04f, 0.04f, 0.04f),
                (1.2f, 0f, 0.04f, 0.04f, 0.04f),
                (1.2f, 2f, -0.04f, -0.04f, -0.04f),
                (1.2f, 2f, 5f, 0.04f, 0.04f),     // slat wider than the screen
            })
            {
                var v = new List<Vector3>();
                var t = new List<int>();
                PartitionMesh.Build(w, h, s, g, d, v, t);
                Assert.Greater(v.Count, 0, $"({w},{h},{s},{g},{d}) produced nothing");
                Assert.AreEqual(0, t.Count % 3);
                foreach (var p in v)
                    Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z));
            }
        }

        [Test]
        public void Build_NormalsMatchFaces()
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            var n = new List<Vector3>();
            PartitionMesh.Build(0.6f, 1.2f, 0.05f, 0.05f, 0.04f, v, t, n);

            Assert.AreEqual(v.Count, n.Count, "one normal per vertex");
            for (int i = 0; i < t.Count; i += 3)
            {
                var geo = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).normalized;
                Assert.Greater(Vector3.Dot(geo, n[t[i]]), 0.9f, "supplied normal disagrees with winding");
            }
        }
    }
}
