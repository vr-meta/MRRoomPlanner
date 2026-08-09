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
            // Use Center mode so BOTH faces are offset: the convex face of the corner then has
            // a non-zero radius and the Round arc actually inserts vertices. (In Outer mode the
            // convex face can fall on the zero-offset drawn line, where an arc collapses to a
            // point and Round == Miter — that's correct geometry, just not what this test checks.)
            var go = new GameObject("WallTest");
            try
            {
                var wall = go.AddComponent<Wall>();
                var pts = new List<Vector3> { A, B, new Vector3(1f, 0f, 1f) };
                wall.Build(pts, 0.2f, 2.7f, WallOffsetMode.Center, WallJoin.Round, Interior);
                Assert.Greater(go.GetComponent<MeshFilter>().sharedMesh.vertexCount, 12);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void MoveBy_ShiftsEveryCenterlinePoint()
        {
            var go = new GameObject("WallTest");
            try
            {
                var wall = go.AddComponent<Wall>();
                var pts = new List<Vector3> { A, B, new Vector3(1f, 0f, 1f) };
                wall.Build(pts, 0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, Interior);
                var before = new List<Vector3>(wall.Points);

                var delta = new Vector3(2f, 0f, -3f);
                wall.MoveBy(delta);

                Assert.AreEqual(before.Count, wall.Points.Count);
                for (int i = 0; i < before.Count; i++)
                {
                    Assert.AreEqual(before[i].x + delta.x, wall.Points[i].x, 1e-4f);
                    Assert.AreEqual(before[i].z + delta.z, wall.Points[i].z, 1e-4f);
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void MoveBy_TranslatesMeshBounds_AndKeepsVertexCount()
        {
            var go = new GameObject("WallTest");
            try
            {
                var wall = go.AddComponent<Wall>();
                wall.Build(new List<Vector3> { A, B }, 0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, Interior);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                int count = mesh.vertexCount;
                Vector3 centerBefore = mesh.bounds.center;

                var delta = new Vector3(1.5f, 0f, 0.5f);
                wall.MoveBy(delta);

                Assert.AreEqual(count, mesh.vertexCount, "move must not change topology");
                Assert.AreEqual(centerBefore.x + delta.x, mesh.bounds.center.x, 1e-3f);
                Assert.AreEqual(centerBefore.z + delta.z, mesh.bounds.center.z, 1e-3f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void MoveBy_ThenInverse_RestoresPoints()
        {
            // This is the round-trip MoveCommand.Undo relies on: MoveBy(d) then MoveBy(-d).
            var go = new GameObject("WallTest");
            try
            {
                var wall = go.AddComponent<Wall>();
                var pts = new List<Vector3> { A, B, new Vector3(1f, 0f, 1f) };
                wall.Build(pts, 0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, Interior);
                var before = new List<Vector3>(wall.Points);

                var delta = new Vector3(2.5f, 0f, -1.25f);
                wall.MoveBy(delta);
                wall.MoveBy(-delta);

                for (int i = 0; i < before.Count; i++)
                {
                    Assert.AreEqual(before[i].x, wall.Points[i].x, 1e-4f);
                    Assert.AreEqual(before[i].z, wall.Points[i].z, 1e-4f);
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---- triangle orientation: every face must point OUTWARD (MeshCollider raycasts
        // only hit front faces; a flipped face makes picks land one thickness off) ----

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

        private static Mesh BuildWallMesh(Vector3 interior, out GameObject go)
        {
            go = new GameObject("WallTest");
            var wall = go.AddComponent<Wall>();
            wall.Build(new List<Vector3> { A, B }, 0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, interior);
            return go.GetComponent<MeshFilter>().sharedMesh;
        }

        [Test]
        public void Build_FacesPointOutward_InteriorOnMinusZ()
        {
            // interior at -Z → wall solid occupies z ∈ [0, 0.2]
            var mesh = BuildWallMesh(new Vector3(0.5f, 0f, -1f), out var go);
            try
            {
                AssertFaceOutward(mesh, p => Mathf.Abs(p.y) < 1e-4f, Vector3.down, "bottom");
                AssertFaceOutward(mesh, p => Mathf.Abs(p.y - 2.7f) < 1e-4f, Vector3.up, "top");
                AssertFaceOutward(mesh, p => Mathf.Abs(p.z) < 1e-4f, Vector3.back, "inner face (toward interior)");
                AssertFaceOutward(mesh, p => Mathf.Abs(p.z - 0.2f) < 1e-4f, Vector3.forward, "outer face");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Build_FacesPointOutward_InteriorOnPlusZ()
        {
            // interior at +Z (opposite side) → wall solid occupies z ∈ [-0.2, 0]
            var mesh = BuildWallMesh(new Vector3(0.5f, 0f, 1f), out var go);
            try
            {
                AssertFaceOutward(mesh, p => Mathf.Abs(p.y) < 1e-4f, Vector3.down, "bottom");
                AssertFaceOutward(mesh, p => Mathf.Abs(p.y - 2.7f) < 1e-4f, Vector3.up, "top");
                AssertFaceOutward(mesh, p => Mathf.Abs(p.z) < 1e-4f, Vector3.forward, "inner face (toward interior)");
                AssertFaceOutward(mesh, p => Mathf.Abs(p.z + 0.2f) < 1e-4f, Vector3.back, "outer face");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---- robustness against messy MR input ----

        [Test]
        public void Build_MiterSpike_IsCappedAtSharpReversal()
        {
            var go = new GameObject("WallTest");
            try
            {
                const float thick = 0.2f;
                // ~170° turn at B — an uncapped miter would spike ~2.3 m out of the corner
                var c = new Vector3(2f, 0f, 0f) + new Vector3(Mathf.Cos(170f * Mathf.Deg2Rad), 0f, Mathf.Sin(170f * Mathf.Deg2Rad));
                var wall = go.AddComponent<Wall>();
                wall.Build(new List<Vector3> { A, new Vector3(2f, 0f, 0f), c },
                    thick, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, new Vector3(1f, 0f, -1f));

                var b = go.GetComponent<MeshFilter>().sharedMesh.bounds;
                // centerline fits in x[0..2], z[-0.2..0.2]; allow thickness * miter-limit margin
                float margin = thick * 4f + 1e-3f;
                Assert.LessOrEqual(b.max.x, 2f + margin, "miter spike must be capped (+X)");
                Assert.GreaterOrEqual(b.min.x, 0f - margin, "miter spike must be capped (−X)");
                Assert.LessOrEqual(b.max.z, 0.2f + margin, "miter spike must be capped (+Z)");
                Assert.GreaterOrEqual(b.min.z, -0.2f - margin, "miter spike must be capped (−Z)");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Build_DuplicateConsecutivePoints_AreCollapsed()
        {
            var go = new GameObject("WallTest");
            try
            {
                var wall = go.AddComponent<Wall>();
                wall.Build(new List<Vector3> { A, A, B, B },   // MR double-clicks
                    0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, Interior);

                Assert.AreEqual(2, wall.Points.Count, "duplicates collapse");
                Assert.AreEqual(8, go.GetComponent<MeshFilter>().sharedMesh.vertexCount,
                    "geometry equals the clean two-point wall");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Build_FullReversal_ProducesFiniteGeometry()
        {
            var go = new GameObject("WallTest");
            try
            {
                var wall = go.AddComponent<Wall>();
                wall.Build(new List<Vector3> { A, B, A },   // user walked straight back
                    0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, Interior);

                foreach (var v in go.GetComponent<MeshFilter>().sharedMesh.vertices)
                {
                    Assert.IsFalse(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z), "no NaN vertices");
                    Assert.LessOrEqual(v.magnitude, 5f, "vertices stay near the centerline");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Build_NegativeThicknessAndHeight_BehaveAsAbsolute()
        {
            var goA = new GameObject("WallA");
            var goB = new GameObject("WallB");
            try
            {
                goA.AddComponent<Wall>().Build(new List<Vector3> { A, B }, 0.2f, 2.7f,
                    WallOffsetMode.Outer, WallJoin.Miter, Interior);
                goB.AddComponent<Wall>().Build(new List<Vector3> { A, B }, -0.2f, -2.7f,
                    WallOffsetMode.Outer, WallJoin.Miter, Interior);

                var ba = goA.GetComponent<MeshFilter>().sharedMesh.bounds;
                var bb = goB.GetComponent<MeshFilter>().sharedMesh.bounds;
                Assert.AreEqual(ba.min, bb.min, "negative params must not mirror the wall");
                Assert.AreEqual(ba.max, bb.max);
            }
            finally { Object.DestroyImmediate(goA); Object.DestroyImmediate(goB); }
        }

        [Test]
        public void Build_BevelCorner_AddsOneExtraSection()
        {
            var go = new GameObject("WallTest");
            try
            {
                var wall = go.AddComponent<Wall>();
                wall.Build(new List<Vector3> { A, B, new Vector3(1f, 0f, 1f) },
                    0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Bevel, Interior);
                // miter corner = 3 sections (12 verts); bevel replaces the corner section with 2
                Assert.AreEqual(16, go.GetComponent<MeshFilter>().sharedMesh.vertexCount);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Build_InnerMode_LineIsOuterFace_ThicknessGrowsTowardInterior()
        {
            var go = new GameObject("WallTest");
            try
            {
                var wall = go.AddComponent<Wall>();
                wall.Build(new List<Vector3> { A, B }, 0.2f, 2.7f, WallOffsetMode.Inner, WallJoin.Miter, Interior);
                var b = go.GetComponent<MeshFilter>().sharedMesh.bounds;
                // interior at -Z: the drawn line (z = 0) is the OUTER face; solid grows to z = -0.2
                Assert.AreEqual(0f, b.max.z, 1e-4f, "outer face sits on the drawn line");
                Assert.AreEqual(-0.2f, b.min.z, 1e-4f, "thickness grows toward the interior");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Build_AliasedPointsInput_DoesNotWipeTheWall()
        {
            var go = new GameObject("WallTest");
            try
            {
                var wall = go.AddComponent<Wall>();
                wall.Build(new List<Vector3> { A, B }, 0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, Interior);

                // Rebuild passing the wall's own points list back in (the aliasing trap).
                wall.Build((List<Vector3>)wall.Points, 0.3f, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, Interior);

                Assert.AreEqual(2, wall.Points.Count, "input aliasing must not clear the centerline");
                Assert.AreEqual(8, go.GetComponent<MeshFilter>().sharedMesh.vertexCount);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Rebuild_ReproducesSameGeometry()
        {
            var go = new GameObject("WallTest");
            try
            {
                var wall = go.AddComponent<Wall>();
                wall.Build(new List<Vector3> { A, B, new Vector3(1f, 0f, 1f) },
                    0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, Interior);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                int count = mesh.vertexCount;
                Vector3 center = mesh.bounds.center;

                wall.Rebuild();

                Assert.AreEqual(count, mesh.vertexCount);
                Assert.AreEqual(center.x, mesh.bounds.center.x, 1e-4f);
                Assert.AreEqual(center.z, mesh.bounds.center.z, 1e-4f);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
