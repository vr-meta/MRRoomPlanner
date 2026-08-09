using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Measure;
using RoomPlanner.Tools;
using RoomPlanner.Editing;

namespace RoomPlanner.Floors
{
    /// <summary>
    /// Floor tool: draw a CLOSED OUTLINE on the current Level — trigger adds a corner, clicking
    /// the first corner again (or B, once there are three) closes the ring and builds the slab
    /// (docs/design/17-floor-outline.md). Real flats are rarely rectangular, and the outline is
    /// what walls will snap to.
    ///
    /// The floorplan image and its placement (scale/rotation/offset) belong to the Blueprint
    /// tool — this controller only reads them when (re)building slabs.
    /// </summary>
    public class FloorController : MonoBehaviour, ITool
    {
        [SerializeField] private PointerProvider pointer;
        [SerializeField] private MeasureInput input;
        [SerializeField] private Transform reticle;
        [SerializeField] private Floor floorPrefab;
        [SerializeField] private LineRenderer previewLine;
        [SerializeField] private ToolManager manager;
        [SerializeField] private SceneModel sceneModel;
        [SerializeField] private BlueprintController blueprint;   // plan placement source

        /// <summary>Click this close to the first point to close the outline (metres).</summary>
        [SerializeField] private float closeRadius = 0.15f;

        /// <summary>Default thickness for NEW slabs. Its own — a slab is not a wall (step C4).</summary>
        [SerializeField] private float defaultThickness = 0.2f;

        private readonly List<Floor> _floors = new();
        private readonly List<Vector3> _pts = new();      // outline being drawn
        private float _planScaleApplied = float.NaN;
        private float _planRotApplied, _planOffXApplied, _planOffZApplied;
        private SettingsSchema _settings;

        public string Id => "floor";
        public string PaletteLabel => "Floor";

        public SettingsSchema GetSettings()
        {
            if (manager == null) return null;
            _settings ??= new SettingsSchema()
                .Stepper("lvl", "Level",
                    () => $"{manager.Level * 100f:0} cm",
                    () => manager.AdjustLevel(-0.1f), () => manager.AdjustLevel(0.1f))
                .Stepper("thk", "Thickness",
                    () => $"{defaultThickness * 100f:0} cm",
                    () => AdjustDefaultThickness(-0.02f), () => AdjustDefaultThickness(0.02f));
            return _settings;
        }

        public void OnActivate() { }

        public void OnDeactivate()
        {
            CancelOutline();
            if (reticle != null) reticle.gameObject.SetActive(false);
            if (previewLine != null) previewLine.enabled = false;
        }

        public void Tick(bool blocked)
        {
            if (pointer == null || input == null) return;
            if (blocked)
            {
                if (reticle != null) reticle.gameObject.SetActive(false);
                if (previewLine != null) previewLine.enabled = false;
                return;
            }

            float level = manager != null ? manager.Level : 0f;

            RebuildIfPlanChanged();

            Ray ray = pointer.GetRay();
            if (!MeasureMath.RayPlaneY(ray, level, out var cursor))
                cursor = ray.origin + ray.direction * 2f;
            cursor.y = level;
            if (manager != null && manager.SnapGrid) cursor = MeasureMath.SnapToGridXZ(cursor, manager.GridSize);

            if (reticle != null) { reticle.gameObject.SetActive(true); reticle.position = cursor; }

            // Hovering the first point closes the ring — show that by looping the preview.
            bool canClose = _pts.Count >= 3 &&
                            Vector3.Distance(cursor, _pts[0]) <= closeRadius;
            if (canClose && reticle != null) reticle.position = _pts[0];

            DrawOutlinePreview(cursor, canClose);

            if (input.ConfirmPressed())
            {
                if (canClose) CloseOutline(level);
                else _pts.Add(cursor);
            }

            if (input.ClearPressed())
            {
                // B closes a ring that already has enough points, otherwise it cancels;
                // on an empty outline it is the Esc gesture (UX v2 P0.3).
                if (_pts.Count >= 3) CloseOutline(level);
                else if (_pts.Count > 0) CancelOutline();
                else if (manager != null) manager.ActivateTool("select");
            }
        }

        private void CloseOutline(float level)
        {
            if (_pts.Count >= 3)
            {
                // Drawn entirely inside an existing slab? Then it is a hole in that slab —
                // a stairwell, not a second floor floating inside the first. No extra mode or
                // button: it is what the gesture already means.
                var host = FindEnclosingSlab(_pts, level);
                if (host == null || !host.AddHole(_pts)) CreateFloor(_pts, level);
            }
            CancelOutline();
        }

