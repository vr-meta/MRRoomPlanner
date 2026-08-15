using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Electrical;
using RoomPlanner.Measure;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Wire gestures through the REAL ElectricController.Tick (#61/#81): clicking the
    /// free end of an existing route picks it up and CONTINUES it — same WireRoute,
    /// one undoable edit for the whole continuation.
    /// </summary>
    public class WireGesturePlayTests
    {
        private class FakeInput : MeasureInput
        {
            public bool Pressed, Clear;
            public override bool ConfirmPressed() => Pressed;
            public override bool ConfirmHeld() => false;
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
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);

        private (ElectricController electric, FakeInput input, FakePointer pointer,
                 SceneModel model, WireRoute route) MakeRig()
        {
            var rig = Track(new GameObject("Rig"));
            var model = rig.AddComponent<SceneModel>();

            // a wall to route along — layer 6, the electric surface raycast mask
            var template = Track(new GameObject("WallTemplate") { layer = 6 });
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
            seg.Offset = WallOffsetMode.Center;   // faces at z = ±0.1, deterministic
            walls.Sync();

            var host = Track(new GameObject("Electric"));
            var input = host.AddComponent<FakeInput>();
            var pointer = host.AddComponent<FakePointer>();
            var raycaster = host.AddComponent<SceneRaycaster>();
            var electric = host.AddComponent<ElectricController>();
            SetField(electric, "input", input);
            SetField(electric, "pointer", pointer);
            SetField(electric, "raycaster", raycaster);
            SetField(electric, "sceneModel", model);
            electric.GetSettings().SelectTab(2);   // Wire sub-mode, the real tab switch

            // an existing route with BOTH ends free, hanging near the wall face
            var wireGo = Track(new GameObject("Wire"));
            wireGo.AddComponent<MeshFilter>();
            wireGo.AddComponent<MeshRenderer>();
            var route = wireGo.AddComponent<WireRoute>();
            route.Build(new List<Vector3>
            {
                new(0.5f, 1.0f, 0.12f), new(2.0f, 1.5f, 0.12f),
            }, CableType.C3x25);
            var sel = wireGo.AddComponent<Selectable>();
            model.Register(sel);

            return (electric, input, pointer, model, route);
        }

        private static Ray AtWall(float x, float y) =>
            new(new Vector3(x, y, 1f), Vector3.back);

        private void Click(ElectricController electric, FakeInput input)
        {
            input.Pressed = true;
            electric.Tick(false);
            input.Pressed = false;
            electric.Tick(false);
        }

        [UnityTest]
        public IEnumerator ClickOnFreeEnd_ContinuesTheSameRoute_AsOneUndo()
        {
            var (electric, input, pointer, model, route) = MakeRig();
            yield return null;
            Physics.SyncTransforms();

            pointer.Ray = AtWall(2f, 1.5f);        // at the route's free tail end
            Click(electric, input);                 // pick it up
            yield return new WaitForSeconds(ElectricalDefaults.PlaceDebounceSeconds + 0.05f);

            pointer.Ray = AtWall(3.2f, 1.5f);      // draw onward along the wall
            Click(electric, input);
            input.Clear = true;
            electric.Tick(false);                   // B — finish
            input.Clear = false;
            yield return null;

            Assert.AreEqual(1, Object.FindObjectsByType<WireRoute>(FindObjectsSortMode.None).Length,
                "continuation must NOT create a second wire");
            Assert.Greater(route.Points.Count, 2, "the same route grew");
            var last = route.Points[route.Points.Count - 1];
            Assert.AreEqual(3.2f, last.x, 0.02f, "the new point is the tail");
            Assert.AreEqual(1, model.History.UndoCount, "whole continuation = one undo entry");

            model.History.Undo();
            Assert.AreEqual(2, route.Points.Count, "undo restores the original polyline");
        }

        [UnityTest]
        public IEnumerator PickingUpTheStart_ReversesSoDrawingAppends()
        {
            var (electric, input, pointer, model, route) = MakeRig();
            yield return null;
            Physics.SyncTransforms();

            pointer.Ray = AtWall(0.5f, 1.0f);      // the route's START is free too
            Click(electric, input);
            yield return new WaitForSeconds(ElectricalDefaults.PlaceDebounceSeconds + 0.05f);

            pointer.Ray = AtWall(0.5f, 0.4f);      // extend downward from the old start
            Click(electric, input);
            input.Clear = true;
            electric.Tick(false);
            input.Clear = false;
            yield return null;

            Assert.AreEqual(1, Object.FindObjectsByType<WireRoute>(FindObjectsSortMode.None).Length);
            Assert.Greater(route.Points.Count, 2);
            var tail = route.Points[route.Points.Count - 1];
            Assert.AreEqual(0.4f, tail.y, 0.02f,
                "grabbing the start reversed the polyline — the new point is the tail");
        }
    }
}
