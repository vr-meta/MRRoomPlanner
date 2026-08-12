using System.IO;
using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core.Furniture;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Pins the packs that ship inside the APK (issue #69): the manifests parse, every
    /// item's model file is really there, and the curated real-world sizes stay sane.
    /// A pack that quietly loses models or grows a 40 m sofa would only show up in the
    /// headset otherwise.
    /// </summary>
    public class FurnitureBundledPackTests
    {
        private const string Root = "Assets/StreamingAssets/Furniture";
        private const string KenneyId = "kenney-furniture";

        private static FurnitureCollection Load(string collectionId, out FurnitureParseReport report)
        {
            string folder = Path.Combine(Root, collectionId);
            string manifest = Path.Combine(folder, FurnitureCatalogParser.ManifestName);
            Assert.IsTrue(File.Exists(manifest), $"missing manifest {manifest} — run RoomPlanner/Furniture/Build bundled catalogs");
            return FurnitureCatalogParser.Parse(File.ReadAllText(manifest), FurnitureSource.Bundled, folder, out report);
        }

        [Test]
        public void KenneyPack_ParsesCleanly()
        {
            var c = Load(KenneyId, out var report);

            Assert.NotNull(c);
            Assert.AreEqual(KenneyId, c.Id);
            Assert.AreEqual("CC0", c.License);
            Assert.IsFalse(c.NeedsAttribution, "a CC0 pack must not force a credits screen");
            Assert.IsFalse(report.HasProblems, report.Problems == null ? "" : string.Join("; ", report.Problems));
            Assert.GreaterOrEqual(c.Items.Count, 100, "the curated Kenney pack ships ~120 items");
        }

        [Test]
        public void KenneyPack_EveryItemHasItsModelFile()
        {
            var c = Load(KenneyId, out _);
            string folder = Path.Combine(Root, KenneyId);

            foreach (var item in c.Items)
            {
                Assert.IsTrue(FurnitureCatalogParser.IsSafeFileName(item.File), item.Id);
                Assert.IsTrue(File.Exists(Path.Combine(folder, item.File)), $"{item.Id}: {item.File} is missing");
            }
        }

        [Test]
        public void KenneyPack_SizesAreRealistic()
        {
            var c = Load(KenneyId, out _);

            foreach (var item in c.Items)
            {
                Assert.IsTrue(FurnitureCatalogParser.IsSaneSize(item.Size), $"{item.Id} size {item.Size}");
                Assert.LessOrEqual(item.Size.y, 2.5f, $"{item.Id} is taller than a room");
            }

            // Spot-checks against the real world — the whole point of curating sizes.
            AssertSize(c, "bedDouble", new Vector3(1.60f, 0.50f, 2.00f));
            AssertSize(c, "kitchenCabinet", new Vector3(0.60f, 0.85f, 0.60f));
            AssertSize(c, "toilet", new Vector3(0.38f, 0.78f, 0.68f));
            AssertSize(c, "bathtub", new Vector3(1.70f, 0.60f, 0.75f));
        }

        [Test]
        public void KenneyPack_WallHungItemsAreAnchoredToWalls()
        {
            var c = Load(KenneyId, out _);

            Assert.AreEqual(FurnitureAnchor.Wall, Item(c, "kitchenCabinetUpper").Anchor);
            Assert.AreEqual(FurnitureAnchor.Wall, Item(c, "bathroomMirror").Anchor);
            Assert.AreEqual(FurnitureAnchor.Wall, Item(c, "hoodLarge").Anchor);
            Assert.AreEqual(FurnitureAnchor.Ceiling, Item(c, "ceilingFan").Anchor);
            Assert.AreEqual(FurnitureAnchor.Floor, Item(c, "loungeSofa").Anchor);
        }

        [Test]
        public void KenneyPack_CarcassesStretch_SilhouettesStayProportional()
        {
            var c = Load(KenneyId, out _);

            Assert.AreEqual(FurnitureFit.Stretch, Item(c, "kitchenCabinet").Fit, "worktop height must be exact");
            Assert.AreEqual(FurnitureFit.Stretch, Item(c, "kitchenFridge").Fit);
            Assert.AreEqual(FurnitureFit.Uniform, Item(c, "loungeSofa").Fit, "a stretched sofa reads as broken");
            Assert.AreEqual(FurnitureFit.Uniform, Item(c, "chair").Fit);
        }

        [Test]
        public void KenneyPack_ShipsNoArchitecture()
        {
            var c = Load(KenneyId, out _);

            // Walls, floors, doorways and stairs are built parametrically by the app —
            // catalog copies of them would compete with the real tools.
            foreach (var forbidden in new[] { "wall", "floorFull", "doorway", "stairs", "paneling" })
                Assert.IsNull(FindItem(c, forbidden), $"{forbidden} must stay excluded from the catalog");
        }

        [Test]
        public void KenneyPack_CoversTheRoomsAUserFurnishes()
        {
            var c = Load(KenneyId, out _);
            var catalog = new FurnitureCatalog();
            catalog.Add(c);

            var cats = new System.Collections.Generic.List<FurnitureCategory>();
            catalog.CategoriesOf(KenneyId, cats);
            foreach (var needed in new[]
            {
                FurnitureCategory.Seating, FurnitureCategory.Table, FurnitureCategory.Storage,
                FurnitureCategory.Bed, FurnitureCategory.Kitchen, FurnitureCategory.Bath,
                FurnitureCategory.Appliance, FurnitureCategory.Decor,
            })
                CollectionAssert.Contains(cats, needed);
        }

        [Test]
        public void PolyHavenPack_ShipsPhotorealModelsAtTheirOwnScale()
        {
            var c = Load("polyhaven-interior", out var report);

            Assert.NotNull(c);
            Assert.AreEqual("CC0", c.License);
            Assert.IsFalse(report.HasProblems, report.Problems == null ? "" : string.Join("; ", report.Problems));
            Assert.GreaterOrEqual(c.Items.Count, 30);

            string folder = Path.Combine(Root, "polyhaven-interior");
            foreach (var item in c.Items)
            {
                // These assets keep their own folder: a .gltf needs its textures next to it.
                StringAssert.Contains("/", item.File, $"{item.Id} should live in its own folder");
                Assert.IsTrue(File.Exists(Path.Combine(folder, item.File)), $"{item.Id}: {item.File} missing");
                // Poly Haven authors in metres, so no size correction is needed at all.
                Assert.AreEqual(FurnitureFit.Uniform, item.Fit, item.Id);
                Assert.IsTrue(FurnitureCatalogParser.IsSaneSize(item.Size), $"{item.Id} size {item.Size}");
                Assert.LessOrEqual(item.Size.y, 2.5f, $"{item.Id} is taller than a room");
            }
        }

        [Test]
        public void Index_ListsEveryBundledPack()
        {
            string index = Path.Combine(Root, RoomPlanner.Core.Furniture.FurnitureIndex.FileName);
            Assert.IsTrue(File.Exists(index), "the loader cannot enumerate StreamingAssets on Android");

            var ids = RoomPlanner.Core.Furniture.FurnitureIndex.Parse(File.ReadAllText(index));
            CollectionAssert.Contains(ids, KenneyId);
            CollectionAssert.Contains(ids, "polyhaven-interior");
            foreach (var id in ids)
                Assert.IsTrue(Directory.Exists(Path.Combine(Root, id)), $"{id} is indexed but not shipped");
        }

        private static FurnitureItem Item(FurnitureCollection c, string id)
        {
            var item = FindItem(c, id);
            Assert.NotNull(item, $"{id} missing from the pack");
            return item;
        }

        private static FurnitureItem FindItem(FurnitureCollection c, string id)
        {
            foreach (var i in c.Items) if (i.Id == id) return i;
            return null;
        }

        private static void AssertSize(FurnitureCollection c, string id, Vector3 expected)
        {
            var size = Item(c, id).Size;
            Assert.AreEqual(expected.x, size.x, 0.001f, id + " width");
            Assert.AreEqual(expected.y, size.y, 0.001f, id + " height");
            Assert.AreEqual(expected.z, size.z, 0.001f, id + " depth");
        }
    }
}
