using System;
using NUnit.Framework;
using RoomPlanner.Core;
using UnityEngine;

namespace RoomPlanner.Tests
{
    public class SettingsSchemaTests
    {
        // ---- numpad entry editing (audit 10 §Б1: no decimal point, zero tests) ----

        [Test]
        public void NumpadPress_DigitsAppend_UpToMaxLen()
        {
            Assert.AreEqual("12", PanelLayout.NumpadPress("1", "2"));
            Assert.AreEqual("1234567", PanelLayout.NumpadPress("1234567", "8"), "capped at 7");
        }

        [Test]
        public void NumpadPress_DecimalPoint_OncePerEntry_StartsWithZero()
        {
            Assert.AreEqual("0.", PanelLayout.NumpadPress("", "."), "leading dot becomes 0.");
            Assert.AreEqual("-0.", PanelLayout.NumpadPress("-", "."), "after the sign too");
            Assert.AreEqual("1.", PanelLayout.NumpadPress("1", "."));
            Assert.AreEqual("1.5", PanelLayout.NumpadPress("1.", "5"));
            Assert.AreEqual("1.5", PanelLayout.NumpadPress("1.5", "."), "second dot refused");
            Assert.AreEqual("0.", PanelLayout.NumpadPress("", ","),
                "localized comma is accepted but stored canonically");
            Assert.AreEqual("1.5", PanelLayout.NumpadPress("1.5", ","),
                "a comma cannot add a second decimal separator");
            Assert.AreEqual("-12,5", PanelLayout.LocalizeNumpadEntry("-12.5", ","));
            Assert.AreEqual("-12.5", PanelLayout.LocalizeNumpadEntry("-12.5", "."));
        }

        [Test]
        public void NumpadPress_SignToggles_BackspaceEats()
        {
            Assert.AreEqual("-27", PanelLayout.NumpadPress("27", "±"));
            Assert.AreEqual("27", PanelLayout.NumpadPress("-27", "±"));
            Assert.AreEqual("2", PanelLayout.NumpadPress("27", "⌫"));
            Assert.AreEqual("", PanelLayout.NumpadPress("", "⌫"), "empty stays empty");
        }

