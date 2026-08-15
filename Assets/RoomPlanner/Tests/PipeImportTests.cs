using NUnit.Framework;
using RoomPlanner.Core.Ifc;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>#118: IfcFlowSegment circle extrusions become native pipes with exact
    /// axis endpoints and radius; non-circle segments fall through to the baked path.</summary>
    public class PipeImportTests
    {
        private const string PipeDoc = @"DATA;
#19=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);
#3=IFCCARTESIANPOINT((0.,0.));
#2=IFCAXIS2PLACEMENT2D(#3,$);
#1=IFCCIRCLEPROFILEDEF(.AREA.,$,#2,55.);
#4=IFCCARTESIANPOINT((1000.,2000.,0.));
#5=IFCDIRECTION((0.,0.,1.));
#6=IFCAXIS2PLACEMENT3D(#4,$,$);
#7=IFCEXTRUDEDAREASOLID(#1,#6,#5,2700.);
#8=IFCSHAPEREPRESENTATION($,'Body','SweptSolid',(#7));
#9=IFCPRODUCTDEFINITIONSHAPE($,$,(#8));
#40=IFCCARTESIANPOINT((0.,0.,0.));
#41=IFCAXIS2PLACEMENT3D(#40,$,$);
#10=IFCLOCALPLACEMENT($,#41);
#11=IFCFLOWSEGMENT('g',$,'Pipe Types:PVC - DWV:1',$,'Pipe Types:PVC - DWV',#10,#9,'1');
ENDSEC;";

        [Test]
        public void CircleExtrusionSegment_BecomesANativePipe()
        {
            var b = IfcImporter.Import(StepFile.Parse(PipeDoc));
            Assert.AreEqual(1, b.Pipes.Count);
            var p = b.Pipes[0];
            // IFC (1, 2, 0) m Z-up → Unity (1, 0, 2) Y-up; the 2.7 m rise goes to +Y
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(1f, 0f, 2f), p.Start), 1e-3f);
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(1f, 2.7f, 2f), p.End), 1e-3f);
            Assert.AreEqual(0.055f, p.Radius, 1e-4f, "55 mm circle radius survives the unit scale");
            StringAssert.Contains("PVC - DWV", p.Name);
            Assert.AreEqual(0, b.Plumbing.Count, "a native pipe is not ALSO baked");
        }

        [Test]
        public void RectangleProfileSegment_IsNotAPipe()
        {
            string doc = PipeDoc.Replace(
                "#1=IFCCIRCLEPROFILEDEF(.AREA.,$,#2,55.);",
                "#1=IFCRECTANGLEPROFILEDEF(.AREA.,$,#2,100.,200.);");
            var b = IfcImporter.Import(StepFile.Parse(doc));
            Assert.AreEqual(0, b.Pipes.Count, "ducts/trays are not drain pipes");
        }

        [Test]
        public void AbsurdRadius_IsRefused()
        {
            string doc = PipeDoc.Replace(",55.);", ",900.);");   // 0.9 m — not a drain pipe
            var b = IfcImporter.Import(StepFile.Parse(doc));
            Assert.AreEqual(0, b.Pipes.Count);
        }
    }
}
