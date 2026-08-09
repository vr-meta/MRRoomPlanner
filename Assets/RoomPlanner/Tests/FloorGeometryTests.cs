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
    }
}
