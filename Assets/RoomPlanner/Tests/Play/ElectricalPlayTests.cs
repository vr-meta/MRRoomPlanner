using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Electrical;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// PlayMode coverage for the Electric tool (design/19-electrical.md): sub-mode schemas,
    /// fixture/wire selection adapters, the fixture→wire attachment following moves, the
    /// panel BOM summary and the undoable parameter commands.
    /// </summary>
    public class ElectricalPlayTests
    {
        private readonly List<GameObject> _spawned = new();
        private SceneModel _model;

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("SceneModelTest");
            _spawned.Add(go);
            _model = go.AddComponent<SceneModel>();
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.Destroy(go);
            _spawned.Clear();
        }

        private ElectricFixture MakeFixture(FixtureKind kind, int posts = 1, int keys = 1)
        {
            var go = new GameObject($"Fixture-{kind}");
            _spawned.Add(go);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            var fx = go.AddComponent<ElectricFixture>();
            fx.Build(kind, posts, keys);
            go.AddComponent<ElectricFixtureParameters>();
            go.AddComponent<Selectable>();          // last: Resolve must see the fixture
            _model.Register(go.GetComponent<Selectable>());
            return fx;
        }

        private WireRoute MakeRoute(CableType cable, params Vector3[] pts)
        {
            var go = new GameObject("Route");
            _spawned.Add(go);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            var route = go.AddComponent<WireRoute>();
            route.Build(new List<Vector3>(pts), cable);
            go.AddComponent<WireRouteParameters>();
            go.AddComponent<RouteHandles>();
            go.AddComponent<Selectable>();
            _model.Register(go.GetComponent<Selectable>());
            return route;
        }

        private static string IdOf(Component c) => c.GetComponent<Selectable>().Id;

        // ---- sub-modes are TABS on one schema (design/20 §2.12) ----

        [Test]
        public void ElectricSchema_TabbedSubModes_SwitchPages()
        {
            var go = new GameObject("Electric");
            _spawned.Add(go);
            var tool = go.AddComponent<ElectricController>();

            var schema = tool.GetSettings();
            Assert.AreSame(schema, tool.GetSettings(), "one tabbed root instance, always");
            Assert.IsTrue(schema.HasTabs, "sub-modes are tabs, not swapped schemas");
            CollectionAssert.AreEqual(new[] { "Outlet", "Switch", "Wire", "Box", "Panel" }, schema.Tabs);

            Assert.AreEqual(0, schema.ActiveTab());
            CollectionAssert.AreEqual(new[] { "posts", "oh", "ofinish" },
                schema.ActivePage().Fields.Select(f => f.Id).ToArray());

            schema.SelectTab(1);
            CollectionAssert.AreEqual(new[] { "keys", "sh", "sfinish" },
                schema.ActivePage().Fields.Select(f => f.Id).ToArray());

            schema.SelectTab(2);
            // v2: no "Ceiling off" row — wires are routed by hand, never auto-lifted
            CollectionAssert.AreEqual(new[] { "cable", "routing" },
                schema.ActivePage().Fields.Select(f => f.Id).ToArray());
            Assert.AreEqual(SettingKind.Segmented, schema.ActivePage().Fields[0].Kind,
                "cable options are all visible (design/20 §2.3)");

            schema.SelectTab(3);
            CollectionAssert.AreEqual(new[] { "jmount", "jfinish" },
                schema.ActivePage().Fields.Select(f => f.Id).ToArray());
            Assert.AreEqual(SettingKind.Readout, schema.ActivePage().Fields[0].Kind,
                "the junction box has nothing to configure — just the mount hint");

            schema.SelectTab(4);
            CollectionAssert.AreEqual(new[] { "res", "pfinish", "popen" },
                schema.ActivePage().Fields.Select(f => f.Id).ToArray());
        }

        [Test]
        public void ElectricTool_IdAndLabel_ForRegistryAndPalette()
        {
            var go = new GameObject("Electric");
            _spawned.Add(go);
            var tool = go.AddComponent<ElectricController>();
            Assert.AreEqual("electric", tool.Id);
            Assert.AreEqual("Elec", tool.PaletteLabel);
        }

        // ---- selection adapters ----

        [Test]
        public void JunctionBox_CountsAttachedRuns_InDescribeAndReadout()
        {
            // the v2 branching point: two runs land on one box, both are counted
            var box = MakeFixture(FixtureKind.Junction);
            var r1 = MakeRoute(CableType.C3x25, new Vector3(0f, 0.3f, 0f), new Vector3(0f, 2.7f, 0f));
            var r2 = MakeRoute(CableType.C3x15, new Vector3(1f, 0.9f, 0f), new Vector3(0f, 2.7f, 0f));
            MakeRoute(CableType.C3x25, new Vector3(3f, 0.3f, 0f), new Vector3(4f, 0.3f, 0f));

            string boxId = IdOf(box);
            r1.EndFixtureId = boxId;
            r2.EndFixtureId = boxId;

            Assert.AreEqual("Junction box · 2 wires", box.GetComponent<Selectable>().Describe());

            var rows = box.GetComponent<ElectricFixtureParameters>().GetSettings();
            var wiresRow = rows.Fields.Single(f => f.Id == "fjwires");
            Assert.AreEqual("2", wiresRow.Value());
        }

        [Test]
        public void Selectable_ResolvesElectricalKinds()
        {
            var fx = MakeFixture(FixtureKind.Outlet, posts: 2);
            var route = MakeRoute(CableType.C3x25, new Vector3(0f, 0.3f, 0f), new Vector3(0f, 2.3f, 0f));

            Assert.AreEqual(SelectableKind.Fixture, fx.GetComponent<Selectable>().Kind);
            Assert.AreEqual(SelectableKind.Wire, route.GetComponent<Selectable>().Kind);
            StringAssert.Contains("Outlet ×2", fx.GetComponent<Selectable>().Describe());
            StringAssert.Contains("3x2.5", route.GetComponent<Selectable>().Describe());
        }

        [Test]
        public void MovingFixture_DragsAttachedWireEnds()
        {
            var fx = MakeFixture(FixtureKind.Switch);
            var attached = MakeRoute(CableType.C3x15,
                fx.TerminalWorld, new Vector3(0f, 2.3f, 0f), new Vector3(2f, 2.3f, 0f));
            var free = MakeRoute(CableType.C3x25,
                new Vector3(5f, 0.3f, 0f), new Vector3(5f, 2.3f, 0f));
            attached.StartFixtureId = IdOf(fx);

            Vector3 start = attached.GetPoint(0);
            Vector3 freeStart = free.GetPoint(0);
            var delta = new Vector3(0.4f, 0f, 0.2f);
            fx.GetComponent<Selectable>().MoveBy(delta);

            Assert.AreEqual(start + delta, attached.GetPoint(0), "attached end follows the fixture");
            Assert.AreEqual(new Vector3(2f, 2.3f, 0f), attached.GetPoint(2), "far end stays put");
            Assert.AreEqual(freeStart, free.GetPoint(0), "unrelated routes must not move");
            Assert.AreEqual(delta, fx.transform.position, "the fixture itself moved");
        }

        [Test]
        public void PanelDescribe_SummarizesBomAndUnroutedRuns()
        {
            var panel = MakeFixture(FixtureKind.Panel);
            var routed = MakeRoute(CableType.C3x15, new Vector3(0f, 2.3f, 0f), new Vector3(2f, 2.3f, 0f));
            routed.EndFixtureId = IdOf(panel);
            MakeRoute(CableType.C3x25, new Vector3(0f, 0.3f, 5f), new Vector3(0f, 2.3f, 5f));   // never reaches the panel

            string s = panel.GetComponent<Selectable>().Describe();
            StringAssert.Contains("3x1.5", s);
            StringAssert.Contains("3x2.5", s);
            StringAssert.Contains("Total — ", s);
            StringAssert.Contains($"(+{panel.ReservePercent}%)", s);
            StringAssert.Contains("unrouted: 1", s);
        }

        [Test]
        public void PanelDescribe_SkipsHiddenRoutes()
        {
            var panel = MakeFixture(FixtureKind.Panel);
            var route = MakeRoute(CableType.C3x15, new Vector3(0f, 2.3f, 0f), new Vector3(2f, 2.3f, 0f));

            route.GetComponent<Selectable>().SetHidden(true);   // deleted = hidden, not destroyed
            string s = panel.GetComponent<Selectable>().Describe();
            StringAssert.Contains("Total — 0.0 m", s, "hidden is not alive for the BOM (rule 2.4)");
        }

        [Test]
        public void PanelDescribe_DropsTheAllowanceOfEndsOnDeletedFixtures()
        {
            // Audit 08 §Б3: WireRoute.Connections trusts string non-emptiness, so an end
            // attached to a DELETED outlet kept billing its 0.15 m allowance forever.
            var panel = MakeFixture(FixtureKind.Panel);
            var outlet = MakeFixture(FixtureKind.Outlet);
            var route = MakeRoute(CableType.C3x25, new Vector3(0f, 0.3f, 0f), new Vector3(2f, 0.3f, 0f));
            route.StartFixtureId = IdOf(outlet);
            route.EndFixtureId = IdOf(panel);

            string Expected(int liveEnds) => ElectricalBom.Describe(
                new List<RouteBomEntry> { new RouteBomEntry(CableType.C3x25, route.Length, liveEnds) },
                panel.ReservePercent, unrouted: 0);

            Assert.AreEqual(Expected(2), panel.GetComponent<Selectable>().Describe(),
                "both ends live — two allowances");

            outlet.GetComponent<Selectable>().SetHidden(true);          // delete the outlet
            Assert.AreEqual(Expected(1), panel.GetComponent<Selectable>().Describe(),
                "the deleted outlet's allowance drops out of the estimate");

            outlet.GetComponent<Selectable>().SetHidden(false);         // undo restores the link
            Assert.AreEqual(Expected(2), panel.GetComponent<Selectable>().Describe(),
                "undo of the delete restores the connection for free");
        }

        // ---- undoable commands ----

        [Test]
        public void FixtureParamCommand_Posts_UndoRedo()
        {
            var fx = MakeFixture(FixtureKind.Outlet, posts: 2);
            var p = fx.GetComponent<ElectricFixtureParameters>();

            _model.History.Execute(FixtureParamCommand.ForPosts(p, 3));
            Assert.AreEqual(3, fx.Posts);
            _model.History.Undo();
            Assert.AreEqual(2, fx.Posts);
            _model.History.Redo();
            Assert.AreEqual(3, fx.Posts);
        }

        [Test]
        public void FixtureParamCommand_Height_MovesAttachedEndBothWays()
        {
            var fx = MakeFixture(FixtureKind.Outlet);
            var route = MakeRoute(CableType.C3x25, fx.TerminalWorld, new Vector3(1f, 2.3f, 0f));
            route.StartFixtureId = IdOf(fx);
            var p = fx.GetComponent<ElectricFixtureParameters>();
            float startY = fx.transform.position.y;
            float endY = route.GetPoint(0).y;

            _model.History.Execute(FixtureParamCommand.ForHeight(p, startY + 0.1f));
            Assert.AreEqual(startY + 0.1f, fx.transform.position.y, 1e-4);
            Assert.AreEqual(endY + 0.1f, route.GetPoint(0).y, 1e-4, "attached end rode along");

            _model.History.Undo();
            Assert.AreEqual(startY, fx.transform.position.y, 1e-4);
            Assert.AreEqual(endY, route.GetPoint(0).y, 1e-4, "undo drags it back too");
        }

        [Test]
        public void FixtureAppearanceCommands_UndoVariantAndPanelDoor()
        {
            var panel = MakeFixture(FixtureKind.Panel);
            var p = panel.GetComponent<ElectricFixtureParameters>();
            int closedVertices = panel.GetComponent<MeshFilter>().sharedMesh.vertexCount;

            _model.History.Execute(FixtureParamCommand.ForVariant(p, true));
            Assert.IsTrue(panel.BlackVariant);
            _model.History.Execute(FixtureParamCommand.ForPanelOpen(p, true));
            Assert.IsTrue(panel.PanelOpen);
            Assert.Greater(panel.GetComponent<MeshFilter>().sharedMesh.vertexCount, closedVertices);

            _model.History.Undo();
            Assert.IsFalse(panel.PanelOpen);
            _model.History.Undo();
            Assert.IsFalse(panel.BlackVariant);
            _model.History.Redo();
            _model.History.Redo();
            Assert.IsTrue(panel.BlackVariant);
            Assert.IsTrue(panel.PanelOpen);
        }

        [Test]
        public void ClosedPanel_HighlightTintsItsVisibleMetalSlot()
        {
            var panel = MakeFixture(FixtureKind.Panel);
            var selectable = panel.GetComponent<Selectable>();
            var renderer = panel.GetComponent<MeshRenderer>();
            var block = new MaterialPropertyBlock();

            selectable.SetHighlight(HighlightState.Selected);
            renderer.GetPropertyBlock(block, ElectricFixture.MetalSubmesh);

            Assert.AreNotEqual(ElectricFixture.BrushedMetal, block.GetColor("_BaseColor"),
                "the closed panel has no plastic faces, so selection must tint its metal slot");
            Assert.AreEqual(0.38f, block.GetFloat("_Smoothness"), 1e-4f);
            Assert.AreEqual(0.65f, block.GetFloat("_Metallic"), 1e-4f);

            selectable.SetHighlight(HighlightState.None);
            renderer.GetPropertyBlock(block, ElectricFixture.MetalSubmesh);
            Assert.AreEqual(ElectricFixture.BrushedMetal, block.GetColor("_BaseColor"));
        }

        [Test]
        public void RouteCableCommand_UndoRestoresType()
        {
            var route = MakeRoute(CableType.C3x25, new Vector3(0f, 2.3f, 0f), new Vector3(2f, 2.3f, 0f));
            var p = route.GetComponent<WireRouteParameters>();

            _model.History.Execute(new RouteCableCommand(p, route.Cable, Cable.Next(route.Cable)));
            Assert.AreEqual(CableType.C3x15, route.Cable);
            _model.History.Undo();
            Assert.AreEqual(CableType.C3x25, route.Cable);
        }

        [Test]
        public void RouteHandles_CommitIsOneUndoableGesture()
        {
            var route = MakeRoute(CableType.C3x25,
                new Vector3(0f, 0.3f, 0f), new Vector3(0f, 2.3f, 0f), new Vector3(2f, 2.3f, 0f));
            var handles = route.GetComponent<RouteHandles>();
            Assert.AreEqual(3, handles.HandleCount);

            Vector3 from = handles.GetHandlePosition(1);
            var to = new Vector3(0.5f, 2.3f, 0f);
            handles.PreviewHandle(1, new Vector3(0.2f, 2.3f, 0f));   // preview frames
            var cmd = handles.CommitHandle(1, from, to);
            Assert.IsNotNull(cmd);
            _model.History.Record(cmd);

            Assert.AreEqual(to, route.GetPoint(1));
            _model.History.Undo();
            Assert.AreEqual(from, route.GetPoint(1));
        }

        [Test]
        public void RouteHandles_ClickWithoutDrag_ProducesNoCommand()
        {
            var route = MakeRoute(CableType.C3x25, new Vector3(0f, 2.3f, 0f), new Vector3(2f, 2.3f, 0f));
            var handles = route.GetComponent<RouteHandles>();
            Vector3 p = handles.GetHandlePosition(0);
            Assert.IsNull(handles.CommitHandle(0, p, p), "a click must not pollute history");
        }

        [Test]
        public void PerInstanceSchemas_ExposeExpectedRows()
        {
            var outlet = MakeFixture(FixtureKind.Outlet);
            var panel = MakeFixture(FixtureKind.Panel);
            panel.transform.position = new Vector3(3f, 1.5f, 0f);   // out of the clearance zone
            var route = MakeRoute(CableType.C3x25, new Vector3(0f, 2.3f, 0f), new Vector3(2f, 2.3f, 0f));

            CollectionAssert.AreEqual(new[] { "fposts", "fh", "ffinish" },
                outlet.GetComponent<Selectable>().GetSettings().Fields.Select(f => f.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "fres", "ffinish", "fopen" },
                panel.GetComponent<Selectable>().GetSettings().Fields.Select(f => f.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "rcable" },
                route.GetComponent<Selectable>().GetSettings().Fields.Select(f => f.Id).ToArray());
        }
    }
}
