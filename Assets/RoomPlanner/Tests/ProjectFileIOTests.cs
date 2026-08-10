using System.IO;
using NUnit.Framework;
using RoomPlanner.Core.Project;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Crash-safe autosave IO (audit 2026-08-10, 12 §Б1): atomic replace keeps the
    /// previous version as .bak; a corrupt main file is quarantined, not recycled.
    /// </summary>
    public class ProjectFileIOTests
    {
        private string _dir;
        private string Main => Path.Combine(_dir, "autosave.rp.json");

        [SetUp]
        public void MakeTempDir()
        {
            _dir = Path.Combine(Path.GetTempPath(), "rp-io-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void DropTempDir()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        [Test]
        public void FirstWrite_CreatesTheFile_NoBackupYet()
        {
            ProjectFileIO.WriteAtomic(Main, "v1");
            Assert.AreEqual("v1", File.ReadAllText(Main));
            Assert.IsFalse(File.Exists(ProjectFileIO.BackupPath(Main)));
            Assert.IsFalse(File.Exists(Main + ".tmp"), "the temp file must not linger");
        }

        [Test]
        public void SecondWrite_KeepsThePreviousVersionAsBackup()
        {
            ProjectFileIO.WriteAtomic(Main, "v1");
            ProjectFileIO.WriteAtomic(Main, "v2");
            Assert.AreEqual("v2", File.ReadAllText(Main));
            Assert.AreEqual("v1", File.ReadAllText(ProjectFileIO.BackupPath(Main)),
                "a kill mid-write can always fall back to the previous save");
        }

        [Test]
        public void Quarantine_SetsTheCorruptFileAside()
        {
            File.WriteAllText(Main, "{trunca");
            ProjectFileIO.QuarantineCorrupt(Main);
            Assert.IsFalse(File.Exists(Main));
            Assert.AreEqual("{trunca", File.ReadAllText(Main + ProjectFileIO.CorruptSuffix),
                "the evidence survives for a bug report");
        }

        [Test]
        public void Quarantine_ReplacesAnOlderQuarantineFile()
        {
            File.WriteAllText(Main + ProjectFileIO.CorruptSuffix, "old");
            File.WriteAllText(Main, "new");
            ProjectFileIO.QuarantineCorrupt(Main);
            Assert.AreEqual("new", File.ReadAllText(Main + ProjectFileIO.CorruptSuffix));
        }
    }
}
