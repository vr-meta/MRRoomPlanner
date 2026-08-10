using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core.Ifc;
using RoomPlanner.Core.Project;
using RoomPlanner.Editing;
using RoomPlanner.Electrical;
using RoomPlanner.Floors;
using RoomPlanner.Import;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Persistence v0 (design/06): build a scene through the import pipeline, Capture →
    /// JSON → Apply into a CLEARED scene, and everything parametric comes back. Loading
    /// reuses BuildScene, so this also guards the clear-then-rebuild path the autosave
    /// runs on every launch.
    /// </summary>
    public class ProjectRoundTripPlayTests
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
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);

        private (ImportController import, WallGraphRenderer walls, SceneModel model) MakeRig()
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

            var import = rig.AddComponent<ImportController>();
            SetField(import, "walls", walls);
            SetField(import, "floors", floors);
            SetField(import, "sceneModel", model);
            return (import, walls, model);
        }

        private static ImportedBuilding SampleBuilding()
        {
            var b = new ImportedBuilding();
            b.Walls.Add(new ImportedWall
            {
                Path = { new Vector3(0f, 0f, 0f), new Vector3(7f, 0f, 0f) },
                Thickness = 0.15f, Height = 3f,
            });
            b.Openings.Add(new ImportedOpening
            {
                WallIndex = 0, AlongFraction = 0.5f, Width = 0.9f, Height = 2.1f, Sill = 0.9f,
            });
            b.Openings.Add(new ImportedOpening
            {
                WallIndex = 0, AlongFraction = 0.2f, Width = 0.9f, Height = 2.1f, IsDoor = true,
                SwingDir = new Vector3(0f, 0f, 1f), HingeDir = new Vector3(-1f, 0f, 0f),
            });
            var slab = new ImportedSlab
            {
                Outline =
                {
                    new Vector3(0f, 0f, 0f), new Vector3(5f, 0f, 0f),
                    new Vector3(5f, 0f, 5f), new Vector3(0f, 0f, 5f),
                },
                Thickness = 0.2f, Level = 0f,
            };
            slab.Holes.Add(new List<Vector3>
            {
                new(2f, 0f, 2f), new(3f, 0f, 2f), new(3f, 0f, 3f), new(2f, 0f, 3f),
            });
            b.Slabs.Add(slab);
            b.Stairs.Add(new ImportedStair
            {
                Base = new Vector3(1f, 0f, 1f), YawDeg = 30f, Width = 1.1f,
                Risers = 5, RiserHeight = 0.18f, TreadDepth = 0.26f,
                Kind = RoomPlanner.Stairs.StairKind.Open,
            });
            var basin = new ImportedMep { Name = "Basin", Origin = new Vector3(4f, 0.8f, 4f), StoreyIndex = 2 };
            basin.Vertices.AddRange(new[]
            {
                new Vector3(-0.2f, 0f, -0.2f), new Vector3(0.2f, 0f, -0.2f), new Vector3(0f, 0f, 0.2f),
            });
            basin.Triangles.AddRange(new[] { 0, 1, 2 });
            b.Plumbing.Add(basin);
            return b;
        }

        [UnityTest]
        public IEnumerator ElectricalLayer_SurvivesTheRoundTrip()
        {
            // Format v2 (audit B1): the autosave used to drop every outlet and wire
            // while ClearScene actively destroyed them — building + wiring + restart
            // returned the building without its electrics.
            var (import, walls, model) = MakeRig();
            var electric = Track(new GameObject("Electric")).AddComponent<ElectricController>();
            SetField(electric, "sceneModel", model);

            import.BuildScene(SampleBuilding());

            var fxGo = Track(new GameObject("Outlet"));
            fxGo.AddComponent<MeshFilter>();
            fxGo.AddComponent<MeshRenderer>();
            var fx = fxGo.AddComponent<ElectricFixture>();
            fx.Build(FixtureKind.Outlet, posts: 2, keys: 1);
            fx.transform.position = new Vector3(1f, 0.3f, 0f);
            var fxSel = fxGo.AddComponent<Selectable>();
            model.Register(fxSel);
            string savedId = fxSel.Id;

            var wGo = Track(new GameObject("Run"));
            wGo.AddComponent<MeshFilter>();
            wGo.AddComponent<MeshRenderer>();
            var route = wGo.AddComponent<WireRoute>();
            Assert.IsTrue(route.Build(new List<Vector3>
                { fx.TerminalWorld, new Vector3(1f, 2.3f, 0f) }, CableType.C3x15));
            route.StartFixtureId = savedId;
            model.Register(wGo.AddComponent<Selectable>());

            var data = ProjectStore.Capture(walls, null);
            Assert.AreEqual(1, data.Fixtures.Count, "the outlet is captured");
            Assert.AreEqual(1, data.Wires.Count, "the run is captured");
            Assert.AreEqual(savedId, data.Wires[0].StartId);

            string json = data.ToJson();
            import.ClearScene();
            yield return null;
            Assert.AreEqual(0,
                Object.FindObjectsByType<ElectricFixture>(FindObjectsSortMode.None).Length,
                "clear really destroys the electrics");

            ProjectStore.Apply(ProjectData.FromJson(json), import, null);
            yield return null;

            var rFx = Object.FindObjectsByType<ElectricFixture>(FindObjectsSortMode.None);
            Assert.AreEqual(1, rFx.Length);
            Assert.AreEqual(FixtureKind.Outlet, rFx[0].Kind);
            Assert.AreEqual(2, rFx[0].Posts);
            Assert.AreEqual(new Vector3(1f, 0.3f, 0f), rFx[0].transform.position);
            var rSel = rFx[0].GetComponent<Selectable>();
            Assert.AreEqual(savedId, rSel.Id, "the saved id is kept verbatim");

            var rRoute = Object.FindObjectsByType<WireRoute>(FindObjectsSortMode.None);
            Assert.AreEqual(1, rRoute.Length);
            Assert.AreEqual(CableType.C3x15, rRoute[0].Cable);
            Assert.AreEqual(savedId, rRoute[0].StartFixtureId);

            // the id link must be LIVE, not just a string: moving the restored outlet
            // drags the attached wire end, exactly like before the restart
            Vector3 endBefore = rRoute[0].GetPoint(0);
            rSel.MoveBy(new Vector3(0.3f, 0f, 0f));
            Assert.AreEqual(endBefore + new Vector3(0.3f, 0f, 0f), rRoute[0].GetPoint(0),
                "wire ends re-attach through the preserved id");
        }

        [UnityTest]
        public IEnumerator CaptureJsonApply_RestoresTheScene()
        {
            var (import, walls, model) = MakeRig();
            yield return null;

            import.BuildScene(SampleBuilding());
            var seg0 = walls.Graph.Segments[0];
            seg0.Join = WallJoin.Round;                      // a user edit that must survive
            var paint = new Color(0.77f, 0.39f, 0.23f);
            walls.ViewOf(seg0).GetComponent<Selectable>().SetPaint(paint);
            string json = ProjectStore.Capture(walls, null).ToJson();

            import.ClearScene();
            Assert.AreEqual(0, walls.Graph.Segments.Count, "scene really cleared");
            Assert.AreEqual(0, model.History.UndoCount);
            yield return null;                               // let Destroy() take effect

            ProjectStore.Apply(RoomPlanner.Core.Project.ProjectData.FromJson(json), import, null);
            yield return null;

            Assert.AreEqual(1, walls.Graph.Segments.Count);
            var seg = walls.Graph.Segments[0];
            Assert.AreEqual(0.15f, seg.Thickness, 1e-4);
            Assert.AreEqual(WallJoin.Round, seg.Join, "user edits round-trip");
            Assert.AreEqual(2, seg.Openings.Count);
            Assert.AreEqual(0.9f, seg.Openings[0].Width, 1e-4);
            var door = seg.Openings.Find(o => o.IsDoor);
            Assert.IsNotNull(door);
            Assert.AreEqual(0f, Vector3.Distance(door.SwingDir, new Vector3(0f, 0f, 1f)), 1e-4,
                "door swing survives the round-trip");
            Assert.AreEqual(0f, Vector3.Distance(door.HingeDir, new Vector3(-1f, 0f, 0f)), 1e-4);
            Assert.IsNotNull(walls.Graph.FindNode(new Vector3(7f, 0f, 0f)), "geometry back in place");

            var restoredSel = walls.ViewOf(seg).GetComponent<Selectable>();
            Assert.IsTrue(restoredSel.IsPainted, "paint survives the round-trip");
            Assert.AreEqual(paint.r, restoredSel.Paint.r, 1e-3);
            Assert.AreEqual(paint.b, restoredSel.Paint.b, 1e-3);

            Floor slab = null;
            foreach (var f in Object.FindObjectsByType<Floor>(FindObjectsSortMode.None))
                if (f.Outline.Count >= 3) slab = f;
            Assert.IsNotNull(slab);
            Assert.AreEqual(1, slab.Holes.Count, "stairwell hole restored");

            var stairs = Object.FindObjectsByType<RoomPlanner.Stairs.Stair>(FindObjectsSortMode.None);
            Assert.AreEqual(1, stairs.Length);
            Assert.AreEqual(RoomPlanner.Stairs.StairKind.Open, stairs[0].Kind, "stair kind survives");
            Assert.AreEqual(5, stairs[0].Risers);
            Assert.AreEqual(30f, stairs[0].YawDeg, 1e-3);

            var mep = Object.FindObjectsByType<MepView>(FindObjectsSortMode.None);
            Assert.AreEqual(1, mep.Length);
            Assert.AreEqual(new Vector3(4f, 0.8f, 4f), mep[0].transform.position);
            Assert.AreEqual(3, mep[0].GetComponent<MeshFilter>().sharedMesh.vertexCount);
            // B6: ProjectMep.Storey existed but Capture never filled it — the storey
            // filter silently died after every save/load round-trip.
            Assert.AreEqual(2, mep[0].StoreyIndex, "storey survives the round-trip");
        }
    }
}
