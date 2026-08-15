using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Core.Ifc;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>Drawing-convention normalizations (#116/#117) applied at import time.</summary>
    public class ImportNormalizationTests
    {
        private static ImportedWall Wall(Vector3 a, Vector3 b, float thickness) =>
            new() { Path = new List<Vector3> { a, b }, Thickness = thickness };

        private static ImportedSlab Square(float size, float level = 0f) =>
            new()
            {
                Level = level,
                Thickness = 0.2f,
                Outline = new List<Vector3>
                {
                    new(0f, level, 0f), new(size, level, 0f),
                    new(size, level, size), new(0f, level, size),
                },
            };

        // ---- #117: slab edges on wall axes extend to the far face ----

        [Test]
        public void SlabEdgeOnWallAxis_ExtendsHalfAThickness()
        {
            var b = new ImportedBuilding();
            var slab = Square(4f);
            b.Slabs.Add(slab);
            // one wall along the z=0 edge, 0.2 thick → that edge moves out to z=-0.1
            b.Walls.Add(Wall(new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f), 0.2f));

            SlabWallAlignment.Apply(b);

            Assert.AreEqual(-0.1f, slab.Outline[0].z, 1e-4, "corner follows the moved edge");
            Assert.AreEqual(-0.1f, slab.Outline[1].z, 1e-4);
            Assert.AreEqual(4f, slab.Outline[2].z, 1e-4, "far edge untouched");
            Assert.AreEqual(0f, slab.Outline[0].x, 1e-4, "un-moved neighbour edge keeps its line");
        }

        [Test]
        public void TwoAxisEdgesAtACorner_IntersectAtTheFarFaces()
        {
            var b = new ImportedBuilding();
            var slab = Square(4f);
            b.Slabs.Add(slab);
            b.Walls.Add(Wall(new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f), 0.2f));  // z=0 edge
            b.Walls.Add(Wall(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 4f), 0.3f));  // x=0 edge

            SlabWallAlignment.Apply(b);

            Assert.AreEqual(-0.15f, slab.Outline[0].x, 1e-4, "corner lands on BOTH far faces");
            Assert.AreEqual(-0.1f, slab.Outline[0].z, 1e-4);
        }

        [Test]
        public void EdgeOffTheAxis_StaysPut()
        {
            var b = new ImportedBuilding();
            var slab = Square(4f);
            b.Slabs.Add(slab);
            // wall 30 cm away from the z=0 edge — not an axis match
            b.Walls.Add(Wall(new Vector3(0f, 0f, 0.3f), new Vector3(4f, 0f, 0.3f), 0.2f));

            SlabWallAlignment.Apply(b);

            Assert.AreEqual(0f, slab.Outline[0].z, 1e-4);
        }

        [Test]
        public void ShortCollinearWall_StillCarriesTheLongEdge()
        {
            var b = new ImportedBuilding();
            var slab = Square(4f);
            b.Slabs.Add(slab);
            // the axis under the 4 m edge is a 1.5 m wall piece — the drawing often
            // splits one edge across several collinear walls (the Project1 terrace)
            b.Walls.Add(Wall(new Vector3(1f, 0f, 0f), new Vector3(2.5f, 0f, 0f), 0.2f));

            SlabWallAlignment.Apply(b);

            Assert.AreEqual(-0.1f, slab.Outline[0].z, 1e-4);
            Assert.AreEqual(-0.1f, slab.Outline[1].z, 1e-4);
        }

        [Test]
        public void ClockwiseOutline_StillExtendsOutward()
        {
            var b = new ImportedBuilding();
            var slab = Square(4f);
            slab.Outline.Reverse();   // CW winding — the source file's choice, not ours
            b.Slabs.Add(slab);
            b.Walls.Add(Wall(new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f), 0.2f));

            SlabWallAlignment.Apply(b);

            float minZ = float.MaxValue;
            foreach (var p in slab.Outline) minZ = Mathf.Min(minZ, p.z);
            Assert.AreEqual(-0.1f, minZ, 1e-4, "outward regardless of winding");
        }

        // ---- #116: a flight ending inside the stairwell hole gets a landing patch ----

        private static ImportedBuilding StairIntoHole()
        {
            var b = new ImportedBuilding();
            var slab = Square(8f, 3f);
            // stairwell hole x 3..5, z 2..6
            slab.Holes.Add(new List<Vector3>
            {
                new(3f, 3f, 2f), new(5f, 3f, 2f), new(5f, 3f, 6f), new(3f, 3f, 6f),
            });
            b.Slabs.Add(slab);
            // flight walking -Z (yaw 180), top edge at z = 5 - 15*0.2... choose numbers:
            // base z=5.75, 15 treads of 0.25 → top edge z = 2.0 + 0.25? compute below
            b.Stairs.Add(new ImportedStair
            {
                Base = new Vector3(4f, 0f, 5.75f),
                YawDeg = 180f,
                Width = 1f,
                Risers = 15,
                RiserHeight = 0.2f,          // topY = 3.0 = slab level
                TreadDepth = 0.23f,          // top edge z = 5.75 - 3.45 = 2.3, inside the hole
            });
            return b;
        }

        [Test]
        public void FlightEndingInsideTheHole_GetsAFlightWidePatch()
        {
            var b = StairIntoHole();
            StairLandingPatch.Apply(b);

            Assert.AreEqual(2, b.Slabs.Count, "one patch added");
            var patch = b.Slabs[1];
            Assert.AreEqual(3f, patch.Level, 1e-4, "at the arrival slab level");
            Assert.AreEqual(4, patch.Outline.Count);
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue;
            foreach (var p in patch.Outline)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z);
            }
            Assert.AreEqual(3.5f, minX, 1e-4, "flight-wide strip");
            Assert.AreEqual(4.5f, maxX, 1e-4);
            // from the top edge (z=2.3) to the hole boundary (z=2) plus the tuck-under
            Assert.AreEqual(2f - StairLandingPatch.Overlap, minZ, 1e-3);
        }

        [Test]
        public void FlightReachingTheSlab_AddsNothing()
        {
            var b = StairIntoHole();
            b.Stairs[0].TreadDepth = 0.27f;   // top edge z = 5.75 - 4.05 = 1.7 — past the hole
            StairLandingPatch.Apply(b);
            Assert.AreEqual(1, b.Slabs.Count);
        }

        [Test]
        public void FlightBelowADifferentStorey_IsIgnored()
        {
            var b = StairIntoHole();
            b.Slabs[0].Level = 6f;            // arrival slab too far above topY=3
            foreach (var p in b.Slabs[0].Outline) { }
            StairLandingPatch.Apply(b);
            Assert.AreEqual(1, b.Slabs.Count);
        }
    }
}
