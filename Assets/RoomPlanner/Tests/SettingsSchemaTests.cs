using NUnit.Framework;
using RoomPlanner.Core;

namespace RoomPlanner.Tests
{
    public class SettingsSchemaTests
    {
        [Test]
        public void Builder_AddsFieldsInOrder_WithKinds()
        {
            var s = new SettingsSchema()
                .Stepper("a", "Alpha", () => "1", () => { }, () => { })
                .Cycle("b", "Beta", () => "x", () => { });

            Assert.AreEqual(2, s.Fields.Count);
            Assert.AreEqual("a", s.Fields[0].Id);
            Assert.AreEqual(SettingKind.Stepper, s.Fields[0].Kind);
            Assert.AreEqual("b", s.Fields[1].Id);
            Assert.AreEqual(SettingKind.Cycle, s.Fields[1].Kind);
        }

        [Test]
        public void Stepper_DelegatesRoute_ToDecreaseAndIncrease()
        {
            int v = 10;
            var s = new SettingsSchema()
                .Stepper("v", "Value", () => v.ToString(), () => v--, () => v++);

            var f = s.Fields[0];
            f.Increase();
            f.Increase();
            f.Decrease();
            Assert.AreEqual(11, v);
            Assert.AreEqual("11", f.Value());
        }

        [Test]
        public void Cycle_UsesIncreaseAsNext_AndHasNoDecrease()
        {
            int i = 0;
            var names = new[] { "Miter", "Bevel", "Round" };
            var s = new SettingsSchema()
                .Cycle("j", "Corner", () => names[i], () => i = (i + 1) % names.Length);

            var f = s.Fields[0];
            Assert.IsNull(f.Decrease, "cycle rows have no decrease action");
            f.Increase();
            Assert.AreEqual("Bevel", f.Value());
        }
    }
}
