using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core.Furniture;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// The catalog is collection-based (design/27 §1): packs are parsed independently and
    /// addressed as "collection/item". These pin the manifest contract — a pack that ships
    /// one broken row must still deliver its other models, and a bad row must be COUNTED,
    /// not silently turned into a zero-sized object (rules 12 §1.3).
    /// </summary>
    public class FurnitureCatalogTests
    {
        private const string GoodManifest = @"{
            ""Id"": ""kenney-furniture"", ""Title"": ""Kenney Furniture Kit"",
            ""Author"": ""Kenney"", ""License"": ""CC0"",
            ""Items"": [
              { ""Id"":""sofa"", ""Name"":""Sofa"", ""Category"":""Seating"", ""Anchor"":""Floor"",
                ""File"":""sofa.glb"", ""Size"":{""x"":2.1,""y"":0.8,""z"":0.9}, ""Tris"":1240 },
              { ""Id"":""cabinet"", ""Name"":""Wall cabinet"", ""Category"":""Kitchen"", ""Anchor"":""Wall"",
                ""File"":""cabinet.glb"", ""Size"":{""x"":0.6,""y"":0.72,""z"":0.35} },
              { ""Id"":""table"", ""Category"":""Table"",
                ""File"":""table.glb"", ""Size"":{""x"":1.4,""y"":0.75,""z"":0.8} }
            ]}";

        private static FurnitureCollection ParseGood(out FurnitureParseReport report) =>
            FurnitureCatalogParser.Parse(GoodManifest, FurnitureSource.Bundled, "Furniture/kenney", out report);

        [Test]
        public void Parse_ReadsMetadataAndItems()
        {
            var c = ParseGood(out var report);

            Assert.NotNull(c);
            Assert.AreEqual("kenney-furniture", c.Id);
            Assert.AreEqual("Kenney Furniture Kit", c.Title);
            Assert.AreEqual(FurnitureSource.Bundled, c.Source);
            Assert.AreEqual(3, c.Items.Count);
            Assert.AreEqual(3, report.Accepted);
            Assert.IsFalse(report.HasProblems);

            var sofa = c.Items[0];
            Assert.AreEqual(FurnitureCategory.Seating, sofa.Category);
            Assert.AreEqual(FurnitureAnchor.Floor, sofa.Anchor);
            Assert.AreEqual(new Vector3(2.1f, 0.8f, 0.9f), sofa.Size);
            Assert.AreEqual(1240, sofa.Tris);
            Assert.AreEqual("kenney-furniture/sofa", sofa.Key);
        }

        [Test]
        public void Parse_MissingOptionalFields_Degrade()
        {
            var c = ParseGood(out _);
            var table = c.Items[2];

            Assert.AreEqual("table", table.Name);                 // Name falls back to the id
            Assert.AreEqual(FurnitureAnchor.Floor, table.Anchor); // absent anchor = stands on the floor
            Assert.AreEqual(0f, table.YawOffset);
        }

        [Test]
        public void Parse_Cc0PackNeedsNoAttribution_ButCcByDoes()
        {
            Assert.IsFalse(ParseGood(out _).NeedsAttribution);

            var ccby = FurnitureCatalogParser.Parse(
                @"{""Id"":""scopia"", ""License"":""CC-BY 4.0"", ""Items"":[]}",
                FurnitureSource.Cached, "cache/scopia", out _);
            Assert.IsTrue(ccby.NeedsAttribution);
        }

        [Test]
        public void Parse_BrokenRows_AreDroppedAndCounted()
        {
            const string manifest = @"{
                ""Id"":""broken"",
                ""Items"": [
                  { ""Id"":"""", ""File"":""a.glb"", ""Size"":{""x"":1,""y"":1,""z"":1} },
                  { ""Id"":""nofile"", ""Size"":{""x"":1,""y"":1,""z"":1} },
                  { ""Id"":""escape"", ""File"":""../../secrets.glb"", ""Size"":{""x"":1,""y"":1,""z"":1} },
                  { ""Id"":""flat"", ""File"":""flat.glb"", ""Size"":{""x"":1,""y"":0,""z"":1} },
                  { ""Id"":""huge"", ""File"":""huge.glb"", ""Size"":{""x"":40,""y"":1,""z"":1} },
                  { ""Id"":""weird"", ""File"":""weird.glb"", ""Anchor"":""Roof"", ""Size"":{""x"":1,""y"":1,""z"":1} },
                  { ""Id"":""ok"", ""File"":""ok.glb"", ""Size"":{""x"":1,""y"":1,""z"":1} },
                  { ""Id"":""ok"", ""File"":""ok2.glb"", ""Size"":{""x"":1,""y"":1,""z"":1} }
                ]}";

            var c = FurnitureCatalogParser.Parse(manifest, FurnitureSource.Cached, "cache/broken", out var report);

            Assert.AreEqual(1, c.Items.Count, "only the sound row survives");
            Assert.AreEqual("ok", c.Items[0].Id);
            Assert.AreEqual(1, report.Accepted);
            Assert.AreEqual(7, report.Rejected);
            CollectionAssert.Contains(report.Problems, "escape: " + FurnitureRejectReason.UnsafeFile);
            CollectionAssert.Contains(report.Problems, "weird: " + FurnitureRejectReason.UnknownAnchor);
            CollectionAssert.Contains(report.Problems, "ok: " + FurnitureRejectReason.DuplicateId);
        }

        [Test]
        public void Parse_FitDefaultsToUniform_AndReadsStretch()
        {
            var c = FurnitureCatalogParser.Parse(
                @"{""Id"":""p"", ""Items"":[
                   {""Id"":""sofa"", ""File"":""s.glb"", ""Size"":{""x"":2.1,""y"":0.8,""z"":0.9}},
                   {""Id"":""unit"", ""File"":""u.glb"", ""Fit"":""Stretch"", ""Size"":{""x"":0.6,""y"":0.85,""z"":0.6}}]}",
                FurnitureSource.Bundled, "p", out _);

            Assert.AreEqual(FurnitureFit.Uniform, c.Items[0].Fit, "silhouette pieces keep their proportions");
            Assert.AreEqual(FurnitureFit.Stretch, c.Items[1].Fit, "boxy carcasses match every axis");
        }

        [Test]
        public void Parse_UnknownCategory_DegradesToDecor()
        {
            var c = FurnitureCatalogParser.Parse(
                @"{""Id"":""p"", ""Items"":[{""Id"":""x"", ""File"":""x.glb"", ""Category"":""Spaceship"",
                   ""Size"":{""x"":1,""y"":1,""z"":1}}]}",
                FurnitureSource.Bundled, "p", out var report);

            Assert.AreEqual(1, report.Accepted, "an unknown category groups oddly, it does not break physics");
            Assert.AreEqual(FurnitureCategory.Decor, c.Items[0].Category);
        }

        [Test]
        public void Parse_UnusableManifest_ReturnsNullWithReason()
        {
            Assert.IsNull(FurnitureCatalogParser.Parse("", FurnitureSource.Bundled, "x", out var r1));
            Assert.IsTrue(r1.HasProblems);

            Assert.IsNull(FurnitureCatalogParser.Parse("{ not json", FurnitureSource.Bundled, "x", out var r2));
            Assert.IsTrue(r2.HasProblems);

            Assert.IsNull(FurnitureCatalogParser.Parse(@"{""Title"":""no id""}", FurnitureSource.Bundled, "x", out var r3));
            Assert.IsTrue(r3.HasProblems);
        }

        [Test]
        public void Registry_KeepsCollectionsSeparate_AndAddressableByKey()
        {
            var catalog = new FurnitureCatalog();
            var a = FurnitureCatalogParser.Parse(
                @"{""Id"":""packA"", ""Items"":[{""Id"":""chair"", ""File"":""a.glb"", ""Category"":""Seating"",
                   ""Size"":{""x"":0.5,""y"":0.9,""z"":0.5}}]}", FurnitureSource.Bundled, "a", out _);
            var b = FurnitureCatalogParser.Parse(
                @"{""Id"":""packB"", ""Items"":[{""Id"":""chair"", ""File"":""b.glb"", ""Category"":""Seating"",
                   ""Size"":{""x"":0.6,""y"":1.0,""z"":0.6}}]}", FurnitureSource.Cached, "b", out _);

            Assert.IsTrue(catalog.Add(a));
            Assert.IsTrue(catalog.Add(b));
            Assert.AreEqual(2, catalog.Count);

            // Colliding item ids stay distinct: the collection is half of the address.
            Assert.AreEqual("a.glb", catalog.FindItem("packA/chair").File);
            Assert.AreEqual("b.glb", catalog.FindItem("packB/chair").File);
            Assert.IsNull(catalog.FindItem("packC/chair"));
            Assert.IsNull(catalog.FindItem("chair"));
        }

        [Test]
        public void Registry_RejectsDuplicateCollectionId()
        {
            var catalog = new FurnitureCatalog();
            Assert.IsTrue(catalog.Add(new FurnitureCollection { Id = "pack" }));
            Assert.IsFalse(catalog.Add(new FurnitureCollection { Id = "pack" }),
                "a second pack under the same id would make stored keys ambiguous");
            Assert.IsFalse(catalog.Add(new FurnitureCollection { Id = "" }));
            Assert.IsFalse(catalog.Add(null));
            Assert.AreEqual(1, catalog.Count);

            Assert.IsTrue(catalog.Remove("pack"));
            Assert.IsFalse(catalog.Remove("pack"));
        }

        [Test]
        public void Registry_FiltersByCategory_AndListsOnlyPresentCategories()
        {
            var catalog = new FurnitureCatalog();
            catalog.Add(ParseGood(out _));

            var items = new System.Collections.Generic.List<FurnitureItem>();
            catalog.ItemsOf("kenney-furniture", FurnitureCategory.Seating, items);
            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("sofa", items[0].Id);

            catalog.ItemsOf("kenney-furniture", null, items);
            Assert.AreEqual(3, items.Count, "no filter = manifest order");
            Assert.AreEqual("sofa", items[0].Id);
            Assert.AreEqual("table", items[2].Id);

            catalog.ItemsOf("missing-pack", null, items);
            Assert.AreEqual(0, items.Count);

            var cats = new System.Collections.Generic.List<FurnitureCategory>();
            catalog.CategoriesOf("kenney-furniture", cats);
            CollectionAssert.AreEqual(
                new[] { FurnitureCategory.Seating, FurnitureCategory.Table, FurnitureCategory.Kitchen },
                cats, "enum order, and only categories that actually have items");
        }
    }
}
