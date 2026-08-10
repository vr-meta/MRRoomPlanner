using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core.Ifc;
using RoomPlanner.Editing;
using RoomPlanner.Floors;
using RoomPlanner.Import;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// IFC import step I5 (docs/design/18-ifc-import.md): an ImportedBuilding becomes real
    /// wall-graph segments and floor slabs, the whole import is ONE undo entry, and the
    /// storey row filters visibility. Real components; only the serialized wiring is done
    /// by reflection (PlayMode has no SerializedObject).
    /// </summary>
    public class ImportPlayTests
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

            // wall view template — parked inactive, exactly like the scene rig's prefab
            var wallTemplate = Track(new GameObject("WallTemplate"));
            wallTemplate.SetActive(false);
            wallTemplate.AddComponent<MeshFilter>();
            wallTemplate.AddComponent<MeshRenderer>();
            var wallPrefab = wallTemplate.AddComponent<Wall>();
            wallTemplate.AddComponent<Selectable>();

            var walls = rig.AddComponent<WallGraphRenderer>();
            walls.Configure(wallPrefab, model);

            // floor template for FloorController's prefab field
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

        private static ImportedBuilding TwoStoreyBuilding()
        {
            var b = new ImportedBuilding();
            b.Storeys.Add(new ImportedStorey { Name = "L1", Elevation = 0f });
            b.Storeys.Add(new ImportedStorey { Name = "L2", Elevation = 3f });
            b.Walls.Add(new ImportedWall
            {
                Path = { new Vector3(0f, 0f, 0f), new Vector3(7f, 0f, 0f) },
                Thickness = 0.15f, Height = 3f, StoreyIndex = 0,
            });
            b.Walls.Add(new ImportedWall   // a 30×30 column on the upper storey
            {
                Path = { new Vector3(2f, 3f, 2f), new Vector3(2.3f, 3f, 2f) },
                Thickness = 0.3f, Height = 3f, StoreyIndex = 1, FromColumn = true,
            });
            b.Openings.Add(new ImportedOpening
            {
                WallIndex = 0, AlongFraction = 0.5f,
                Width = 0.9f, Height = 2.1f, Sill = 0f, IsDoor = true,
            });
            var slab = new ImportedSlab
            {
                Outline =
                {
                    new Vector3(0f, 0f, 0f), new Vector3(5f, 0f, 0f),
                    new Vector3(5f, 0f, 5f), new Vector3(0f, 0f, 5f),
                },
                Thickness = 0.2f, Level = 0f, StoreyIndex = 0,
            };
            slab.Holes.Add(new List<Vector3>
            {
                new(2f, 0f, 2f), new(3f, 0f, 2f), new(3f, 0f, 3f), new(2f, 0f, 3f),
            });
            b.Slabs.Add(slab);
            b.Stairs.Add(new ImportedStair
            {
                Base = new Vector3(1f, 0f, 1f), YawDeg = 0f, Width = 1f,
                Risers = 3, RiserHeight = 0.2f, TreadDepth = 0.25f, StoreyIndex = 0,
            });
            var basin = new ImportedMep
            {
                Name = "Basin", Origin = new Vector3(4f, 0.8f, 4f), StoreyIndex = 0,
            };
            basin.Vertices.AddRange(new[]
            {
                new Vector3(-0.2f, 0f, -0.2f), new Vector3(0.2f, 0f, -0.2f),
                new Vector3(0.2f, 0f, 0.2f), new Vector3(-0.2f, 0f, 0.2f),
            });
            basin.Triangles.AddRange(new[] { 0, 1, 2, 0, 2, 3 });
            b.Plumbing.Add(basin);
            return b;
        }

        [UnityTest]
        public IEnumerator BuildsSegmentsAndSlabs_FromImportedBuilding()
        {
            var (import, walls, _) = MakeRig();
            yield return null;

            import.BuildScene(TwoStoreyBuilding());

            Assert.AreEqual(2, walls.Graph.Segments.Count, "wall + column segment");
            var seg = walls.Graph.Segments[0];
            Assert.AreEqual(0.15f, seg.Thickness, 1e-4);
            Assert.AreEqual(WallOffsetMode.Center, seg.Offset, "IFC axis is the centerline");
            Assert.IsTrue(walls.IsVisible(seg), "the view is alive and shown");
            Assert.AreEqual(1, seg.Openings.Count, "the door landed on the graph segment as data");
            Assert.AreEqual(0.9f, seg.Openings[0].Width, 1e-4);

            var slabs = Object.FindObjectsByType<Floor>(FindObjectsSortMode.None);
            Assert.AreEqual(1, slabs.Length);
            Assert.AreEqual(1, slabs[0].Holes.Count, "the stairwell hole was cut");

            var stairs = Object.FindObjectsByType<RoomPlanner.Stairs.Stair>(FindObjectsSortMode.None);
            Assert.AreEqual(1, stairs.Length, "the flight became a parametric Stair");
            Assert.AreEqual(3, stairs[0].Risers);

            var mep = Object.FindObjectsByType<MepView>(FindObjectsSortMode.None);
            Assert.AreEqual(1, mep.Length, "the plumbing fixture arrived");
            Assert.AreEqual(new Vector3(4f, 0.8f, 4f), mep[0].transform.position);

            StringAssert.Contains("2w 1s 1o 1h 1st 1p", import.Status);
        }

        [UnityTest]
        public IEnumerator WholeImportIsOneUndoEntry()
        {
            var (import, walls, model) = MakeRig();
            yield return null;

            import.BuildScene(TwoStoreyBuilding());
            Assert.AreEqual(1, model.History.UndoCount, "one batch command for the whole file");

            model.History.Undo();
            foreach (var s in walls.Graph.Segments)
                Assert.IsFalse(walls.IsVisible(s), "undo hides every imported wall");
            // the parked template also matches Include — the imported slab is the one with an outline
            Floor imported = null;
            foreach (var f in Object.FindObjectsByType<Floor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (f.Outline.Count >= 3) imported = f;
            Assert.IsNotNull(imported);
            Assert.IsFalse(imported.gameObject.activeSelf, "undo hides the slab");

            model.History.Redo();
            foreach (var s in walls.Graph.Segments)
                Assert.IsTrue(walls.IsVisible(s), "redo shows them again");
        }

        [UnityTest]
        public IEnumerator StoreyRowFiltersVisibilityByLevel()
        {
            var (import, walls, _) = MakeRig();
            yield return null;

            import.BuildScene(TwoStoreyBuilding());
            var ground = walls.Graph.Segments[0];   // storey 0
            var column = walls.Graph.Segments[1];   // storey 1

            import.NextStorey();                    // All → L1
            Assert.AreEqual(0, import.StoreyFilter);
            Assert.IsTrue(walls.IsVisible(ground));
            Assert.IsFalse(walls.IsVisible(column));

            import.NextStorey();                    // L1 → L2
            Assert.IsFalse(walls.IsVisible(ground));
            Assert.IsTrue(walls.IsVisible(column));

            import.NextStorey();                    // L2 → All
            Assert.AreEqual(-1, import.StoreyFilter);
            Assert.IsTrue(walls.IsVisible(ground));
            Assert.IsTrue(walls.IsVisible(column));
        }

        [UnityTest]
        public IEnumerator ImportedWallsMergeNodesWithinTolerance()
        {
            var (import, walls, _) = MakeRig();
            yield return null;

            // Two IFC walls whose endpoints meet — Revit writes them a hair apart sometimes.
            var b = new ImportedBuilding();
            b.Walls.Add(new ImportedWall
            {
                Path = { new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f) },
                Thickness = 0.15f, Height = 3f,
            });
            b.Walls.Add(new ImportedWall
            {
                Path = { new Vector3(4.004f, 0f, 0.004f), new Vector3(4f, 0f, 6f) },
                Thickness = 0.15f, Height = 3f,
            });
            import.BuildScene(b);

            Assert.AreEqual(3, walls.Graph.Nodes.Count, "shared corner became ONE node");
            Assert.AreEqual(2, walls.Graph.Segments.Count);
        }
    }
}
