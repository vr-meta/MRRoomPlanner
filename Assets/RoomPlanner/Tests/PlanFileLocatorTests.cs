using System.IO;
using System.Threading;
using NUnit.Framework;
using RoomPlanner.Core;

namespace RoomPlanner.Tests
{
    public class PlanFileLocatorTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "PlanFileLocatorTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private string Touch(string dir, string name)
        {
            Directory.CreateDirectory(dir);
            string p = Path.Combine(dir, name);
            File.WriteAllText(p, "x");
            return p;
        }

        [Test]
        public void FindsImages_AcrossDirectories_IgnoringOtherExtensions()
        {
            string a = Path.Combine(_root, "a");
            string b = Path.Combine(_root, "b");
            Touch(a, "plan.png");
            Touch(a, "notes.txt");
            Touch(b, "photo.JPG");     // extension match is case-insensitive
            Touch(b, "model.gltf");

            var found = PlanFileLocator.FindImages(new[] { a, b });

            Assert.AreEqual(2, found.Count);
            Assert.IsTrue(found.Exists(f => f.EndsWith("plan.png")));
            Assert.IsTrue(found.Exists(f => f.EndsWith("photo.JPG")));
        }

        [Test]
        public void SortsNewestFirst_AndCaps()
        {
            string dir = Path.Combine(_root, "d");
            string old = Touch(dir, "old.png");
            File.SetLastWriteTimeUtc(old, System.DateTime.UtcNow.AddHours(-2));
            string mid = Touch(dir, "mid.jpeg");
            File.SetLastWriteTimeUtc(mid, System.DateTime.UtcNow.AddHours(-1));
            string fresh = Touch(dir, "fresh.jpg");

            var all = PlanFileLocator.FindImages(new[] { dir });
            Assert.AreEqual(fresh, all[0], "newest first");
            Assert.AreEqual(old, all[2]);

            var capped = PlanFileLocator.FindImages(new[] { dir }, max: 2);
            Assert.AreEqual(2, capped.Count);
            Assert.AreEqual(fresh, capped[0]);
        }

        [Test]
        public void MissingAndNullDirectories_AreSkippedSilently()
        {
            string real = Path.Combine(_root, "real");
            Touch(real, "plan.png");

            var found = PlanFileLocator.FindImages(new[]
                { null, "", Path.Combine(_root, "missing"), real });

            Assert.AreEqual(1, found.Count);
        }
    }
}
