using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Measure;
using RoomPlanner.Tools;
using RoomPlanner.Editing;

namespace RoomPlanner.Walls
{
    /// <summary>
    /// Wall tool: the trigger commits centerline points, B ends the chain. Points go into the
    /// WallGraph — snapping onto an existing wall SPLITS it, so walls meeting at a point really
    /// share a node and T-junctions are genuine (design/13-phase-b-wallgraph.md).
    /// The tool owns no walls: WallGraphRenderer owns the graph and one view per segment.
    /// Reuses the shared cursor (surface / mid-air with stick depth), node magnet and axis snap.
    /// </summary>
    public class WallController : MonoBehaviour, ITool
    {
        [SerializeField] private PointerProvider pointer;
        [SerializeField] private MeasureInput input;
        [SerializeField] private SceneRaycaster raycaster;
        [SerializeField] private Transform reticle;
        [SerializeField] private WallGraphRenderer renderer;   // owns the graph and the wall views
        [SerializeField] private LineRenderer previewLine;
        [SerializeField] private ToolManager manager;
        [SerializeField] private float snapDistance = 0.12f;
        [SerializeField] private float angleStepDeg = 15f;   // grip = snap wall direction to 15°

        // Chain state. The walls themselves live in the graph (renderer owns them) — the tool
        // only remembers where the current chain is attached (Phase B / B3).
        private WallNode _lastNode;
        private float _chainLevel;
        private float _cursorDistance = 2f;
        private SettingsSchema _settings;

        private WallGraph Graph => renderer != null ? renderer.Graph : null;

        public string Id => "wall";
        public string PaletteLabel => "Wall";
        public string IconId => "wall";

        /// <summary>Wall parameters live in ToolManager (shared store); the schema binds to it.
        /// v2 widgets (design/20 §2): sliders for the bounded lengths, segmented rows for
        /// the mode enums — every option visible, no blind cycling.</summary>
        public SettingsSchema GetSettings()
        {
            if (manager == null) return null;
            _settings ??= new SettingsSchema()
                .Slider("thk", "Thickness", 0.02f, 1f, 0.01f,
                    () => manager.WallThickness, v => manager.SetWallThickness(v),
                    (_, v) => manager.SetWallThickness(v),
                    () => $"{manager.WallThickness * 100f:0} cm", displayScale: 100f)
                .Slider("h", "Height", 0.2f, 5f, 0.05f,
                    () => manager.WallHeight, v => manager.SetWallHeight(v),
                    (_, v) => manager.SetWallHeight(v),
                    () => $"{manager.WallHeight * 100f:0} cm", displayScale: 100f)
                .Stepper("ang", "Angle step",
                    () => $"{manager.AngleStep:0}°",
                    () => manager.AdjustAngleStep(-5f), () => manager.AdjustAngleStep(5f))
                .Segmented("off", "Offset", new[] { "Outer", "Center", "Inner" },
                    () => manager.OffsetModeIndex, i => manager.OffsetModeIndex = i)
                // NOTE: no "Corner" row — graph joints always miter; the old row changed
                // nothing (audit B8). Segment.Join stays in the model/format untouched.
                .Segmented("place", "Place", new[] { "Surface", "Free", "Floor" },
                    () => manager.PlaceModeIndex, i => manager.PlaceModeIndex = i);
            return _settings;
        }

        public void OnActivate() { }

        public void OnDeactivate()
        {
            FinishChain();
            if (reticle != null) reticle.gameObject.SetActive(false);
            if (previewLine != null) previewLine.enabled = false;
        }

