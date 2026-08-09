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
            }
        }

        [Test]
        public void CycleRow_BuildsCaptionValueButton()
        {
            int i = 0;
            var names = new[] { "Miter", "Bevel" };
            var schema = new SettingsSchema()
                .Cycle("j", "Corner", () => names[i], () => i = (i + 1) % names.Length);
            var panel = MakePanel(out var rows, out _, out _);

            panel.ShowFor(schema, showSelection: false);

            Assert.AreEqual(3, rows.childCount, "caption + value + [>]");
            Assert.AreEqual(1, rows.GetComponentsInChildren<MenuButton>(true).Length);
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
            Assert.AreEqual(3, s.Fields.Count, "Level/Thickness/Plan scale");

            float lvl = mgr.Level;
            s.Fields[0].Increase();
            Assert.AreEqual(lvl + 0.1f, mgr.Level, 1e-5f);

            float plan = mgr.PlanScale;
            s.Fields[2].Decrease();
            Assert.AreEqual(plan - 0.25f, mgr.PlanScale, 1e-5f);
        }
    }
}
