using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Core.Ifc;
using RoomPlanner.Editing;
using RoomPlanner.Floors;
using RoomPlanner.Measure;
using RoomPlanner.Tools;
using RoomPlanner.Walls;

namespace RoomPlanner.Import
{
    /// <summary>
    /// IFC import tool ("Imp", docs/design/18-ifc-import.md): picks an .ifc from the
    /// device (same folders the Blueprint tool scans), parses it with the Core importer
    /// and turns the result into ordinary editable scene objects — wall-graph segments
    /// (columns included, as short segments) and floor slabs. The whole import is ONE
    /// undo entry. A storey row filters visibility so a multi-floor building can be
    /// worked on one level at a time.
    /// </summary>
    public class ImportController : MonoBehaviour, ITool
    {
        [SerializeField] private ToolManager manager;
        [SerializeField] private MeasureInput input;
        [SerializeField] private WallGraphRenderer walls;
        [SerializeField] private FloorController floors;
        [SerializeField] private SceneModel sceneModel;

        private readonly List<string> _files = new();
        private int _fileIndex = -1;
        private string _status = "pick file";
        private SettingsSchema _settings;

        // Everything the LAST import created, per storey — drives the visibility filter.
        private readonly List<(Selectable view, int storey)> _created = new();
        private ImportedBuilding _building;
        private int _storeyFilter = -1;   // -1 = show all storeys

        public string Id => "import";
        public string PaletteLabel => "Imp";

        public SettingsSchema GetSettings()
        {
            _settings ??= new SettingsSchema()
                .Cycle("file", "IFC file", SelectedFileLabel, NextFile)
                .Cycle("load", "Load", () => _status, Load)
                .Cycle("storey", "Storey", StoreyLabel, NextStorey);
            return _settings;
        }

        public void OnActivate()
        {
            RefreshFileList();
            if (_fileIndex < 0 && _files.Count > 0) _fileIndex = 0;
        }

        public void OnDeactivate() { }

        public void Tick(bool blocked)
        {
            if (blocked || input == null) return;
            // B on this tool = Esc back to Select (UX v2 P0.3) — importing has no gesture input.
            if (input.ClearPressed() && manager != null) manager.ActivateTool("select");
        }

        // ---- file picking (same folders as the Blueprint tool) ----

        private static readonly string[] IfcExtensions = { ".ifc" };

        private IEnumerable<string> CandidateDirs()
        {
            yield return Application.persistentDataPath;
#if UNITY_ANDROID && !UNITY_EDITOR
            yield return "/sdcard/Download";
            yield return "/sdcard/Documents";
#endif
        }

        public IReadOnlyList<string> Files => _files;
        public string SelectedFile =>
            _fileIndex >= 0 && _fileIndex < _files.Count ? _files[_fileIndex] : null;

        private string SelectedFileLabel()
        {
            var f = SelectedFile;
            return f != null ? Path.GetFileName(f) : "no files";
        }

        public void NextFile()
        {
            string keep = SelectedFile;
            RefreshFileList();
            if (_files.Count == 0) { _fileIndex = -1; return; }
            int cur = keep != null ? _files.IndexOf(keep) : -1;
            _fileIndex = (cur + 1) % _files.Count;
        }

        public void RefreshFileList()
        {
            _files.Clear();
            _files.AddRange(PlanFileLocator.FindFiles(CandidateDirs(), IfcExtensions));
            if (_fileIndex >= _files.Count) _fileIndex = _files.Count - 1;
        }

        // ---- import ----

        public string Status => _status;

        /// <summary>Load the selected file (inspector "Load" row).</summary>
        public void Load()
        {
            var path = SelectedFile;
            if (path == null || !File.Exists(path)) { _status = "no file"; return; }
            try
            {
                BuildScene(IfcImporter.Import(StepFile.Parse(File.ReadAllText(path))));
            }
            catch (System.Exception e)
            {
                _status = "parse error";
                Debug.LogError($"[Import] {path}: {e}");
            }
        }

