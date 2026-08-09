using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Core.Ifc;

namespace RoomPlanner.Tests
{
    public class StepTokenizerTests
    {
        [Test]
        public void ParsesRecordFrame()
        {
            Assert.IsTrue(StepTokenizer.TryParseRecord(
                "#150=IFCWALLSTANDARDCASE('gid',#18,'name',$,$,#134,#149,'tag')",
                out int id, out string type, out string body));
            Assert.AreEqual(150, id);
            Assert.AreEqual("IFCWALLSTANDARDCASE", type);
            StringAssert.StartsWith("'gid'", body);
        }

        [Test]
        public void RejectsNonRecords()
        {
            Assert.IsFalse(StepTokenizer.TryParseRecord("FILE_SCHEMA(('IFC2X3'))", out _, out _, out _));
            Assert.IsFalse(StepTokenizer.TryParseRecord("#12", out _, out _, out _));
        }

        [Test]
        public void ParsesScalars()
        {
            var args = StepTokenizer.ParseArgs("$,*,#42,3.5,-2.,.MILLI.,'text'");
            Assert.AreEqual(7, args.Count);
            Assert.AreEqual(StepKind.Null, args[0].Kind);
            Assert.AreEqual(StepKind.Star, args[1].Kind);
            Assert.AreEqual(42, args[2].Ref);
            Assert.AreEqual(3.5, args[3].Number, 1e-12);
            Assert.AreEqual(-2.0, args[4].Number, 1e-12);
            Assert.AreEqual("MILLI", args[5].Text);
            Assert.AreEqual("text", args[6].Text);
        }

        [Test]
        public void ParsesExponentNumbers()
        {
            var args = StepTokenizer.ParseArgs("1.E-05,2.5e3");
            Assert.AreEqual(1e-5, args[0].Number, 1e-18);
            Assert.AreEqual(2500.0, args[1].Number, 1e-9);
        }

        [Test]
        public void ParsesNestedLists()
        {
            var args = StepTokenizer.ParseArgs("((#1,#2),(3.,4.))");
            Assert.AreEqual(1, args.Count);
            Assert.AreEqual(StepKind.List, args[0].Kind);
            Assert.AreEqual(2, args[0].Count);
            Assert.AreEqual(2, args[0][0].Count);
            Assert.AreEqual(2, args[0][0][1].Ref);
            Assert.AreEqual(4.0, args[0][1][1].Number, 1e-12);
        }

        [Test]
        public void ParsesTypedValues()
        {
            var args = StepTokenizer.ParseArgs("IFCPLANEANGLEMEASURE(0.017453292519943278)");
            Assert.AreEqual(StepKind.Typed, args[0].Kind);
            Assert.AreEqual("IFCPLANEANGLEMEASURE", args[0].Text);
            Assert.AreEqual(0.017453292519943278, args[0][0].Number, 1e-15);
        }

        [Test]
        public void DecodesQuoteEscape()
        {
            var args = StepTokenizer.ParseArgs("'it''s'");
            Assert.AreEqual("it's", args[0].Text);
        }

        [Test]
        public void DecodesUnicodeDirective()
        {
            // Revit writes cyrillic as big-endian UTF-16: \X2\0423\X0\ = 'У'
            var args = StepTokenizer.ParseArgs(@"'\X2\04230423\X0\ ok'");
            Assert.AreEqual("УУ ok", args[0].Text);
        }

        [Test]
        public void KeepsCommasAndParensInsideStrings()
        {
            var args = StepTokenizer.ParseArgs("'a,b(c)',#5");
            Assert.AreEqual("a,b(c)", args[0].Text);
            Assert.AreEqual(5, args[1].Ref);
        }
    }
}
