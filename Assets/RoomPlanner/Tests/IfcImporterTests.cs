using System.IO;
using System.Linq;
using NUnit.Framework;
using RoomPlanner.Core.Ifc;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Runs the importer against a fixture extracted verbatim from a real Revit 24
    /// export (docs/design/18-ifc-import.md): 2 walls, 1 rectangular column, 2 slabs
    /// (one rectangular, one arbitrary outline with a flipped Z frame), 5 storeys.
    /// </summary>
    public class IfcImporterTests
    {
        private const string FixturePath = "Assets/RoomPlanner/Tests/Fixtures/MiniRevit.ifc";

        private static ImportedBuilding _building; // parsed once — the fixture is immutable

        private static ImportedBuilding Building =>
            _building ??= IfcImporter.Import(StepFile.Parse(File.ReadAllText(FixturePath)));

        [Test]
        public void FixtureUsesMillimeters()
        {
            var f = StepFile.Parse(File.ReadAllText(FixturePath));
            Assert.AreEqual(0.001, f.LengthToMeters, 1e-12);
        }

        [Test]
        public void ImportsStoreysSortedByElevation()
        {
            var s = Building.Storeys;
            Assert.AreEqual(5, s.Count);
            CollectionAssert.AreEqual(
                new[] { "L1", "L2", "L3", "L4 Terrace", "Roof" },
                s.Select(x => x.Name).ToArray());
            Assert.AreEqual(0f, s[0].Elevation, 1e-4);
            Assert.AreEqual(3.15f, s[1].Elevation, 1e-4);
            Assert.AreEqual(6.1f, s[2].Elevation, 1e-4);
            Assert.AreEqual(9.05f, s[3].Elevation, 1e-4);
            Assert.AreEqual(11.3f, s[4].Elevation, 1e-4);
        }

        [Test]
        public void ImportsWallsWithAxisThicknessHeight()
        {
            var walls = Building.Walls.Where(w => !w.FromColumn).ToList();
            Assert.AreEqual(2, walls.Count);
            Assert.AreEqual(0, Building.SkippedWalls);
            foreach (var w in walls)
            {
                Assert.AreEqual(2, w.Path.Count);
                Assert.AreEqual(0.15f, w.Thickness, 1e-4, "thickness from material layers");
                Assert.AreEqual(3.0f, w.Height, 1e-4, "height from extrusion depth");
                Assert.AreEqual(0, w.StoreyIndex, "both fixture walls sit on L1");
            }
        }

        [Test]
        public void WallAxisLandsAtExactWorldPosition()
        {
            // Wall #150: placement (8425,0,0) rotated X→north, axis (0,0)→(7000,0).
            // Expected Unity endpoints: (8.425, 0, 0) → (8.425, 0, 7).
            var w = Building.Walls.First(x => !x.FromColumn);
            Assert.AreEqual(0f, Vector3.Distance(w.Path[0], new Vector3(8.425f, 0f, 0f)), 1e-3);
            Assert.AreEqual(0f, Vector3.Distance(w.Path[1], new Vector3(8.425f, 0f, 7f)), 1e-3);
        }

        [Test]
        public void ImportsRectangularColumnAsShortWallSegment()
        {
            var cols = Building.Walls.Where(w => w.FromColumn).ToList();
            Assert.AreEqual(1, cols.Count);
            Assert.AreEqual(0, Building.SkippedColumns);
            var col = cols[0];
            Assert.AreEqual(0.3f, Vector3.Distance(col.Path[0], col.Path[1]), 1e-4, "axis = long profile side");
            Assert.AreEqual(0.3f, col.Thickness, 1e-4);
            Assert.AreEqual(3.15f, col.Height, 1e-4);
            // Column #277 is placed at (150,150,0) mm on L1 → Unity centre (0.15, 0, 0.15).
            var mid = (col.Path[0] + col.Path[1]) * 0.5f;
            Assert.AreEqual(0f, Vector3.Distance(mid, new Vector3(0.15f, 0f, 0.15f)), 1e-3);
        }

        [Test]
        public void ImportsSlabOutlines()
        {
            Assert.AreEqual(2, Building.Slabs.Count);
            Assert.AreEqual(0, Building.SkippedSlabs);
            foreach (var slab in Building.Slabs)
            {
                Assert.AreEqual(4, slab.Outline.Count, "closing duplicate point must be dropped");
                Assert.AreEqual(0.2f, slab.Thickness, 1e-4);
                foreach (var p in slab.Outline)
                    Assert.AreEqual(slab.Level, p.y, 1e-4, "outline sits on the top plane");
            }
        }

        [Test]
        public void ArbitraryProfileSlabKeepsItsShape()
        {
            // Slab #154113 (on L4 Terrace): trapezoid 8500 × (8600/7300) mm in a frame
            // with Z flipped — exercises the full matrix composition path.
            var slab = Building.Slabs.First(s => s.StoreyIndex == 3);
            float dx = slab.Outline.Max(p => p.x) - slab.Outline.Min(p => p.x);
            float dz = slab.Outline.Max(p => p.z) - slab.Outline.Min(p => p.z);
            Assert.AreEqual(8.5f, Mathf.Min(dx, dz), 1e-3);
            Assert.AreEqual(8.6f, Mathf.Max(dx, dz), 1e-3);
        }

        [Test]
        public void FoundationSlabTopSitsAtGroundLevel()
        {
            // Slab #236 on L1: extruded 200 mm; the top face is the storey plane.
            var slab = Building.Slabs.First(s => s.StoreyIndex == 0);
            Assert.AreEqual(0f, slab.Level, 1e-3);
        }
    }
}
