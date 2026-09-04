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
            Assert.Greater(Mesh.vertexCount, oneKey, "each key adds a separate chamfered rocker");
            Assert.AreEqual(ElectricalDefaults.PostModule, Mesh.bounds.size.x, 1e-4,
                "multi-key switch keeps the single-module frame");
            Assert.Greater(ElectricFixture.SwitchKeyGap, 0f);
            Assert.Greater(ElectricFixture.RockerTiltDegrees, 0f);
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
            Assert.Greater(SignedVolume(), 0f,
                "negative volume = inverted chamfer/cup faces (rule 1.1)");

            _fixture.Build(FixtureKind.Panel, 1, 1);
            Assert.Greater(SignedVolume(), 0f);
            _fixture.Build(FixtureKind.Switch, 1, 2);
            Assert.Greater(SignedVolume(), 0f);
        }

        [Test]
        public void Outlet_HasChamferedPlateRecessedCupAndVisiblePinHoles()
        {
            _fixture.Build(FixtureKind.Outlet, 1, 1);
            Assert.AreEqual(ElectricFixture.SubmeshCount, Mesh.subMeshCount);
            Assert.Greater(Mesh.GetTriangles(ElectricFixture.PlasticSubmesh).Length, 36,
                "plate plus recessed cup needs more geometry than a raw box");
            Assert.GreaterOrEqual(Mesh.GetTriangles(ElectricFixture.DetailSubmesh).Length, 60,
                "two ten-sided pin holes are carried by the dark detail material");
            Assert.Greater(ElectricFixture.PlateChamfer, 0f);
            Assert.Greater(ElectricFixture.SocketCupDepth, 0f);
        }

        [Test]
        public void PlateChamferFaces_AreAllWoundOutward()
        {
            _fixture.Build(FixtureKind.Outlet, 1, 1);
            var vertices = Mesh.vertices;
            var plateTriangles = Mesh.GetTriangles(ElectricFixture.PlasticSubmesh);
            var plateCenter = new Vector3(0f, 0f, ElectricalDefaults.PlateDepth * 0.5f);
            const int plateIndexCount = 10 * 6; // ten quads in AddFrontChamferedBox
            for (int i = 0; i < plateIndexCount; i += 3)
            {
                Vector3 a = vertices[plateTriangles[i]];
                Vector3 b = vertices[plateTriangles[i + 1]];
                Vector3 c = vertices[plateTriangles[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                Vector3 fromCenter = (a + b + c) / 3f - plateCenter;
                Assert.Greater(Vector3.Dot(normal, fromCenter), 0f,
                    $"plate triangle {i / 3} points inward");
            }
        }

        [Test]
        public void FixtureUvs_AreMetric_NotNormalizedPerObject()
        {
            _fixture.Build(FixtureKind.Panel, 1, 1);
            var uv = Mesh.uv;
            float max = 0f;
            foreach (var p in uv) max = Mathf.Max(max, p.x, p.y);
            Assert.Greater(max, 0.20f, "a 45 cm panel must span a comparable UV distance");
            Assert.Less(max, 1f, "fixture UVs are metres, not a full 0..1 tile per face");
        }

        [Test]
        public void PanelOpen_RevealsDinRailsAndBreakers_AndDoorSwingsOut()
        {
            _fixture.Build(FixtureKind.Panel, 1, 1, false, false);
            int closedVertices = Mesh.vertexCount;
            float closedDepth = Mesh.bounds.size.z;

            _fixture.SetPanelOpen(true);

            Assert.IsTrue(_fixture.PanelOpen);
            Assert.Greater(Mesh.vertexCount, closedVertices, "open panel adds DIN rails and breakers");
            Assert.Greater(Mesh.bounds.size.z, closedDepth, "the hinged door swings into the room");
            Assert.Greater(Mesh.GetTriangles(ElectricFixture.MetalSubmesh).Length, 0);
            Assert.Greater(Mesh.GetTriangles(ElectricFixture.DetailSubmesh).Length, 0);
            Assert.Greater(Mesh.GetTriangles(ElectricFixture.PlasticSubmesh).Length, 0);
        }

        [Test]
        public void BlackVariant_UsesPropertyBlocksWithoutRebuilding()
        {
            _fixture.Build(FixtureKind.Outlet, 1, 1);
            var before = Mesh;
            _fixture.SetBlackVariant(true);

            Assert.IsTrue(_fixture.BlackVariant);
            Assert.AreSame(before, Mesh);
            var block = new MaterialPropertyBlock();
            _go.GetComponent<MeshRenderer>().GetPropertyBlock(block, ElectricFixture.PlasticSubmesh);
            Assert.AreEqual(ElectricFixture.BlackPlastic, block.GetColor("_BaseColor"));
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
