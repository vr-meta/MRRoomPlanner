using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Editing;
using RoomPlanner.Floors;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Phase C / step C4 — editing a slab: its own thickness and level, and draggable corners
    /// (docs/design/17-floor-outline.md). Corner dragging reuses the handle machinery built for
    /// wall vertices in Phase B, which is the point of that interface.
    /// </summary>
    public class FloorEditingPlayTests
    {
        private readonly List<GameObject> _spawned = new();

        private static Vector3 P(float x, float z) => new Vector3(x, 0f, z);

        private static List<Vector3> LShape() => new()
        {
            P(0, 0), P(4, 0), P(4, 2), P(2, 2), P(2, 5), P(0, 5)
        };

        private (Floor slab, SceneModel model) MakeSlab(List<Vector3> outline)
        {
            var rigGo = new GameObject("Rig");
            _spawned.Add(rigGo);
            var model = rigGo.AddComponent<SceneModel>();

            var go = new GameObject("Floor");
            _spawned.Add(go);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            var slab = go.AddComponent<Floor>();
            go.AddComponent<FloorParameters>();
            go.AddComponent<FloorHandles>();
            var sel = go.AddComponent<Selectable>();

            slab.BuildOutline(outline, 0f, 0.2f, 5f, 0f, 0f, 0f);
            model.Register(sel);
            return (slab, model);
        }

        private static Core.SettingField Row(Floor slab, string id)
        {
            var schema = slab.GetComponent<Selectable>().GetSettings();
            Assert.IsNotNull(schema, "a selected slab must offer its own rows");
            foreach (var f in schema.Fields) if (f.Id == id) return f;
            Assert.Fail($"no row '{id}' in the floor schema");
            return null;
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        // v2: slab rows are numeric fields (design/20 §2.6) — commit an absolute value
        private static void Bump(RoomPlanner.Core.SettingField f, float delta)
        {
            float before = f.GetNumber();
            f.CommitNumber(before, before + delta);
        }

        [UnityTest]
        public IEnumerator SlabOffersItsOwnRows()
        {
            var (slab, _) = MakeSlab(LShape());
            yield return null;

            var ids = new List<string>();
            foreach (var f in slab.GetComponent<Selectable>().GetSettings().Fields) ids.Add(f.Id);

            CollectionAssert.AreEquivalent(new[] { "fthk", "flvl" }, ids);
            Assert.AreEqual("20 cm", Row(slab, "fthk").Value());
        }

        [UnityTest]
        public IEnumerator ThicknessIsTheSlabsOwn_AndUndoable()
        {
            // it used to borrow the WALL thickness, which tied two unrelated things together
            var (slab, model) = MakeSlab(LShape());
            yield return null;

            Bump(Row(slab, "fthk"), +0.02f);
            Assert.AreEqual(0.22f, slab.Thickness, 1e-4f);

            model.History.Undo();
            Assert.AreEqual(0.20f, slab.Thickness, 1e-4f);
        }

        [UnityTest]
        public IEnumerator ChangingThickness_KeepsTheShape()
        {
            var (slab, _) = MakeSlab(LShape());
            yield return null;

            Bump(Row(slab, "fthk"), +0.02f);

            Assert.AreEqual(6, slab.Outline.Count, "an L must not turn into a box");
            Assert.AreEqual(14f, slab.Area, 1e-3f);
        }

        [UnityTest]
        public IEnumerator LevelMovesTheWholeSlab_AndIsUndoable()
        {
            var (slab, model) = MakeSlab(LShape());
            yield return null;

            Bump(Row(slab, "flvl"), +0.1f);
            Assert.AreEqual(0.1f, slab.Level, 1e-3f);
            foreach (var p in slab.Outline)
                Assert.AreEqual(0.1f, p.y, 1e-3f, "the outline travels with the level");

            model.History.Undo();
            Assert.AreEqual(0f, slab.Level, 1e-3f);
        }

        // ---- corner handles ----

        [UnityTest]
        public IEnumerator HandlesMatchTheOutlineCorners()
        {
            var (slab, _) = MakeSlab(LShape());
            yield return null;

            var h = slab.GetComponent<FloorHandles>();
            Assert.AreEqual(6, h.HandleCount);
            for (int i = 0; i < h.HandleCount; i++)
                Assert.AreEqual(slab.Outline[i], h.GetHandlePosition(i));
        }

        [UnityTest]
        public IEnumerator DraggingACorner_ReshapesTheSlab_OneUndoEntry()
        {
            var (slab, model) = MakeSlab(new List<Vector3> { P(0, 0), P(4, 0), P(4, 3), P(0, 3) });
            yield return null;

            var h = slab.GetComponent<FloorHandles>();
            Vector3 from = h.GetHandlePosition(1);
            float before = slab.Area;

            h.PreviewHandle(1, P(6, 0));          // several frames of dragging
            h.PreviewHandle(1, P(7, 0));
            var cmd = h.CommitHandle(1, from, P(7, 0));
            model.History.Record(cmd);

            Assert.Greater(slab.Area, before, "the slab grew");
            Assert.AreEqual(1, model.History.UndoCount, "one entry for the gesture");

            model.History.Undo();
            Assert.AreEqual(before, slab.Area, 1e-3f, "undo restores the original shape");
        }

        [UnityTest]
        public IEnumerator ClickingACornerWithoutMoving_RecordsNothing()
        {
            var (slab, _) = MakeSlab(LShape());
            yield return null;

            var h = slab.GetComponent<FloorHandles>();
            Vector3 from = h.GetHandlePosition(0);
            Assert.IsNull(h.CommitHandle(0, from, from));
        }

        [UnityTest]
        public IEnumerator SelectionText_ReportsAreaNotBoundingBox()
        {
            var (slab, _) = MakeSlab(LShape());
            yield return null;

            string info = slab.GetComponent<Selectable>().Describe();
            StringAssert.Contains("m²", info);
            StringAssert.Contains("6", info);      // corner count
        }
    }
}
