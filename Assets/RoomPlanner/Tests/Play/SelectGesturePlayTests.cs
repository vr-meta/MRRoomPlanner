using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Measure;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Move-gesture coverage through the REAL SelectController.Tick (user feedback
    /// 2026-08-12: "тесты не покрывают сценарии перемещения"). MeasureInput and
    /// PointerProvider grew virtual seams, so the trigger and the ray are scripted
    /// here frame by frame — no device needed.
    /// </summary>
    public class SelectGesturePlayTests
    {
        private class FakeInput : MeasureInput
        {
            public bool Pressed, Held, Clear;
            public override bool ConfirmPressed() => Pressed;
            public override bool ConfirmHeld() => Held;
            public override bool ClearPressed() => Clear;
            public override void Pulse(float amplitude = 0.5f, float duration = 0.06f) { }
            public override void PulseLeft(float amplitude = 0.5f, float duration = 0.02f) { }
        }

        private class FakePointer : PointerProvider
        {
            public Ray Ray;
            public override Ray GetRay() => Ray;
        }

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

        private (SelectController select, FakeInput input, FakePointer pointer,
                 SceneModel model, Wall view, WallSegment seg) MakeDoorRig()
        {
            var rig = Track(new GameObject("Rig"));
            var model = rig.AddComponent<SceneModel>();

            var template = Track(new GameObject("WallTemplate"));
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
                Id = 3, AlongFraction = 0.5f, Width = 1f, Height = 2.1f, Kind = OpeningKind.Door,
            });
            walls.Sync();

            var host = Track(new GameObject("Select"));
            var input = host.AddComponent<FakeInput>();
            var pointer = host.AddComponent<FakePointer>();
            var select = host.AddComponent<SelectController>();
            SetField(select, "input", input);
            SetField(select, "pointer", pointer);
            SetField(select, "sceneModel", model);
            return (select, input, pointer, model, walls.ViewOf(seg), seg);
        }

        /// <summary>A slanted ray at the door: hits the leaf AND crosses the horizontal
        /// drag plane, so RayPlaneY works — a flat forward ray would be parallel to it.</summary>
        private static Ray DoorRay(float x) =>
            new(new Vector3(x, 2f, -2f), new Vector3(0f, -1f, 2f).normalized);

        [UnityTest]
        public IEnumerator FirstTap_SelectsTheDoor_WithoutTogglingOrMoving()
        {
            var (select, input, pointer, model, view, seg) = MakeDoorRig();
            yield return null;
            Physics.SyncTransforms();
            pointer.Ray = DoorRay(2f);

            input.Pressed = true; input.Held = true;
            select.Tick(false);
            input.Pressed = false; input.Held = false;
            select.Tick(false);   // quick release — a tap

            Assert.IsTrue(select.HasSelection, "the tap selected the door");
            Assert.AreEqual(0f, seg.Openings[0].OpenFraction, 1e-4f,
                "the FIRST tap must not toggle — it only selects");
            Assert.AreEqual(0.5f, seg.Openings[0].AlongFraction, 1e-4f, "nothing moved");
            Assert.AreEqual(0, model.History.UndoCount, "no undo entry for a pure select");
        }

        [UnityTest]
        public IEnumerator SecondTap_TogglesTheDoor()
        {
            var (select, input, pointer, model, view, seg) = MakeDoorRig();
            yield return null;
            Physics.SyncTransforms();
            pointer.Ray = DoorRay(2f);

            input.Pressed = true; input.Held = true;
            select.Tick(false);
            input.Pressed = false; input.Held = false;
            select.Tick(false);   // tap 1 — select

            input.Pressed = true; input.Held = true;
            select.Tick(false);
            input.Pressed = false; input.Held = false;
            select.Tick(false);   // tap 2 — toggle

            float deadline = Time.time + OpeningLeafView.AnimSeconds * 3f;
            while (seg.Openings[0].OpenFraction < 0.9f && Time.time < deadline) yield return null;
            Assert.Greater(seg.Openings[0].OpenFraction, 0.9f, "the second tap swings the door open");
            Assert.AreEqual(0, model.History.UndoCount, "toggling is a view action — no undo entry");
        }

        [UnityTest]
        public IEnumerator HoldAndTravel_DragsTheDoorAlongTheWall_AsOneUndoEntry()
        {
            var (select, input, pointer, model, view, seg) = MakeDoorRig();
            yield return null;
            Physics.SyncTransforms();
            pointer.Ray = DoorRay(2f);

            input.Pressed = true; input.Held = true;
            select.Tick(false);                     // press on the door — arms the gesture
            input.Pressed = false;

            yield return new WaitForSeconds(0.35f); // hold past the tap window
            select.Tick(false);                     // engages the drag

            for (int i = 1; i <= 5; i++)
            {
                pointer.Ray = DoorRay(2f + 0.16f * i);   // slide the hand 0.8 m right
                select.Tick(false);
                yield return null;
            }
            input.Held = false;
            select.Tick(false);                     // release — records the move

            float along = seg.Openings[0].AlongFraction;
            Assert.Greater(along, 0.6f, "the door slid along the wall");
            Assert.AreEqual(0f, seg.Openings[0].OpenFraction, 1e-4f, "dragging must not toggle");
            Assert.AreEqual(1, model.History.UndoCount, "one gesture = one undo entry");

            model.History.Undo();
            Assert.AreEqual(0.5f, seg.Openings[0].AlongFraction, 1e-4f, "undo returns the door");
        }

        [UnityTest]
        public IEnumerator ObjectTap_SelectsWithoutNudging_ThenHoldDragMoves()
        {
            // The #46 regression scenario, now runnable headless: tap = pure select,
            // hold+travel = one recorded MoveCommand.
            var rig = Track(new GameObject("Rig"));
            var model = rig.AddComponent<SceneModel>();
            var slabGo = Track(new GameObject("Slab"));
            slabGo.AddComponent<MeshFilter>();
            slabGo.AddComponent<MeshRenderer>();
            var slab = slabGo.AddComponent<RoomPlanner.Floors.Floor>();
            slab.BuildOutline(new List<Vector3>
            {
                new(0f, 0f, 0f), new(2f, 0f, 0f), new(2f, 0f, 2f), new(0f, 0f, 2f),
            }, 0f, 0.2f, 5f, 0f, 0f, 0f);
            var sel = slabGo.AddComponent<Selectable>();
            model.Register(sel);

            var host = Track(new GameObject("Select"));
            var input = host.AddComponent<FakeInput>();
            var pointer = host.AddComponent<FakePointer>();
            var select = host.AddComponent<SelectController>();
            SetField(select, "input", input);
            SetField(select, "pointer", pointer);
            SetField(select, "sceneModel", model);
            yield return null;
            Physics.SyncTransforms();

            var down = new Ray(new Vector3(1f, 2f, 1f), Vector3.down);
            pointer.Ray = down;
            input.Pressed = true; input.Held = true;
            select.Tick(false);
            input.Pressed = false; input.Held = false;
            select.Tick(false);   // tap

            Assert.IsTrue(select.HasSelection, "tap selects the slab");
            Assert.AreEqual(0, model.History.UndoCount, "…and moves nothing (#46)");
            Assert.AreEqual(0f, slab.Outline[0].x, 1e-4f);

            input.Pressed = true; input.Held = true;
            select.Tick(false);
            input.Pressed = false;
            yield return new WaitForSeconds(0.35f);
            select.Tick(false);   // engage
            pointer.Ray = new Ray(new Vector3(1.5f, 2f, 1f), Vector3.down);
            select.Tick(false);   // travel
            input.Held = false;
            select.Tick(false);   // release → records

            Assert.AreEqual(1, model.History.UndoCount, "one drag = one MoveCommand");
            Assert.AreEqual(0.5f, slab.Outline[0].x, 1e-2f, "the slab moved with the hand");
        }
    }
}
