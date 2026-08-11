using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core.Ifc;

namespace RoomPlanner.Tests
{
    /// <summary>Placement of an imported building at a marked point (#60): the anchor
    /// is the footprint's bottom-center, Translate shifts every world datum and only
    /// those, MoveTo stands the anchor exactly on the target.</summary>
    public class ImportPlacementTests
    {
        private static ImportedBuilding Sample()
        {
            var b = new ImportedBuilding();
            b.Storeys.Add(new ImportedStorey { Name = "L1", Elevation = 0f });
            b.Walls.Add(new ImportedWall
            {
                Path = { new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f) },
                Thickness = 0.2f, Height = 3f, BaseHeight = 0f,
            });
            b.Slabs.Add(new ImportedSlab
            {
                Outline =
                {
                    new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f),
                    new Vector3(4f, 0f, 6f), new Vector3(0f, 0f, 6f),
                },
                Holes = { new System.Collections.Generic.List<Vector3>
                {
                    new Vector3(1f, 0f, 1f), new Vector3(2f, 0f, 1f), new Vector3(2f, 0f, 2f),
                } },
                Thickness = 0.2f, Level = 0f,
            });
            b.Stairs.Add(new ImportedStair { Base = new Vector3(3f, 0f, 5f), YawDeg = 90f });
            b.Plumbing.Add(new ImportedMep
            {
                Origin = new Vector3(1f, 0.4f, 5f),
                Vertices = { new Vector3(0.1f, 0f, 0.1f) },
            });
            b.Openings.Add(new ImportedOpening { WallIndex = 0, AlongFraction = 0.5f, Sill = 0.9f });
            return b;
        }

        [Test]
        public void Anchor_IsFootprintBottomCenter()
        {
            var a = ImportPlacement.Anchor(Sample());
            Assert.AreEqual(2f, a.x, 1e-5f, "XZ center of the 0..4 footprint");
            Assert.AreEqual(3f, a.z, 1e-5f, "XZ center of the 0..6 footprint");
            Assert.AreEqual(-0.2f, a.y, 1e-5f, "lowest base = slab underside");
        }

        [Test]
        public void Translate_ShiftsEveryWorldDatum_AndOnlyThose()
        {
            var b = Sample();
            var d = new Vector3(10f, 2f, -5f);
            ImportPlacement.Translate(b, d);

            Assert.AreEqual(new Vector3(10f, 2f, -5f), b.Walls[0].Path[0]);
            Assert.AreEqual(2f, b.Walls[0].BaseHeight, 1e-5f);
            Assert.AreEqual(2f, b.Storeys[0].Elevation, 1e-5f);
            Assert.AreEqual(new Vector3(10f, 2f, 1f), b.Slabs[0].Outline[3]);
            Assert.AreEqual(new Vector3(11f, 2f, -4f), b.Slabs[0].Holes[0][0]);
            Assert.AreEqual(2f, b.Slabs[0].Level, 1e-5f);
            Assert.AreEqual(new Vector3(13f, 2f, 0f), b.Stairs[0].Base);
            Assert.AreEqual(new Vector3(11f, 2.4f, 0f), b.Plumbing[0].Origin);

            // wall-relative and local data must NOT move
            Assert.AreEqual(0.5f, b.Openings[0].AlongFraction, 1e-5f);
            Assert.AreEqual(0.9f, b.Openings[0].Sill, 1e-5f);
            Assert.AreEqual(new Vector3(0.1f, 0f, 0.1f), b.Plumbing[0].Vertices[0]);
        }

        [Test]
        public void MoveTo_StandsAnchorOnTarget()
        {
            var b = Sample();
            var target = new Vector3(20f, 1.5f, 8f);
            ImportPlacement.MoveTo(b, target);
            var a = ImportPlacement.Anchor(b);
            Assert.AreEqual(target.x, a.x, 1e-4f);
            Assert.AreEqual(target.y, a.y, 1e-4f);
            Assert.AreEqual(target.z, a.z, 1e-4f);
        }

        [Test]
        public void EmptyBuilding_AnchorsAtOrigin_TranslateIsSafe()
        {
            var b = new ImportedBuilding();
            Assert.AreEqual(Vector3.zero, ImportPlacement.Anchor(b));
            ImportPlacement.MoveTo(b, new Vector3(5f, 0f, 5f));   // must not throw
        }
    }
}
