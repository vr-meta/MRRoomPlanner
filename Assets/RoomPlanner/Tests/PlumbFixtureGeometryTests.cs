using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Plumbing;
using UnityEngine;

namespace RoomPlanner.Tests
{
    public class PlumbFixtureGeometryTests
    {
        private GameObject _go;
        private PlumbFixture _fx;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("PlumbFixture");
            _go.AddComponent<MeshFilter>();
            _go.AddComponent<MeshRenderer>();
            _fx = _go.AddComponent<PlumbFixture>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        private Mesh Mesh => _go.GetComponent<MeshFilter>().sharedMesh;

        [Test]
        public void ToiletOutlet90_StubPointsOutOfTheWall()
        {
            _fx.Build(PlumbFixtureKind.ToiletOutlet, OutletAngle.Deg90);
            Assert.Greater(Mesh.vertexCount, 0);
            Assert.AreEqual(new Vector3(0f, 0f, PlumbingDefaults.StubLength), _fx.TerminalLocal);
            Assert.AreEqual(PipeDiameter.D110, _fx.Diameter);
            // the tube reaches the D110 radius on both sides of the axis
            Assert.Greater(Mesh.bounds.size.x, PipeSpec.Radius(PipeDiameter.D110) * 2f * 0.9f);
        }

        [Test]
        public void ToiletOutlet45_TerminalDropsBelowTheAxis()
        {
            _fx.Build(PlumbFixtureKind.ToiletOutlet, OutletAngle.Deg45);
            Assert.Less(_fx.TerminalLocal.y, 0f, "the 45 elbow leans down toward the riser");
            Assert.Greater(_fx.TerminalLocal.z, PlumbingDefaults.Stub45Run);
        }

        [Test]
        public void SinkOutlet_IsD50_AndSlimmerThanToilet()
        {
            _fx.Build(PlumbFixtureKind.ToiletOutlet, OutletAngle.Deg90);
            float toiletWidth = Mesh.bounds.size.x;
            _fx.Build(PlumbFixtureKind.SinkOutlet, OutletAngle.Deg90);
            Assert.AreEqual(PipeDiameter.D50, _fx.Diameter);
            Assert.Less(Mesh.bounds.size.x, toiletWidth);
        }

        [Test]
        public void FloorDrain_BodySunkBelowTheFloor_PortIsTheTerminal()
        {
            _fx.Build(PlumbFixtureKind.FloorDrain, OutletAngle.Deg90);
            Assert.Greater(Mesh.vertexCount, 0);
            Assert.Less(Mesh.bounds.min.y, -PlumbingDefaults.DrainDepth * 0.9f, "body under the grate");
            var t = _fx.TerminalLocal;
            Assert.Less(t.y, 0f);
            Assert.Greater(t.z, PlumbingDefaults.DrainSize * 0.5f, "the D50 port sticks out of the side");
        }

        [Test]
        public void TerminalWorld_FollowsTheTransform()
        {
            _fx.Build(PlumbFixtureKind.SinkOutlet, OutletAngle.Deg90);
            _fx.transform.SetPositionAndRotation(new Vector3(1f, 0.45f, 2f),
                Quaternion.LookRotation(Vector3.right));
            var t = _fx.TerminalWorld;
            Assert.AreEqual(1f + PlumbingDefaults.StubLength, t.x, 1e-4, "stub follows the wall normal");
            Assert.AreEqual(0.45f, t.y, 1e-4);
        }

        [Test]
        public void MoveBy_IsAPureTransformShift()
        {
            _fx.Build(PlumbFixtureKind.FloorDrain, OutletAngle.Deg90);
            int verts = Mesh.vertexCount;
            _fx.MoveBy(new Vector3(1f, 0f, -2f));
            Assert.AreEqual(new Vector3(1f, 0f, -2f), _fx.transform.position);
            Assert.AreEqual(verts, Mesh.vertexCount, "no re-cook on move (rule 4.2)");
        }

        [Test]
        public void HeightAboveLevel_UsesBaseLevel()
        {
            _fx.Build(PlumbFixtureKind.SinkOutlet, OutletAngle.Deg90);
            _fx.BaseLevel = -3f;
            _fx.transform.position = new Vector3(0f, -2.55f, 0f);
            Assert.AreEqual(0.45f, _fx.HeightAboveLevel, 1e-4);
        }

        [Test]
        public void Meshes_AllKinds_WoundOutward_PositiveVolume()
        {
            foreach (var (kind, angle) in new (PlumbFixtureKind, OutletAngle)[]
            {
                (PlumbFixtureKind.ToiletOutlet, OutletAngle.Deg90),
                (PlumbFixtureKind.ToiletOutlet, OutletAngle.Deg45),
                (PlumbFixtureKind.SinkOutlet, OutletAngle.Deg45),
                (PlumbFixtureKind.FloorDrain, OutletAngle.Deg90),
            })
            {
                _fx.Build(kind, angle);
                var v = new List<Vector3>();
                Mesh.GetVertices(v);
                var t = Mesh.triangles;
                float vol = 0f;
                for (int i = 0; i + 2 < t.Length; i += 3)
                    vol += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]])) / 6f;
                Assert.Greater(vol, 0f, $"{kind}/{angle}: negative volume = inverted winding");
            }
        }
    }
}
