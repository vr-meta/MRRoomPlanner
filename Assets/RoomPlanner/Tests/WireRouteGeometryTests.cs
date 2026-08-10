using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Electrical;
using UnityEngine;

namespace RoomPlanner.Tests
{
    public class WireRouteGeometryTests
    {
        private GameObject _go;
        private WireRoute _route;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Wire");
            _go.AddComponent<MeshFilter>();
            _go.AddComponent<MeshRenderer>();
            _route = _go.AddComponent<WireRoute>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        private Mesh Mesh => _go.GetComponent<MeshFilter>().sharedMesh;

        private static List<Vector3> Elbow() => new()
        {
            new Vector3(0f, 0.3f, 0f),
            new Vector3(0f, 2.3f, 0f),
            new Vector3(3f, 2.3f, 0f),
        };

        [Test]
        public void Build_CreatesTube_WithLengthAndCollider()
        {
            Assert.IsTrue(_route.Build(Elbow(), CableType.C3x25));
            Assert.AreEqual(5f, _route.Length, 1e-4);
            Assert.Greater(Mesh.vertexCount, 0);
            Assert.IsNotNull(_go.GetComponent<MeshCollider>().sharedMesh, "pickable");
            Assert.AreEqual(CableType.C3x25, _route.Cable);
        }

        [Test]
        public void Build_CollapsesDoubleClicks()
        {
            var pts = Elbow();
            pts.Insert(1, new Vector3(0f, 0.305f, 0f));   // 5 mm twin
            Assert.IsTrue(_route.Build(pts, CableType.C3x15));
            Assert.AreEqual(3, _route.PointCount);
        }

        [Test]
        public void Build_DegeneratePolyline_RefusedWithEmptyMesh()
        {
            var pts = new List<Vector3> { Vector3.one, Vector3.one + new Vector3(0.002f, 0f, 0f) };
            Assert.IsFalse(_route.Build(pts, CableType.C3x15));
            Assert.AreEqual(0, Mesh.vertexCount, "no silent garbage geometry (rule 1.3)");
        }

        [Test]
        public void Build_NullInput_Refused()
        {
            Assert.IsFalse(_route.Build(null, CableType.C3x15));
            Assert.AreEqual(0, _route.PointCount);
        }

        [Test]
        public void MoveBy_TranslatesEveryPoint_KeepsLength()
        {
            _route.Build(Elbow(), CableType.C3x25);
            var delta = new Vector3(1f, 0f, -2f);
            _route.MoveBy(delta);
            Assert.AreEqual(new Vector3(1f, 0.3f, -2f), _route.GetPoint(0));
            Assert.AreEqual(5f, _route.Length, 1e-4);
        }

        [Test]
        public void MovePoint_ChangesLength()
        {
            _route.Build(Elbow(), CableType.C3x25);
            _route.MovePoint(2, new Vector3(4f, 2.3f, 0f));
            Assert.AreEqual(6f, _route.Length, 1e-4);
            _route.MovePoint(99, Vector3.zero);   // out of range — no-op, no throw
            Assert.AreEqual(3, _route.PointCount);
        }

        [Test]
        public void TryMoveAttachedEnd_MovesOnlyMatchingEndpoints()
        {
            _route.Build(Elbow(), CableType.C3x25);
            _route.StartFixtureId = "7";
            _route.EndFixtureId = "9";

            Assert.IsFalse(_route.TryMoveAttachedEnd("42", Vector3.right), "unknown id is a no-op");
            Assert.IsTrue(_route.TryMoveAttachedEnd("7", new Vector3(0.5f, 0f, 0f)));
            Assert.AreEqual(new Vector3(0.5f, 0.3f, 0f), _route.GetPoint(0));
            Assert.AreEqual(new Vector3(3f, 2.3f, 0f), _route.GetPoint(2), "far end untouched");

            Assert.IsTrue(_route.TryMoveAttachedEnd("9", new Vector3(0f, 0f, 1f)));
            Assert.AreEqual(new Vector3(3f, 2.3f, 1f), _route.GetPoint(2));
        }

        [Test]
        public void Connections_CountsAttachedEndsOnly()
        {
            _route.Build(Elbow(), CableType.C3x25);
            Assert.AreEqual(0, _route.Connections);
            _route.StartFixtureId = "7";
            Assert.AreEqual(1, _route.Connections);
            _route.EndFixtureId = "9";
            Assert.AreEqual(2, _route.Connections);
        }

        [Test]
        public void Mesh_AllFacesWoundOutward_PositiveEnclosedVolume()
        {
            _route.Build(Elbow(), CableType.C3x25);
            var v = new List<Vector3>();
            Mesh.GetVertices(v);
            var t = Mesh.triangles;
            float vol = 0f;
            for (int i = 0; i + 2 < t.Length; i += 3)
                vol += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]])) / 6f;
            float prism = 0.5f * WireRoute.Sides * WireRoute.Radius * WireRoute.Radius
                * Mathf.Sin(2f * Mathf.PI / WireRoute.Sides) * _route.Length;
            Assert.Greater(vol, 0f, "negative volume = inverted winding (rule 1.1)");
            Assert.AreEqual(prism, vol, prism * 0.02f, "capped segments enclose the full prism");
        }

        [Test]
        public void SetCable_ChangesTypeWithoutTouchingGeometry()
        {
            _route.Build(Elbow(), CableType.C3x25);
            int verts = Mesh.vertexCount;
            _route.SetCable(CableType.C3x15);
            Assert.AreEqual(CableType.C3x15, _route.Cable);
            Assert.AreEqual(verts, Mesh.vertexCount);
        }
    }
}
