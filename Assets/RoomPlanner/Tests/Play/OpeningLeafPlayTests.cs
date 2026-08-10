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
    /// Openable doors (issue #50): the leaf is a transform-driven child view — it
    /// survives wall rebuilds without being recreated, carries a Door Selectable with
    /// per-door settings, opens without touching the wall mesh, and leaves the scene
    /// together with its opening.
    /// </summary>
    public class OpeningLeafPlayTests
    {
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private (WallGraphRenderer walls, SceneModel model, WallSegment seg, Wall view) MakeDoorWall(
            OpeningKind kind = OpeningKind.Door, float width = 1f)
        {
            var rig = new GameObject("Rig");
            _spawned.Add(rig);
            var model = rig.AddComponent<SceneModel>();

            var template = new GameObject("WallTemplate");
            _spawned.Add(template);
            template.SetActive(false);
            template.AddComponent<MeshFilter>();
            template.AddComponent<MeshRenderer>();
            var prefab = template.AddComponent<Wall>();
            template.AddComponent<Selectable>();

            var walls = rig.AddComponent<WallGraphRenderer>();
            walls.Configure(prefab, model);
            var seg = walls.Graph.AddSegment(
                walls.Graph.SnapOrCreateNode(Vector3.zero),
                walls.Graph.SnapOrCreateNode(new Vector3(4f, 0f, 0f)));
            seg.Thickness = 0.2f;
            seg.Height = 2.7f;
            seg.Offset = WallOffsetMode.Center;
            seg.Openings.Add(new WallOpening
            {
                Id = 7, AlongFraction = 0.5f, Width = width, Height = 2.1f, Kind = kind,
            });
            walls.Sync();
            return (walls, model, seg, walls.ViewOf(seg));
        }

        [UnityTest]
        public IEnumerator Leaf_IsADoorSelectable_WithSettings_AndSurvivesRebuilds()
        {
            var (walls, _, seg, view) = MakeDoorWall();
            yield return null;

            var leaf = view.GetComponentInChildren<OpeningLeafView>();
            Assert.IsNotNull(leaf, "a door opening grows a leaf child");
            var sel = leaf.GetComponent<Selectable>();
            Assert.IsNotNull(sel, "the renderer dresses the leaf with a Selectable");
            Assert.AreEqual(SelectableKind.Door, sel.Kind);
            Assert.IsNotNull(sel.GetSettings(), "per-door settings page exists (#50)");
            Assert.IsNotNull(leaf.GetComponentInChildren<BoxCollider>(), "pickable");

            int leafId = leaf.GetInstanceID();
            walls.RebuildSegment(seg);   // node drags rebuild every frame — the leaf
            walls.RebuildSegment(seg);   // must be re-placed, never re-created
            yield return null;
            var after = view.GetComponentInChildren<OpeningLeafView>();
            Assert.AreEqual(leafId, after.GetInstanceID(), "same leaf instance after rebuilds");
        }

        [UnityTest]
        public IEnumerator SetFraction_OpensTheDoorway_WallMeshUntouched()
        {
            var (_, _, _, view) = MakeDoorWall();
            yield return null;
            var leaf = view.GetComponentInChildren<OpeningLeafView>();
            var wallMesh = view.GetComponent<MeshFilter>().sharedMesh;
            int wallVerts = wallMesh.vertexCount;

            var ray = new Ray(new Vector3(2f, 1f, -2f), Vector3.forward);
            Assert.IsTrue(LeafBlocked(leaf, ray), "closed leaf blocks the doorway");

            leaf.SetFraction(1f, animate: false);
            yield return null;
            Assert.IsFalse(LeafBlocked(leaf, ray), "open leaf clears the doorway");
            Assert.AreEqual(1f, leaf.Opening.OpenFraction, 1e-4f, "state kept on the opening");
            Assert.AreEqual(wallVerts, wallMesh.vertexCount, "opening = transforms only, no mesh rebuild");

            leaf.Toggle();               // animated close
            float deadline = Time.time + OpeningLeafView.AnimSeconds * 3f;
            while (leaf.Opening.OpenFraction > 0.01f && Time.time < deadline) yield return null;
            Assert.Less(leaf.Opening.OpenFraction, 0.01f, "toggle animates back to closed");
            Assert.IsTrue(LeafBlocked(leaf, ray), "closed again");

            leaf.Toggle();               // and re-opens to the remembered fraction
            deadline = Time.time + OpeningLeafView.AnimSeconds * 3f;
            while (leaf.Opening.OpenFraction < 0.99f && Time.time < deadline) yield return null;
            Assert.Greater(leaf.Opening.OpenFraction, 0.99f, "toggle returns to the last-used %");
        }

        private static bool LeafBlocked(OpeningLeafView leaf, Ray ray)
        {
            Physics.SyncTransforms();
            foreach (var col in leaf.GetComponentsInChildren<Collider>())
                if (col.Raycast(ray, out _, 20f)) return true;
            return false;
        }

        [UnityTest]
        public IEnumerator GarageLeaf_LiftsItsPanelsUnderTheCeiling()
        {
            var (_, _, _, view) = MakeDoorWall(OpeningKind.Garage, width: 2.5f);
            yield return null;
            var leaf = view.GetComponentInChildren<OpeningLeafView>();
            Assert.IsNotNull(leaf);
            Assert.AreEqual(OpeningPose.GaragePanels, leaf.GetComponentsInChildren<BoxCollider>().Length,
                "one collider per sectional panel");

            var ray = new Ray(new Vector3(2f, 1f, -2f), Vector3.forward);
            Assert.IsTrue(LeafBlocked(leaf, ray), "closed sectional leaf blocks the doorway");

            leaf.SetFraction(1f, animate: false);
            yield return null;
            Assert.IsFalse(LeafBlocked(leaf, ray), "lifted panels clear the doorway");
        }

        [UnityTest]
        public IEnumerator SettingsPage_EditsAreUndoable_OpenPercentIsNot()
        {
            var (_, model, seg, view) = MakeDoorWall();
            yield return null;
            var leaf = view.GetComponentInChildren<OpeningLeafView>();
            var pars = leaf.GetComponent<OpeningParameters>();
            var schema = pars.GetSettings();
            Assert.IsNotNull(schema);

            SettingField Field(string id)
            {
                foreach (var f in schema.Fields) if (f.Id == id) return f;
                return null;
            }

            var w = Field("w");
            Assert.IsNotNull(w, "width row");
            int undoBefore = model.History.UndoCount;
            w.CommitNumber(leaf.Opening.Width, 1.1f);
            Assert.AreEqual(1.1f, leaf.Opening.Width, 1e-4f, "width edit applies");
            Assert.AreEqual(undoBefore + 1, model.History.UndoCount, "…as ONE undo entry");
            model.History.Undo();
            Assert.AreEqual(1f, leaf.Opening.Width, 1e-4f, "and undoes");

            var open = Field("open");
            Assert.IsNotNull(open, "Open % row (#50 explicit request)");
            undoBefore = model.History.UndoCount;
            open.CommitNumber(0f, 0.6f);
            Assert.AreEqual(0.6f, leaf.Opening.OpenFraction, 1e-4f, "slider drives the leaf");
            Assert.AreEqual(undoBefore, model.History.UndoCount,
                "opening a door is a view action — never an undo entry");

            var del = Field("delete");
            Assert.IsNotNull(del);
            Assert.IsTrue(del.Destructive, "delete row is hold-to-confirm destructive");
        }

        [UnityTest]
        public IEnumerator DeleteRow_RemovesTheOpening_UndoBringsLeafBack()
        {
            var (_, model, seg, view) = MakeDoorWall();
            yield return null;
            var leaf = view.GetComponentInChildren<OpeningLeafView>();
            var pars = leaf.GetComponent<OpeningParameters>();

            model.History.Execute(pars.BuildDeleteCommand());
            yield return null;
            Assert.AreEqual(0, seg.Openings.Count, "the OPENING is deleted, not just the view");
            Assert.IsNull(view.GetComponentInChildren<OpeningLeafView>(), "leaf child gone");

            model.History.Undo();
            yield return null;
            Assert.AreEqual(1, seg.Openings.Count);
            Assert.IsNotNull(view.GetComponentInChildren<OpeningLeafView>(), "leaf grows back on undo");
        }
    }
}
