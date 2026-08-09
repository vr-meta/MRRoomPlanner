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

                Assert.AreEqual(a + delta, floor.CornerA);
                Assert.AreEqual(b + delta, floor.CornerB);
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
