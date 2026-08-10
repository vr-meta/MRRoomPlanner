using NUnit.Framework;
using RoomPlanner.Electrical;
using UnityEngine;

namespace RoomPlanner.Tests
{
    public class ElectricFixtureGeometryTests
    {
        private GameObject _go;
        private ElectricFixture _fixture;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Fixture");
            _go.AddComponent<MeshFilter>();
            _go.AddComponent<MeshRenderer>();
            _fixture = _go.AddComponent<ElectricFixture>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        private Mesh Mesh => _go.GetComponent<MeshFilter>().sharedMesh;

        private float SignedVolume()
        {
            var v = new System.Collections.Generic.List<Vector3>();
            Mesh.GetVertices(v);
            var t = Mesh.triangles;
            float vol = 0f;
            for (int i = 0; i + 2 < t.Length; i += 3)
                vol += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]])) / 6f;
            return vol;
        }

        [Test]
        public void Outlet_WidthGrowsWithPosts()
        {
            _fixture.Build(FixtureKind.Outlet, 3, 1);
            var b = Mesh.bounds;
            Assert.AreEqual(3 * ElectricalDefaults.PostModule, b.size.x, 1e-4);
            Assert.AreEqual(ElectricalDefaults.PostModule, b.size.y, 1e-4);
            Assert.AreEqual(0f, b.min.z, 1e-4, "back plate sits on the wall plane");
        }

        [Test]
        public void PostsAndKeys_AreClamped()
        {
            _fixture.Build(FixtureKind.Outlet, 0, 0);
            Assert.AreEqual(1, _fixture.Posts);
            _fixture.Build(FixtureKind.Outlet, 99, 1);
            Assert.AreEqual(ElectricalDefaults.MaxPosts, _fixture.Posts);
            _fixture.Build(FixtureKind.Switch, 1, 99);
            Assert.AreEqual(ElectricalDefaults.MaxKeys, _fixture.Keys);
        }

        [Test]
        public void Switch_KeyCountChangesMesh()
        {
            _fixture.Build(FixtureKind.Switch, 1, 1);
            int oneKey = Mesh.vertexCount;
            _fixture.Build(FixtureKind.Switch, 1, 3);
            Assert.AreEqual(oneKey + 2 * 24, Mesh.vertexCount, "each key is one more box (24 verts)");
            Assert.AreEqual(ElectricalDefaults.PostModule, Mesh.bounds.size.x, 1e-4,
                "multi-key switch keeps the single-module frame");
        }

        [Test]
        public void Panel_HasCabinetBounds()
        {
            _fixture.Build(FixtureKind.Panel, 1, 1);
            var b = Mesh.bounds;
            Assert.AreEqual(ElectricalDefaults.PanelBoxWidth, b.size.x, 1e-4);
            Assert.AreEqual(ElectricalDefaults.PanelBoxHeight, b.size.y, 1e-4);
            Assert.Greater(b.size.z, ElectricalDefaults.PanelBoxDepth - 1e-4, "door sits proud of the box");
        }

        [Test]
        public void Junction_IsASmallLiddedBox()
        {
            // the v2 distribution box: 8×8 cm face, lid proud of the body, back on the mount plane
            _fixture.Build(FixtureKind.Junction, 1, 1);
            var b = Mesh.bounds;
            Assert.AreEqual(ElectricalDefaults.JunctionBoxSize, b.size.x, 1e-4);
            Assert.AreEqual(ElectricalDefaults.JunctionBoxSize, b.size.y, 1e-4);
            Assert.AreEqual(0f, b.min.z, 1e-4, "back sits on the wall/ceiling plane");
            Assert.Greater(b.size.z, ElectricalDefaults.JunctionBoxDepth - 1e-4, "lid sits proud of the box");
            Assert.AreEqual(ElectricalDefaults.JunctionBoxSize, _fixture.BlockWidth, 1e-4);
            Assert.AreEqual(ElectricalDefaults.JunctionBoxSize, _fixture.BlockHeight, 1e-4);
        }

        [Test]
        public void Junction_TerminalSitsOnTheLidCenter()
        {
            _fixture.Build(FixtureKind.Junction, 1, 1);
            // mounted on a ceiling: +Z (the lid) looks straight down into the room
            _go.transform.SetPositionAndRotation(new Vector3(1f, 3f, 2f),
                Quaternion.LookRotation(Vector3.down, Vector3.forward));
            var t = _fixture.TerminalWorld;
            Assert.AreEqual(1f, t.x, 1e-4);
            Assert.AreEqual(2f, t.z, 1e-4, "terminal is centered on the box face");
            Assert.AreEqual(3f - ElectricalDefaults.JunctionBoxDepth, t.y, 1e-4,
                "the cable entry hangs below the ceiling by the box depth");
        }

        [Test]
        public void Meshes_AreWoundOutward_PositiveVolume()
        {
            _fixture.Build(FixtureKind.Outlet, 2, 1);
            float expected = 2 * ElectricalDefaults.PostModule * ElectricalDefaults.PostModule
                * ElectricalDefaults.PlateDepth + 2 * 0.06f * 0.06f * 0.006f;
            Assert.AreEqual(expected, SignedVolume(), expected * 0.01f,
                "negative/short volume = inverted faces (rule 1.1)");

            _fixture.Build(FixtureKind.Panel, 1, 1);
            Assert.Greater(SignedVolume(), 0f);
            _fixture.Build(FixtureKind.Switch, 1, 2);
            Assert.Greater(SignedVolume(), 0f);
        }

        [Test]
        public void Terminal_TopForWallFixtures_BottomForPanel()
        {
            _fixture.Build(FixtureKind.Outlet, 1, 1);
            Assert.Greater(_fixture.TerminalLocal.y, 0f, "wires enter outlets from above");
            _fixture.Build(FixtureKind.Panel, 1, 1);
            Assert.Less(_fixture.TerminalLocal.y, 0f, "wires dive into the panel bottom");
        }

        [Test]
        public void TerminalWorld_FollowsTransform()
        {
            _fixture.Build(FixtureKind.Outlet, 1, 1);
            _go.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 90f, 0f));
            Vector3 expected = _go.transform.TransformPoint(_fixture.TerminalLocal);
            Assert.AreEqual(expected.x, _fixture.TerminalWorld.x, 1e-5);
            Assert.AreEqual(expected.y, _fixture.TerminalWorld.y, 1e-5);
            Assert.AreEqual(expected.z, _fixture.TerminalWorld.z, 1e-5);
        }

        [Test]
        public void MoveBy_IsPureTransformMove()
        {
            _fixture.Build(FixtureKind.Outlet, 1, 1);
            var meshBefore = Mesh;
            int verts = Mesh.vertexCount;
            _fixture.MoveBy(new Vector3(0.5f, 0f, 0f));
            Assert.AreEqual(new Vector3(0.5f, 0f, 0f), _go.transform.position);
            Assert.AreSame(meshBefore, Mesh, "no mesh rebuild on drag (rule 4.2)");
            Assert.AreEqual(verts, Mesh.vertexCount);
        }

        [Test]
        public void HeightAboveLevel_IsStoreyRelative()
        {
            _fixture.Build(FixtureKind.Outlet, 1, 1);
            _fixture.BaseLevel = 3f;                       // second storey
            _go.transform.position = new Vector3(1f, 3.3f, 0f);
            Assert.AreEqual(0.3f, _fixture.HeightAboveLevel, 1e-5,
                "mounting height counts from the storey level, not world zero");
        }

        [Test]
        public void ReservePercent_Clamped()
        {
            _fixture.ReservePercent = -5;
            Assert.AreEqual(0, _fixture.ReservePercent);
            _fixture.ReservePercent = 99;
            Assert.AreEqual(ElectricalDefaults.MaxReservePercent, _fixture.ReservePercent);
        }
    }
}