        /// <summary>
        /// Turn an imported building into scene objects. Public so PlayMode tests can feed
        /// a building without touching the filesystem.
        /// </summary>
        public void BuildScene(ImportedBuilding building)
        {
            var graph = walls != null ? walls.Graph : null;
            if (graph == null || floors == null) { _status = "rig not wired"; return; }

            _building = building;
            _created.Clear();
            _storeyFilter = -1;

            var touched = new HashSet<WallNode>();
            var segments = new List<(WallSegment seg, int storey)>();
            foreach (var iw in building.Walls)
            {
                for (int i = 0; i + 1 < iw.Path.Count; i++)
                {
                    var a = graph.SnapOrCreateNode(iw.Path[i]);
                    var b = graph.SnapOrCreateNode(iw.Path[i + 1]);
                    var seg = graph.AddSegment(a, b);
                    if (seg == null) continue;                 // degenerate stretch
                    seg.Thickness = Mathf.Max(0.01f, iw.Thickness);
                    seg.Height = Mathf.Max(0.05f, iw.Height);
                    // The IFC axis IS the centerline — never offset to a face.
                    seg.Offset = WallOffsetMode.Center;
                    touched.Add(a);
                    touched.Add(b);
                    segments.Add((seg, iw.StoreyIndex));
                }
            }
            walls.Sync();                                      // one view per new segment
            foreach (var n in touched) walls.RebuildAround(n); // joints need all neighbours
            foreach (var (seg, storey) in segments)
            {
                var view = walls.ViewOf(seg);
                if (view != null)
                    _created.Add((view.GetComponent<Selectable>(), storey));
            }
            int wallCount = segments.Count;

            int slabCount = 0;
            foreach (var slab in building.Slabs)
            {
                var f = floors.CreateImported(slab.Outline, slab.Level, slab.Thickness);
                if (f == null) continue;
                _created.Add((f.GetComponent<Selectable>(), slab.StoreyIndex));
                slabCount++;
            }

            // One undo entry for the whole import (objects are already live → Record).
            if (sceneModel != null && _created.Count > 0)
                sceneModel.History.Record(new ImportBatchCommand(CollectSelectables()));

            int skipped = building.SkippedWalls + building.SkippedColumns + building.SkippedSlabs;
            _status = $"{wallCount}w {slabCount}s" + (skipped > 0 ? $" ({skipped} skip)" : "");
            Debug.Log($"[Import] built {wallCount} wall segments, {slabCount} slabs, skipped {skipped}");
        }

        private List<ISelectable> CollectSelectables()
        {
            var list = new List<ISelectable>(_created.Count);
            foreach (var (view, _) in _created)
                if (view != null) list.Add(view);
            return list;
        }

        // ---- storey visibility filter (view state, not an edit — no undo entry) ----

        public int StoreyFilter => _storeyFilter;

        private string StoreyLabel()
        {
            if (_building == null || _building.Storeys.Count == 0) return "—";
            if (_storeyFilter < 0) return "All";
            var s = _building.Storeys[_storeyFilter];
            return s.Name;
        }

        public void NextStorey()
        {
            if (_building == null || _building.Storeys.Count == 0) return;
            _storeyFilter = _storeyFilter + 1 >= _building.Storeys.Count ? -1 : _storeyFilter + 1;
            ApplyStoreyFilter();
        }

        /// <summary>Show only the chosen storey's imports (plus anything without one).</summary>
        private void ApplyStoreyFilter()
        {
            foreach (var (view, storey) in _created)
            {
                if (view == null || !view.IsAlive) continue;
                bool show = _storeyFilter < 0 || storey < 0 || storey == _storeyFilter;
                view.gameObject.SetActive(show);
            }
        }
    }

    /// <summary>
    /// Undo unit for one import: hide everything the import created, redo shows it again —
    /// the same hide-don't-destroy contract as DeleteCommand, so history replay is safe.
    /// </summary>
    public class ImportBatchCommand : ICommand
    {
        private readonly List<ISelectable> _objects;

        public ImportBatchCommand(List<ISelectable> objects) => _objects = objects;

        public string Name => "Import";

        public void Do()
        {
            foreach (var o in _objects)
                if (o != null && o.IsAlive) o.SetHidden(false);
        }

        public void Undo()
        {
            foreach (var o in _objects)
                if (o != null && o.IsAlive) o.SetHidden(true);
        }
    }
}
