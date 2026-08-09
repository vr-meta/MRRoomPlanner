using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Measure;
using RoomPlanner.Tools;
using RoomPlanner.Editing;

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
        [SerializeField] private SceneModel sceneModel;
        [SerializeField] private Material planMaterial;   // shared floor-top material; receives the plan texture
        [SerializeField] private float offsetSpeed = 0.6f; // m/s of plan nudging via stick

        private const string PlanFile = "plan.png";

        private readonly List<Floor> _floors = new();
        private Vector3? _cornerA;
        private Texture2D _planTex;
        private float _planScaleApplied = float.NaN;
        private float _planOffXApplied, _planOffZApplied;
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
                    () => manager.AdjustWallThickness(-0.02f), () => manager.AdjustWallThickness(0.02f))
                .Stepper("plan", "Plan scale",
                    () => $"{manager.PlanScale:0.0} m",
                    () => manager.AdjustPlanScale(-0.25f), () => manager.AdjustPlanScale(0.25f));
            return _settings;
        }

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
            if (sceneModel != null) sceneModel.Register(f.GetComponent<Selectable>());
            f.Build(a, b, level, Thickness(), Scale(), OffX(), OffZ());
        }

        private void RebuildIfPlanChanged()
        {
            // Compare the fields directly — an additive "signature" can collide when two
            // parameters change in opposite directions.
            float s = Scale(), ox = OffX(), oz = OffZ();
            if (s == _planScaleApplied && ox == _planOffXApplied && oz == _planOffZApplied) return;
            _planScaleApplied = s; _planOffXApplied = ox; _planOffZApplied = oz;
            foreach (var f in _floors)
                if (f != null) f.Build(f.CornerA, f.CornerB, f.Level, Thickness(), s, ox, oz);
        }

        private void DeleteLast()
        {
            // Delete the last VISIBLE slab, routed through the command stack (undoable). A slab
            // hidden by a DeleteCommand is already "deleted" to the user — skip it, don't
            // destroy it out from under its undo entry.
            for (int i = _floors.Count - 1; i >= 0; i--)
            {
                var f = _floors[i];
                if (f == null || !f.gameObject.activeSelf) continue;
                var sel = f.GetComponent<Selectable>();
                if (sceneModel != null && sel != null)
                {
                    sceneModel.History.Execute(new DeleteCommand(sel));
                }
                else
                {
                    _floors.RemoveAt(i);
                    Destroy(f.gameObject);
                }
                return;
            }
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
                // Free the previous decode — each activation would otherwise leak a full-size
                // texture until Horizon OS kills the app.
                if (_planTex != null) Destroy(_planTex);
                _planTex = tex;
                if (planMaterial.HasProperty("_BaseMap")) planMaterial.SetTexture("_BaseMap", tex);
                planMaterial.mainTexture = tex;
                Debug.Log($"[Floor] plan loaded {tex.width}x{tex.height}");
            }
            else Destroy(tex);
        }

        private float Thickness() => manager != null ? manager.WallThickness : 0.2f;
        private float Scale() => manager != null ? manager.PlanScale : 5f;
        private float OffX() => manager != null ? manager.PlanOffsetX : 0f;
        private float OffZ() => manager != null ? manager.PlanOffsetZ : 0f;
    }
}
