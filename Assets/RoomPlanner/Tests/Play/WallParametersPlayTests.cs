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

        [UnityTest]
        public IEnumerator SelectedWall_OffersItsOwnRows()
        {
            var (r, _) = MakeRig();
            var s = Draw(r, P(0, 0), P(3, 0));
            yield return null;

            var schema = r.ViewOf(s).GetComponent<Selectable>().GetSettings();
            var ids = new List<string>();
            foreach (var f in schema.Fields) ids.Add(f.Id);

            CollectionAssert.AreEquivalent(new[] { "wthk", "wh", "woff", "wjoin", "wside" }, ids);
            Assert.AreEqual("20 cm", Row(r, s, "wthk").Value(), "the row shows THIS wall's value");
        }

        [UnityTest]
        public IEnumerator EditingOneWall_LeavesItsNeighbourAlone()
        {
            var (r, _) = MakeRig();
            var ab = Draw(r, P(0, 0), P(2, 0));
            var bc = Draw(r, P(2, 0), P(2, 2));   // shares the corner node
            yield return null;

            Row(r, ab, "wthk").Increase();

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

            Row(r, s, "wthk").Increase();
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

            Row(r, s, "wthk").Increase();
            Assert.AreEqual(0.22f, s.Thickness, 1e-4f);

            model.History.Undo();
            Assert.AreEqual(0.20f, s.Thickness, 1e-4f, "undo restores the exact previous value");

            model.History.Redo();
            Assert.AreEqual(0.22f, s.Thickness, 1e-4f);
        }

        [UnityTest]
        public IEnumerator ThicknessIsClamped_AndUndoStillRestoresExactly()
        {
            // stepping down past the minimum must not let undo drift back up by the raw delta
            var (r, model) = MakeRig();
            var s = Draw(r, P(0, 0), P(3, 0));
            s.Thickness = 0.03f;
            yield return null;

            Row(r, s, "wthk").Decrease();
            Assert.AreEqual(0.02f, s.Thickness, 1e-4f, "clamped at the minimum");

            model.History.Undo();
            Assert.AreEqual(0.03f, s.Thickness, 1e-4f, "undo returns the value that was actually there");
        }

        [UnityTest]
        public IEnumerator CyclingOffsetAndJoin_IsUndoable()
        {
            var (r, model) = MakeRig();
            var s = Draw(r, P(0, 0), P(3, 0));
            s.Offset = WallOffsetMode.Outer;
            s.Join = WallJoin.Miter;
            yield return null;

            Row(r, s, "woff").Increase();
            Row(r, s, "wjoin").Increase();
            Assert.AreEqual(WallOffsetMode.Center, s.Offset);
            Assert.AreEqual(WallJoin.Bevel, s.Join);

            model.History.Undo();
            model.History.Undo();
            Assert.AreEqual(WallOffsetMode.Outer, s.Offset);
            Assert.AreEqual(WallJoin.Miter, s.Join);
        }

        [UnityTest]
        public IEnumerator FlippingSide_MirrorsTheWall_AndIsUndoable()
        {
            var (r, model) = MakeRig();
            var s = Draw(r, P(0, 0), P(4, 0));
            yield return null;
            var mesh = r.ViewOf(s).GetComponent<MeshFilter>().sharedMesh;
            float before = mesh.bounds.center.z;

            Row(r, s, "wside").Increase();
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

            Row(r, s, "wh").Increase();

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
