using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Core;
using RoomPlanner.Floors;
using RoomPlanner.Walls;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>Baked vertex AO (design/04): the builders write contact shading into
    /// vertex colors — skirting line on walls/stairs, outline/hole rims on floor tops.</summary>
    public class AOShadingTests
    {
        private bool _savedFlag;

        [SetUp]
        public void SetUp() => _savedFlag = MeshShading.VertexAO;

        [TearDown]
        public void TearDown() => MeshShading.VertexAO = _savedFlag;

        [Test]
        public void HeightAO_DarkAtTheFloor_FullAboveHalfMeter()
        {
            MeshShading.VertexAO = true;
            Assert.AreEqual(0.70f, MeshShading.HeightAO(0f), 1e-4);
            Assert.AreEqual(1f, MeshShading.HeightAO(0.45f), 1e-4);
            Assert.AreEqual(1f, MeshShading.HeightAO(2f), 1e-4);
            Assert.Less(MeshShading.HeightAO(0.1f), MeshShading.HeightAO(0.3f));
        }

        [Test]
        public void TogglingOffMeansWhite()
        {
            MeshShading.VertexAO = false;
            Assert.AreEqual(1f, MeshShading.HeightAO(0f), 1e-6);
            Assert.AreEqual(1f, MeshShading.EdgeAO(0f), 1e-6);
        }

        [Test]
        public void DistanceToRing_InsideASquare()
        {
            var ring = new List<Vector3>
            {
                new(0f, 0f, 0f), new(4f, 0f, 0f), new(4f, 0f, 4f), new(0f, 0f, 4f),
            };
            Assert.AreEqual(2f, MeshShading.DistanceToRingXZ(ring, new Vector3(2f, 0f, 2f)), 1e-4);
            Assert.AreEqual(0.5f, MeshShading.DistanceToRingXZ(ring, new Vector3(0.5f, 0f, 2f)), 1e-4);
        }

        [Test]
        public void WallVerticesDarkenTowardTheBase()
        {
            MeshShading.VertexAO = true;
            var go = new GameObject("Wall");
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            var wall = go.AddComponent<Wall>();
            var graph = new WallGraph();
            var seg = graph.AddSegment(
                graph.SnapOrCreateNode(Vector3.zero),
                graph.SnapOrCreateNode(new Vector3(4f, 0f, 0f)));
            seg.Thickness = 0.2f;
            seg.Height = 2.7f;
            wall.BuildSegment(seg);

            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            var colors = mesh.colors;
            var verts = mesh.vertices;
            Assert.AreEqual(verts.Length, colors.Length, "every vertex carries AO");
            float lowMin = 1f, highMin = 1f;
            for (int i = 0; i < verts.Length; i++)
            {
                if (verts[i].y < 0.01f) lowMin = Mathf.Min(lowMin, colors[i].r);
                if (verts[i].y > 2.5f) highMin = Mathf.Min(highMin, colors[i].r);
            }
            Assert.AreEqual(0.70f, lowMin, 1e-3, "base vertices carry the skirting shadow");
            Assert.AreEqual(1f, highMin, 1e-3, "top vertices are unoccluded");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void FloorTopDarkensTowardTheOutline()
        {
            MeshShading.VertexAO = true;
            var go = new GameObject("Floor");
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            var floor = go.AddComponent<Floor>();
            floor.BuildOutline(new List<Vector3>
            {
                new(0f, 0f, 0f), new(6f, 0f, 0f), new(6f, 0f, 6f), new(0f, 0f, 6f),
            }, 0f, 0.2f, 5f, 0f, 0f, 0f);

            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            var colors = mesh.colors;
            var verts = mesh.vertices;
            float rimMin = 1f, centerMax = 0f;
            for (int i = 0; i < verts.Length; i++)
            {
                if (verts[i].y < -0.01f) continue;              // top face only
                float d = Mathf.Min(
                    Mathf.Min(verts[i].x, 6f - verts[i].x),
                    Mathf.Min(verts[i].z, 6f - verts[i].z));
                if (d < 0.01f) rimMin = Mathf.Min(rimMin, colors[i].r);
                if (d > 1f) centerMax = Mathf.Max(centerMax, colors[i].r);
            }
            Assert.AreEqual(0.76f, rimMin, 1e-3, "outline rim carries the wall contact shadow");
            Assert.AreEqual(1f, centerMax, 1e-3, "the middle of the room is unoccluded");
            Object.DestroyImmediate(go);
        }
    }
}
