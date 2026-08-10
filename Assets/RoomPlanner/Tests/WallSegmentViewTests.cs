using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Phase B / step B3a — Wall.BuildSegment: one Wall view drawing ONE graph segment
    /// (docs/design/13-phase-b-wallgraph.md). The polyline Build stays for the legacy path.
    ///
    /// Winding is checked for BOTH side signs: mirroring the footprint flips triangle
    /// orientation, and an inward-facing face makes MeshCollider picks land one thickness
    /// off (coding rule 1.1 / audit WP2).
    /// </summary>
    public class WallSegmentViewTests
    {
        private const float Thick = 0.2f;
        private const float Height = 2.7f;

        private static Vector3 P(float x, float z) => new Vector3(x, 0f, z);

        private static WallSegment Seg(WallGraph g, Vector3 a, Vector3 b, float sideSign)
        {
            var s = g.AddSegment(g.SnapOrCreateNode(a), g.SnapOrCreateNode(b));
            s.Thickness = Thick;
            s.Height = Height;
            s.Offset = WallOffsetMode.Outer;
            s.SideSign = sideSign;
            return s;
        }

        private static Vector3 TriNormal(Vector3 a, Vector3 b, Vector3 c)
            => Vector3.Cross(b - a, c - a).normalized;

        private static void AssertFaceOutward(Mesh mesh, System.Predicate<Vector3> onFace,
            Vector3 expected, string label)
        {
            var v = mesh.vertices;
            var t = mesh.triangles;
            int found = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                if (!onFace(a) || !onFace(b) || !onFace(c)) continue;
                found++;
                Assert.Greater(Vector3.Dot(TriNormal(a, b, c), expected), 0.9f,
                    $"{label}: triangle normal must point {expected}");
            }
            Assert.Greater(found, 0, $"{label}: no triangles found on that face");
        }

        // ---- shape ----

        [Test]
        public void BuildSegment_MakesAnExtrudedSlab()
        {
            var g = new WallGraph();
            var s = Seg(g, P(0, 0), P(4, 0), +1f);
            var go = new GameObject("SegView");
            try
            {
                var wall = go.AddComponent<Wall>();
                wall.BuildSegment(s);

                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                // Texture unwrap (2026-08-11): sides, caps, top and bottom carry dedicated
                // vertices for their own metric UVs.
                Assert.AreEqual(24, mesh.vertexCount,
                    "2 cross-sections * (4 ring + 4 top/bottom) + 8 cap verts");
                Assert.AreSame(s, wall.Segment);
                Assert.AreEqual(2, wall.Points.Count, "centerline stays inspectable");
                Assert.AreEqual(Height, mesh.bounds.size.y, 1e-3f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void BuildSegment_AssignsTheCollider()
        {
            var g = new WallGraph();
            var s = Seg(g, P(0, 0), P(4, 0), +1f);
            var go = new GameObject("SegView");
            try
            {
                var wall = go.AddComponent<Wall>();
                wall.BuildSegment(s);
                var col = go.GetComponent<MeshCollider>();
                Assert.IsNotNull(col, "the view must be pickable by the Select tool");
                Assert.IsNotNull(col.sharedMesh);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void BuildSegment_BaseHeight_LiftsTheWall()
        {
            var g = new WallGraph();
            var s = Seg(g, P(0, 0), P(4, 0), +1f);
            s.BaseHeight = 0.5f;
            var go = new GameObject("SegView");
            try
            {
                var wall = go.AddComponent<Wall>();
                wall.BuildSegment(s);
                var b = go.GetComponent<MeshFilter>().sharedMesh.bounds;
                Assert.AreEqual(0.5f, b.min.y, 1e-3f, "the wall starts at its base height");
                Assert.AreEqual(0.5f + Height, b.max.y, 1e-3f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void BuildSegment_DegenerateSegment_ClearsMeshAndCollider()
        {
            var g = new WallGraph();
            var a = g.SnapOrCreateNode(P(0, 0));
            var b = g.SnapOrCreateNode(P(4, 0));
            var s = g.AddSegment(a, b);
            g.MoveNode(b, a.Position);           // collapsed to nothing

            var go = new GameObject("SegView");
            try
            {
                var wall = go.AddComponent<Wall>();
                wall.BuildSegment(s);
                Assert.AreEqual(0, go.GetComponent<MeshFilter>().sharedMesh.vertexCount);
                Assert.IsNull(go.GetComponent<MeshCollider>().sharedMesh,
                    "a collapsed wall must not leave a stale collider behind");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---- winding, both sides ----

        [Test]
        public void BuildSegment_FacesPointOutward_SidePlus()
        {
            var g = new WallGraph();
            var s = Seg(g, P(0, 0), P(4, 0), +1f);   // body grows toward -Z (the right normal)
            var go = new GameObject("SegView");
            try
            {
                var wall = go.AddComponent<Wall>();
                wall.BuildSegment(s);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;

                AssertFaceOutward(mesh, p => Mathf.Abs(p.z - 0f) < 1e-3f, Vector3.forward, "z=0 face");
                AssertFaceOutward(mesh, p => Mathf.Abs(p.z + Thick) < 1e-3f, Vector3.back, "z=-0.2 face");
                AssertFaceOutward(mesh, p => Mathf.Abs(p.y - Height) < 1e-3f, Vector3.up, "top");
                AssertFaceOutward(mesh, p => Mathf.Abs(p.y) < 1e-3f, Vector3.down, "bottom");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void BuildSegment_FacesPointOutward_SideMinus()
        {
            var g = new WallGraph();
            var s = Seg(g, P(0, 0), P(4, 0), -1f);   // mirrored: body grows toward +Z
            var go = new GameObject("SegView");
            try
            {
                var wall = go.AddComponent<Wall>();
                wall.BuildSegment(s);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;

                AssertFaceOutward(mesh, p => Mathf.Abs(p.z - 0f) < 1e-3f, Vector3.back, "z=0 face");
                AssertFaceOutward(mesh, p => Mathf.Abs(p.z - Thick) < 1e-3f, Vector3.forward, "z=+0.2 face");
                AssertFaceOutward(mesh, p => Mathf.Abs(p.y - Height) < 1e-3f, Vector3.up, "top");
                AssertFaceOutward(mesh, p => Mathf.Abs(p.y) < 1e-3f, Vector3.down, "bottom");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---- the view agrees with the pure footprint ----

        [Test]
        public void BuildSegment_UsesTheGraphJoint_AtACorner()
        {
            var g = new WallGraph();
            var ab = Seg(g, P(0, 0), P(2, 0), -1f);
            Seg(g, P(2, 0), P(2, 2), -1f);           // neighbour makes it a corner

            var go = new GameObject("SegView");
            try
            {
                var wall = go.AddComponent<Wall>();
                wall.BuildSegment(ab);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                var f = WallMesh.BuildFootprint(ab);

                // every ground vertex of the view must be one of the footprint corners
                foreach (var v in mesh.vertices)
                {
                    if (Mathf.Abs(v.y) > 1e-3f) continue;   // skip the extruded top
                    bool matches =
                        (v - f.ARight).sqrMagnitude < 1e-6f || (v - f.ALeft).sqrMagnitude < 1e-6f ||
                        (v - f.BRight).sqrMagnitude < 1e-6f || (v - f.BLeft).sqrMagnitude < 1e-6f;
                    Assert.IsTrue(matches, $"ground vertex {v} is not a footprint corner — the view drifted from WallMesh");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---- moving a graph-backed wall ----

        [Test]
        public void MoveBy_MovesTheNodes_SoNeighboursFollow()
        {
            var g = new WallGraph();
            var ab = Seg(g, P(0, 0), P(2, 0), +1f);
            var bc = Seg(g, P(2, 0), P(2, 2), +1f);   // shares the corner node with ab
            var corner = ab.B;

            var go = new GameObject("SegView");
            try
            {
                var wall = go.AddComponent<Wall>();
                wall.BuildSegment(ab);

                WallSegment notified = null;
                wall.GeometryChanged = s => notified = s;
                wall.MoveBy(new Vector3(1f, 0f, 0f));

                Assert.AreEqual(P(1, 0), ab.A.Position, "node A moved");
                Assert.AreEqual(P(3, 0), corner.Position, "the shared node moved too");
                Assert.AreSame(corner, bc.A, "the neighbour still hangs off that same node");
                Assert.AreSame(ab, notified, "the owner is told to rebuild the neighbours");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
