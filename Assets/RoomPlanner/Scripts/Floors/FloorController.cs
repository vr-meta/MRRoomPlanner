using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Measure;
using RoomPlanner.Tools;

namespace RoomPlanner.Floors
{
    /// <summary>
    /// Floor tool: place a rectangular slab by two corners on the current Level, with thickness
    /// (Thk). Top gets the floorplan image (plan.png in the app folder). Plan scale (menu Plan −/+)
    /// and offset (right stick while this tool is active) position the plan; slabs rebuild live.
    /// </summary>
    public class FloorController : MonoBehaviour, ITool
    {
        [SerializeField] private PointerProvider pointer;
        [SerializeField] private MeasureInput input;
        [SerializeField] private Transform reticle;
        [SerializeField] private Floor floorPrefab;
        [SerializeField] private LineRenderer previewLine;
        [SerializeField] private ToolManager manager;
        [SerializeField] private Material planMaterial;   // shared floor-top material; receives the plan texture
        [SerializeField] private float offsetSpeed = 0.6f; // m/s of plan nudging via stick

        private const string PlanFile = "plan.png";

        private readonly List<Floor> _floors = new();
        private Vector3? _cornerA;
        private float _planSig = float.NaN;

        public void OnActivate() => ReloadPlan();

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

            // move the plan with the stick when we're not mid-rectangle
            if (manager != null && !_cornerA.HasValue)
            {
                Vector2 s = input.Thumbstick();
                if (s.sqrMagnitude > 0.02f)
                    manager.NudgePlan(s.x * offsetSpeed * Time.deltaTime, s.y * offsetSpeed * Time.deltaTime);
            }

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
                else DeleteLast();                        // else remove last slab
            }
        }

        private void CreateFloor(Vector3 a, Vector3 b, float level)
        {
            var f = Instantiate(floorPrefab, transform);
            _floors.Add(f);
            f.Build(a, b, level, Thickness(), Scale(), OffX(), OffZ());
        }

        private void RebuildIfPlanChanged()
        {
            float sig = Scale() * 1000f + OffX() * 7.3f + OffZ() * 13.1f;
            if (sig == _planSig) return;
            _planSig = sig;
            foreach (var f in _floors)
                if (f != null) f.Build(f.CornerA, f.CornerB, f.Level, Thickness(), Scale(), OffX(), OffZ());
        }

        private void DeleteLast()
        {
            if (_floors.Count == 0) return;
            var f = _floors[_floors.Count - 1];
            _floors.RemoveAt(_floors.Count - 1);
            if (f != null) Destroy(f.gameObject);
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

        private void ReloadPlan()
        {
            if (planMaterial == null) return;
            string path = Path.Combine(Application.persistentDataPath, PlanFile);
            if (!File.Exists(path)) { Debug.Log($"[Floor] no plan at {path}"); return; }
            var tex = new Texture2D(2, 2) { wrapMode = TextureWrapMode.Clamp };
            if (tex.LoadImage(File.ReadAllBytes(path)))
            {
                if (planMaterial.HasProperty("_BaseMap")) planMaterial.SetTexture("_BaseMap", tex);
                planMaterial.mainTexture = tex;
                Debug.Log($"[Floor] plan loaded {tex.width}x{tex.height}");
            }
        }

        private float Thickness() => manager != null ? manager.WallThickness : 0.2f;
        private float Scale() => manager != null ? manager.PlanScale : 5f;
        private float OffX() => manager != null ? manager.PlanOffsetX : 0f;
        private float OffZ() => manager != null ? manager.PlanOffsetZ : 0f;
    }
}
