using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Core;
using RoomPlanner.Floors;
using RoomPlanner.Walls;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Metric unwrap regressions (design/04, headset feedback 2026-08-11): wall caps and
    /// tops smeared textures into stripes (degenerate UVs), and slab tops carried only
    /// the blueprint projection so floor textures never repeated. Wall faces must unwrap
    /// 1:1 with world metres (TileMeters = 1); slab tops carry a metric uv1 channel.
    /// </summary>
    public class MetricUvTests
    {
        private bool _savedAO;

        [SetUp]
        public void DisableVertexAO()
        {
            // AO subdivision adds vertices; these tests pin the plain topology
            _savedAO = MeshShading.VertexAO;
            MeshShading.VertexAO = false;
        }

        [TearDown]
        public void RestoreVertexAO() => MeshShading.VertexAO = _savedAO;

        /// <summary>Every triangle's UV area must match its world area (metric unwrap,
        /// TileMeters = 1) — a degenerate cap/top collapses to zero UV area.</summary>
        private static void AssertMetricUnwrap(Mesh mesh, int submesh)
        {
            var verts = mesh.vertices;
            var uv = mesh.uv;
            var tris = mesh.GetTriangles(submesh);
            int checked_ = 0;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 p0 = verts[tris[i]], p1 = verts[tris[i + 1]], p2 = verts[tris[i + 2]];
                float worldArea = Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5f;
                if (worldArea < 1e-6f) continue;   // collapsed geometry (mitred slivers)
                Vector2 t0 = uv[tris[i]], t1 = uv[tris[i + 1]], t2 = uv[tris[i + 2]];
                Vector2 e1 = t1 - t0, e2 = t2 - t0;
                float uvArea = Mathf.Abs(e1.x * e2.y - e1.y * e2.x) * 0.5f;
                Assert.AreEqual(worldArea, uvArea, worldArea * 0.05f,
                    $"triangle {i / 3}: UV area must equal world area (metric unwrap)");
                checked_++;
            }
            Assert.Greater(checked_, 0, "the mesh has no measurable faces to check");
        }

        [Test]
        public void StraightWall_AllFacesUnwrapMetric_CapsIncluded()
        {
            var go = new GameObject("Wall");
            try
            {
                var wall = go.AddComponent<Wall>();
                wall.Build(new List<Vector3> { Vector3.zero, new(2f, 0f, 0f) },
                    0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, new Vector3(1f, 0f, -1f));
                AssertMetricUnwrap(go.GetComponent<MeshFilter>().sharedMesh, 0);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void WallWithOpening_AllFacesUnwrapMetric_JambsIncluded()
        {
            var go = new GameObject("Wall");
            try
            {
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                var wall = go.AddComponent<Wall>();
                var graph = new WallGraph();
                var seg = graph.AddSegment(
                    graph.SnapOrCreateNode(Vector3.zero),
                    graph.SnapOrCreateNode(new Vector3(4f, 0f, 0f)));
                seg.Thickness = 0.2f;
                seg.Height = 2.7f;
                seg.Openings.Add(new WallOpening
                {
                    AlongFraction = 0.5f, Width = 1.2f, SillHeight = 0.9f, Height = 1.4f,
                    IsDoor = false,
                });
                wall.BuildSegment(seg);
                AssertMetricUnwrap(go.GetComponent<MeshFilter>().sharedMesh, 0);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void SlabTop_CarriesMetricUv1_WhilePlanStaysInUv0()
        {
            var go = new GameObject("Floor");
            try
            {
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                var floor = go.AddComponent<Floor>();
                // plan scale 5 → uv0 of the top face is visibly NOT metric
                floor.BuildOutline(new List<Vector3>
                {
                    new(0f, 0f, 0f), new(4f, 0f, 0f), new(4f, 0f, 3f), new(0f, 0f, 3f),
                }, 0f, 0.2f, 5f, 0f, 0f, 0f);

                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                var uv1 = new List<Vector2>();
                mesh.GetUVs(1, uv1);
                Assert.AreEqual(mesh.vertexCount, uv1.Count, "uv1 covers every vertex");

                // builder layout: top-face vertices come first, until the first below-top vertex
                var verts = mesh.vertices;
                int n = 0;
                while (n < verts.Length && verts[n].y > -0.001f) n++;
                Assert.Greater(n, 2, "top face missing");

                for (int i = 0; i < n; i++)
                {
                    Assert.AreEqual(verts[i].x / Floor.TileMeters, uv1[i].x, 1e-4f,
                        "uv1.x is metric world X");
                    Assert.AreEqual(verts[i].z / Floor.TileMeters, uv1[i].y, 1e-4f,
                        "uv1.y is metric world Z");
                }

                // and uv0 keeps the plan projection (differs from the metric channel)
                var uv0 = mesh.uv;
                bool planDiffers = false;
                for (int i = 0; i < n; i++)
                    if ((uv0[i] - uv1[i]).sqrMagnitude > 1e-6f) planDiffers = true;
                Assert.IsTrue(planDiffers, "uv0 must stay the blueprint projection, not metric");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
