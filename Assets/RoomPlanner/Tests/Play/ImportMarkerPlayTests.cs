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
    /// Placement marker (#60): an IFC load (LoadBuilding) lands the building on the
    /// marked point, while a project load (BuildScene via ProjectStore.Apply) must
    /// NEVER re-offset saved coordinates — even with a marker still set.
    /// </summary>
    public class ImportMarkerPlayTests
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

        private (ImportController import, WallGraphRenderer walls) MakeRig()
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
            return (import, walls);
        }

        private static ImportedBuilding OneWall() // 0..4 on X, so the anchor is (2, 0, 0)
        {
            var b = new ImportedBuilding();
            b.Walls.Add(new ImportedWall
            {
                Path = { new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f) },
                Thickness = 0.15f, Height = 3f,
            });
            return b;
        }

        private static Vector3 MinNode(WallGraphRenderer walls)
        {
            var min = new Vector3(float.MaxValue, 0f, float.MaxValue);
            foreach (var n in walls.Graph.Nodes)
                if (n.Position.x < min.x) min = n.Position;
            return min;
        }

        [UnityTest]
        public IEnumerator IfcLandsOnMarker_ProjectLoadDoesNot_Reoffset()
        {
            var (import, walls) = MakeRig();

            import.SetMarker(new Vector3(10f, 0f, 5f));
            import.LoadBuilding(OneWall());
            yield return null;

            // anchor (2,0,0) → marker (10,0,5): the wall now runs 8..12 at z=5
            Assert.AreEqual(new Vector3(8f, 0f, 5f), MinNode(walls), "IFC lands on the marker");

            // capture the scene and load it back as a PROJECT, marker still set
            var data = ProjectStore.Capture(walls, null);
            ProjectStore.Apply(data, import, null);
            yield return null;

            Assert.AreEqual(new Vector3(8f, 0f, 5f), MinNode(walls),
                "a project load must never re-offset saved coordinates");
        }
    }
}
