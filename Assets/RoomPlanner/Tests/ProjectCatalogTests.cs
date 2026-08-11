using NUnit.Framework;
using RoomPlanner.Core.Project;

namespace RoomPlanner.Tests
{
    /// <summary>Naming rules of the named-project catalog (#58, design/06 «Проекты v1») —
    /// generated names, file mapping and the numeric-aware list order.</summary>
    public class ProjectCatalogTests
    {
        [Test]
        public void NextName_StartsAtOne_AndCounts()
        {
            Assert.AreEqual("Project 1", ProjectCatalog.NextName(new string[0]));
            Assert.AreEqual("Project 3",
                ProjectCatalog.NextName(new[] { "Project 1", "Project 2" }));
        }

        [Test]
        public void NextName_ReusesDeletedSlot()
        {
            Assert.AreEqual("Project 2",
                ProjectCatalog.NextName(new[] { "Project 1", "Project 3" }));
        }

        [Test]
        public void NextName_IgnoresForeignNames()
        {
            Assert.AreEqual("Project 1", ProjectCatalog.NextName(new[] { "Kitchen" }));
        }

        [Test]
        public void FileName_NameOf_RoundTrip()
        {
            Assert.AreEqual("Project 5.rp.json", ProjectCatalog.FileName("Project 5"));
            Assert.AreEqual("Project 5", ProjectCatalog.NameOf("Project 5.rp.json"));
        }

        [Test]
        public void NameOf_RejectsAutosaveAndForeignFiles()
        {
            Assert.IsNull(ProjectCatalog.NameOf("autosave.rp.json"), "reserved name");
            Assert.IsNull(ProjectCatalog.NameOf("plan.ifc"), "wrong extension");
            Assert.IsNull(ProjectCatalog.NameOf(".rp.json"), "empty name");
            Assert.IsNull(ProjectCatalog.NameOf(null));
        }

        [Test]
        public void CompareNames_NumericAware()
        {
            Assert.Less(ProjectCatalog.CompareNames("Project 2", "Project 10"), 0,
                "2 before 10 — not ordinal");
            Assert.Less(ProjectCatalog.CompareNames("Project 1", "Kitchen"), 0,
                "generated names before custom ones");
            Assert.Greater(ProjectCatalog.CompareNames("Zoo", "Kitchen"), 0,
                "custom names ordinal");
        }
    }
}
