using NUnit.Framework;
using RoomPlanner.Core.Ifc;
using RoomPlanner.Stairs;
using UnityEngine;

namespace RoomPlanner.Tests
{
    public class StairGeometryTests
    {
        private GameObject _go;
        private Stair _stair;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Stair");
            _go.AddComponent<MeshFilter>();
            _go.AddComponent<MeshRenderer>();
            _stair = _go.AddComponent<Stair>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        private Mesh Mesh => _go.GetComponent<MeshFilter>().sharedMesh;

        [Test]
        public void BuildsSolidFlightWithExpectedBounds()
        {
            _stair.Build(Vector3.zero, 0f, 1.2f, 13, 0.175f, 0.275f);

            Assert.Greater(Mesh.vertexCount, 0);
            var b = Mesh.bounds;
            Assert.AreEqual(13 * 0.175f, b.size.y, 1e-4, "total height = risers × riser height");
            Assert.AreEqual(12 * 0.275f, b.size.z, 1e-4, "run length = (risers-1) × tread");
            Assert.AreEqual(1.2f, b.size.x, 1e-4, "width");
            Assert.AreEqual(0f, b.min.y, 1e-4, "sits on its base level");
        }

        [Test]
        public void YawRotatesTheRun()
        {
            _stair.Build(Vector3.zero, 90f, 1f, 4, 0.2f, 0.25f);
            var b = Mesh.bounds;
            // rotated 90°: the run now spans X, the width spans Z
            Assert.AreEqual(3 * 0.25f, b.size.x, 1e-4);
            Assert.AreEqual(1f, b.size.z, 1e-4);
        }

        [Test]
        public void MoveByTranslatesAndRebuilds()
        {
            _stair.Build(Vector3.zero, 0f, 1f, 3, 0.2f, 0.25f);
            _stair.MoveBy(new Vector3(2f, 1f, -3f));
            Assert.AreEqual(new Vector3(2f, 1f, -3f), _stair.Base);
            Assert.AreEqual(1f, Mesh.bounds.min.y, 1e-4);
        }

        [Test]
        public void ClampsDegenerateParameters()
        {
            _stair.Build(Vector3.zero, 0f, 0.01f, 0, 5f, 0.001f);
            Assert.AreEqual(1, _stair.Risers);
            Assert.GreaterOrEqual(_stair.Width, 0.2f);
            Assert.LessOrEqual(_stair.RiserHeight, 0.5f);
            Assert.GreaterOrEqual(_stair.TreadDepth, 0.1f);
        }

        [Test]
        public void OpenKindSharesFootprintButDiffersInBody()
        {
            _stair.Build(Vector3.zero, 0f, 1.2f, 13, 0.175f, 0.275f, StairKind.Solid);
            var solidBounds = Mesh.bounds;
            int solidTris = Mesh.triangles.Length;

            _stair.Build(Vector3.zero, 0f, 1.2f, 13, 0.175f, 0.275f, StairKind.Open);
            Assert.AreEqual(StairKind.Open, _stair.Kind);
            var openBounds = Mesh.bounds;

            Assert.AreEqual(solidBounds.size.y, openBounds.size.y, 1e-4, "same total height");
            Assert.AreEqual(solidBounds.size.z, openBounds.size.z, 1e-4, "same run length");
            Assert.AreEqual(solidBounds.size.x, openBounds.size.x, 1e-4, "same width");
            Assert.AreNotEqual(solidTris, Mesh.triangles.Length, "different construction");
        }

        [Test]
        public void OpenKindKeepsWalkableTreads()
        {
            _stair.Build(Vector3.zero, 0f, 1.2f, 13, 0.175f, 0.275f, StairKind.Open);
            var col = _go.GetComponent<MeshCollider>();
            // straight down onto the middle of the 6th tread (top at 6 × 0.175)
            var ray = new Ray(new Vector3(0f, 5f, 5.5f * 0.275f), Vector3.down);
            Assert.IsTrue(col.Raycast(ray, out var hit, 10f), "tread is there to stand on");
            Assert.AreEqual(6 * 0.175f, hit.point.y, 1e-3, "landed on the tread top");
        }

        [Test]
        public void NormalizeStairSize_HandlesMmMetersAndRevitFeet()
        {
            // honest file units: 175 mm in a milli file
            Assert.AreEqual(0.175f, IfcImporter.NormalizeStairSize(175f, 0.001f), 1e-4);
            // already metres (rare but legal)
            Assert.AreEqual(0.175f, IfcImporter.NormalizeStairSize(0.175f, 0.001f), 1e-4);
            // Revit's feet: 0.574147 ft = 175 mm
            Assert.AreEqual(0.175f, IfcImporter.NormalizeStairSize(0.5741470f, 0.001f), 1e-3);
            // Revit's feet for the tread: 0.902231 ft = 275 mm
            Assert.AreEqual(0.275f, IfcImporter.NormalizeStairSize(0.9022310f, 0.001f), 1e-3);
        }
    }
}
