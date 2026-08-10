using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Floors;
using RoomPlanner.Tools;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Paint room (design/24, issue #52): one storey-wide slab + a wall graph with two
    /// rooms — painting inside a room carves a nested sub-slab along the room ring and
    /// paints only it, as one undo entry. Adjacent rooms carve next to each other
    /// (the 2 cm inset keeps their rings apart).
    /// </summary>
    public class PaintRoomPlayTests
    {
        private readonly List<GameObject> _spawned = new();

        private GameObject Track(GameObject go)
        {
            _spawned.Add(go);
            return go;
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private static void SetField(object target, string field, object value) =>
            target.GetType()
                .GetField(field, System.Reflection.BindingFlags.Instance
                                 | System.Reflection.BindingFlags.NonPublic)
                .SetValue(target, value);

        private (WallGraphRenderer walls, FloorController floors, Floor slab, SceneModel model) MakeTwoRoomRig()
        {
            var rig = Track(new GameObject("Rig"));
            var model = rig.AddComponent<SceneModel>();

            var wallTemplate = Track(new GameObject("WallTemplate"));
            wallTemplate.SetActive(false);
            wallTemplate.AddComponent<MeshFilter>();
            wallTemplate.AddComponent<MeshRenderer>();
            var wallPrefab = wallTemplate.AddComponent<Wall>();
            wallTemplate.AddComponent<Selectable>();

            var walls = rig.AddComponent<WallGraphRenderer>();
            walls.Configure(wallPrefab, model);

            var floorTemplate = Track(new GameObject("FloorTemplate"));
            floorTemplate.SetActive(false);
            floorTemplate.AddComponent<MeshFilter>();
            floorTemplate.AddComponent<MeshRenderer>();
            var floorPrefab = floorTemplate.AddComponent<Floor>();
            floorTemplate.AddComponent<Selectable>();

            var floors = rig.AddComponent<FloorController>();
            SetField(floors, "floorPrefab", floorPrefab);
            SetField(floors, "sceneModel", model);

            // 8×3 box with a divider at x = 4 → two 4×3 rooms
            var g = walls.Graph;
            void W(float ax, float az, float bx, float bz) =>
                g.AddSegment(g.SnapOrCreateNode(new Vector3(ax, 0f, az)),
                             g.SnapOrCreateNode(new Vector3(bx, 0f, bz)));
            W(0, 0, 4, 0); W(4, 0, 8, 0);
            W(8, 0, 8, 3);
            W(8, 3, 4, 3); W(4, 3, 0, 3);
            W(0, 3, 0, 0);
            W(4, 0, 4, 3);
            walls.Sync();

            // one slab for the whole storey, slightly larger than the walls
            var slab = floors.CreateImported(new List<Vector3>
            {
                new(-0.3f, 0f, -0.3f), new(8.3f, 0f, -0.3f),
                new(8.3f, 0f, 3.3f), new(-0.3f, 0f, 3.3f),
            }, 0f, 0.2f);
            return (walls, floors, slab, model);
        }

        private static readonly Color Terracotta = new(0.77f, 0.39f, 0.23f);

        private static bool TryRoomPaint(PaintController paint, Selectable sel, Vector3 point,
            SurfaceFinish finish)
        {
            var m = typeof(PaintController).GetMethod("TryRoomPaint",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return (bool)m.Invoke(paint, new object[] { sel, point, finish, null, null });
        }

        private PaintController MakePaint(WallGraphRenderer walls, FloorController floors, SceneModel model)
        {
            var paint = Track(new GameObject("Paint")).AddComponent<PaintController>();
            SetField(paint, "walls", walls);
            SetField(paint, "floors", floors);
            SetField(paint, "sceneModel", model);
            return paint;
        }

        [UnityTest]
        public IEnumerator PaintingInsideARoom_CarvesAndPaintsOnlyThatRoom()
        {
            var (walls, floors, slab, model) = MakeTwoRoomRig();
            var paint = MakePaint(walls, floors, model);
            yield return null;

            var slabSel = slab.GetComponent<Selectable>();
            float donorArea = slab.Area;
            bool carved = TryRoomPaint(paint, slabSel, new Vector3(2f, 0f, 1.5f),
                SurfaceFinish.OfColor(Terracotta));
            Assert.IsTrue(carved, "a hit inside the left room carves it");

            Assert.AreEqual(1, slab.Holes.Count, "the donor slab got the room hole");
            Assert.Less(slab.Area, donorArea, "the donor lost the room's area");

            // the nested sub-slab exists, roughly room-sized, and carries the paint
            Floor sub = null;
            foreach (var f in Object.FindObjectsByType<Floor>(FindObjectsSortMode.None))
                if (f != slab && f.gameObject.activeSelf) sub = f;
            Assert.IsNotNull(sub, "a nested sub-slab appeared");
            Assert.AreEqual(12f, sub.Area, 0.5f, "≈ the 4×3 room (minus the 2 cm inset)");
            var subSel = sub.GetComponent<Selectable>();
            Assert.IsTrue(subSel.IsPainted, "the room slab took the finish");
            Assert.AreEqual(Terracotta.r, subSel.Paint.r, 1e-2f);
            Assert.IsFalse(slabSel.IsPainted, "the donor keeps its own look");
        }

        [UnityTest]
        public IEnumerator AdjacentRoom_CarvesNextToTheFirst_AndUndoIsOneEntry()
        {
            var (walls, floors, slab, model) = MakeTwoRoomRig();
            var paint = MakePaint(walls, floors, model);
            yield return null;

            var slabSel = slab.GetComponent<Selectable>();
            Assert.IsTrue(TryRoomPaint(paint, slabSel, new Vector3(2f, 0f, 1.5f),
                SurfaceFinish.OfColor(Terracotta)), "left room");
            Assert.IsTrue(TryRoomPaint(paint, slabSel, new Vector3(6f, 0f, 1.5f),
                SurfaceFinish.OfColor(Color.white)), "right room next to it (the inset keeps rings apart)");
            Assert.AreEqual(2, slab.Holes.Count, "two room holes in the donor");

            int undoBefore = model.History.UndoCount;
            model.History.Undo();   // undoes the SECOND room as one entry
            Assert.AreEqual(undoBefore - 1, model.History.UndoCount);
            Assert.AreEqual(1, slab.Holes.Count, "the second hole is gone");
            int visibleSubs = 0;
            foreach (var f in Object.FindObjectsByType<Floor>(FindObjectsSortMode.None))
                if (f != slab && f.gameObject.activeSelf) visibleSubs++;
            Assert.AreEqual(1, visibleSubs, "the second sub-slab hid with the undo");

            model.History.Redo();
            Assert.AreEqual(2, slab.Holes.Count, "redo re-carves");
        }

        [UnityTest]
        public IEnumerator NoRoomUnderTheHit_FallsBackToWholeSlab()
        {
            var (walls, floors, slab, model) = MakeTwoRoomRig();
            var paint = MakePaint(walls, floors, model);
            yield return null;

            // outside the walls but still on the slab margin → no room ring contains it
            bool carved = TryRoomPaint(paint, slab.GetComponent<Selectable>(),
                new Vector3(-0.2f, 0f, -0.2f), SurfaceFinish.OfColor(Terracotta));
            Assert.IsFalse(carved, "no room → the caller paints the whole slab");
            Assert.AreEqual(0, slab.Holes.Count);
        }

        [UnityTest]
        public IEnumerator CarvedRoomSlab_PaintsWholeOnSecondHit()
        {
            var (walls, floors, slab, model) = MakeTwoRoomRig();
            var paint = MakePaint(walls, floors, model);
            yield return null;

            Assert.IsTrue(TryRoomPaint(paint, slab.GetComponent<Selectable>(),
                new Vector3(2f, 0f, 1.5f), SurfaceFinish.OfColor(Terracotta)));
            Floor sub = null;
            foreach (var f in Object.FindObjectsByType<Floor>(FindObjectsSortMode.None))
                if (f != slab && f.gameObject.activeSelf) sub = f;

            // the sub-slab IS the room now — no second carve, plain paint applies
            bool carvedAgain = TryRoomPaint(paint, sub.GetComponent<Selectable>(),
                new Vector3(2f, 0f, 1.5f), SurfaceFinish.OfColor(Color.white));
            Assert.IsFalse(carvedAgain, "a slab that already matches the room paints whole");
            Assert.AreEqual(0, sub.Holes.Count, "no hole carved into the room slab");
        }
    }
}
