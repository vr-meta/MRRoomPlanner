using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Phase B / step B4 — per-instance wall parameters in the inspector
    /// (docs/design/13-phase-b-wallgraph.md).
    ///
    /// The promises under test: editing a selected wall changes THAT wall only, the menu values
    /// stay defaults for the next one, and every change is undoable.
    /// </summary>
    public class WallParametersPlayTests
    {
        private readonly List<GameObject> _spawned = new();

        private static Vector3 P(float x, float z) => new Vector3(x, 0f, z);

        private (WallGraphRenderer r, SceneModel model) MakeRig()
        {
            var prefabGo = new GameObject("WallPrefab");
            prefabGo.AddComponent<MeshFilter>();
            prefabGo.AddComponent<MeshRenderer>();
            prefabGo.AddComponent<Wall>();
            prefabGo.AddComponent<WallParameters>();
            prefabGo.AddComponent<Selectable>();
            prefabGo.SetActive(false);
            _spawned.Add(prefabGo);

            var rigGo = new GameObject("Rig");
            _spawned.Add(rigGo);
            var model = rigGo.AddComponent<SceneModel>();
            var r = rigGo.AddComponent<WallGraphRenderer>();
            r.Configure(prefabGo.GetComponent<Wall>(), model);
            return (r, model);
        }

        private static WallSegment Draw(WallGraphRenderer r, Vector3 a, Vector3 b)
        {
            var g = r.Graph;
            var s = g.AddSegment(g.SnapOrCreateNode(a), g.SnapOrCreateNode(b));
            if (s != null) { s.Thickness = 0.2f; s.Height = 2.7f; s.SideSign = 1f; }
            r.Sync();
            return s;
        }

        private static SettingField Row(WallGraphRenderer r, WallSegment s, string id)
        {
            var schema = r.ViewOf(s).GetComponent<Selectable>().GetSettings();
            Assert.IsNotNull(schema, "a selected wall must offer its own rows");
            foreach (var f in schema.Fields) if (f.Id == id) return f;
            Assert.Fail($"no row '{id}' in the wall schema");
            return null;
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        // v2 widget drivers (design/20 §2): numeric fields commit absolute values,
        // segmented rows set an option index — these mimic what the inspector does.
        private static void Bump(RoomPlanner.Core.SettingField f, float delta)
        {
            float before = f.GetNumber();
            f.CommitNumber(before, before + delta);
        }

        private static void NextOption(RoomPlanner.Core.SettingField f)
        {
            int n = f.ResolveOptions().Length;
            f.SetIndex((f.GetIndex() + 1) % n);
        }

        [UnityTest]
        public IEnumerator SelectedWall_OffersItsOwnRows()
        {
            var (r, _) = MakeRig();
            var s = Draw(r, P(0, 0), P(3, 0));
            yield return null;

            var schema = r.ViewOf(s).GetComponent<Selectable>().GetSettings();
            var ids = new List<string>();
            foreach (var f in schema.Fields) ids.Add(f.Id);

            // no "wjoin": the Corner row was UI with no effect — WallMesh always miters (B8)
            CollectionAssert.AreEquivalent(new[] { "wlen", "wthk", "wh", "woff", "wside" }, ids);
            Assert.AreEqual("20 cm", Row(r, s, "wthk").Value(), "the row shows THIS wall's value");
        }

        [UnityTest]
        public IEnumerator EditingOneWall_LeavesItsNeighbourAlone()
        {
            var (r, _) = MakeRig();
            var ab = Draw(r, P(0, 0), P(2, 0));
            var bc = Draw(r, P(2, 0), P(2, 2));   // shares the corner node
            yield return null;

            Bump(Row(r, ab, "wthk"), +0.02f);

            Assert.AreEqual(0.22f, ab.Thickness, 1e-4f, "the edited wall changed");
            Assert.AreEqual(0.20f, bc.Thickness, 1e-4f, "its neighbour did NOT");
        }

        [UnityTest]
        public IEnumerator Editing_DoesNotTouchTheMenuDefaults()
        {
            // the palette value is the default for the NEXT wall, not a live link to this one
            var (r, _) = MakeRig();
            var s = Draw(r, P(0, 0), P(3, 0));
            yield return null;

            Bump(Row(r, s, "wthk"), +0.02f);
            var next = Draw(r, P(0, 5), P(3, 5));

            Assert.AreEqual(0.22f, s.Thickness, 1e-4f);
            Assert.AreEqual(0.20f, next.Thickness, 1e-4f, "the next wall still uses the default");
        }

        [UnityTest]
        public IEnumerator ThicknessChange_IsUndoable()
        {
            var (r, model) = MakeRig();
            var s = Draw(r, P(0, 0), P(3, 0));
            yield return null;

            Bump(Row(r, s, "wthk"), +0.02f);
            Assert.AreEqual(0.22f, s.Thickness, 1e-4f);

            model.History.Undo();
            Assert.AreEqual(0.20f, s.Thickness, 1e-4f, "undo restores the exact previous value");

            model.History.Redo();
            Assert.AreEqual(0.22f, s.Thickness, 1e-4f);
        }

        [UnityTest]
        public IEnumerator ExactLength_MovesTheFreeEnd_PreservesOpening_AndIsUndoable()
        {
            var (r, model) = MakeRig();
            var s = Draw(r, P(0, 0), P(4, 0));
            var opening = new WallOpening { AlongFraction = 0.5f, Width = 0.9f };
            s.Openings.Add(opening);
            yield return null;

            var length = Row(r, s, "wlen");
            length.CommitNumber(4f, 6f);

            Assert.AreEqual(6f, s.Length, 1e-4f);
            Assert.AreSame(opening, s.Openings[0]);
            Assert.AreEqual(0.5f, opening.AlongFraction, 1e-5f);
            Assert.AreEqual(1, s.A.Degree);
            Assert.AreEqual(1, s.B.Degree);

            model.History.Undo();
            Assert.AreEqual(4f, s.Length, 1e-4f);
            Assert.AreSame(opening, s.Openings[0]);
            model.History.Redo();
            Assert.AreEqual(6f, s.Length, 1e-4f);
        }

        [UnityTest]
        public IEnumerator ExactOffset_DuplicatesParametricWall_DeepCopiesOpenings_AndUndoRestores()
        {
            var (r, model) = MakeRig();
            var source = Draw(r, P(0, 0), P(4, 0));
            source.Thickness = 0.32f;
            source.Height = 3.1f;
            source.Openings.Add(new WallOpening
            {
                Id = 7, AlongFraction = 0.4f, Width = 1.2f, Height = 1.4f,
                SillHeight = 0.8f, Kind = OpeningKind.Window,
            });
            r.RebuildSegment(source);
            yield return null;

            Vector3 delta = WallDuplicateCommand.OffsetDelta(source, 0.35f);
            var command = new WallDuplicateCommand(r, r.ViewOf(source), delta);
            model.History.Execute(command);

            Assert.IsNotNull(command.Result);
            Assert.AreEqual(2, r.Graph.Segments.Count);
            var copy = command.ResultSegment;
            Assert.AreEqual(source.A.Position + delta, copy.A.Position);
            Assert.AreEqual(source.B.Position + delta, copy.B.Position);
            Assert.AreEqual(0.32f, copy.Thickness, 1e-4f);
            Assert.AreEqual(3.1f, copy.Height, 1e-4f);
            Assert.AreEqual(1, copy.Openings.Count);
            Assert.AreNotSame(source.Openings[0], copy.Openings[0],
                "editing a copied opening must not mutate the source wall");
            Assert.AreEqual(source.Openings[0].AlongFraction, copy.Openings[0].AlongFraction, 1e-5f);
            Assert.AreEqual("L 4.00 m", command.Result.CompactDimensions());

            model.History.Undo();
            Assert.IsTrue(command.Result.IsHidden);
            Assert.IsTrue(copy.Suppressed, "hidden copy must leave wall-joint calculations");
            model.History.Redo();
            Assert.IsFalse(command.Result.IsHidden);
            Assert.IsFalse(copy.Suppressed);
        }

        [UnityTest]
        public IEnumerator ThicknessIsClamped_AndUndoStillRestoresExactly()
        {
            // stepping down past the minimum must not let undo drift back up by the raw delta
            var (r, model) = MakeRig();
            var s = Draw(r, P(0, 0), P(3, 0));
            s.Thickness = 0.03f;
            yield return null;

            Bump(Row(r, s, "wthk"), -0.02f);
            Assert.AreEqual(0.02f, s.Thickness, 1e-4f, "clamped at the minimum");

            model.History.Undo();
            Assert.AreEqual(0.03f, s.Thickness, 1e-4f, "undo returns the value that was actually there");
        }

        [UnityTest]
        public IEnumerator CyclingOffset_IsUndoable()
        {
            var (r, model) = MakeRig();
            var s = Draw(r, P(0, 0), P(3, 0));
            s.Offset = WallOffsetMode.Outer;
            yield return null;

            NextOption(Row(r, s, "woff"));
            Assert.AreEqual(WallOffsetMode.Center, s.Offset);

            model.History.Undo();
            Assert.AreEqual(WallOffsetMode.Outer, s.Offset);
        }

        [UnityTest]
        public IEnumerator FlippingSide_MirrorsTheWall_AndIsUndoable()
        {
            var (r, model) = MakeRig();
            var s = Draw(r, P(0, 0), P(4, 0));
            yield return null;
            var mesh = r.ViewOf(s).GetComponent<MeshFilter>().sharedMesh;
            float before = mesh.bounds.center.z;

            NextOption(Row(r, s, "wside"));
            Assert.AreEqual(-1f, s.SideSign, 1e-4f);
            Assert.AreNotEqual(before, mesh.bounds.center.z, "the wall actually moved to the other side");

            model.History.Undo();
            Assert.AreEqual(1f, s.SideSign, 1e-4f);
            Assert.AreEqual(before, mesh.bounds.center.z, 1e-3f);
        }

        [UnityTest]
        public IEnumerator EditingRebuildsTheMesh()
        {
            var (r, _) = MakeRig();
            var s = Draw(r, P(0, 0), P(4, 0));
            yield return null;
            var mesh = r.ViewOf(s).GetComponent<MeshFilter>().sharedMesh;
            float beforeHeight = mesh.bounds.size.y;

            Bump(Row(r, s, "wh"), +0.1f);

            Assert.AreEqual(beforeHeight + 0.1f, mesh.bounds.size.y, 1e-3f,
                "the view is rebuilt, not left showing the old height");
        }

        [UnityTest]
        public IEnumerator UnselectedObjectWithoutProvider_HasNoRows()
        {
            // a plain selectable (no provider component) must simply have no settings
            var go = new GameObject("Bare");
            _spawned.Add(go);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            var sel = go.AddComponent<Selectable>();
            yield return null;

            Assert.IsNull(sel.GetSettings());
        }
    }
}