        [Test]
        public void Builder_AddsFieldsInOrder_WithKinds()
        {
            var s = new SettingsSchema()
                .Stepper("a", "Alpha", () => "1", () => { }, () => { })
                .Readout("b", "Beta", () => "x");

            Assert.AreEqual(2, s.Fields.Count);
            Assert.AreEqual("a", s.Fields[0].Id);
            Assert.AreEqual(SettingKind.Stepper, s.Fields[0].Kind);
            Assert.AreEqual("b", s.Fields[1].Id);
            Assert.AreEqual(SettingKind.Readout, s.Fields[1].Kind);
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
        public void NumericStepper_OptsIntoExactEntryWithoutLosingStepActions()
        {
            float value = 10f;
            var field = new SettingsSchema()
                .NumericStepper("v", "Value", 0f, 20f, () => value,
                    (_, after) => value = after, () => value.ToString("0"),
                    () => value--, () => value++)
                .Fields[0];

            Assert.AreEqual(SettingKind.Stepper, field.Kind);
            field.Increase();
            field.CommitNumber(11f, 17f);
            Assert.AreEqual(17f, value);
        }

        // ---- v2 vocabulary (design/20 §2) ----

        [Test]
        public void Slider_CarriesRangeAndDelegates()
        {
            float v = 2.7f, committedBefore = -1f, committedAfter = -1f;
            var s = new SettingsSchema()
                .Slider("h", "Height", 0.2f, 5f, 0.05f,
                    () => v, x => v = x, (b, a) => { committedBefore = b; committedAfter = a; },
                    () => $"{v * 100f:0} cm");

            var f = s.Fields[0];
            Assert.AreEqual(SettingKind.Slider, f.Kind);
            Assert.AreEqual(0.2f, f.Min);
            Assert.AreEqual(5f, f.Max);
            Assert.AreEqual(0.05f, f.Step);
            f.SetNumber(3f);
            Assert.AreEqual(3f, f.GetNumber());
            f.CommitNumber(2.7f, 3f);
            Assert.AreEqual(2.7f, committedBefore);
            Assert.AreEqual(3f, committedAfter);
        }

        [Test]
        public void Segmented_SelectsByIndex()
        {
            int mode = 0;
            var s = new SettingsSchema()
                .Segmented("o", "Offset", new[] { "Outer", "Center", "Inner" },
                    () => mode, i => mode = i);

            var f = s.Fields[0];
            Assert.AreEqual(SettingKind.Segmented, f.Kind);
            Assert.AreEqual(3, f.ResolveOptions().Length);
            f.SetIndex(2);
            Assert.AreEqual(2, f.GetIndex());
        }

        [Test]
        public void Select_ResolvesDynamicOptions()
        {
            var files = new[] { "plan-a.png" };
            var s = new SettingsSchema()
                .Select("f", "Plan file", () => files, () => 0, _ => { });

            var f = s.Fields[0];
            Assert.AreEqual(1, f.ResolveOptions().Length);
            files = new[] { "plan-a.png", "plan-b.png" };   // list changed on disk
            Assert.AreEqual(2, f.ResolveOptions().Length, "provider wins over a stale snapshot");
        }

        [Test]
        public void Toggle_RoutesBool()
        {
            bool on = false;
            var s = new SettingsSchema().Toggle("t", "Ceiling off", () => on, x => on = x);
            s.Fields[0].SetOn(true);
            Assert.IsTrue(s.Fields[0].GetOn());
        }

        [Test]
        public void Action_CanBeDestructive_AndCarriesIcon()
        {
            bool fired = false;
            var s = new SettingsSchema()
                .Action("d", "Clear circuit", "trash", () => fired = true, destructive: true);

            var f = s.Fields[0];
            Assert.AreEqual(SettingKind.Action, f.Kind);
            Assert.IsTrue(f.Destructive);
            Assert.AreEqual("trash", f.IconId);
            f.Increase();
            Assert.IsTrue(fired);
        }

        [Test]
        public void SwatchReadoutProgressHeader_CarryTheirData()
        {
            int pick = 1;
            var s = new SettingsSchema()
                .Swatch("c", "Color", new[] { Color.red, Color.white }, () => pick, i => pick = i)
                .Readout("r", "Total", () => "62.7 m")
                .Progress("p", "Parsing", () => 0.4f)
                .Header("g", "Panel");

            Assert.AreEqual(2, s.Fields[0].Palette.Length);
            Assert.AreEqual("62.7 m", s.Fields[1].Value());
            Assert.AreEqual(0.4f, s.Fields[2].GetProgress(), 1e-5f);
            Assert.AreEqual(SettingKind.Header, s.Fields[3].Kind);
        }

        [Test]
        public void Tabbed_ExposesActivePage()
        {
            int tab = 0;
            var outlet = new SettingsSchema().Stepper("p", "Posts", () => "2", () => { }, () => { });
            var wire = new SettingsSchema().Toggle("c", "Ceiling", () => true, _ => { });
            var s = SettingsSchema.Tabbed(
                new[] { "Outlet", "Wire" }, () => tab, i => tab = i, outlet, wire);

            Assert.IsTrue(s.HasTabs);
            Assert.AreSame(outlet, s.ActivePage());
            s.SelectTab(1);
            Assert.AreSame(wire, s.ActivePage());
            Assert.AreEqual(0, s.Fields.Count, "a tabbed schema has no rows of its own");
        }

        [Test]
        public void Tabbed_MismatchedPages_Throws()
        {
            Assert.Throws<ArgumentException>(() => SettingsSchema.Tabbed(
                new[] { "A", "B" }, () => 0, _ => { }, new SettingsSchema()));
        }

        [Test]
        public void Untabbed_ActivePage_IsSelf()
        {
            var s = new SettingsSchema().Header("h", "X");
            Assert.IsFalse(s.HasTabs);
            Assert.AreSame(s, s.ActivePage());
        }
    }

