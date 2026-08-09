using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Measure;
using RoomPlanner.Tools;
using RoomPlanner.Editing;

namespace RoomPlanner.Walls
{
    /// <summary>
    /// Инструмент стен: триггером ставим осевые точки полилинии (цепочка), стена генерится
    /// процедурно с толщиной/высотой/смещением из ToolManager. B — завершить цепочку.
    /// Переиспользует курсор (поверхность/воздух, глубина стиком), магнит к узлам, привязку
    /// к оси (грип).
    /// </summary>
    public class WallController : MonoBehaviour, ITool
    {
        [SerializeField] private PointerProvider pointer;
        [SerializeField] private MeasureInput input;
        [SerializeField] private SceneRaycaster raycaster;
        [SerializeField] private Transform reticle;
        [SerializeField] private Wall wallPrefab;
        [SerializeField] private LineRenderer previewLine;
        [SerializeField] private ToolManager manager;
        [SerializeField] private SceneModel sceneModel;
        [SerializeField] private float snapDistance = 0.12f;
        [SerializeField] private float angleStepDeg = 15f;   // grip = snap wall direction to 15°

        private readonly List<Wall> _walls = new();
        private readonly List<Vector3> _pts = new();
        private Wall _current;
        private Vector3 _interior;
        private float _cursorDistance = 2f;
        private SettingsSchema _settings;

        public string Id => "wall";
        public string PaletteLabel => "Wall";

        /// <summary>Wall parameters live in ToolManager (shared store); the schema binds to it.</summary>
        public SettingsSchema GetSettings()
        {
            if (manager == null) return null;
            _settings ??= new SettingsSchema()
                .Stepper("thk", "Thickness",
                    () => $"{manager.WallThickness * 100f:0} cm",
                    () => manager.AdjustWallThickness(-0.02f), () => manager.AdjustWallThickness(0.02f))
                .Stepper("h", "Height",
                    () => $"{manager.WallHeight * 100f:0} cm",
                    () => manager.AdjustWallHeight(-0.1f), () => manager.AdjustWallHeight(0.1f))
                .Stepper("ang", "Angle step",
                    () => $"{manager.AngleStep:0}°",
                    () => manager.AdjustAngleStep(-5f), () => manager.AdjustAngleStep(5f))
                .Cycle("off", "Offset", () => manager.OffsetName(), manager.CycleOffsetMode)
                .Cycle("join", "Corner", () => manager.JoinName(), manager.CycleJoinMode)
                .Cycle("place", "Place", () => manager.PlaceName(), manager.CyclePlaceMode);
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
            bool onSurface = raycaster.TryRaycast(ray, out var hit, out _, out _) && place == PlaceMode.Surface;
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
                    // no vertical drift. Active chain keeps the first point's level.
                    float floorY = _pts.Count > 0 ? _pts[0].y : (manager != null ? manager.Level : 0f);
                    if (RayToPlaneY(ray, floorY, out var fp)) cursor = fp;
                    else cursor.y = floorY;
                }
            }

            bool angleOn = input.SnapHeld() || (manager != null && manager.SnapAngle);
            float step = manager != null ? manager.AngleStep : angleStepDeg;
            bool corners = manager == null || manager.SnapCorner;
            bool edges = manager == null || manager.SnapEdge;

            Vector3 target = cursor;
            if (_pts.Count > 0 && angleOn)
                target = MeasureMath.SnapToAngleXZ(_pts[_pts.Count - 1], cursor, step);

            if ((corners || edges) && TrySnapToNode(cursor, out var sp, corners, edges)) target = sp;
            else if (manager != null && manager.SnapGrid) target = MeasureMath.SnapToGridXZ(target, manager.GridSize);

            if (reticle != null) { reticle.gameObject.SetActive(true); reticle.position = target; }

            if (previewLine != null)
            {
                if (_pts.Count > 0)
                {
                    previewLine.enabled = true;
                    previewLine.positionCount = 2;
                    previewLine.SetPosition(0, _pts[_pts.Count - 1]);
                    previewLine.SetPosition(1, target);
                }
                else previewLine.enabled = false;
            }

            if (input.ConfirmPressed()) AddPoint(target);
            if (input.ClearPressed()) FinishChain();
        }

        private void AddPoint(Vector3 p)
        {
            if (_current == null)
            {
                _current = Instantiate(wallPrefab, transform);
                _walls.Add(_current);
                if (sceneModel != null) sceneModel.Register(_current.GetComponent<Selectable>());
                _pts.Clear();
                _interior = Camera.main != null ? Camera.main.transform.position : p; // где стоим = «внутри»
            }
            _pts.Add(p);
            Rebuild();
        }

        private void Rebuild()
        {
            if (_current != null && manager != null)
                _current.Build(_pts, manager.WallThickness, manager.WallHeight, manager.OffsetMode, manager.Join, _interior);
        }

        private void FinishChain()
        {
            if (_current != null && _pts.Count < 2)
            {
                if (sceneModel != null) sceneModel.Unregister(_current.GetComponent<Selectable>());
                _walls.Remove(_current);
                Destroy(_current.gameObject);
            }
            _current = null;
            _pts.Clear();
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

        // Snap the new point to existing walls: corners (vertices) of any wall, and edges
        // (closest point on a segment) of OTHER walls — enables T-junctions.
        private bool TrySnapToNode(Vector3 p, out Vector3 result, bool corners, bool edges)
        {
            float best = snapDistance;
            result = default;
            bool found = false;
            foreach (var w in _walls)
            {
                // Skip destroyed AND hidden (delete-command) walls — invisible geometry must
                // not act as a snap magnet.
                if (w == null || !w.gameObject.activeSelf) continue;
                var pts = w.Points;
                bool isCurrent = w == _current;

                if (corners)
                {
                    // corners (all walls; skip the point we're currently extending from)
                    for (int i = 0; i < pts.Count; i++)
                    {
                        if (isCurrent && i == pts.Count - 1) continue;
                        float d = Vector3.Distance(p, pts[i]);
                        if (d < best) { best = d; result = pts[i]; found = true; }
                    }
                }

                // edges (other walls only, so the preview doesn't stick to itself)
                if (edges && !isCurrent)
                {
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        Vector3 cp = MeasureMath.ClosestPointOnSegment(pts[i], pts[i + 1], p);
                        float d = Vector3.Distance(p, cp);
                        if (d < best) { best = d; result = cp; found = true; }
                    }
                }
            }
            return found;
        }
    }
}
