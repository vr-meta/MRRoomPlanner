using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Plumbing;
using UnityEngine;

namespace RoomPlanner.Tests
{
    public class PipeRouteGeometryTests
    {
        private GameObject _go;
        private PipeRoute _route;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Pipe");
            _go.AddComponent<MeshFilter>();
            _go.AddComponent<MeshRenderer>();
            _route = _go.AddComponent<PipeRoute>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        private Mesh Mesh => _go.GetComponent<MeshFilter>().sharedMesh;

        private static List<Vector3> FloorRun() => new()
        {
            new Vector3(0f, 0.1f, 0f),
            new Vector3(3f, 0.1f, 0f),
            new Vector3(3f, 0.1f, 2f),
        };

        [Test]
        public void Build_CreatesTube_WithLengthColliderAndDiameter()
        {
            Assert.IsTrue(_route.Build(FloorRun(), PipeDiameter.D50));
            Assert.AreEqual(5f, _route.Length, 1e-4);
            Assert.Greater(Mesh.vertexCount, 0);
            Assert.IsNotNull(_go.GetComponent<MeshCollider>().sharedMesh, "pickable");
            Assert.AreEqual(PipeDiameter.D50, _route.Diameter);
            Assert.AreEqual(PipeSpec.Radius(PipeDiameter.D50), _route.Radius, 1e-6);
        }

        [Test]
        public void Riser_IsTwoPointsAndAFlag()
        {
            _route.IsRiser = true;
            Assert.IsTrue(_route.Build(new List<Vector3>
            {
                new(1f, 0f, 1f), new(1f, 2.7f, 1f),
            }, PipeDiameter.D110));
            Assert.IsTrue(_route.IsRiser);
            Assert.AreEqual(2.7f, _route.Length, 1e-4);
        }

        [Test]
        public void SetDiameter_RebuildsThicker()
        {
            _route.Build(FloorRun(), PipeDiameter.D40);
            var slim = Mesh.bounds.size.y;
            _route.SetDiameter(PipeDiameter.D110);
            Assert.AreEqual(PipeDiameter.D110, _route.Diameter);
            Assert.Greater(Mesh.bounds.size.y, slim, "a D110 tube is visibly fatter than D40");
        }

        [Test]
        public void TryMoveAttachedEnd_MovesOnlyMatchingEndpoints()
        {
            _route.Build(FloorRun(), PipeDiameter.D50);
            _route.StartFixtureId = "riser-1";
            Assert.IsFalse(_route.TryMoveAttachedEnd("other", Vector3.right));
            Assert.IsTrue(_route.TryMoveAttachedEnd("riser-1", new Vector3(0f, 0.2f, 0f)));
            Assert.AreEqual(new Vector3(0f, 0.3f, 0f), _route.GetPoint(0));
            Assert.AreEqual(new Vector3(3f, 0.1f, 2f), _route.GetPoint(2), "far end untouched");
        }

        [Test]
        public void Mesh_AllFacesWoundOutward_PositiveEnclosedVolume()
        {
            _route.Build(FloorRun(), PipeDiameter.D110);
            var v = new List<Vector3>();
            Mesh.GetVertices(v);
            var t = Mesh.triangles;
            float vol = 0f;
            for (int i = 0; i + 2 < t.Length; i += 3)
                vol += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]])) / 6f;
            float r = PipeSpec.Radius(PipeDiameter.D110);
            float prism = 0.5f * PipeRoute.Sides * r * r
                * Mathf.Sin(2f * Mathf.PI / PipeRoute.Sides) * _route.Length;
            Assert.Greater(vol, 0f, "negative volume = inverted winding (rule 1.1)");
            Assert.AreEqual(prism, vol, prism * 0.02f, "capped segments enclose the full prism");
        }

        [Test]
        public void Build_DegeneratePolyline_RefusedWithEmptyMesh()
        {
            var pts = new List<Vector3> { Vector3.one, Vector3.one + new Vector3(0.002f, 0f, 0f) };
            Assert.IsFalse(_route.Build(pts, PipeDiameter.D50));
            Assert.AreEqual(0, Mesh.vertexCount, "no silent garbage geometry (rule 1.3)");
        }

        [Test]
        public void ReservePercent_Clamped()
        {
            _route.ReservePercent = 95;
            Assert.AreEqual(PlumbingDefaults.MaxReservePercent, _route.ReservePercent);
            _route.ReservePercent = -5;
            Assert.AreEqual(0, _route.ReservePercent);
        }
    }
}
