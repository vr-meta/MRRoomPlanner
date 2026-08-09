using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests
{
    public class WallGeometryTests
    {
        // interior on the -Z side, so "outward" resolves to +Z for an X-aligned segment.
        private static readonly Vector3 A = Vector3.zero;
        private static readonly Vector3 B = new Vector3(1f, 0f, 0f);
        private static readonly Vector3 Interior = new Vector3(0.5f, 0f, -1f);

        [Test]
        public void SegmentVertices_ReturnsEightVerts()
        {
            var v = Wall.SegmentVertices(A, B, 0.2f, 2.7f, WallOffsetMode.Outer, Interior);
            Assert.AreEqual(8, v.Length);
        }

        [Test]
        public void OuterMode_LineIsInnerFace_ThicknessGrowsAwayFromInterior()
        {
            const float thick = 0.2f;
            var v = Wall.SegmentVertices(A, B, thick, 2.7f, WallOffsetMode.Outer, Interior);
            // v[0] = inner at A (on the line), v[1] = outer at A
            Assert.AreEqual(0f, v[0].z, 1e-5f, "inner face sits on the drawn line");
            Assert.AreEqual(thick, v[1].z, 1e-5f, "outer face is one thickness away, toward +Z (away from interior)");
        }

        [Test]
        public void Height_ExtrudesTopByHeight()
        {
            const float h = 2.7f;
            var v = Wall.SegmentVertices(A, B, 0.2f, h, WallOffsetMode.Outer, Interior);
            for (int i = 0; i < 4; i++)
                Assert.AreEqual(v[i].y + h, v[i + 4].y, 1e-5f, "top vertex is height above its bottom vertex");
        }

        [Test]
        public void CenterMode_IsSymmetricAboutLine()
        {
            const float thick = 0.2f;
            var v = Wall.SegmentVertices(A, B, thick, 2.7f, WallOffsetMode.Center, Interior);
            Assert.AreEqual(-thick * 0.5f, v[0].z, 1e-5f);
            Assert.AreEqual(thick * 0.5f, v[1].z, 1e-5f);
        }

        [Test]
        public void Build_StraightWall_HasTwoCrossSections()
        {
            var go = new GameObject("WallTest");
            try
            {
                var wall = go.AddComponent<Wall>();
                var pts = new List<Vector3> { A, B }; // straight, no corners
                wall.Build(pts, 0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, Interior);

                var mf = go.GetComponent<MeshFilter>();
                Assert.IsNotNull(mf.sharedMesh);
                Assert.AreEqual(8, mf.sharedMesh.vertexCount); // 2 cross-sections * 4 verts
                Assert.AreEqual(2, wall.Points.Count);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Build_MiterCorner_HasThreeCrossSections()
        {
            var go = new GameObject("WallTest");
            try
            {
                var wall = go.AddComponent<Wall>();
                var pts = new List<Vector3> { A, B, new Vector3(1f, 0f, 1f) }; // right angle at B
                wall.Build(pts, 0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, Interior);

                var mf = go.GetComponent<MeshFilter>();
                Assert.IsNotNull(mf.sharedMesh);
                Assert.AreEqual(12, mf.sharedMesh.vertexCount); // 3 cross-sections * 4 (miter = 1 per corner)
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Build_RoundCorner_AddsMoreVertsThanMiter()
        {
            var go = new GameObject("WallTest");
            try
            {
                var wall = go.AddComponent<Wall>();
                var pts = new List<Vector3> { A, B, new Vector3(1f, 0f, 1f) };
                wall.Build(pts, 0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Round, Interior);
                Assert.Greater(go.GetComponent<MeshFilter>().sharedMesh.vertexCount, 12);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