        public void Tick(bool blocked)
        {
            if (pointer == null || raycaster == null || input == null) return;
            if (blocked)
            {
                if (reticle != null) reticle.gameObject.SetActive(false);
                if (previewLine != null) previewLine.enabled = false;
                return;
            }

            Ray ray = pointer.GetRay();
            PlaceMode place = manager != null ? manager.Place : PlaceMode.Surface;

            Vector3 cursor;
            // TryRaycastSurface: own walls/slabs count as surfaces too (device feedback
            // 2026-08-10 — points sailed past own geometry, esp. in the scan-less mode)
            bool onSurface = raycaster.TryRaycastSurface(ray, out var hit, out _, out _) && place == PlaceMode.Surface;
            if (onSurface)
            {
                _cursorDistance = Vector3.Distance(ray.origin, hit);
                cursor = hit;
            }
            else
            {
                // Free / Floor (или нет поверхности): курсор в воздухе, глубина стиком
                _cursorDistance = Mathf.Clamp(_cursorDistance + input.DepthAdjust() * 1.5f * Time.deltaTime, 0.2f, 10f);
                cursor = ray.origin + ray.direction * _cursorDistance;

                if (place == PlaceMode.Floor)
                {
                    // Snap to the working level plane (menu Level) — reliable, no scan dependency,
                    // no vertical drift. Active chain keeps the level it started on.
                    float floorY = _lastNode != null ? _chainLevel : (manager != null ? manager.Level : 0f);
                    if (RayToPlaneY(ray, floorY, out var fp)) cursor = fp;
                    else cursor.y = floorY;
                }
            }

            bool angleOn = input.SnapHeld() || (manager != null && manager.SnapAngle);
            float step = manager != null ? manager.AngleStep : angleStepDeg;
            bool corners = manager == null || manager.SnapCorner;
            bool edges = manager == null || manager.SnapEdge;

            Vector3 target = cursor;
            if (_lastNode != null && angleOn)
                target = MeasureMath.SnapToAngleXZ(_lastNode.Position, cursor, step);

            // Remember WHICH wall an edge-snap landed on: committing there splits it into a
            // real T-junction rather than just dropping a point on top of it.
            _snapEdge = null;
            if ((corners || edges) && TrySnap(cursor, out var sp, out _snapEdge, corners, edges)) target = sp;
            else if (manager != null && manager.SnapGrid) target = MeasureMath.SnapToGridXZ(target, manager.GridSize);

            if (reticle != null) { reticle.gameObject.SetActive(true); reticle.position = target; }

            if (previewLine != null)
            {
                if (_lastNode != null)
                {
                    previewLine.enabled = true;
                    previewLine.positionCount = 2;
                    previewLine.SetPosition(0, _lastNode.Position);
                    previewLine.SetPosition(1, target);
                }
                else previewLine.enabled = false;
            }

            if (input.ConfirmPressed()) AddPoint(target);
            if (input.ClearPressed()) FinishChain();
        }

        private WallSegment _snapEdge;   // wall the cursor is snapped onto, if any

        /// <summary>
        /// Commit a point: resolve it to a graph node (splitting a wall we snapped onto, which
        /// is what makes a real T-junction), then connect it to the previous one.
        /// </summary>
        private void AddPoint(Vector3 p)
        {
            var graph = Graph;
            if (graph == null) return;

            WallNode node = _snapEdge != null
                ? graph.SplitSegmentAt(_snapEdge, p)          // tee into an existing wall
                : graph.SnapOrCreateNode(p);
            if (node == null) return;

            if (_lastNode == null)
            {
                _lastNode = node;
                _chainLevel = node.Position.y;
                renderer.Sync();                              // a split may have added a wall
                renderer.RebuildAround(node);
                return;
            }

            var seg = graph.AddSegment(_lastNode, node);
            if (seg != null)
            {
                ApplyDefaults(seg);
                renderer.Sync();
                renderer.RebuildAround(seg.A);
                renderer.RebuildAround(seg.B);
                _lastNode = node;                             // chain continues
            }
            else
            {
                // degenerate (same node / zero length) — keep drawing from where we are
                renderer.Sync();
                renderer.RebuildAround(node);
            }
        }

        /// <summary>Stamp the menu defaults onto a freshly drawn wall, side decided once.</summary>
        private void ApplyDefaults(WallSegment seg)
        {
            if (manager != null)
            {
                seg.Thickness = manager.WallThickness;
                seg.Height = manager.WallHeight;
                seg.Offset = manager.OffsetMode;
                seg.Join = manager.Join;
            }
            // where we stand is "inside" — resolved to a fixed side now, never recomputed
            Vector3 interior = Camera.main != null ? Camera.main.transform.position : seg.Midpoint;
            seg.SetSideFromInterior(interior);
        }

        private void FinishChain()
        {
            _lastNode = null;
            _snapEdge = null;
            if (previewLine != null) previewLine.enabled = false;
        }

