using System.Collections.Generic;
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
        public void CyclicLocalPlacement_DoesNotOverflowTheStack()
        {
            // Audit 09 §Б2: the placement cache was written AFTER the recursive call, so
            // a broken file with #40↔#41 referencing each other recursed forever — a
            // StackOverflow that no catch can stop killed the whole app, not the import.
            const string doc = @"DATA;
#20=IFCCARTESIANPOINT((0.,0.,0.));
#21=IFCCARTESIANPOINT((4.,0.,0.));
#22=IFCPOLYLINE((#20,#21));
#23=IFCSHAPEREPRESENTATION($,'Axis','Curve2D',(#22));
#30=IFCRECTANGLEPROFILEDEF(.AREA.,$,$,4.,0.15);
#31=IFCEXTRUDEDAREASOLID(#30,#32,#33,3.);
#32=IFCAXIS2PLACEMENT3D(#20,$,$);
#33=IFCDIRECTION((0.,0.,1.));
#34=IFCSHAPEREPRESENTATION($,'Body','SweptSolid',(#31));
#35=IFCPRODUCTDEFINITIONSHAPE($,$,(#23,#34));
#40=IFCLOCALPLACEMENT(#41,#32);
#41=IFCLOCALPLACEMENT(#40,#32);
#50=IFCWALLSTANDARDCASE('gid',$,'W',$,$,#40,#35,$);
ENDSEC;";
            ImportedBuilding b = null;
            Assert.DoesNotThrow(() => b = IfcImporter.Import(StepFile.Parse(doc)));
            // The cycle resolves to identity — the wall must come through, not crash.
            Assert.AreEqual(1, b.Walls.Count + b.SkippedWalls, "the wall is processed either way");
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
            Assert.AreEqual(3, walls.Count);
            Assert.AreEqual(0, Building.SkippedWalls);
            foreach (var w in walls)
                Assert.AreEqual(2, w.Path.Count);

            // two Generic-150 walls (3 m) and one low Generic-125 partition (1.975 m)
            Assert.AreEqual(2, walls.Count(w =>
                Mathf.Abs(w.Thickness - 0.15f) < 1e-4 && Mathf.Abs(w.Height - 3f) < 1e-4));
            Assert.AreEqual(1, walls.Count(w =>
                Mathf.Abs(w.Thickness - 0.125f) < 1e-4 && Mathf.Abs(w.Height - 1.975f) < 1e-4),
                "thickness comes from the material layers, height from the extrusion");
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
            // 3 extruded slabs + the Brep-only stair landing (#25807).
            Assert.AreEqual(4, Building.Slabs.Count);
            Assert.AreEqual(0, Building.SkippedSlabs);
            var extruded = Building.Slabs.Where(s => s.Outline.Count == 4).ToList();
            Assert.AreEqual(3, extruded.Count);
            foreach (var slab in extruded)
            {
                Assert.AreEqual(0.2f, slab.Thickness, 1e-4);
                foreach (var p in slab.Outline)
                    Assert.AreEqual(slab.Level, p.y, 1e-4, "outline sits on the top plane");
            }
        }

        [Test]
        public void ImportsBrepLandingAsSlab()
        {
            // Landing #25807 has no extrusion — a FacetedBrep whose top face (7 points,
            // 2100 × 1050 at (3400, 5800)) sits at z 2275; the sloped soffit bottoms out
            // at 1922.2, so the honest thickness is the full shell height.
            var landing = Building.Slabs.Single(s => s.Outline.Count == 7);
            Assert.AreEqual(2.275f, landing.Level, 1e-3);
            Assert.AreEqual(0.3528f, landing.Thickness, 1e-3);
            Assert.AreEqual(0, landing.StoreyIndex, "storey inherited from the stair via IfcRelAggregates");
            float dx = landing.Outline.Max(p => p.x) - landing.Outline.Min(p => p.x);
            float dz = landing.Outline.Max(p => p.z) - landing.Outline.Min(p => p.z);
            Assert.AreEqual(2.1f, dx, 1e-3);
            Assert.AreEqual(1.05f, dz, 1e-3);
            Assert.AreEqual(3.4f, landing.Outline.Min(p => p.x), 1e-3);
            Assert.AreEqual(5.8f, landing.Outline.Min(p => p.z), 1e-3);
            foreach (var p in landing.Outline)
                Assert.AreEqual(landing.Level, p.y, 1e-4, "outline sits on the top plane");
        }

        [Test]
        public void ImportsSlabHolesFromOpeningElements()
        {
            // Two voided slabs: #125193 (stairwell 2225 × 2100) and #154113 (4569 × 2101).
            var holed = Building.Slabs.Where(s => s.Holes.Count > 0).ToList();
            Assert.AreEqual(2, holed.Count);
            foreach (var slab in holed)
            {
                Assert.AreEqual(1, slab.Holes.Count);
                Assert.AreEqual(4, slab.Holes[0].Count);
                foreach (var p in slab.Holes[0])
                    Assert.AreEqual(slab.Level, p.y, 1e-4, "hole ring lives on the top plane");
            }

            (float lo, float hi) Extents(List<Vector3> ring) =>
                (Mathf.Min(ring.Max(p => p.x) - ring.Min(p => p.x), ring.Max(p => p.z) - ring.Min(p => p.z)),
                 Mathf.Max(ring.Max(p => p.x) - ring.Min(p => p.x), ring.Max(p => p.z) - ring.Min(p => p.z)));
            Assert.IsTrue(holed.Any(s =>
            {
                var (lo, hi) = Extents(s.Holes[0]);
                return Mathf.Abs(lo - 2.1f) < 1e-3 && Mathf.Abs(hi - 2.225f) < 1e-3;
            }), "the stairwell hole");
            Assert.IsTrue(holed.Any(s =>
            {
                var (lo, hi) = Extents(s.Holes[0]);
                return Mathf.Abs(lo - 2.101f) < 1e-3 && Mathf.Abs(hi - 4.569f) < 1e-3;
            }), "the terrace void");
        }

        [Test]
        public void ImportsStairFlightAsParameters()
        {
            // Flight #25653: 13 risers / 12 treads, sizes written by Revit in feet.
            Assert.AreEqual(1, Building.Stairs.Count);
            Assert.AreEqual(0, Building.SkippedStairs);
            var s = Building.Stairs[0];
            Assert.AreEqual(13, s.Risers);
            Assert.AreEqual(0.175f, s.RiserHeight, 1e-3, "0.5741 ft → 175 mm");
            Assert.AreEqual(0.275f, s.TreadDepth, 1e-3, "0.9022 ft → 275 mm");
            Assert.IsTrue(s.Width > 0.7f && s.Width < 2f, $"plausible flight width, got {s.Width}");
            Assert.AreEqual(RoomPlanner.Stairs.StairKind.Waist, s.Kind,
                "imports default to the waist-slab kind");
            Assert.AreEqual(0, s.StoreyIndex, "storey inherited from the stair via IfcRelAggregates");
        }

        [Test]
        public void ImportsPlumbingTerminalAsBakedMesh()
        {
            // Washbasin #30058 (cyrillic name decodes) — the only MEP the export carries.
            Assert.AreEqual(1, Building.Plumbing.Count);
            Assert.AreEqual(0, Building.SkippedMep);
            var mep = Building.Plumbing[0];
            Assert.Greater(mep.Vertices.Count, 100, "a real fixture Brep");
            Assert.AreEqual(0, mep.Triangles.Count % 3);
            Assert.Greater(mep.Triangles.Count, 0);
            StringAssert.Contains("Умывальник", mep.Name, "unicode directives decoded");
            // local verts are baked around the origin — bbox centre sits at zero
            Vector3 min = mep.Vertices[0], max = mep.Vertices[0];
            foreach (var p in mep.Vertices) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            Assert.AreEqual(0f, ((min + max) * 0.5f).magnitude, 1e-3);
            Assert.Less((max - min).magnitude, 3f, "a washbasin, not a building");
        }

        [Test]
        public void ImportsDoorAndWindowOpenings()
        {
            // Wall #481 hosts a passage door; wall #189 an exterior door AND a window.
            Assert.AreEqual(3, Building.Openings.Count);
            Assert.AreEqual(0, Building.SkippedOpenings);
            Assert.AreEqual(2, Building.Openings.Count(o => o.IsDoor));

            string dump = string.Join("; ", Building.Openings.Select(o =>
                $"{(o.IsDoor ? "door" : "win")} w={o.Width:0.###} h={o.Height:0.###} " +
                $"sill={o.Sill:0.###} at={o.AlongFraction:0.###} wall={o.WallIndex}"));

            // Passage door 700 × 2100 near the far end of the 2.02 m partition #481.
            var door = Building.Openings.FirstOrDefault(o => o.IsDoor && Mathf.Abs(o.Width - 0.7f) < 1e-2);
            Assert.IsNotNull(door, $"no 0.7 m door among: {dump}");
            Assert.AreEqual(2.1f, door.Height, 1e-3);
            Assert.AreEqual(0f, door.Sill, 1e-3, "doors start at the wall base");
            Assert.AreEqual(0.802f, door.AlongFraction, 5e-3);
            var host = Building.Walls[door.WallIndex];
            Assert.AreEqual(2.02f, Vector3.Distance(host.Path[0], host.Path[1]), 1e-3);

            // Exterior door leaf is 750 × 2000 (#64874), but its ROUGH OPENING — what we
            // import — is cut 850 × 2100 (frame + clearances). We model the opening.
            var exterior = Building.Openings.FirstOrDefault(o => o.IsDoor && Mathf.Abs(o.Width - 0.85f) < 1e-2);
            Assert.IsNotNull(exterior, $"no 0.85 m door opening among: {dump}");
            Assert.AreEqual(2.1f, exterior.Height, 1e-2);

            // Double-hung window 310 × 2100 in wall #189.
            var window = Building.Openings.Single(o => !o.IsDoor);
            Assert.AreEqual(0.31f, window.Width, 1e-3);
            Assert.AreEqual(2.1f, window.Height, 1e-3);
            Assert.IsTrue(window.AlongFraction > 0f && window.AlongFraction < 1f);
        }

        [Test]
        public void DoorSwingComesFromStyleAndPlacement()
        {
            // Exterior door #64874: style #64867 is SINGLE_SWING_RIGHT and the placement
            // parent #177 turns X → −X, so the door faces file −Y (Unity −Z) and hinges
            // on the world +X side of its opening.
            var exterior = Building.Openings.First(o => o.IsDoor && Mathf.Abs(o.Width - 0.85f) < 1e-2);
            Assert.AreEqual(0f, Vector3.Distance(exterior.SwingDir, new Vector3(0f, 0f, -1f)), 1e-3);
            Assert.AreEqual(0f, Vector3.Distance(exterior.HingeDir, new Vector3(1f, 0f, 0f)), 1e-3);

            // The passage door's style is not in the fixture — swing stays unknown (closed).
            var passage = Building.Openings.First(o => o.IsDoor && Mathf.Abs(o.Width - 0.7f) < 1e-2);
            Assert.AreEqual(Vector3.zero, passage.SwingDir);

            // Windows never swing.
            var window = Building.Openings.Single(o => !o.IsDoor);
            Assert.AreEqual(Vector3.zero, window.SwingDir);
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
            // (the stair landing is also on L1 — filter to the 4-point extruded slab)
            var slab = Building.Slabs.First(s => s.StoreyIndex == 0 && s.Outline.Count == 4);
            Assert.AreEqual(0f, slab.Level, 1e-3);
        }
    }
}
