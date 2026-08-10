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
        [SerializeField] private Material stairMat;
        [SerializeField] private Material plumbingMat;

        private const int SelectableLayer = 6;   // picked by Select, ignored by the surface raycaster

        private readonly List<string> _files = new();
        private int _fileIndex = -1;
        private string _status = "pick file";
        private SettingsSchema _settings;

        // Everything the LAST import created, per storey — drives the visibility filter
        // and lets a repeated Load REPLACE the previous building instead of stacking two.
        private readonly List<(Selectable view, int storey)> _created = new();
        private readonly List<WallSegment> _importedSegments = new();
        private ImportedBuilding _building;
        private int _storeyFilter = -1;   // -1 = show all storeys

        public string Id => "import";
        public string PaletteLabel => "Imp";

        public SettingsSchema GetSettings()
        {
            _settings ??= new SettingsSchema()
                .Cycle("file", "IFC file", SelectedFileLabel, NextFile)
                .Cycle("load", "Load", () => _status, Load)
                .Cycle("storey", "Storey", StoreyLabel, NextStorey)
                .Cycle("new", "New project", () => "reset all", NewProject);
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

            RemovePreviousImport();

            _building = building;
            _storeyFilter = -1;

            var touched = new HashSet<WallNode>();
            var segments = new List<(WallSegment seg, int storey, int wallIndex)>();
            var wallSegments = new List<List<WallSegment>>();   // per imported wall, for openings
            for (int wi = 0; wi < building.Walls.Count; wi++)
            {
                var iw = building.Walls[wi];
                var ownSegments = new List<WallSegment>();
                wallSegments.Add(ownSegments);
                for (int i = 0; i + 1 < iw.Path.Count; i++)
                {
                    var a = graph.SnapOrCreateNode(iw.Path[i]);
                    var b = graph.SnapOrCreateNode(iw.Path[i + 1]);
                    var seg = graph.AddSegment(a, b);
                    if (seg == null) continue;                 // degenerate stretch
                    seg.Thickness = Mathf.Max(0.01f, iw.Thickness);
                    seg.Height = Mathf.Max(0.05f, iw.Height);
                    // The IFC axis IS the centerline — never offset to a face. Project
                    // files carry the user's actual settings as overrides.
                    seg.Offset = iw.OffsetOverride >= 0 ? (WallOffsetMode)iw.OffsetOverride : WallOffsetMode.Center;
                    if (iw.JoinOverride >= 0) seg.Join = (WallJoin)iw.JoinOverride;
                    if (iw.SideSignOverride != 0f) seg.SideSign = iw.SideSignOverride;
                    seg.BaseHeight = iw.BaseHeight;
                    touched.Add(a);
                    touched.Add(b);
                    segments.Add((seg, iw.StoreyIndex, wi));
                    ownSegments.Add(seg);
                    _importedSegments.Add(seg);
                }
            }
            int openingCount = AttachOpenings(building, wallSegments);
            walls.Sync();                                      // one view per new segment
            foreach (var n in touched) walls.RebuildAround(n); // joints need all neighbours
            for (int i = 0; i < segments.Count; i++)
            {
                var view = walls.ViewOf(segments[i].seg);
                if (view == null) continue;
                var sel = view.GetComponent<Selectable>();
                _created.Add((sel, segments[i].storey));
                var src = building.Walls[segments[i].wallIndex];
                if (src.HasPaint && sel != null) sel.SetPaint(src.PaintColor);
            }
            int wallCount = segments.Count;

            int slabCount = 0, holeCount = 0;
            foreach (var slab in building.Slabs)
            {
                var f = floors.CreateImported(slab.Outline, slab.Level, slab.Thickness);
                if (f == null) continue;
                foreach (var hole in slab.Holes)
                    if (f.AddHole(hole)) holeCount++;          // refusal (outside/crossed) is not fatal
                var slabSel = f.GetComponent<Selectable>();
                if (slab.HasPaint && slabSel != null) slabSel.SetPaint(slab.PaintColor);
                _created.Add((slabSel, slab.StoreyIndex));
                slabCount++;
            }

            int stairCount = 0;
            foreach (var st in building.Stairs)
            {
                var go = new GameObject("Stair (imported)") { layer = SelectableLayer };
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                if (stairMat != null) mr.sharedMaterial = stairMat;
                var stair = go.AddComponent<RoomPlanner.Stairs.Stair>();
                stair.Build(st.Base, st.YawDeg, st.Width, st.Risers, st.RiserHeight, st.TreadDepth,
                    st.Open ? RoomPlanner.Stairs.StairKind.Open : RoomPlanner.Stairs.StairKind.Solid);
                var sel = go.AddComponent<Selectable>();
                if (st.HasPaint) sel.SetPaint(st.PaintColor);
                if (sceneModel != null) sceneModel.Register(sel);
                _created.Add((sel, st.StoreyIndex));
                stairCount++;
            }

            int mepCount = 0;
            foreach (var mep in building.Plumbing)
            {
                var go = new GameObject($"Plumbing {mep.Name}");
                go.transform.SetParent(transform, false);
                go.transform.position = mep.Origin;
                var mesh = new Mesh { name = "MepMesh" };
                mesh.SetVertices(mep.Vertices);
                mesh.SetTriangles(mep.Triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                if (plumbingMat != null) mr.sharedMaterial = plumbingMat;
                go.AddComponent<MepView>();
                // Selectable only for the hide/show machinery (undo, storey filter) — no
                // collider, so it is invisible to picking and never registered for it.
                var sel = go.AddComponent<Selectable>();
                _created.Add((sel, mep.StoreyIndex));
                mepCount++;
            }

            // One undo entry for the whole import (objects are already live → Record).
            if (sceneModel != null && _created.Count > 0)
                sceneModel.History.Record(new ImportBatchCommand(CollectSelectables()));

            int skipped = building.SkippedWalls + building.SkippedColumns + building.SkippedSlabs
                + building.SkippedOpenings + building.SkippedStairs + building.SkippedMep;
            _status = $"{wallCount}w {slabCount}s {openingCount}o {holeCount}h {stairCount}st {mepCount}p"
                + (skipped > 0 ? $" ({skipped} skip)" : "");
            Debug.Log($"[Import] built {wallCount} wall segments, {slabCount} slabs, {openingCount} openings, "
                + $"{holeCount} holes, {stairCount} stairs, {mepCount} plumbing, skipped {skipped}");
        }

        /// <summary>
        /// Write imported doors/windows onto their wall-graph segments as WallOpening data.
        /// The wall mesh does not cut them yet (Phase D panelisation, docs/design/03) — the
        /// data rides on the graph so they appear the moment that lands.
        /// </summary>
        private static int AttachOpenings(ImportedBuilding building, List<List<WallSegment>> wallSegments)
        {
            int count = 0, nextId = 1;
            foreach (var op in building.Openings)
            {
                if (op.WallIndex < 0 || op.WallIndex >= wallSegments.Count) continue;
                var segs = wallSegments[op.WallIndex];
                if (segs.Count == 0) continue;

                // locate the segment containing the opening's arc position along the path
                float total = 0f;
                foreach (var s in segs) total += s.Length;
                float target = op.AlongFraction * total, run = 0f;
                WallSegment host = segs[segs.Count - 1];
                float local = 1f;
                foreach (var s in segs)
                {
                    if (target <= run + s.Length) { host = s; local = (target - run) / s.Length; break; }
                    run += s.Length;
                }

                host.Openings.Add(new WallOpening
                {
                    Id = nextId++,
                    AlongFraction = Mathf.Clamp01(local),
                    Width = op.Width,
                    Height = op.Height,
                    SillHeight = op.Sill,
                    IsDoor = op.IsDoor,
                });
                count++;
            }
            return count;
        }

        /// <summary>
        /// Tear down what the PREVIOUS import created — a repeated Load replaces the
        /// building instead of stacking a second one (headset feedback 2026-08-10).
        /// User-drawn geometry is untouched.
        /// </summary>
        private void RemovePreviousImport()
        {
            foreach (var seg in _importedSegments)
                if (seg != null) walls.RemoveSegment(seg);   // renderer unregisters + destroys views
            _importedSegments.Clear();
            foreach (var (sel, _) in _created)
            {
                if (sel == null) continue;                   // wall views died with their segments
                if (sceneModel != null) sceneModel.Unregister(sel);
                Destroy(sel.gameObject);
            }
            _created.Clear();
            // the old batch command would replay against destroyed objects — drop it
            if (sceneModel != null) sceneModel.History.PurgeWhere(c => c is ImportBatchCommand);
        }

        /// <summary>Full reset (inspector "New project" row): clear the scene AND delete the
        /// autosave, or yesterday's building would resurrect on the next launch.</summary>
        public void NewProject()
        {
            ClearScene();
            try
            {
                if (System.IO.File.Exists(ProjectAutosave.DefaultPath))
                    System.IO.File.Delete(ProjectAutosave.DefaultPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Import] could not delete autosave: {e.Message}");
            }
            _status = "new project";
        }

        /// <summary>
        /// Remove EVERYTHING buildable from the scene (project load starts clean): the wall
        /// graph with its views, every slab, stair and MEP fixture, plus the history —
        /// commands referencing destroyed objects must never replay.
        /// </summary>
        public void ClearScene()
        {
            var graph = walls != null ? walls.Graph : null;
            if (graph != null)
            {
                graph.Clear();
                walls.Sync();                       // drops every orphaned view
            }
            foreach (var f in TeleportCommand.CollectFloors())
                if (f != null) Destroy(f.gameObject);
            foreach (var s in TeleportCommand.CollectStairs())
                if (s != null) Destroy(s.gameObject);
            foreach (var m in TeleportCommand.CollectMep())
                if (m != null) Destroy(m.gameObject);
            _created.Clear();
            _importedSegments.Clear();
            _building = null;
            _storeyFilter = -1;
            if (sceneModel != null) sceneModel.History.Clear();
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
