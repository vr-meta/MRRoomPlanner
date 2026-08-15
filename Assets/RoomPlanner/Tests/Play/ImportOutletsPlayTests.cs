using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core;
using RoomPlanner.Core.Ifc;
using RoomPlanner.Editing;
using RoomPlanner.Electrical;
using RoomPlanner.Floors;
using RoomPlanner.Import;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// IFC outlets become NATIVE electrical fixtures (#79): a proxy plate in the file
    /// turns into an editable ElectricFixture facing away from its wall, replaced on
    /// re-import and captured by the project round trip like a hand-placed one.
    /// </summary>
    public class ImportOutletsPlayTests
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

            // no fixture prefab — RestoreFixture's fallback path builds the fixture
            var electric = rig.AddComponent<ElectricController>();
            SetField(electric, "sceneModel", model);

            var import = rig.AddComponent<ImportController>();
            SetField(import, "walls", walls);
            SetField(import, "floors", floors);
            SetField(import, "sceneModel", model);
            return (import, walls, model);
        }

        private static ImportedBuilding BuildingWithOutlet()
        {
            var b = new ImportedBuilding();
            b.Storeys.Add(new ImportedStorey { Name = "L1", Elevation = 0f });
            b.Walls.Add(new ImportedWall
            {
                Path = { new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f) },
                Thickness = 0.2f, Height = 3f, StoreyIndex = 0,
            });
            b.Outlets.Add(new ImportedOutlet
            {
                Name = "Р Розетка:220 V:1",
                Position = new Vector3(2f, 0.3f, 0.15f),   // on the +Z face of the wall
                Normal = Vector3.forward,                   // plate's thin axis
                StoreyIndex = 0,
            });
            return b;
        }

        [UnityTest]
        public IEnumerator IfcOutlet_BecomesANativeFixture_FacingAwayFromTheWall()
        {
            var (import, _, model) = MakeRig();
            import.BuildScene(BuildingWithOutlet());
            yield return null;

            var fixtures = Object.FindObjectsByType<ElectricFixture>(FindObjectsSortMode.None);
            Assert.AreEqual(1, fixtures.Length, "one outlet in the file = one native fixture");
            var fx = fixtures[0];
            Assert.AreEqual(FixtureKind.Outlet, fx.Kind);
            Assert.AreEqual(2f, fx.transform.position.x, 1e-3f);
            Assert.AreEqual(0.3f, fx.transform.position.y, 1e-3f);
            Assert.Greater(Vector3.Dot(fx.transform.forward, Vector3.forward), 0.99f,
                "the face looks AWAY from the wall centreline (toward +Z)");

            // captured like a hand-placed one — the project round trip keeps it
            var data = ProjectStore.Capture(null, null);
            Assert.AreEqual(1, data.Fixtures.Count, "imported outlet persists");
        }

        [UnityTest]
        public IEnumerator Reimport_ReplacesOutlets_InsteadOfStacking()
        {
            var (import, _, _) = MakeRig();
            import.BuildScene(BuildingWithOutlet());
            yield return null;
            import.BuildScene(BuildingWithOutlet());
            yield return null;

            var fixtures = Object.FindObjectsByType<ElectricFixture>(FindObjectsSortMode.None);
            Assert.AreEqual(1, fixtures.Length, "a repeated Load replaces the electrical layer");
        }
    }
}
