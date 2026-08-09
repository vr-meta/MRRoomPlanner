using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Measure;
using RoomPlanner.Tools;
using RoomPlanner.Editing;

namespace RoomPlanner.Floors
{
    /// <summary>
    /// Floor tool: place a rectangular slab by two corners on the current Level, with thickness
    /// (Thk). The floorplan image and its placement (scale/rotation/offset) belong to the
    /// Blueprint tool — this controller only reads them when (re)building slabs.
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

        private readonly List<Floor> _floors = new();
        private Vector3? _cornerA;
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
                    () => $"{manager.WallThickness * 100f:0} cm",
                    () => manager.AdjustWallThickness(-0.02f), () => manager.AdjustWallThickness(0.02f));
            return _settings;
        }

        public void OnActivate() { }

        public void OnDeactivate()
        {
            _cornerA = null;
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

            if (previewLine != null)
            {
                if (_cornerA.HasValue) DrawRect(_cornerA.Value, cursor, level);
                else previewLine.enabled = false;
            }

            if (input.ConfirmPressed())
            {
                if (!_cornerA.HasValue) _cornerA = cursor;
                else { CreateFloor(_cornerA.Value, cursor, level); _cornerA = null; }
            }

            if (input.ClearPressed())
            {
                if (_cornerA.HasValue) _cornerA = null;   // cancel current rectangle
                // No blind LIFO delete (UX v2 P0.3): deleting a slab is the Select tool's job;
                // B on empty is the Esc gesture.
                else if (manager != null) manager.ActivateTool("select");
            }
        }

        private void CreateFloor(Vector3 a, Vector3 b, float level)
        {
            var f = Instantiate(floorPrefab, transform);
            _floors.Add(f);
            if (sceneModel != null) sceneModel.Register(f.GetComponent<Selectable>());
            f.Build(a, b, level, Thickness(), Scale(), Rotation(), OffX(), OffZ());
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
                if (f != null) f.Build(f.CornerA, f.CornerB, f.Level, Thickness(), s, r, ox, oz);
        }

        private void DrawRect(Vector3 a, Vector3 b, float level)
        {
            previewLine.enabled = true;
            previewLine.loop = true;
            previewLine.positionCount = 4;
            previewLine.SetPosition(0, new Vector3(a.x, level, a.z));
            previewLine.SetPosition(1, new Vector3(b.x, level, a.z));
            previewLine.SetPosition(2, new Vector3(b.x, level, b.z));
            previewLine.SetPosition(3, new Vector3(a.x, level, b.z));
        }

        private float Thickness() => manager != null ? manager.WallThickness : 0.2f;
        private float Scale() => blueprint != null ? blueprint.PlanScale : 5f;
        private float Rotation() => blueprint != null ? blueprint.PlanRotationDeg : 0f;
        private float OffX() => blueprint != null ? blueprint.PlanOffsetX : 0f;
        private float OffZ() => blueprint != null ? blueprint.PlanOffsetZ : 0f;
    }
}
