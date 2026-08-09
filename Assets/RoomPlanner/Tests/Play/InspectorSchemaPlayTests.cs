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

        private InspectorPanel MakePanel(out Transform rowsRoot, out GameObject panelRoot, out GameObject selectionGroup)
        {
            var root = new GameObject("InspectorTest");
            _spawned.Add(root);
            var panel = root.AddComponent<InspectorPanel>();

            panelRoot = new GameObject("Panel");
            panelRoot.transform.SetParent(root.transform, false);
            var rows = new GameObject("Rows");
            rows.transform.SetParent(panelRoot.transform, false);
            selectionGroup = new GameObject("SelectionGroup");
            selectionGroup.transform.SetParent(panelRoot.transform, false);

            SetPrivate(panel, "panelRoot", panelRoot);
            SetPrivate(panel, "rowsRoot", rows.transform);
            SetPrivate(panel, "selectionGroup", selectionGroup);
            rowsRoot = rows.transform;
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
            // top edge stays fixed: center sits at Top − h/2
            Assert.AreEqual(0.22f - long6 * 0.5f, bg.localPosition.y, 1e-4f);
        }

        [Test]
        public void PerInstanceRows_ShiftBelowSelectionGroup()
        {
            int v = 0;
            var schema = new SettingsSchema()
                .Stepper("t", "T", () => v.ToString(), () => v--, () => v++);
            var panel = MakePanel(out var rows, out var panelRoot, out _);
            AddBackground(panel, panelRoot);

            panel.ShowFor(schema, showSelection: false);
            Assert.AreEqual(0f, rows.localPosition.y, 1e-5f, "tool settings sit at the top");

            panel.ShowFor(schema, showSelection: true);
            Assert.Less(rows.localPosition.y, -0.3f, "per-instance rows drop below the selection group");
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
            Assert.AreEqual(6, s.Fields.Count, "Thickness/Height/Angle/Offset/Corner/Place");

            float before = mgr.WallThickness;
            s.Fields[0].Increase();
            Assert.AreEqual(before + 0.02f, mgr.WallThickness, 1e-5f, "stepper mutates the shared store");

            for (int i = 0; i < 100; i++) s.Fields[0].Increase();
            Assert.LessOrEqual(mgr.WallThickness, 1f + 1e-5f, "store clamps stay enforced");

            string joinBefore = mgr.JoinName();
            var join = s.Fields.Count > 4 ? s.Fields[4] : null;
            Assert.IsNotNull(join);
            join.Increase();
            Assert.AreNotEqual(joinBefore, mgr.JoinName(), "cycle advances the mode");
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

            float lvl = mgr.Level;
            s.Fields[0].Increase();
            Assert.AreEqual(lvl + 0.1f, mgr.Level, 1e-5f);
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
            Assert.AreEqual(4, s.Fields.Count, "Plan scale / Rotation / Calibrate / Plan file");

            float scale = bp.PlanScale;
            s.Fields[0].Increase();
            Assert.AreEqual(scale + 0.25f, bp.PlanScale, 1e-5f, "scale stepper mutates the tool's own state");

            float rot = bp.PlanRotationDeg;
            s.Fields[1].Increase();
            Assert.AreEqual(Mathf.Repeat(rot + 5f, 360f), bp.PlanRotationDeg, 1e-5f, "rotation stepper");

            for (int i = 0; i < 300; i++) s.Fields[1].Increase();
            Assert.That(bp.PlanRotationDeg, Is.InRange(0f, 360f), "rotation wraps, never accumulates");
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
    }
}