        /// <summary>The visible slab on this level that fully contains the drawn ring, if any.</summary>
        private Floor FindEnclosingSlab(List<Vector3> ring, float level)
        {
            foreach (var f in _floors)
            {
                if (f == null || !f.gameObject.activeSelf) continue;
                if (Mathf.Abs(f.Level - level) > 1e-3f) continue;      // a different storey
                bool allInside = true;
                foreach (var p in ring)
                    if (!Polygon.Contains(f.Outline, p)) { allInside = false; break; }
                if (allInside) return f;
            }
            return null;
        }

        private void CancelOutline()
        {
            _pts.Clear();
            if (previewLine != null) previewLine.enabled = false;
        }

        /// <summary>Polyline through the placed points plus a rubber band to the cursor.</summary>
        private void DrawOutlinePreview(Vector3 cursor, bool closing)
        {
            if (previewLine == null) return;
            if (_pts.Count == 0) { previewLine.enabled = false; return; }

            previewLine.enabled = true;
            previewLine.loop = closing;
            previewLine.positionCount = _pts.Count + (closing ? 0 : 1);
            for (int i = 0; i < _pts.Count; i++) previewLine.SetPosition(i, _pts[i]);
            if (!closing) previewLine.SetPosition(_pts.Count, cursor);
        }

        private void CreateFloor(List<Vector3> outline, float level)
            => CreateSlab(outline, level, Thickness());

        /// <summary>
        /// Create a slab from an imported outline (IFC import, design/18) — the exact same
        /// path a drawn outline takes, so imported floors are ordinary editable slabs.
        /// Returns null if the outline builds nothing (degenerate/crossed).
        /// </summary>
        public Floor CreateImported(IReadOnlyList<Vector3> outline, float level, float thickness)
            => CreateSlab(new List<Vector3>(outline), level, thickness);

        private Floor CreateSlab(List<Vector3> outline, float level, float thickness)
        {
            var f = Instantiate(floorPrefab, transform);
            // A parked (inactive) template is a valid prefab source; a slab that exists
            // must be visible and pickable — hiding is DeleteCommand's job only.
            if (!f.gameObject.activeSelf) f.gameObject.SetActive(true);
            f.BuildOutline(outline, level, thickness, Scale(), Rotation(), OffX(), OffZ());

            // A crossed outline builds nothing; do not leave an invisible, unpickable slab
            // registered in the scene (coding rule 2.4 — hidden is not alive).
            if (f.Outline.Count < 3 || f.Area <= 0f)
            {
                Destroy(f.gameObject);
                return null;
            }

            _floors.Add(f);
            if (sceneModel != null) sceneModel.Register(f.GetComponent<Selectable>());
            return f;
        }

        /// <summary>Force a plan re-apply on all slabs (called by the Blueprint tool after
        /// its placement changes — floors rebuild live even while another tool is active).</summary>
        public void RefreshPlan()
        {
            _planScaleApplied = float.NaN;
            RebuildIfPlanChanged();
        }

        private void RebuildIfPlanChanged()
        {
            // Compare the fields directly — an additive "signature" can collide when two
            // parameters change in opposite directions.
            float s = Scale(), r = Rotation(), ox = OffX(), oz = OffZ();
            if (s == _planScaleApplied && r == _planRotApplied && ox == _planOffXApplied && oz == _planOffZApplied) return;
            _planScaleApplied = s; _planRotApplied = r; _planOffXApplied = ox; _planOffZApplied = oz;
            foreach (var f in _floors)
                if (f != null) f.SetPlanPlacement(s, r, ox, oz);   // keeps the outline
        }

        private void AdjustDefaultThickness(float delta) =>
            defaultThickness = Mathf.Clamp(defaultThickness + delta, 0.02f, 1f);

        private float Thickness() => defaultThickness;
        private float Scale() => blueprint != null ? blueprint.PlanScale : 5f;
        private float Rotation() => blueprint != null ? blueprint.PlanRotationDeg : 0f;
        private float OffX() => blueprint != null ? blueprint.PlanOffsetX : 0f;
        private float OffZ() => blueprint != null ? blueprint.PlanOffsetZ : 0f;
    }
}
