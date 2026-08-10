using System.IO;
using UnityEngine;
using RoomPlanner.Core.Project;
using RoomPlanner.Floors;
using RoomPlanner.Walls;

namespace RoomPlanner.Import
{
    /// <summary>
    /// Persistence v0 (docs/design/06): the scene autosaves to ONE project file whenever
    /// the app pauses or quits (Quest apps are killed silently — pause IS the exit), and
    /// restores it on the next launch. No buttons: the room is simply still there.
    /// </summary>
    public class ProjectAutosave : MonoBehaviour
    {
        [SerializeField] private WallGraphRenderer walls;
        [SerializeField] private ImportController import;
        [SerializeField] private BlueprintController blueprint;
        [SerializeField] private bool autoload = true;

        private const string FileName = "autosave.rp.json";

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        private void Start()
        {
            if (autoload) TryLoad(DefaultPath);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) Save(DefaultPath);
        }

        private void OnApplicationQuit() => Save(DefaultPath);

        public void Save(string path)
        {
            try
            {
                var data = ProjectStore.Capture(walls, blueprint);
                // an empty scene must not wipe yesterday's work (e.g. pause on the permission dialog)
                if (data.Walls.Count == 0 && data.Floors.Count == 0
                    && data.Stairs.Count == 0 && data.Plumbing.Count == 0
                    && data.Fixtures.Count == 0 && data.Wires.Count == 0
                    && data.Measures.Count == 0) return;
                ProjectFileIO.WriteAtomic(path, data.ToJson());
                Debug.Log($"[Project] saved {data.Walls.Count}w {data.Floors.Count}f "
                    + $"{data.Stairs.Count}st {data.Plumbing.Count}p → {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Project] save failed: {e}");
            }
        }

        public bool TryLoad(string path)
        {
            if (!File.Exists(path))
                return File.Exists(ProjectFileIO.BackupPath(path)) && LoadFile(ProjectFileIO.BackupPath(path));
            if (LoadFile(path)) return true;

            // The main file exists but would not parse (truncated by a mid-write kill).
            // Quarantine it — the next save must not destroy the evidence — and give
            // the backup left by the previous atomic save a chance (audit 12 §Б1).
            ProjectFileIO.QuarantineCorrupt(path);
            string bak = ProjectFileIO.BackupPath(path);
            if (File.Exists(bak) && LoadFile(bak))
            {
                Debug.LogWarning($"[Project] main file corrupt (quarantined) — restored from {bak}");
                return true;
            }
            return false;
        }

        private bool LoadFile(string path)
        {
            try
            {
                var data = ProjectData.FromJson(File.ReadAllText(path));
                if (data == null) return false;
                ProjectStore.Apply(data, import, blueprint);
                Debug.Log($"[Project] restored {data.Walls.Count}w {data.Floors.Count}f "
                    + $"{data.Stairs.Count}st {data.Plumbing.Count}p from {path}");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Project] load failed: {e}");
                return false;
            }
        }
    }
}
