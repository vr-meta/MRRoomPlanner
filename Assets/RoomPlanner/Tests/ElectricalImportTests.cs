using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core.Ifc;

namespace RoomPlanner.Tests
{
    /// <summary>Recognizing outlets in an IFC export (#79): name rules and the
    /// thin-plate mounting axis.</summary>
    public class ElectricalImportTests
    {
        [Test]
        public void IsOutlet_MatchesRealRevitNames()
        {
            Assert.IsTrue(ElectricalImport.IsOutlet("Р Розетка:220 V:1980743"),
                "the user's Revit export names");
            Assert.IsTrue(ElectricalImport.IsOutlet("Socket outlet, double"));
            Assert.IsTrue(ElectricalImport.IsOutlet("Power OUTLET 16A"));
        }

        [Test]
        public void IsOutlet_RejectsOtherProducts()
        {
            Assert.IsFalse(ElectricalImport.IsOutlet("Умывальник_Полукруглый"));
            Assert.IsFalse(ElectricalImport.IsOutlet("Handrail - Rectangular"));
            Assert.IsFalse(ElectricalImport.IsOutlet(""));
            Assert.IsFalse(ElectricalImport.IsOutlet(null));
        }

        [Test]
        public void PlateNormal_IsTheThinnestAxis()
        {
            // the user's outlets: 1×5×5 cm plates on X-facing walls
            Assert.AreEqual(Vector3.right, ElectricalImport.PlateNormal(new Vector3(0.01f, 0.05f, 0.05f)));
            Assert.AreEqual(Vector3.forward, ElectricalImport.PlateNormal(new Vector3(0.05f, 0.05f, 0.01f)));
            Assert.AreEqual(Vector3.up, ElectricalImport.PlateNormal(new Vector3(0.05f, 0.005f, 0.05f)),
                "a lying plate mounts up (floor box)");
        }
    }
}
