using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Floors;

namespace RoomPlanner.Tests
{
    public class FloorGeometryTests
    {
        [Test]
        public void Build_MakesBoxAndStoresCorners()
        {
            var go = new GameObject("FloorTest");
            try
            {
                var floor = go.AddComponent<Floor>();
                var a = new Vector3(0f, 0f, 0f);
                var b = new Vector3(4f, 0f, 3f);
                floor.Build(a, b, 0f, 0.2f, 5f, 0f, 0f);

                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsNotNull(mesh);
                Assert.AreEqual(8, mesh.vertexCount); // 4 top + 4 bottom
                Assert.AreEqual(b, floor.CornerB);
                Assert.AreEqual(0f, floor.Level, 1e-5f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Build_DegenerateRect_ProducesNoTriangles()
        {
            var go = new GameObject("FloorTest");
            try
            {
                var floor = go.AddComponent<Floor>();
                floor.Build(Vector3.zero, new Vector3(0f, 0f, 3f), 0f, 0.2f, 5f, 0f, 0f); // zero width
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                Assert.AreEqual(0, mesh.vertexCount);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void MoveBy_ShiftsCornersInXZ_KeepsLevel()
        {
            var go = new GameObject("FloorTest");
            try
            {
                var floor = go.AddComponent<Floor>();
                var a = new Vector3(0f, 0f, 0f);
                var b = new Vector3(4f, 0f, 3f);
                floor.Build(a, b, 1.5f, 0.2f, 5f, 0f, 0f);

                var delta = new Vector3(2f, 0f, -1f);   // horizontal move
                floor.MoveBy(delta);

                // Stored corners always sit on the slab's top plane (y = Level).
                Assert.AreEqual(new Vector3(2f, 1.5f, -1f), floor.CornerA);
                Assert.AreEqual(new Vector3(6f, 1.5f, 2f), floor.CornerB);
                Assert.AreEqual(1.5f, floor.Level, 1e-5f, "horizontal move keeps the level");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void MoveBy_WithVerticalDelta_ShiftsLevel()
        {
            var go = new GameObject("FloorTest");
            try
            {
                var floor = go.AddComponent<Floor>();
                floor.Build(Vector3.zero, new Vector3(4f, 0f, 3f), 0f, 0.2f, 5f, 0f, 0f);
                floor.MoveBy(new Vector3(0f, 2.8f, 0f));   // e.g. next storey
                Assert.AreEqual(2.8f, floor.Level, 1e-5f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---- triangle orientation & UV mapping ----

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

        [Test]
        public void Build_AllFacesPointOutward()
        {
            var go = new GameObject("FloorTest");
            try
            {
                var floor = go.AddComponent<Floor>();
                floor.Build(Vector3.zero, new Vector3(4f, 0f, 3f), 0f, 0.2f, 5f, 0f, 0f);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;

                // A downward raycast must hit the TOP face — front faces only.
                AssertFaceOutward(mesh, p => Mathf.Abs(p.y) < 1e-4f, Vector3.up, "top");
                AssertFaceOutward(mesh, p => Mathf.Abs(p.y + 0.2f) < 1e-4f, Vector3.down, "bottom");
                AssertFaceOutward(mesh, p => Mathf.Abs(p.z) < 1e-4f, Vector3.back, "side z=min");
                AssertFaceOutward(mesh, p => Mathf.Abs(p.z - 3f) < 1e-4f, Vector3.forward, "side z=max");
                AssertFaceOutward(mesh, p => Mathf.Abs(p.x) < 1e-4f, Vector3.left, "side x=min");
                AssertFaceOutward(mesh, p => Mathf.Abs(p.x - 4f) < 1e-4f, Vector3.right, "side x=max");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Build_TopUVs_FollowWorldPlan()
        {
            var go = new GameObject("FloorTest");
            try
            {
                const float scale = 2f, ox = 1f, oz = 3f;
                var floor = go.AddComponent<Floor>();
                floor.Build(Vector3.zero, new Vector3(4f, 0f, 3f), 0f, 0.2f, scale, ox, oz);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                var v = mesh.vertices;
                var uv = mesh.uv;

                for (int i = 0; i < 4; i++)   // verts 0-3 = top face, UV by WORLD position
                {
                    Assert.AreEqual((v[i].x - ox) / scale, uv[i].x, 1e-5f, $"top vert {i} u");
                    Assert.AreEqual((v[i].z - oz) / scale, uv[i].y, 1e-5f, $"top vert {i} v");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Build_PlanRotation90_RotatesTopUVs()
        {
            var go = new GameObject("FloorTest");
            try
            {
                var floor = go.AddComponent<Floor>();
                // scale 1, rotation 90°, origin 0 → uv = (z, −x)
                floor.Build(Vector3.zero, new Vector3(4f, 0f, 3f), 0f, 0.2f, 1f, 90f, 0f, 0f);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                for (int i = 0; i < 4; i++)
                {
                    Assert.AreEqual(mesh.vertices[i].z, mesh.uv[i].x, 1e-4f, $"vert {i}: u = z");
                    Assert.AreEqual(-mesh.vertices[i].x, mesh.uv[i].y, 1e-4f, $"vert {i}: v = −x");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Build_LegacyOverload_MeansZeroRotation()
        {
            var goA = new GameObject("FloorA");
            var goB = new GameObject("FloorB");
            try
            {
                goA.AddComponent<Floor>().Build(Vector3.zero, new Vector3(4f, 0f, 3f), 0f, 0.2f, 2f, 1f, 3f);
                goB.AddComponent<Floor>().Build(Vector3.zero, new Vector3(4f, 0f, 3f), 0f, 0.2f, 2f, 0f, 1f, 3f);
                var uvA = goA.GetComponent<MeshFilter>().sharedMesh.uv;
                var uvB = goB.GetComponent<MeshFilter>().sharedMesh.uv;
                for (int i = 0; i < 4; i++)
                {
                    Assert.AreEqual(uvB[i].x, uvA[i].x, 1e-5f);
                    Assert.AreEqual(uvB[i].y, uvA[i].y, 1e-5f);
                }
            }
            finally { Object.DestroyImmediate(goA); Object.DestroyImmediate(goB); }
        }

        [Test]
        public void Build_ZeroPlanScale_FallsBackToOne()
        {
            var go = new GameObject("FloorTest");
            try
            {
                var floor = go.AddComponent<Floor>();
                floor.Build(Vector3.zero, new Vector3(4f, 0f, 3f), 0f, 0.2f, 0f, 0f, 0f);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                Assert.AreEqual(mesh.vertices[1].x, mesh.uv[1].x, 1e-5f, "scale 0 → treated as 1");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Build_NegativePlanScale_MirrorsUVs()
        {
            var go = new GameObject("FloorTest");
            try
            {
                var floor = go.AddComponent<Floor>();
                floor.Build(Vector3.zero, new Vector3(4f, 0f, 3f), 0f, 0.2f, -2f, 0f, 0f);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                Assert.AreEqual(mesh.vertices[1].x / -2f, mesh.uv[1].x, 1e-5f,
                    "negative scale mirrors the plan instead of being replaced by 1");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Build_CornerYs_SnapToLevel()
        {
            var go = new GameObject("FloorTest");
            try
            {
                var floor = go.AddComponent<Floor>();
                floor.Build(new Vector3(0f, 5f, 0f), new Vector3(4f, -3f, 3f), 1.5f, 0.2f, 5f, 0f, 0f);
                Assert.AreEqual(1.5f, floor.CornerA.y, 1e-5f, "stored corners sit on the slab's top plane");
                Assert.AreEqual(1.5f, floor.CornerB.y, 1e-5f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Build_NegativeThickness_BehavesAsAbsolute()
        {
            var go = new GameObject("FloorTest");
            try
            {
                var floor = go.AddComponent<Floor>();
                floor.Build(Vector3.zero, new Vector3(4f, 0f, 3f), 0f, -0.2f, 5f, 0f, 0f);
                var b = go.GetComponent<MeshFilter>().sharedMesh.bounds;
                Assert.AreEqual(0f, b.max.y, 1e-4f, "top stays at level");
                Assert.AreEqual(-0.2f, b.min.y, 1e-4f, "bottom extrudes DOWN even for negative input");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Rebuild_KeepsCornersAndMesh()
        {
            var go = new GameObject("FloorTest");
            try
            {
                var floor = go.AddComponent<Floor>();
                var a = Vector3.zero;
                var b = new Vector3(4f, 0f, 3f);
                floor.Build(a, b, 0f, 0.2f, 5f, 0f, 0f);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                int count = mesh.vertexCount;

                floor.Rebuild();

                Assert.AreEqual(a, floor.CornerA);
                Assert.AreEqual(b, floor.CornerB);
                Assert.AreEqual(count, mesh.vertexCount);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