    /// <summary>The 6 mm grid and slider mapping (design/20 §3).</summary>
    public class PanelLayoutTests
    {
        [Test]
        public void Rows_StepDownFromTheTitle()
        {
            float r0 = PanelLayout.RowY(0, hasTabs: false);
            float r1 = PanelLayout.RowY(1, hasTabs: false);
            Assert.AreEqual(UiTokens.RowStep, r1 - r0, 1e-5f);
            Assert.AreEqual(UiTokens.RowStep,
                PanelLayout.RowY(0, hasTabs: true) - r0, 1e-5f, "tab strip shifts rows down");
        }

        [Test]
        public void PanelHeight_GrowsWithRows()
        {
            float h0 = PanelLayout.PanelHeight(0, false);
            float h3 = PanelLayout.PanelHeight(3, false);
            Assert.AreEqual(3 * UiTokens.RowStep - (UiTokens.RowStep - UiTokens.RowHeight),
                h3 - h0, 1e-5f);
        }

        [Test]
        public void TallPanel_IsCappedAndScrollsWithinItsContent()
        {
            float content = PanelLayout.PanelHeight(20, hasTabs: true);
            Assert.AreEqual(UiTokens.MaxPanelHeight, PanelLayout.VisiblePanelHeight(content));
            Assert.AreEqual(content - UiTokens.MaxPanelHeight,
                PanelLayout.ScrollRange(content), 1e-5f);
            Assert.Greater(PanelLayout.ScrollBy(0f, -1f, 1f, content), 0f);
            Assert.AreEqual(PanelLayout.ScrollRange(content),
                PanelLayout.ScrollBy(999f, -1f, 1f, content), 1e-5f);
        }

        [Test]
        public void Columns_SplitCaptionControl()
        {
            Assert.Less(PanelLayout.CaptionX, PanelLayout.ControlLeft);
            Assert.Less(PanelLayout.ControlLeft, PanelLayout.ControlRight);
            Assert.AreEqual(UiTokens.CaptionColumn,
                (PanelLayout.ControlLeft - PanelLayout.CaptionX) / PanelLayout.ContentWidth, 1e-4f);
        }

        [Test]
        public void Slider_RoundTripsThroughT()
        {
            Assert.AreEqual(0.5f, PanelLayout.SliderT(2.6f, 0.2f, 5f), 1e-5f);
            Assert.AreEqual(2.6f, PanelLayout.SliderValue(0.5f, 0.2f, 5f, 0f), 1e-5f);
        }

        [Test]
        public void Slider_SnapsToDetents_AndClamps()
        {
            Assert.AreEqual(2.6f, PanelLayout.SliderValue(0.501f, 0.2f, 5f, 0.05f), 1e-5f);
            Assert.AreEqual(0.2f, PanelLayout.SliderValue(-1f, 0.2f, 5f, 0.05f));
            Assert.AreEqual(5f, PanelLayout.SliderValue(2f, 0.2f, 5f, 0.05f));
            Assert.AreEqual(0f, PanelLayout.SliderT(1f, 3f, 3f), "degenerate range");
        }

        [Test]
        public void Scroll_KeepsSelectionVisible_AndClamps()
        {
            Assert.AreEqual(0, PanelLayout.ScrollTo(2, 5));
            Assert.AreEqual(0, PanelLayout.ScrollTo(0, 20));
            Assert.AreEqual(13, PanelLayout.ScrollTo(19, 20));
            Assert.AreEqual(6, PanelLayout.ScrollTo(9, 20));
            Assert.AreEqual(13, PanelLayout.ClampScroll(99, 20));
            Assert.AreEqual(0, PanelLayout.ClampScroll(-5, 20));
        }
    }
}
