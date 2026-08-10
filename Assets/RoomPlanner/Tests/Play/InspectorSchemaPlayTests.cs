using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core;
using RoomPlanner.Tools;
using RoomPlanner.Walls;
using RoomPlanner.Floors;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// PlayMode coverage for the schema-driven UI layer (design/14-modularity.md):
    /// InspectorPanel generates rows from a SettingsSchema at runtime, buttons carry
    /// runtime OnClick delegates on the menu layer, and the real tool schemas bind to
    /// ToolManager's shared parameter store.
    /// </summary>
    public class InspectorSchemaPlayTests
    {
        private const int MenuLayer = 2;

        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.Destroy(go);
            _spawned.Clear();
        }

        // Serialized fields are wired by the Editor-only setup; tests inject them directly.
        private static void SetPrivate(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, $"missing serialized field '{field}' on {target.GetType().Name}");
            f.SetValue(target, value);
        }

        private Transform _tabsRoot;

        private InspectorPanel MakePanel(out Transform rowsRoot, out GameObject panelRoot, out GameObject selectionGroup)
        {
            var root = new GameObject("InspectorTest");
            _spawned.Add(root);
            var panel = root.AddComponent<InspectorPanel>();

            panelRoot = new GameObject("Panel");
            panelRoot.transform.SetParent(root.transform, false);
            var rows = new GameObject("Rows");
            rows.transform.SetParent(panelRoot.transform, false);
            var tabs = new GameObject("Tabs");
            tabs.transform.SetParent(panelRoot.transform, false);
            selectionGroup = new GameObject("SelectionGroup");
            selectionGroup.transform.SetParent(panelRoot.transform, false);

            SetPrivate(panel, "panelRoot", panelRoot);
            SetPrivate(panel, "rowsRoot", rows.transform);
            SetPrivate(panel, "tabsRoot", tabs.transform);
            SetPrivate(panel, "selectionGroup", selectionGroup);
            rowsRoot = rows.transform;
            _tabsRoot = tabs.transform;
            return panel;
        }

        // ---- row generation ----

        [Test]
        public void StepperRow_BuildsCaptionButtonsValue_OnMenuLayer()
        {
            int v = 5;
            var schema = new SettingsSchema()
                .Stepper("t", "Test", () => v.ToString(), () => v--, () => v++);
            var panel = MakePanel(out var rows, out _, out _);

            panel.ShowFor(schema, showSelection: false);

            Assert.AreEqual(4, rows.childCount, "caption + [−] + value + [+]");
            var buttons = rows.GetComponentsInChildren<MenuButton>(true);
            Assert.AreEqual(2, buttons.Length);
            foreach (var b in buttons)
            {
                Assert.IsNotNull(b.OnClick, "schema buttons are runtime-bound");
                Assert.IsNotNull(b.GetComponent<BoxCollider>(), "button needs a hit target");
                Assert.AreEqual(MenuLayer, b.gameObject.layer, "buttons must sit on the menu layer");
                Assert.IsTrue(b.Repeatable, "stepper −/+ must auto-repeat on hold (UX v2 P0.2)");
            }
        }

        [Test]
        public void CycleRow_BuildsCaptionValueButton_NotRepeatable()
        {
            int i = 0;
            var names = new[] { "Miter", "Bevel" };
            var schema = new SettingsSchema()
                .Cycle("j", "Corner", () => names[i], () => i = (i + 1) % names.Length);
            var panel = MakePanel(out var rows, out _, out _);

            panel.ShowFor(schema, showSelection: false);

            Assert.AreEqual(3, rows.childCount, "caption + value + [>]");
            var buttons = rows.GetComponentsInChildren<MenuButton>(true);
            Assert.AreEqual(1, buttons.Length);
            Assert.IsFalse(buttons[0].Repeatable, "holding on a cycle must not spin modes");
        }

        [Test]
        public void Click_MutatesValue_AndRefreshUpdatesLabel()
        {
            int v = 5;
            var schema = new SettingsSchema()
                .Stepper("t", "Test", () => v.ToString(), () => v--, () => v++);
            var panel = MakePanel(out var rows, out _, out _);
            panel.ShowFor(schema, showSelection: false);

            var buttons = rows.GetComponentsInChildren<MenuButton>(true);
            var plus = System.Array.Find(buttons, b => b.name.EndsWith("+"));
            Assert.IsNotNull(plus);
            plus.OnClick();
            Assert.AreEqual(6, v, "OnClick routes to the schema delegate");

            panel.RefreshValues();
            bool labelShowsNewValue = false;
            foreach (var l in rows.GetComponentsInChildren<TMP_Text>(true))
                if (l.text == "6") labelShowsNewValue = true;
            Assert.IsTrue(labelShowsNewValue, "RefreshValues pushes the new value into the label");
        }

        [UnityTest]
        public IEnumerator Rebind_ReplacesRows()
        {
            int a = 0, b = 0;
            var first = new SettingsSchema().Stepper("a", "A", () => a.ToString(), () => a--, () => a++);
            var second = new SettingsSchema()
                .Cycle("c1", "C1", () => b.ToString(), () => b++)
                .Cycle("c2", "C2", () => b.ToString(), () => b++);
            var panel = MakePanel(out var rows, out _, out _);

            panel.ShowFor(first, showSelection: false);
            panel.ShowFor(second, showSelection: false);
            yield return null;   // deferred Destroy of the old rows applies

            Assert.AreEqual(6, rows.childCount, "two cycle rows: 2 × (caption + value + [>])");
            Assert.AreEqual(2, rows.GetComponentsInChildren<MenuButton>(true).Length);
        }

        // ---- v2 widget rows (design/20 §2) ----

        [Test]
        public void SliderRow_BuildsWidgetWithCollider_PreviewAndCommitRoute()
        {
            float v = 2.7f;
            float committedAfter = -1f;
            var schema = new SettingsSchema()
                .Slider("h", "Height", 0.2f, 5f, 0.05f,
                    () => v, x => v = x, (_, a) => committedAfter = a,
                    () => $"{v * 100f:0} cm", displayScale: 100f);
            var panel = MakePanel(out var rows, out _, out _);

            panel.ShowFor(schema, showSelection: false);

            var widget = rows.GetComponentInChildren<SliderWidget>(true);
            Assert.IsNotNull(widget, "slider rows carry a SliderWidget for drag capture");
            Assert.IsNotNull(widget.GetComponent<BoxCollider>(), "the whole track is the hit zone");
            Assert.AreEqual(MenuLayer, widget.gameObject.layer);

            // simulate the ToolManager drag: preview snaps to detents, release commits ONE value
            widget.BeginDrag();
            widget.PreviewAt(widget.GetComponent<BoxCollider>().size.x * 0.5f);
            Assert.AreNotEqual(2.7f, v, "preview moves the value");
            widget.EndDrag();
            Assert.AreEqual(v, committedAfter, 1e-5f, "release commits the final value once");
        }

        [Test]
        public void SegmentedRow_BuildsOneButtonPerOption()
        {
            int mode = 0;
            var schema = new SettingsSchema()
                .Segmented("o", "Offset", new[] { "Outer", "Center", "Inner" },
                    () => mode, i => mode = i);
            var panel = MakePanel(out var rows, out _, out _);

            panel.ShowFor(schema, showSelection: false);

            var buttons = rows.GetComponentsInChildren<MenuButton>(true);
            Assert.AreEqual(3, buttons.Length, "every option is its own visible button");
            buttons[2].OnClick();
            Assert.AreEqual(2, mode, "clicking an option selects it by index");
        }

        [Test]
        public void ToggleRow_ClickFlipsTheBool()
        {
            bool on = false;
            var schema = new SettingsSchema().Toggle("t", "Scan", () => on, x => on = x);
            var panel = MakePanel(out var rows, out _, out _);

            panel.ShowFor(schema, showSelection: false);

            var button = rows.GetComponentsInChildren<MenuButton>(true);
            Assert.AreEqual(1, button.Length);
            button[0].OnClick();
            Assert.IsTrue(on);
            button[0].OnClick();
            Assert.IsFalse(on);
        }

        [UnityTest]
        public IEnumerator TabbedSchema_BuildsStrip_AndSwitchesPages()
        {
            int tab = 0, posts = 2;
            bool ceiling = false;
            var outlet = new SettingsSchema()
                .Stepper("posts", "Posts", () => posts.ToString(), () => posts--, () => posts++);
            var wire = new SettingsSchema()
                .Toggle("coff", "Ceiling", () => ceiling, x => ceiling = x);
            var schema = SettingsSchema.Tabbed(
                new[] { "Outlet", "Wire" }, () => tab, i => tab = i, outlet, wire);
            var panel = MakePanel(out var rows, out _, out _);

            panel.ShowFor(schema, showSelection: false);
            Assert.AreEqual(2, _tabsRoot.GetComponentsInChildren<MenuButton>(true).Length,
                "one tab button per page");
            Assert.AreEqual(2, rows.GetComponentsInChildren<MenuButton>(true).Length,
                "outlet page: [−][+]");

            // click the second tab — the page below must rebuild to the wire rows
            var tabButtons = _tabsRoot.GetComponentsInChildren<MenuButton>(true);
            tabButtons[1].OnClick();
            yield return null;   // deferred destroy of the old rows

            Assert.AreEqual(1, tab, "tab click routes through SelectTab");
            Assert.AreEqual(1, rows.GetComponentsInChildren<MenuButton>(true).Length,
                "wire page: the toggle row");
        }

        [Test]
        public void ActionRow_Destructive_IsMarkedForHoldToFire()
        {
            bool fired = false;
            var schema = new SettingsSchema()
                .Action("d", "New project", "trash", () => fired = true, destructive: true);
            var panel = MakePanel(out var rows, out _, out _);

            panel.ShowFor(schema, showSelection: false);

            var b = rows.GetComponentInChildren<MenuButton>(true);
            Assert.IsTrue(b.Destructive, "destructive actions must demand the 0.5 s hold");
            Assert.IsFalse(fired, "building the row must not fire the action");
        }

        [Test]
        public void SelectionOnly_ShowsSelectionGroup_HidesRows()
        {
            var panel = MakePanel(out var rows, out var panelRoot, out var selectionGroup);

            panel.ShowFor(null, showSelection: true);

            Assert.IsTrue(panelRoot.activeSelf);
            Assert.IsTrue(selectionGroup.activeSelf);
            Assert.IsFalse(rows.gameObject.activeSelf, "no schema → rows hidden");
        }

        [Test]
        public void NoSchemaNoSelection_HidesPanel()
        {
            var panel = MakePanel(out _, out var panelRoot, out _);
            panel.ShowFor(null, showSelection: false);
            Assert.IsFalse(panelRoot.activeSelf);
        }

        // ---- background auto-height (UX v2 P0.4) ----

        private static Transform AddBackground(InspectorPanel panel, GameObject panelRoot)
        {
            var bg = new GameObject("Bg");
            bg.transform.SetParent(panelRoot.transform, false);
            bg.transform.localScale = new Vector3(0.36f, 0.44f, 1f);
            SetPrivate(panel, "background", bg.transform);
            return bg.transform;
        }

        [Test]
        public void Background_ShrinksForShortSchemas_AndGrowsForLong()
        {
            int v = 0;
            SettingsSchema Make(int n)
            {
                var s = new SettingsSchema();
                for (int i = 0; i < n; i++)
                    s.Stepper($"f{i}", $"F{i}", () => v.ToString(), () => v--, () => v++);
                return s;
            }
            var panel = MakePanel(out _, out var panelRoot, out _);
            var bg = AddBackground(panel, panelRoot);

            panel.ShowFor(Make(2), showSelection: false);
            float short2 = bg.localScale.y;
            panel.ShowFor(Make(6), showSelection: false);
            float long6 = bg.localScale.y;

            Assert.Less(short2, long6, "background height follows the row count");
            Assert.Less(short2, 0.30f, "2 rows must not keep the full-size quad");
            Assert.AreEqual(PanelLayout.PanelHeight(6, hasTabs: false), long6, 1e-4f,
                "auto-height follows the 6 mm grid (design/20 §3.3)");
            // v2 panel origin is TOP-center: the quad hangs down from y = 0
            Assert.AreEqual(-long6 * 0.5f, bg.localPosition.y, 1e-4f);
        }

        [UnityTest]
        public IEnumerator PerInstanceRows_ShiftBelowSelectionGroup()
        {
            int v = 0;
            var schema = new SettingsSchema()
                .Stepper("t", "T", () => v.ToString(), () => v--, () => v++);
            var panel = MakePanel(out var rows, out var panelRoot, out _);
            AddBackground(panel, panelRoot);

            panel.ShowFor(schema, showSelection: false);
            float topAlone = RowTop(rows);
            panel.ShowFor(schema, showSelection: true);
            yield return null;   // deferred Destroy of the re-bound rows applies
            float topWithSelection = RowTop(rows);

            Assert.Less(topWithSelection, topAlone - 0.10f,
                "per-instance rows drop below the selection band (design/20 §3)");
        }

        private static float RowTop(Transform rows)
        {
            float top = float.MinValue;
            foreach (Transform child in rows)
                if (child != null) top = Mathf.Max(top, child.localPosition.y);
            return top;
        }

        // ---- real tool schemas against the real shared store ----

        private ToolManager MakeManager()
        {
            var go = new GameObject("Manager");
            _spawned.Add(go);
            return go.AddComponent<ToolManager>();
        }

        [Test]
        public void WallSchema_BindsToManagerStore_AndClamps()
        {
            var mgr = MakeManager();
            var wallGo = new GameObject("WallCtl");
            _spawned.Add(wallGo);
            var wall = wallGo.AddComponent<WallController>();
            SetPrivate(wall, "manager", mgr);

            var s = wall.GetSettings();
            Assert.IsNotNull(s);
            // No "Corner" row: graph joints always miter, the row changed nothing (audit B8).
            Assert.AreEqual(5, s.Fields.Count, "Thickness/Height/Angle/Offset/Place");

            // v2 (design/20 §2.2): thickness is a slider — preview + commit route to the store
            var thk = s.Fields[0];
            Assert.AreEqual(SettingKind.Slider, thk.Kind);
            thk.SetNumber(0.30f);
            Assert.AreEqual(0.30f, mgr.WallThickness, 1e-5f, "slider preview mutates the shared store");
            thk.CommitNumber(0.30f, 5f);
            Assert.LessOrEqual(mgr.WallThickness, 1f + 1e-5f, "store clamps stay enforced");

            // §2.3: offset mode is segmented — all options visible, set by index
            var off = s.Fields[3];
            Assert.AreEqual(SettingKind.Segmented, off.Kind);
            Assert.AreEqual(3, off.ResolveOptions().Length);
            off.SetIndex(2);
            Assert.AreEqual(2, mgr.OffsetModeIndex, "segmented sets the mode by index");
        }

        [Test]
        public void FloorSchema_BindsToManagerStore()
        {
            var mgr = MakeManager();
            var floorGo = new GameObject("FloorCtl");
            _spawned.Add(floorGo);
            var floor = floorGo.AddComponent<FloorController>();
            SetPrivate(floor, "manager", mgr);

            var s = floor.GetSettings();
            Assert.IsNotNull(s);
            Assert.AreEqual(2, s.Fields.Count, "Level/Thickness — plan placement moved to the Blueprint tool");

            // v2: Level is a numeric field (numpad) — commit writes the store
            Assert.AreEqual(SettingKind.Numeric, s.Fields[0].Kind);
            s.Fields[0].CommitNumber(0f, 2.8f);
            Assert.AreEqual(2.8f, mgr.Level, 1e-5f);
        }

        [Test]
        public void BlueprintSchema_OwnsItsState_NoManagerNeeded()
        {
            // The Blueprint tool is the first whose parameters live in the tool itself
            // (design/14 §"что остаётся связанным") — its schema must work with NO manager.
            var go = new GameObject("BlueprintCtl");
            _spawned.Add(go);
            var bp = go.AddComponent<BlueprintController>();

            var s = bp.GetSettings();
            Assert.IsNotNull(s);
            Assert.AreEqual(6, s.Fields.Count,
                "Plan scale / Rotation / Plan file / Load / Calibrate / Status");

            var scale = s.Fields[0];
            Assert.AreEqual(SettingKind.Slider, scale.Kind);
            scale.CommitNumber(bp.PlanScale, 3.25f);
            Assert.AreEqual(3.25f, bp.PlanScale, 1e-5f, "scale slider mutates the tool's own state");

            float rot = bp.PlanRotationDeg;
            s.Fields[1].Increase();
            Assert.AreEqual(Mathf.Repeat(rot + 5f, 360f), bp.PlanRotationDeg, 1e-5f, "rotation stepper");

            for (int i = 0; i < 300; i++) s.Fields[1].Increase();
            Assert.That(bp.PlanRotationDeg, Is.InRange(0f, 360f), "rotation wraps, never accumulates");

            // §2.4: the file row is a Select with a dynamic options provider
            Assert.AreEqual(SettingKind.Select, s.Fields[2].Kind);
            Assert.IsNotNull(s.Fields[2].ResolveOptions());
        }

        [Test]
        public void BlueprintCalibration_TwoPairs_AlignThePlan()
        {
            var go = new GameObject("BlueprintCtl");
            _spawned.Add(go);
            var bp = go.AddComponent<BlueprintController>();

            // where two plan features SHOULD end up (known target placement)
            var target = new BlueprintPlacement { Scale = 2f, RotationDeg = 90f, OriginX = 1f, OriginZ = -2f };
            var current = new BlueprintPlacement
            {
                Scale = bp.PlanScale, RotationDeg = bp.PlanRotationDeg,
                OriginX = bp.PlanOffsetX, OriginZ = bp.PlanOffsetZ,
            };
            var uvA = new Vector2(0.2f, 0.3f);
            var uvB = new Vector2(0.8f, 0.5f);

            bp.BeginCalibration();
            Assert.AreEqual(0, bp.CalibrationStep);
            bp.CalibratePoint(BlueprintMath.PlanUVToWorld(uvA, 0f, current));   // A on projected plan
            bp.CalibratePoint(BlueprintMath.PlanUVToWorld(uvA, 0f, target));    // A where it must be
            bp.CalibratePoint(BlueprintMath.PlanUVToWorld(uvB, 0f, current));   // B on projected plan
            bp.CalibratePoint(BlueprintMath.PlanUVToWorld(uvB, 0f, target));    // B where it must be

            Assert.AreEqual(-1, bp.CalibrationStep, "calibration completes after the 4th point");
            Assert.AreEqual(target.Scale, bp.PlanScale, 1e-3f);
            Assert.AreEqual(target.RotationDeg, bp.PlanRotationDeg, 1e-2f);
            Assert.AreEqual(target.OriginX, bp.PlanOffsetX, 1e-3f);
            Assert.AreEqual(target.OriginZ, bp.PlanOffsetZ, 1e-3f);
        }

        [Test]
        public void BlueprintCalibration_ScaleOutsideSliderRange_IsRefused()
        {
            // Audit 07 §Б1: the solved similarity used to be applied unclamped — after a
            // bad calibration the scale slider pointed outside its own track.
            var go = new GameObject("BlueprintCtl");
            _spawned.Add(go);
            var bp = go.AddComponent<BlueprintController>();
            float scale0 = bp.PlanScale, ox0 = bp.PlanOffsetX;

            var current = new BlueprintPlacement
            {
                Scale = scale0, RotationDeg = bp.PlanRotationDeg,
                OriginX = ox0, OriginZ = bp.PlanOffsetZ,
            };
            var wild = new BlueprintPlacement { Scale = 100f };   // far past the 50 m maximum
            var uvA = new Vector2(0.2f, 0.3f);
            var uvB = new Vector2(0.8f, 0.5f);

            bp.BeginCalibration();
            bp.CalibratePoint(BlueprintMath.PlanUVToWorld(uvA, 0f, current));
            bp.CalibratePoint(BlueprintMath.PlanUVToWorld(uvA, 0f, wild));
            bp.CalibratePoint(BlueprintMath.PlanUVToWorld(uvB, 0f, current));
            bp.CalibratePoint(BlueprintMath.PlanUVToWorld(uvB, 0f, wild));

            Assert.AreEqual(-1, bp.CalibrationStep, "the gesture still ends");
            Assert.AreEqual(scale0, bp.PlanScale, 1e-4f, "wild scale refused — placement kept");
            Assert.AreEqual(ox0, bp.PlanOffsetX, 1e-4f);
        }
    }
}
