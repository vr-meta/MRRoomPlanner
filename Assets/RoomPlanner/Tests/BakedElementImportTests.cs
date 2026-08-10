using System.Linq;
using NUnit.Framework;
using RoomPlanner.Core.Ifc;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Baked-mesh elements beyond plumbing (design/18 I17): furniture, proxies and
    /// railings import as Breps, and IfcStyledItem supplies colour + transparency.
    /// Runs on a synthetic STEP document — a unit cube shared by all three elements.
    /// </summary>
    public class BakedElementImportTests
    {
        // No unit assignment → file units are metres (scale 1). Coordinates are IFC
        // Z-up; the cube spans 0..1 on every axis so the Unity swap changes nothing.
        private const string Doc = @"ISO-10303-21;
HEADER;
ENDSEC;
DATA;
#1=IFCCARTESIANPOINT((0.,0.,0.));
#2=IFCCARTESIANPOINT((1.,0.,0.));
#3=IFCCARTESIANPOINT((1.,1.,0.));
#4=IFCCARTESIANPOINT((0.,1.,0.));
#5=IFCCARTESIANPOINT((0.,0.,1.));
#6=IFCCARTESIANPOINT((1.,0.,1.));
#7=IFCCARTESIANPOINT((1.,1.,1.));
#8=IFCCARTESIANPOINT((0.,1.,1.));
#10=IFCPOLYLOOP((#1,#4,#3,#2));
#11=IFCFACEOUTERBOUND(#10,.T.);
#12=IFCFACE((#11));
#13=IFCPOLYLOOP((#5,#6,#7,#8));
#14=IFCFACEOUTERBOUND(#13,.T.);
#15=IFCFACE((#14));
#16=IFCPOLYLOOP((#1,#2,#6,#5));
#17=IFCFACEOUTERBOUND(#16,.T.);
#18=IFCFACE((#17));
#19=IFCPOLYLOOP((#3,#4,#8,#7));
#20=IFCFACEOUTERBOUND(#19,.T.);
#21=IFCFACE((#20));
#22=IFCPOLYLOOP((#2,#3,#7,#6));
#23=IFCFACEOUTERBOUND(#22,.T.);
#24=IFCFACE((#23));
#25=IFCPOLYLOOP((#4,#1,#5,#8));
#26=IFCFACEOUTERBOUND(#25,.T.);
#27=IFCFACE((#26));
#30=IFCCLOSEDSHELL((#12,#15,#18,#21,#24,#27));
#31=IFCFACETEDBREP(#30);
#32=IFCSHAPEREPRESENTATION($,'Body','Brep',(#31));
#33=IFCPRODUCTDEFINITIONSHAPE($,$,(#32));
#34=IFCCARTESIANPOINT((0.,0.,0.));
#35=IFCAXIS2PLACEMENT3D(#34,$,$);
#36=IFCLOCALPLACEMENT($,#35);
#40=IFCBUILDINGELEMENTPROXY('p1',$,'ShowerBox',$,$,#36,#33,$,$);
#41=IFCFURNISHINGELEMENT('f1',$,'Sofa',$,$,#36,#33,$);
#42=IFCRAILING('r1',$,'Rail',$,$,#36,#33,$,.HANDRAIL.);
#50=IFCCOLOURRGB($,0.8,0.2,0.1);
#51=IFCSURFACESTYLERENDERING(#50,0.5,$,$,$,$,$,.FLAT.);
#52=IFCSURFACESTYLE('Glassy',.BOTH.,(#51));
#53=IFCPRESENTATIONSTYLEASSIGNMENT((#52));
#54=IFCSTYLEDITEM(#31,(#53),$);
ENDSEC;
END-ISO-10303-21;";

        private static ImportedBuilding _building;

        private static ImportedBuilding Building =>
            _building ??= IfcImporter.Import(StepFile.Parse(Doc));

        [Test]
        public void ImportsFurnitureProxyAndRailingAsBakedMeshes()
        {
            Assert.AreEqual(3, Building.Plumbing.Count);
            Assert.AreEqual(0, Building.SkippedMep);
            CollectionAssert.AreEquivalent(
                new[] { MepCategory.Proxy, MepCategory.Furniture, MepCategory.Railing },
                Building.Plumbing.Select(m => m.Category).ToArray());

            foreach (var m in Building.Plumbing)
            {
                Assert.AreEqual(24, m.Vertices.Count, "6 quad faces, 4 corners each");
                Assert.AreEqual(36, m.Triangles.Count, "2 triangles per face");
                // baked around the bbox centre of the unit cube
                Assert.AreEqual(0f, Vector3.Distance(m.Origin, new Vector3(0.5f, 0.5f, 0.5f)), 1e-4);
            }
            Assert.AreEqual("Sofa", Building.Plumbing.Single(m => m.Category == MepCategory.Furniture).Name);
        }

        [Test]
        public void StyledItemSuppliesColourAndTransparency()
        {
            foreach (var m in Building.Plumbing)
            {
                Assert.IsTrue(m.HasColor, $"{m.Category} carries the file colour");
                Assert.AreEqual(0.8f, m.Color.r, 1e-4);
                Assert.AreEqual(0.2f, m.Color.g, 1e-4);
                Assert.AreEqual(0.1f, m.Color.b, 1e-4);
                Assert.AreEqual(0.5f, m.Transparency, 1e-4);
            }
        }
    }
}
