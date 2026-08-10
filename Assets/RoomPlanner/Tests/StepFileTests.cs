using NUnit.Framework;
using RoomPlanner.Core.Ifc;

namespace RoomPlanner.Tests
{
    public class StepFileTests
    {
        private const string Doc = @"ISO-10303-21;
HEADER;
FILE_SCHEMA(('IFC2X3'));
ENDSEC;
DATA;
#19=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);
#3=IFCCARTESIANPOINT((0.,0.,0.));
#5=IFCWALL('has;semicolon',#3,'multi
line',$,$,$,$,$);
ENDSEC;
END-ISO-10303-21;
";

        [Test]
        public void IndexesRecordsAndSkipsHeader()
        {
            var f = StepFile.Parse(Doc);
            Assert.AreEqual(3, f.Count);
            Assert.AreEqual("IFCCARTESIANPOINT", f.TypeOf(3));
            Assert.IsFalse(f.Has(999));
            Assert.IsNull(f.Args(999));
        }

        [Test]
        public void SurvivesSemicolonsInStringsAndMultilineRecords()
        {
            var f = StepFile.Parse(Doc);
            var a = f.Args(5);
            Assert.AreEqual("has;semicolon", a[0].Text);
            StringAssert.Contains("line", a[2].Text);
        }

        [Test]
        public void DetectsMilliLengthUnit()
        {
            var f = StepFile.Parse(Doc);
            Assert.AreEqual(0.001, f.LengthToMeters, 1e-12);
        }

        [Test]
        public void DefaultsToMetersWithoutUnits()
        {
            var f = StepFile.Parse("DATA;\n#1=IFCWALL($);\nENDSEC;");
            Assert.AreEqual(1.0, f.LengthToMeters, 1e-12);
        }

        [Test]
        public void DetectsImperialFeetViaConversionBasedUnit()
        {
            // Audit 09 §Б3: imperial exports have no SI length unit — the factor lives in
            // IFCCONVERSIONBASEDUNIT → IFCMEASUREWITHUNIT. Assuming metres made the
            // model 3.28× too large, silently.
            var f = StepFile.Parse(@"DATA;
#10=IFCDIMENSIONALEXPONENTS(1,0,0,0,0,0,0);
#11=IFCMEASUREWITHUNIT(IFCRATIOMEASURE(0.3048),#12);
#12=IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.);
#13=IFCCONVERSIONBASEDUNIT(#10,.LENGTHUNIT.,'FOOT',#11);
ENDSEC;");
            // #12 (the SI metre) is only the conversion's BASE unit — the conversion
            // must win, which is why DetectUnits probes it before IFCSIUNIT.
            Assert.AreEqual(0.3048, f.LengthToMeters, 1e-9);
        }

        [Test]
        public void OfTypeAndDeref()
        {
            var f = StepFile.Parse(Doc);
            Assert.AreEqual(1, f.OfType("IFCWALL").Count);
            Assert.AreEqual(0, f.OfType("IFCDOOR").Count);
            var wall = f.Args(f.OfType("IFCWALL")[0]);
            var pt = f.Deref(wall[1]); // #3
            Assert.AreEqual(StepKind.List, pt[0].Kind);
        }
    }
}
