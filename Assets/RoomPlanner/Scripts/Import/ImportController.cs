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
        [SerializeField] private PointerProvider pointer;
        [SerializeField] private SceneRaycaster raycaster;
        [SerializeField] private LineRenderer markerRing;   // placement target visual (#60)
        [SerializeField] private WallGraphRenderer walls;
        [SerializeField] private FloorController floors;
        [SerializeField] private SceneModel sceneModel;
        [SerializeField] private Material stairMat;
        [SerializeField] private Material plumbingMat;
        [SerializeField] private Material furnitureMat;
        [SerializeField] private Material proxyMat;
        [SerializeField] private Material railingMat;
        [SerializeField] private Material mepGlassMat;   // transparency ≥ GlassThreshold
        [SerializeField] private Material screenMat;     // TVs/monitors: dark glossy glass

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
        public string IconId => "import-file";

        public SettingsSchema GetSettings()
        {
            // v2 (design/20 §2): Select lists for files/storeys, Load is a button.
            // Import is import-only (#58): "New project" moved to the Projects tool.
            _settings ??= new SettingsSchema()
                .Select("file", "IFC file", FileOptions, () => Mathf.Max(0, _fileIndex), SelectFile)
                .Action("load", "Load IFC", "folder", Load)
                .Select("storey", "Storey", StoreyOptions,
                    () => _storeyFilter + 1, i => SetStoreyFilter(i - 1))
                .Readout("status", "Status", () => _status);
            return _settings;
        }

        private string[] FileOptions()
        {
            RefreshFileList();
            if (_files.Count == 0) return new[] { "no files" };
            var names = new string[_files.Count];
            for (int i = 0; i < _files.Count; i++) names[i] = Path.GetFileName(_files[i]);
            return names;
        }

        private void SelectFile(int index)
        {
            if (_files.Count == 0) { _fileIndex = -1; return; }
            _fileIndex = Mathf.Clamp(index, 0, _files.Count - 1);
        }

        public void OnActivate()
        {
            RefreshFileList();
            if (_fileIndex < 0 && _files.Count > 0) _fileIndex = 0;
            if (_hasMarker) ShowMarkerRing();
        }

        public void OnDeactivate()
        {
            if (markerRing != null) markerRing.enabled = false;
        }

        public void Tick(bool blocked)
        {
            if (blocked || input == null) return;
            if (input.ClearPressed())
            {
                // B clears the placement target first; on a bare tool it is Esc (UX v2 P0.3).
                if (_hasMarker) { ClearMarker(); return; }
                if (manager != null) manager.ActivateTool("select");
                return;
            }
            // Trigger marks where the imported building will stand (#60).
            if (pointer != null && input.ConfirmPressed()
                && TryHitSurface(pointer.GetRay(), out Vector3 point))
                SetMarker(point);
        }

        // ---- placement target (#60): trigger marks the spot, Load lands the building there ----

        private bool _hasMarker;
        private Vector3 _marker;
        private readonly RaycastHit[] _ownHits = new RaycastHit[8];

        public bool HasMarker => _hasMarker;

        /// <summary>Set the placement target. Public seam for tests and future tools.</summary>
        public void SetMarker(Vector3 point)
        {
            _hasMarker = true;
            _marker = point;
            ShowMarkerRing();
            _status = "target set";
        }

        public void ClearMarker()
        {
            _hasMarker = false;
            if (markerRing != null) markerRing.enabled = false;
            _status = "target cleared";
        }

        private void ShowMarkerRing()
        {
            if (markerRing == null) return;
            const int points = 24;
            const float radius = 0.35f;
            markerRing.positionCount = points;
            for (int i = 0; i < points; i++)
            {
                float a = i * Mathf.PI * 2f / points;
                markerRing.SetPosition(i, _marker
                    + new Vector3(Mathf.Cos(a) * radius, 0.01f, Mathf.Sin(a) * radius));
            }
            markerRing.enabled = true;
        }

        /// <summary>Scanned room + own selectables (layer 6, which the shared raycaster
        /// deliberately skips) — nearest of the two wins; same duality as Electric.</summary>
        private bool TryHitSurface(Ray ray, out Vector3 point)
        {
            point = default;
            Vector3 sp = default;
            bool haveScan = raycaster != null
                && raycaster.TryRaycast(ray, out sp, out _, out _);
            float scanDist = haveScan ? Vector3.Distance(ray.origin, sp) : float.MaxValue;
            if (haveScan) point = sp;

            float ownDist = float.MaxValue;
            int n = Physics.RaycastNonAlloc(ray, _ownHits, 10f, 1 << SelectableLayer,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = _ownHits[i];
                if (h.collider == null || h.distance >= ownDist) continue;
                if (!h.collider.gameObject.activeInHierarchy) continue;
                if (h.collider.GetComponentInParent<Selectable>() == null) continue;
                ownDist = h.distance;
                if (ownDist < scanDist) point = h.point;
            }
            return haveScan || ownDist < float.MaxValue;
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
                LoadBuilding(IfcImporter.Import(StepFile.Parse(File.ReadAllText(path))));
            }
            catch (System.Exception e)
            {
                _status = "parse error";
                Debug.LogError($"[Import] {path}: {e}");
            }
        }

        /// <summary>IFC entry: honors the placement marker (#60). Project loads call
        /// BuildScene directly — a saved scene must NEVER be re-offset.</summary>
        public void LoadBuilding(ImportedBuilding building)
        {
            if (_hasMarker) ImportPlacement.MoveTo(building, _marker);
            BuildScene(building);
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
            // Loading a building REPLACES the scene's electrical layer too — orphaned
            // outlets and wires hanging inside the new building's walls are worse than
            // re-wiring (headset feedback 2026-08-12). A project load restores its own
            // electrics right after this.
            ClearElectrical();

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
            // T-heal (design/24): imported partitions merely TOUCH long walls mid-span;
            // splitting there closes room rings and scopes per-side paint to one room.
            // Tail halves born from the splits count as imported too — a repeated Load
            // must replace them together with everything else.
            int beforeHeal = graph.Segments.Count;
            walls.HealTJunctions();
            for (int i = beforeHeal; i < graph.Segments.Count; i++)
                _importedSegments.Add(graph.Segments[i]);
            for (int i = 0; i < segments.Count; i++)
            {
                var view = walls.ViewOf(segments[i].seg);
                if (view == null) continue;
                var sel = view.GetComponent<Selectable>();
                _created.Add((sel, segments[i].storey));
                var src = building.Walls[segments[i].wallIndex];
                if (!ApplyWallFinish(sel, src.Finish, src.FinishB) && src.HasPaint && sel != null)
                    sel.SetPaint(src.PaintColor);
            }
            int wallCount = segments.Count;

            int slabCount = 0, holeCount = 0;
            var importedFloors = new List<RoomPlanner.Floors.Floor>();
            foreach (var slab in building.Slabs)
            {
                var f = floors.CreateImported(slab.Outline, slab.Level, slab.Thickness);
                if (f == null) continue;
                foreach (var hole in slab.Holes)
                    if (f.AddHole(hole)) holeCount++;          // refusal (outside/crossed) is not fatal
                var slabSel = f.GetComponent<Selectable>();
                if (!ApplyFinish(slabSel, slab.Finish) && slab.HasPaint && slabSel != null)
                    slabSel.SetPaint(slab.PaintColor);
                _created.Add((slabSel, slab.StoreyIndex));
                importedFloors.Add(f);
                slabCount++;
            }

            int stairCount = 0;
            var importedStairs = new List<RoomPlanner.Stairs.Stair>();
            foreach (var st in building.Stairs)
            {
                var go = new GameObject("Stair (imported)") { layer = SelectableLayer };
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                if (stairMat != null) mr.sharedMaterial = stairMat;
                var stair = go.AddComponent<RoomPlanner.Stairs.Stair>();
                stair.Build(st.Base, st.YawDeg, st.Width, st.Risers, st.RiserHeight, st.TreadDepth,
                    st.Kind);
                go.AddComponent<RoomPlanner.Stairs.StairParameters>();   // per-instance rows (F2)
                var sel = go.AddComponent<Selectable>();
                if (!ApplyFinish(sel, st.Finish) && st.HasPaint) sel.SetPaint(st.PaintColor);
                if (sceneModel != null) sceneModel.Register(sel);
                _created.Add((sel, st.StoreyIndex));
                importedStairs.Add(stair);
                stairCount++;
            }

            // Stairs must never leave you head-butting the slab above (audit 05 §Б1):
            // the file's own stairwell holes are checked against each flight and widened
            // (or created) wherever the 2.0 m headroom rule is violated.
            int headroomFixes = 0;
            foreach (var st in importedStairs)
                foreach (var f in importedFloors)
                    if (st.CutHeadroomIn(f)) headroomFixes++;
            if (headroomFixes > 0)
                Debug.Log($"[Import] stair headroom: widened/created {headroomFixes} slab opening(s)");

            int mepCount = 0;
            foreach (var mep in building.Plumbing)
            {
                var go = new GameObject($"{mep.Category} {mep.Name}");
                go.transform.SetParent(transform, false);
                go.transform.position = mep.Origin;
                var mesh = new Mesh { name = "MepMesh" };
                mesh.SetVertices(mep.Vertices);
                mesh.SetTriangles(mep.Triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var mat = MaterialFor(mep);
                if (mat != null) mr.sharedMaterial = mat;
                var view = go.AddComponent<MepView>();
                view.Category = mep.Category;
                view.Transparency = mep.Transparency;
                view.StoreyIndex = mep.StoreyIndex;   // survives capture — storey filter after load (B6)
                view.ApplyShadowMode();   // interior objects cast sun shadows (toggleable)
                // Selectable only for the hide/show machinery (undo, storey filter) — no
                // collider, so it is invisible to picking and never registered for it.
                var sel = go.AddComponent<Selectable>();
                // The file's own colour rides the paint machinery: one visual writer,
                // undo-able, round-trips through the project format.
                if (!ApplyFinish(sel, mep.Finish) && mep.HasColor)
                    sel.SetPaint(new Color(mep.Color.r, mep.Color.g, mep.Color.b,
                        1f - Mathf.Clamp01(mep.Transparency)));
                _created.Add((sel, mep.StoreyIndex));
                mepCount++;
            }

            int outletCount = SpawnImportedOutlets(building);

            // One undo entry for the whole import (objects are already live → Record).
            if (sceneModel != null && _created.Count > 0)
                sceneModel.History.Record(new ImportBatchCommand(CollectSelectables()));

            int skipped = building.SkippedWalls + building.SkippedColumns + building.SkippedSlabs
                + building.SkippedOpenings + building.SkippedStairs + building.SkippedMep;
            _status = $"{wallCount}w {slabCount}s {openingCount}o {holeCount}h {stairCount}st {mepCount}p"
                + (outletCount > 0 ? $" {outletCount}el" : "")
                + (headroomFixes > 0 ? $" {headroomFixes}hr" : "")
                + (skipped > 0 ? $" ({skipped} skip)" : "");
            Debug.Log($"[Import] built {wallCount} wall segments, {slabCount} slabs, {openingCount} openings, "
                + $"{holeCount} holes, {stairCount} stairs, {mepCount} plumbing, {outletCount} outlets, skipped {skipped}");
        }

        /// <summary>
        /// Native electrical from the IFC (#79): outlets arrive as tiny proxy plates and
        /// leave as REAL fixtures — editable, wireable, counted by the panel BOM. Reuses
        /// the persistence factory (RestoreFixture), so an imported outlet is
        /// indistinguishable from a hand-placed one, round-trip included.
        /// </summary>
        private int SpawnImportedOutlets(ImportedBuilding building)
        {
            if (building.Outlets.Count == 0) return 0;
            var electric = FindFirstObjectByType<Electrical.ElectricController>();
            if (electric == null)
            {
                Debug.LogWarning("[Import] file carries outlets but the rig has no ElectricController");
                return 0;
            }
            int made = 0;
            foreach (var o in building.Outlets)
            {
                Vector3 n = OutletFacing(o);
                var fx = electric.RestoreFixture(new Core.Project.ProjectFixture
                {
                    Kind = (int)Electrical.FixtureKind.Outlet,
                    Posts = 1,
                    Keys = 1,
                    Position = o.Position + n * 0.002f,   // keep the back plate off the wall
                    Rotation = Quaternion.LookRotation(n,
                        Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up),
                    BaseLevel = o.StoreyIndex >= 0 && o.StoreyIndex < building.Storeys.Count
                        ? building.Storeys[o.StoreyIndex].Elevation : 0f,
                });
                if (fx == null) continue;
                var sel = fx.GetComponent<Selectable>();
                if (sel != null) _created.Add((sel, o.StoreyIndex));
                made++;
            }
            return made;
        }

        /// <summary>Mounting direction: the plate's thin axis, signed AWAY from the
        /// nearest wall centreline so the face looks into the room.</summary>
        private Vector3 OutletFacing(ImportedOutlet o)
        {
            Vector3 n = o.Normal;
            if (Mathf.Abs(n.y) > 0.9f) return Vector3.up;   // lying flat — floor/ceiling box
            var graph = walls != null ? walls.Graph : null;
            if (graph == null) return n;

            float bestD = float.MaxValue;
            Vector3 bestP = o.Position;
            foreach (var s in graph.Segments)
            {
                Vector3 p = ClosestOnSegmentXZ(s.A.Position, s.B.Position, o.Position);
                float dx = p.x - o.Position.x, dz = p.z - o.Position.z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; bestP = p; }
            }
            Vector3 off = o.Position - bestP;
            off.y = 0f;
            if (off.sqrMagnitude > 1e-8f && Vector3.Dot(off, n) < 0f) n = -n;
            return n;
        }

        private static Vector3 ClosestOnSegmentXZ(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            ab.y = 0f;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-8f) return a;
            Vector3 ap = p - a;
            ap.y = 0f;
            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / len2);
            return a + ab * t;
        }

        private FinishLibrary _finishLibrary;

        /// <summary>
        /// Apply a full v2 surface finish (audit B2); false = nothing recorded, the
        /// caller falls back to the flat v1 colour. A texture whose files are not on
        /// this device degrades VISUALLY to the tint but keeps its id — the next save
        /// still carries the texture instead of flattening it to white.
        /// </summary>
        private bool ApplyFinish(Selectable sel, Core.SurfaceFinish finish)
        {
            if (sel == null || finish.IsNone) return false;
            sel.SetFinish(finish, ResolveFinishTexture(finish), ResolveFinishNormal(finish));
            return true;
        }

        /// <summary>Wall finishes are a PAIR since format v3 (issue #34). Equal sides
        /// (old files, whole-wall paint) go through the whole-object path; mixed
        /// sides restore each face, a None side staying the material's own look.</summary>
        private bool ApplyWallFinish(Selectable sel, Core.SurfaceFinish inner, Core.SurfaceFinish outer)
        {
            if (sel == null) return false;
            if (inner.IsNone && outer.IsNone) return false;
            if (inner.Equals(outer)) return ApplyFinish(sel, inner);
            sel.SetFinishSide(Core.WallSide.Inner, inner,
                ResolveFinishTexture(inner), ResolveFinishNormal(inner));
            sel.SetFinishSide(Core.WallSide.Outer, outer,
                ResolveFinishTexture(outer), ResolveFinishNormal(outer));
            return true;
        }

        private Texture2D ResolveFinishTexture(Core.SurfaceFinish finish)
        {
            if (finish.Kind != Core.FinishKind.Texture) return null;
            if (_finishLibrary == null) _finishLibrary = FindFirstObjectByType<FinishLibrary>();
            Texture2D tex = null;
            if (_finishLibrary != null) _finishLibrary.TryGet(finish.TextureId, out tex, out _);
            return tex;
        }

        /// <summary>Optional relief of the finish (laminate — design/22); null otherwise.</summary>
        private Texture2D ResolveFinishNormal(Core.SurfaceFinish finish)
        {
            if (finish.Kind != Core.FinishKind.Texture) return null;
            if (_finishLibrary == null) _finishLibrary = FindFirstObjectByType<FinishLibrary>();
            return _finishLibrary != null ? _finishLibrary.NormalOf(finish.TextureId) : null;
        }

        /// <summary>Material by category; strongly transparent surfaces (glass shower
        /// walls and the like) get the see-through material whatever the category.</summary>
        private Material MaterialFor(ImportedMep mep)
        {
            const float glassThreshold = 0.3f;
            if (mep.Transparency >= glassThreshold && mepGlassMat != null) return mepGlassMat;
            if (screenMat != null && LooksLikeScreen(mep.Name)) return screenMat;
            return mep.Category switch
            {
                MepCategory.Furniture => furnitureMat != null ? furnitureMat : plumbingMat,
                MepCategory.Proxy => proxyMat != null ? proxyMat : plumbingMat,
                MepCategory.Railing => railingMat != null ? railingMat : plumbingMat,
                _ => plumbingMat,
            };
        }

        /// <summary>TVs and monitors read as wood/plastic under the category material
        /// (headset feedback 2026-08-11) — the IFC name is the only hint we have.</summary>
        private static bool LooksLikeScreen(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.Contains("tv") || n.Contains("televi") || n.Contains("телевизор")
                || n.Contains("monitor") || n.Contains("screen") || n.Contains("display");
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
                    // Explicit kind when the source carries one (v2 files, hand-placed
                    // garage doors); the IFC path still speaks bool IsDoor.
                    Kind = op.Kind >= 0 ? (OpeningKind)op.Kind
                        : (op.IsDoor ? OpeningKind.Door : OpeningKind.Window),
                    SwingDir = op.SwingDir,
                    HingeDir = op.HingeDir,
                    OpenFraction = Mathf.Clamp01(op.OpenFraction),
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

        /// <summary>Full reset (the Projects tool's "New project" action, #58): clear the
        /// scene AND delete the unnamed autosave, or yesterday's building would resurrect
        /// on the next launch. Named project files are not touched.</summary>
        public void NewProject()
        {
            ClearScene();
            // The BACKUP must die too: TryLoad falls back to .bak when the main file
            // is missing, so deleting only the autosave resurrected yesterday's
            // building on the next launch (headset feedback 2026-08-12 —
            // "New project ничего не делает").
            foreach (var path in new[]
            {
                ProjectAutosave.DefaultPath,
                Core.Project.ProjectFileIO.BackupPath(ProjectAutosave.DefaultPath),
            })
            {
                try
                {
                    if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[Import] could not delete {path}: {e.Message}");
                }
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
            // Tape measurements are scene content too ("New project leaves the tape
            // hanging" — headset feedback 2026-08-11).
            foreach (var m in TeleportCommand.CollectMeasurements())
                if (m != null) Destroy(m.gameObject);
            ClearElectrical();
            ClearFurniture();
            _created.Clear();
            _importedSegments.Clear();
            _building = null;
            _storeyFilter = -1;
            if (sceneModel != null) sceneModel.History.Clear();
        }

        /// <summary>Destroy the electrical layer — "New project" and a building load
        /// must not leave orphaned outlets/wires (headset feedback 2026-08-10/12).
        /// Only REGISTERED objects: the parked fixture template and the tool's ghost
        /// preview are not in the model and must survive.</summary>
        private void ClearElectrical()
        {
            if (sceneModel == null) return;
            foreach (var item in new List<ISelectable>(sceneModel.Items))
                if (item is Selectable s && s.IsAlive
                    && (s.Kind == SelectableKind.Fixture || s.Kind == SelectableKind.Wire))
                    Destroy(s.gameObject);
        }

        /// <summary>Destroy placed furniture (design/27, v4 projects) — same rule as the
        /// electrical layer: a loaded project starts clean, or yesterday's sofa haunts
        /// today's room (the bug measurements already taught us, audit B3).</summary>
        private void ClearFurniture()
        {
            if (sceneModel == null) return;
            foreach (var item in new List<ISelectable>(sceneModel.Items))
                if (item is Selectable s && s.IsAlive && s.Kind == SelectableKind.Furniture)
                    Destroy(s.gameObject);
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

        /// <summary>Select-list options for the v2 storey filter: "All" + every storey name.</summary>
        private string[] StoreyOptions()
        {
            if (_building == null || _building.Storeys.Count == 0) return new[] { "All" };
            var names = new string[_building.Storeys.Count + 1];
            names[0] = "All";
            for (int i = 0; i < _building.Storeys.Count; i++)
                names[i + 1] = _building.Storeys[i].Name;
            return names;
        }

        private void SetStoreyFilter(int storey)
        {
            int max = _building != null ? _building.Storeys.Count - 1 : -1;
            _storeyFilter = Mathf.Clamp(storey, -1, max);
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
