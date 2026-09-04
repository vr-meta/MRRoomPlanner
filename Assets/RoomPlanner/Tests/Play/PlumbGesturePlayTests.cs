using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Measure;
using RoomPlanner.Plumbing;
using RoomPlanner.Tools;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Plumb gestures through the REAL PlumbController.Tick (design/30, #88): a riser
    /// from one floor click, a pipe run teeing into the riser axis, stub-out and drain
    /// placement, and the save/load round-trip of the whole layer.
    /// </summary>
    public class PlumbGesturePlayTests
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
            foreach (var fx in Object.FindObjectsByType<PlumbFixture>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (fx != null) Object.DestroyImmediate(fx.gameObject);
            foreach (var p in Object.FindObjectsByType<PipeRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (p != null) Object.DestroyImmediate(p.gameObject);
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private static void SetField(object target, string field, object value) =>
            target.GetType()
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);

        private (PlumbController plumb, FakeInput input, FakePointer pointer, SceneModel model)
            MakeRig()
        {
            var rig = Track(new GameObject("Rig"));
            var model = rig.AddComponent<SceneModel>();

            // a wall to mount stub-outs on — layer 6, Center offset: faces at z = ±0.1
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
            seg.Offset = WallOffsetMode.Center;
            walls.Sync();

            // the floor: a flat selectable box, top face at y = 0
            var floor = Track(new GameObject("FloorSlab") { layer = 6 });
            var box = floor.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.05f, 0f);
            box.size = new Vector3(20f, 0.1f, 20f);
            floor.AddComponent<Selectable>();

            var host = Track(new GameObject("Plumb"));
            var input = host.AddComponent<FakeInput>();
            var pointer = host.AddComponent<FakePointer>();
            var raycaster = host.AddComponent<SceneRaycaster>();
            var plumb = host.AddComponent<PlumbController>();
            SetField(plumb, "input", input);
            SetField(plumb, "pointer", pointer);
            SetField(plumb, "raycaster", raycaster);
            SetField(plumb, "sceneModel", model);

            // parked templates play the prefab role (the electric-test recipe)
            var pipeTemplate = Track(new GameObject("PipeTemplate") { layer = 6 });
            pipeTemplate.SetActive(false);
            pipeTemplate.AddComponent<MeshFilter>();
            pipeTemplate.AddComponent<MeshRenderer>();
            var pipePrefab = pipeTemplate.AddComponent<PipeRoute>();
            pipeTemplate.AddComponent<Selectable>();
            SetField(plumb, "pipePrefab", pipePrefab);

            var fxTemplate = Track(new GameObject("PlumbFixtureTemplate") { layer = 6 });
            fxTemplate.SetActive(false);
            fxTemplate.AddComponent<MeshFilter>();
            fxTemplate.AddComponent<MeshRenderer>();
            var fxPrefab = fxTemplate.AddComponent<PlumbFixture>();
            fxTemplate.AddComponent<Selectable>();
            SetField(plumb, "fixturePrefab", fxPrefab);

            return (plumb, input, pointer, model);
        }

        private static Ray AtFloor(float x, float z) =>
            new(new Vector3(x, 2f, z), Vector3.down);

        private static Ray AtWall(float x, float y) =>
            new(new Vector3(x, y, 1f), Vector3.back);

        private void Click(PlumbController plumb, FakeInput input)
        {
            input.Pressed = true;
            plumb.Tick(false);
            input.Pressed = false;
            plumb.Tick(false);
        }

        private static WaitForSeconds Debounce() =>
            new(PlumbingDefaults.PlaceDebounceSeconds + 0.05f);

        [UnityTest]
        public IEnumerator RiserClick_PlacesVerticalD110_FloorToCeiling()
        {
            var (plumb, input, pointer, _) = MakeRig();
            yield return null;
            Physics.SyncTransforms();

            plumb.GetSettings().SelectTab(0);      // Riser
            pointer.Ray = AtFloor(1f, 2f);
            Click(plumb, input);

            // the ghost column (#112) is an active PipeRoute too — count registered only
            var placed = new List<PipeRoute>();
            foreach (var pr in Object.FindObjectsByType<PipeRoute>(FindObjectsSortMode.None))
            {
                var sel = pr.GetComponent<Selectable>();
                if (sel != null && !string.IsNullOrEmpty(sel.Id)) placed.Add(pr);
            }
            Assert.AreEqual(1, placed.Count);
            var r = placed[0];
            Assert.IsTrue(r.IsRiser);
            Assert.AreEqual(PipeDiameter.D110, r.Diameter);
            Assert.AreEqual(0f, r.GetPoint(0).y, 0.01f, "foot on the floor");
            Assert.AreEqual(2.7f, r.GetPoint(1).y, 0.01f, "no ceiling in the rig — wall-height fallback");
            Assert.AreEqual(1f, r.GetPoint(0).x, 0.01f);
            Assert.AreEqual(2f, r.GetPoint(0).z, 0.01f);
        }

        [UnityTest]
        public IEnumerator PipeRun_TeesIntoTheRiserAxis_AndRecordsTheLink()
        {
            var (plumb, input, pointer, model) = MakeRig();
            yield return null;
            Physics.SyncTransforms();

            plumb.GetSettings().SelectTab(0);      // Riser: one click on the floor
            pointer.Ray = AtFloor(1f, 0.3f);
            Click(plumb, input);
            yield return Debounce();

            PipeRoute riser = null;   // skip the ghost column (#112) — registered only
            foreach (var pr in Object.FindObjectsByType<PipeRoute>(FindObjectsSortMode.None))
            {
                var s = pr.GetComponent<Selectable>();
                if (s != null && !string.IsNullOrEmpty(s.Id)) riser = pr;
            }
            Assert.IsNotNull(riser, "one registered riser placed");
            string riserId = riser.GetComponent<Selectable>().Id;
            Assert.IsFalse(string.IsNullOrEmpty(riserId), "registered risers carry an id");
            Physics.SyncTransforms();

            plumb.GetSettings().SelectTab(1);      // Pipe: start ON the riser axis
            // 8 cm off the axis: outside the D110 tube itself (so the ray lands on the
            // floor, not the pipe top) but inside the 10 cm magnet radius
            pointer.Ray = AtFloor(1.08f, 0.3f);
            Click(plumb, input);
            yield return Debounce();

            pointer.Ray = AtWall(3f, 0.4f);        // then off along the wall
            Click(plumb, input);
            input.Clear = true;
            plumb.Tick(false);                     // B — finish
            input.Clear = false;
            yield return null;

            var routes = Object.FindObjectsByType<PipeRoute>(FindObjectsSortMode.None);
            Assert.AreEqual(2, routes.Length, "the riser and one run");
            PipeRoute run = routes[0].IsRiser ? routes[1] : routes[0];
            Assert.AreEqual(riserId, run.StartFixtureId, "the tee is a logical link by id");
            var first = run.GetPoint(0);
            Assert.AreEqual(1f, first.x, 0.02f, "the start snapped onto the riser axis");
            Assert.AreEqual(0.3f, first.z, 0.02f);
        }

        [UnityTest]
        public IEnumerator OutletClick_MountsOnTheWall_AtThePresetHeight()
        {
            var (plumb, input, pointer, _) = MakeRig();
            yield return null;
            Physics.SyncTransforms();

            plumb.GetSettings().SelectTab(2);      // Outlet (defaults: Toilet, 90°)
            pointer.Ray = AtWall(2f, 1.2f);        // click high — the preset must win
            Click(plumb, input);

            var fixtures = Object.FindObjectsByType<PlumbFixture>(FindObjectsSortMode.None);
            // the ghost preview is active too — count only registered ones
            PlumbFixture placed = null;
            foreach (var f in fixtures)
            {
                var sel = f.GetComponent<Selectable>();
                if (sel != null && !string.IsNullOrEmpty(sel.Id)) placed = f;
            }
            Assert.IsNotNull(placed, "one stub-out placed");
            Assert.AreEqual(PlumbFixtureKind.ToiletOutlet, placed.Kind);
            Assert.AreEqual(OutletAngle.Deg90, placed.Angle);
            Assert.AreEqual(PlumbingDefaults.ToiletOutletHeight, placed.transform.position.y, 1e-3f,
                "height snaps to the preset, not the ray tremor");
            Assert.Greater(placed.transform.position.z, 0.09f, "sits on the wall face");
        }

        [UnityTest]
        public IEnumerator DrainClick_SitsFlushInTheFloor()
        {
            var (plumb, input, pointer, _) = MakeRig();
            yield return null;
            Physics.SyncTransforms();

            plumb.GetSettings().SelectTab(3);      // Drain
            pointer.Ray = AtFloor(2.5f, 3f);
            Click(plumb, input);

            PlumbFixture placed = null;
            foreach (var f in Object.FindObjectsByType<PlumbFixture>(FindObjectsSortMode.None))
            {
                var sel = f.GetComponent<Selectable>();
                if (sel != null && !string.IsNullOrEmpty(sel.Id)) placed = f;
            }
            Assert.IsNotNull(placed);
            Assert.AreEqual(PlumbFixtureKind.FloorDrain, placed.Kind);
            Assert.AreEqual(0f, placed.transform.position.y, 1e-3f, "grate flush with the floor");
            Assert.AreEqual(2.5f, placed.transform.position.x, 0.01f);
        }

        [UnityTest]
        public IEnumerator Teleport_ShiftsAHalfDrawnRun_WithTheModel()
        {
            var (plumb, input, pointer, model) = MakeRig();
            yield return null;
            Physics.SyncTransforms();

            plumb.GetSettings().SelectTab(1);      // Pipe: two points, run NOT finished
            pointer.Ray = AtWall(1f, 0.5f);
            Click(plumb, input);
            yield return Debounce();
            pointer.Ray = AtWall(2f, 0.5f);
            Click(plumb, input);

            // the tape-measure lesson: a half-drawn run is model data — it shifts
            var delta = new Vector3(3f, 0f, -1f);
            model.History.Execute(new RoomPlanner.Tools.TeleportCommand(
                null, RoomPlanner.Tools.TeleportCommand.CollectFloors(), delta,
                draftShift: d => plumb.ShiftDraft(d)));

            input.Clear = true;
            plumb.Tick(false);                     // B — finish from the SHIFTED draft
            input.Clear = false;
            yield return null;

            PipeRoute run = null;
            foreach (var pr in Object.FindObjectsByType<PipeRoute>(FindObjectsSortMode.None))
            {
                var s = pr.GetComponent<Selectable>();
                if (s != null && !string.IsNullOrEmpty(s.Id)) run = pr;
            }
            Assert.IsNotNull(run);
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(4f, 0.5f, -0.9f), run.GetPoint(0)), 0.02f,
                "the first drawn point followed the teleport");
        }

        [UnityTest]
        public IEnumerator SaveLoad_RoundTripsTheLayer_WithIdsIntact()
        {
            var (plumb, input, pointer, model) = MakeRig();
            yield return null;
            Physics.SyncTransforms();

            plumb.GetSettings().SelectTab(0);
            pointer.Ray = AtFloor(1f, 0.3f);
            Click(plumb, input);                   // riser
            yield return Debounce();
            Physics.SyncTransforms();

            plumb.GetSettings().SelectTab(1);
            pointer.Ray = AtFloor(1.08f, 0.3f);
            Click(plumb, input);                   // pipe from the riser…
            yield return Debounce();
            pointer.Ray = AtWall(3f, 0.4f);
            Click(plumb, input);
            input.Clear = true;
            plumb.Tick(false);                     // …finished with B
            input.Clear = false;
            yield return null;

            var data = RoomPlanner.Import.ProjectStore.Capture(null, null);
            Assert.AreEqual(2, data.Pipes.Count, "riser + run captured");
            string savedRiserId = null;
            foreach (var p in data.Pipes) if (p.IsRiser) savedRiserId = p.Id;
            Assert.IsFalse(string.IsNullOrEmpty(savedRiserId));

            foreach (var p in Object.FindObjectsByType<PipeRoute>(FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            yield return null;

            RoomPlanner.Import.ProjectStore.RestorePlumbing(data);
            yield return null;

            var routes = Object.FindObjectsByType<PipeRoute>(FindObjectsSortMode.None);
            Assert.AreEqual(2, routes.Length, "both pipes came back");
            PipeRoute riser = routes[0].IsRiser ? routes[0] : routes[1];
            PipeRoute run = routes[0].IsRiser ? routes[1] : routes[0];
            Assert.AreEqual(savedRiserId, riser.GetComponent<Selectable>().Id, "id verbatim");
            Assert.AreEqual(savedRiserId, run.StartFixtureId, "the tee link survived the trip");
            Assert.AreEqual(PipeDiameter.D110, riser.Diameter);
        }
    }
}
