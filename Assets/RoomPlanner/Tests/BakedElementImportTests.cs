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
#60=IFCCARTESIANPOINT((0.,0.));
#61=IFCAXIS2PLACEMENT2D(#60,$);
#62=IFCRECTANGLEPROFILEDEF(.AREA.,$,#61,1.,0.5);
#63=IFCAXIS2PLACEMENT3D(#34,$,$);
#64=IFCDIRECTION((0.,0.,1.));
#65=IFCEXTRUDEDAREASOLID(#62,#63,#64,0.4);
#66=IFCSHAPEREPRESENTATION($,'Body','SweptSolid',(#65));
#67=IFCPRODUCTDEFINITIONSHAPE($,$,(#66));
#68=IFCFURNISHINGELEMENT('f2',$,'Box',$,$,#36,#67,$);
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

        private static ImportedMep[] CubeElements() =>
            Building.Plumbing.Where(m => m.Name != "Box").ToArray();

        [Test]
        public void ImportsFurnitureProxyAndRailingAsBakedMeshes()
        {
            Assert.AreEqual(4, Building.Plumbing.Count);
            Assert.AreEqual(0, Building.SkippedMep);
            CollectionAssert.AreEquivalent(
                new[] { MepCategory.Proxy, MepCategory.Furniture, MepCategory.Railing },
                CubeElements().Select(m => m.Category).ToArray());

            foreach (var m in CubeElements())
            {
                Assert.AreEqual(24, m.Vertices.Count, "6 quad faces, 4 corners each");
                Assert.AreEqual(36, m.Triangles.Count, "2 triangles per face");
                // baked around the bbox centre of the unit cube
                Assert.AreEqual(0f, Vector3.Distance(m.Origin, new Vector3(0.5f, 0.5f, 0.5f)), 1e-4);
            }
            Assert.AreEqual("Sofa", CubeElements().Single(m => m.Category == MepCategory.Furniture).Name);
        }

        [Test]
        public void StyledItemSuppliesColourAndTransparency()
        {
            foreach (var m in CubeElements())
            {
                Assert.IsTrue(m.HasColor, $"{m.Category} carries the file colour");
                Assert.AreEqual(0.8f, m.Color.r, 1e-4);
                Assert.AreEqual(0.2f, m.Color.g, 1e-4);
                Assert.AreEqual(0.1f, m.Color.b, 1e-4);
                Assert.AreEqual(0.5f, m.Transparency, 1e-4);
            }
        }

        [Test]
        public void ExtrudedSolidFurnitureTessellates()
        {
            // IKEA-style furniture ships as SweptSolid extrusions, not Breps: a 1 × 0.5
            // rectangle extruded 0.4 up → a box of 8 verts, 4 side quads + 2 caps.
            var box = Building.Plumbing.Single(m => m.Name == "Box");
            Assert.AreEqual(MepCategory.Furniture, box.Category);
            Assert.AreEqual(8, box.Vertices.Count);
            Assert.AreEqual(36, box.Triangles.Count);
            Assert.IsFalse(box.HasColor, "no styled item on the extrusion");

            Vector3 min = box.Vertices[0], max = box.Vertices[0];
            foreach (var p in box.Vertices) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            var size = max - min;
            Assert.AreEqual(1f, size.x, 1e-4);
            Assert.AreEqual(0.4f, size.y, 1e-4, "extrusion depth becomes Unity height");
            Assert.AreEqual(0.5f, size.z, 1e-4);
        }
    }
}
