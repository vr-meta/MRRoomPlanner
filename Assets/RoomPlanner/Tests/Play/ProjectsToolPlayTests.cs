using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core;
using RoomPlanner.Core.Ifc;
using RoomPlanner.Core.Project;
using RoomPlanner.Editing;
using RoomPlanner.Floors;
using RoomPlanner.Import;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Projects tool lifecycle (#58) driven through the REAL schema widgets — the same
    /// delegates the inspector rows invoke: Save allocates "Project N" and writes the
    /// file, New empties the scene without touching named files, Open restores the
    /// saved scene and marks it current, Delete removes the file and demotes the
    /// session to unnamed.
    /// </summary>
    public class ProjectsToolPlayTests
    {
        private readonly List<GameObject> _spawned = new();
        private string _savedCurrent;
        private string _createdName;

        private GameObject Track(GameObject go)
        {
            _spawned.Add(go);
            return go;
        }

        [SetUp]
        public void SaveState()
        {
            _savedCurrent = ProjectPaths.CurrentName;
            ProjectPaths.CurrentName = "";
            _createdName = null;
        }

        [TearDown]
        public void Cleanup()
        {
            if (!string.IsNullOrEmpty(_createdName))
            {
                string path = ProjectPaths.PathFor(_createdName);
                if (File.Exists(path)) File.Delete(path);
                string bak = ProjectFileIO.BackupPath(path);
                if (File.Exists(bak)) File.Delete(bak);
            }
            ProjectPaths.CurrentName = _savedCurrent;
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private static void SetField(object target, string field, object value) =>
            target.GetType()
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);

        private (ProjectsController projects, ImportController import, WallGraphRenderer walls)
            MakeRig()
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

            var autosave = rig.AddComponent<ProjectAutosave>();
            SetField(autosave, "autoload", false);   // the test drives every load itself
            SetField(autosave, "walls", walls);
            SetField(autosave, "import", import);

            var projects = rig.AddComponent<ProjectsController>();
            SetField(projects, "import", import);
            SetField(projects, "autosave", autosave);
            return (projects, import, walls);
        }

        private static ImportedBuilding OneWallBuilding()
        {
            var b = new ImportedBuilding();
            b.Walls.Add(new ImportedWall
            {
                Path = { new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f) },
                Thickness = 0.15f, Height = 3f,
            });
            return b;
        }

        private static SettingField Field(SettingsSchema schema, string id)
        {
            foreach (var f in schema.Fields)
                if (f.Id == id) return f;
            Assert.Fail($"schema has no field '{id}'");
            return null;
        }

        [UnityTest]
        public IEnumerator SaveNewOpenDelete_FullLifecycle()
        {
            var (projects, import, walls) = MakeRig();
            import.BuildScene(OneWallBuilding());
            yield return null;
            Assert.AreEqual(1, walls.Graph.Segments.Count, "precondition: one wall");

            var schema = projects.GetSettings();

            // Save: first save of an unnamed scene allocates "Project N" and writes it
            Field(schema, "save").Increase();
            _createdName = ProjectPaths.CurrentName;
            Assert.IsNotEmpty(_createdName, "save names the project");
            Assert.IsTrue(File.Exists(ProjectPaths.PathFor(_createdName)), "file written");

            // New: empty unnamed scene; the named file SURVIVES
            Field(schema, "new").Increase();
            yield return null;
            Assert.AreEqual(0, walls.Graph.Segments.Count, "scene cleared");
            Assert.IsFalse(ProjectPaths.HasCurrent, "current is unnamed again");
            Assert.IsTrue(File.Exists(ProjectPaths.PathFor(_createdName)),
                "New must not touch named projects");

            // Open: select the saved project in the real Select row and open it
            projects.OnActivate();
            var proj = Field(schema, "proj");
            var options = proj.ResolveOptions();
            int idx = System.Array.FindIndex(options, o => o.StartsWith(_createdName));
            Assert.GreaterOrEqual(idx, 0, "saved project listed");
            proj.SetIndex(idx);
            Field(schema, "open").Increase();
            yield return null;
            Assert.AreEqual(_createdName, ProjectPaths.CurrentName, "opened becomes current");
            Assert.AreEqual(1, walls.Graph.Segments.Count, "wall restored");

            // Delete: file gone, session demoted to unnamed, scene untouched
            projects.OnActivate();
            proj.SetIndex(System.Array.FindIndex(proj.ResolveOptions(),
                o => o.StartsWith(_createdName)));
            Field(schema, "del").Increase();
            Assert.IsFalse(File.Exists(ProjectPaths.PathFor(_createdName)), "file deleted");
            Assert.IsFalse(ProjectPaths.HasCurrent, "deleting the open project unnames it");
            Assert.AreEqual(1, walls.Graph.Segments.Count, "scene stays on delete");
        }

        [UnityTest]
        public IEnumerator Save_EmptyScene_AllocatesNoName()
        {
            var (projects, _, _) = MakeRig();
            yield return null;
            projects.GetSettings();
            Field(projects.GetSettings(), "save").Increase();
            Assert.IsFalse(ProjectPaths.HasCurrent,
                "an empty scene must not allocate a project name");
        }
    }
}
