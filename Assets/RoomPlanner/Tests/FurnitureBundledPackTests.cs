using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core.Furniture;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Pins whatever packs ship inside the APK. The tests are written against the pack
    /// FORMAT, not against one particular collection: packs come and go (the stylised
    /// starter packs were dropped on 2026-08-15 — "от них толку ноль"), and a test tied to
    /// a specific sofa would have to be rewritten every time the catalog is re-curated.
    /// What must never regress: the manifests parse, every referenced file exists, sizes
    /// stay believable, and the licence of each pack is stated.
    /// </summary>
    public class FurnitureBundledPackTests
    {
        private const string Root = "Assets/StreamingAssets/Furniture";

        private static List<string> PackFolders()
        {
            var list = new List<string>();
            if (!Directory.Exists(Root)) return list;
            foreach (var d in Directory.GetDirectories(Root))
                if (File.Exists(Path.Combine(d, FurnitureCatalogParser.ManifestName))) list.Add(d);
            return list;
        }

        private static FurnitureCollection Load(string folder, out FurnitureParseReport report)
        {
            string manifest = Path.Combine(folder, FurnitureCatalogParser.ManifestName);
            return FurnitureCatalogParser.Parse(File.ReadAllText(manifest),
                FurnitureSource.Bundled, folder, out report);
        }

        [Test]
        public void EveryBundledPack_ParsesCleanly()
        {
            foreach (var folder in PackFolders())
            {
                var c = Load(folder, out var report);
                Assert.NotNull(c, folder);
                Assert.IsFalse(report.HasProblems,
                    $"{folder}: {(report.Problems == null ? "" : string.Join("; ", report.Problems))}");
                Assert.IsFalse(string.IsNullOrEmpty(c.License), $"{folder}: a pack must state its licence");
                Assert.Greater(c.Items.Count, 0, folder);
            }
        }

        [Test]
        public void EveryItem_HasItsFilesAndASaneSize()
        {
            foreach (var folder in PackFolders())
            {
                var c = Load(folder, out _);
                foreach (var item in c.Items)
                {
                    Assert.IsTrue(FurnitureCatalogParser.IsSafeFileName(item.File), $"{c.Id}/{item.Id}");
                    Assert.IsTrue(File.Exists(Path.Combine(folder, item.File)),
                        $"{c.Id}/{item.Id}: {item.File} is missing");
                    if (!string.IsNullOrEmpty(item.Preview))
                        Assert.IsTrue(File.Exists(Path.Combine(folder, item.Preview)),
                            $"{c.Id}/{item.Id}: preview {item.Preview} is missing");

                    Assert.IsTrue(FurnitureCatalogParser.IsSaneSize(item.Size),
                        $"{c.Id}/{item.Id} size {item.Size}");
                    Assert.LessOrEqual(item.Size.y, 3f, $"{c.Id}/{item.Id} is taller than a room");
                }
            }
        }

        [Test]
        public void Index_ListsExactlyWhatShips()
        {
            string index = Path.Combine(Root, FurnitureIndex.FileName);
            if (!File.Exists(index) && PackFolders().Count == 0) Assert.Pass("no bundled packs");
            Assert.IsTrue(File.Exists(index), "the loader cannot enumerate StreamingAssets on Android");

            var ids = FurnitureIndex.Parse(File.ReadAllText(index));
            foreach (var id in ids)
                Assert.IsTrue(Directory.Exists(Path.Combine(Root, id)), $"{id} is indexed but not shipped");
            foreach (var folder in PackFolders())
                CollectionAssert.Contains(ids, Path.GetFileName(folder), "a shipped pack must be indexed");
        }

        [Test]
        public void Packs_CoverTheRoomsAUserFurnishes()
        {
            var folders = PackFolders();
            if (folders.Count == 0) Assert.Pass("no bundled packs");

            var catalog = new FurnitureCatalog();
            var categories = new HashSet<FurnitureCategory>();
            foreach (var folder in folders)
            {
                var c = Load(folder, out _);
                catalog.Add(c);
                foreach (var item in c.Items) categories.Add(item.Category);
            }

            // The point of the catalog is furnishing a flat: seating, tables, storage and
            // beds are the minimum a layout needs.
            foreach (var needed in new[]
            {
                FurnitureCategory.Seating, FurnitureCategory.Table,
                FurnitureCategory.Storage, FurnitureCategory.Bed,
            })
                CollectionAssert.Contains(categories, needed);
        }

        [Test]
        public void Items_AreAddressableAndMostlyIllustrated()
        {
            var folders = PackFolders();
            if (folders.Count == 0) Assert.Pass("no bundled packs");

            foreach (var folder in folders)
            {
                var c = Load(folder, out _);
                var catalog = new FurnitureCatalog();
                catalog.Add(c);

                int withPreview = 0;
                foreach (var item in c.Items)
                {
                    Assert.AreSame(item, catalog.FindItem(item.Key), $"{item.Key} must resolve");
                    if (!string.IsNullOrEmpty(item.Preview)) withPreview++;
                }
                // Picking furniture from names does not work (#83) — a shipped pack is
                // expected to be illustrated, and a pack that is not says so loudly here.
                Assert.GreaterOrEqual(withPreview, c.Items.Count * 0.9f,
                    $"{c.Id}: only {withPreview}/{c.Items.Count} items have previews");
            }
        }
    }
}