        // Intersect a ray with the horizontal plane y = planeY.
        private static bool RayToPlaneY(Ray ray, float planeY, out Vector3 hit)
        {
            hit = default;
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return false;
            float t = (planeY - ray.origin.y) / ray.direction.y;
            if (t <= 0f) return false;
            hit = ray.origin + ray.direction * t;
            return true;
        }

        /// <summary>
        /// Magnet the cursor to the graph: node corners first, then wall faces. When it lands
        /// on a face, <paramref name="edgeSegment"/> names the wall — committing there splits
        /// it into a real T-junction instead of leaving a point sitting on top of it.
        /// Hidden (deleted) walls are skipped: invisible geometry must not magnet (rule 2.4).
        /// </summary>
        private bool TrySnap(Vector3 p, out Vector3 result, out WallSegment edgeSegment,
            bool corners, bool edges)
        {
            result = default;
            edgeSegment = null;

            var graph = Graph;
            if (graph == null) return false;

            // shared policy (corners beat edges) — the tool only supplies its candidates
            var finder = SnapFinder.WithRadius(snapDistance);

            if (corners)
            {
                var nodes = graph.Nodes;
                for (int i = 0; i < nodes.Count; i++)
                {
                    var n = nodes[i];
                    if (n == _lastNode) continue;              // don't snap onto where we start
                    if (!HasVisibleSegment(n)) continue;
                    finder.TryCorner(p, n.Position);
                }
            }

            if (edges)
            {
                var segs = graph.Segments;
                for (int i = 0; i < segs.Count; i++)
                {
                    var s = segs[i];
                    if (!renderer.IsVisible(s)) continue;
                    if (_lastNode != null && s.Has(_lastNode)) continue;   // not the wall we're extending
                    finder.TryEdge(p, s.A.Position, s.B.Position, i);
                }
            }

            // Floor outlines are snap targets too (step C5): drawing walls along the slab you
            // just laid down is the main workflow, and the outline is exactly the room boundary.
            // Index stays -1 for these — a floor edge is not a wall to split.
            AddFloorSnapCandidates(p, ref finder, corners, edges);

            if (!finder.Found) return false;
            result = finder.Point;
            // only an EDGE hit gets split into a T-junction; a corner is already a shared node
            if (finder.Kind == SnapKind.Edge && finder.Index >= 0) edgeSegment = graph.Segments[finder.Index];
            return true;
        }

        // Floors are found through the scene registry rather than a serialized reference, so the
        // wall tool needs no wiring to the floor tool. GetComponent is cached: this runs per
        // frame while the cursor moves (coding rule 4.1).
        private readonly System.Collections.Generic.List<Floors.Floor> _floorCache = new();
        private int _floorCacheStamp = -1;

        /// <summary>
        /// Offer the corners and edges of every visible floor outline as snap candidates.
        /// Public so the behaviour can be tested directly — it is the point of Phase C.
        /// </summary>
        public void AddFloorSnapCandidates(Vector3 p, ref SnapFinder finder, bool corners, bool edges)
        {
            var model = SceneModel.Instance;
            if (model == null || (!corners && !edges)) return;

            if (_floorCacheStamp != model.Items.Count) RefreshFloorCache(model);

            for (int f = 0; f < _floorCache.Count; f++)
            {
                var slab = _floorCache[f];
                if (slab == null || !slab.gameObject.activeSelf) continue;   // deleted slabs don't magnet
                var outline = slab.Outline;
                for (int i = 0; i < outline.Count; i++)
                {
                    if (corners) finder.TryCorner(p, outline[i]);
                    if (edges) finder.TryEdge(p, outline[i], outline[(i + 1) % outline.Count]);
                }
            }
        }

        private void RefreshFloorCache(SceneModel model)
        {
            _floorCacheStamp = model.Items.Count;
            _floorCache.Clear();
            for (int i = 0; i < model.Items.Count; i++)
            {
                var item = model.Items[i];
                if (item == null || !item.IsAlive || item.Kind != SelectableKind.Floor) continue;
                var slab = item.Transform.GetComponent<Floors.Floor>();
                if (slab != null) _floorCache.Add(slab);
            }
        }

        /// <summary>A node is only a snap target while at least one wall on it is visible.</summary>
        private bool HasVisibleSegment(WallNode n)
        {
            for (int i = 0; i < n.Segments.Count; i++)
                if (renderer.IsVisible(n.Segments[i])) return true;
            return false;
        }
    }
}
