using System.IO;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Measure;
using RoomPlanner.Tools;

namespace RoomPlanner.Floors
{
    /// <summary>
    /// Blueprint tool ("Plan"): positions the floorplan image on the floor — scale, ROTATION
    /// and offset (design/15-blueprint.md). Owns the whole plan state (first tool whose
    /// parameters live in the tool itself, not in ToolManager — design/14-modularity.md).
    /// Stick nudges the plan; scale/rotation/reload come from the settings schema. Floors
    /// rebuild live via <see cref="FloorController.RefreshPlan"/>.
    /// </summary>
    public class BlueprintController : MonoBehaviour, ITool
    {
        [SerializeField] private PointerProvider pointer;
        [SerializeField] private MeasureInput input;
        [SerializeField] private Transform reticle;
        [SerializeField] private ToolManager manager;
        [SerializeField] private FloorController floors;    // rebuilt live on plan changes
        [SerializeField] private Material planMaterial;     // shared floor-top material; receives the texture
        [SerializeField] private float offsetSpeed = 0.6f;  // m/s of plan nudging via stick
        [SerializeField] private float planScale = 5f;      // meters across the plan image width
        [SerializeField] private float planRotationDeg = 0f;
        [SerializeField] private float planOffsetX = 0f;    // world position of the plan origin
        [SerializeField] private float planOffsetZ = 0f;

        private const string PlanFile = "plan.png";

        private Texture2D _planTex;
        private string _planStatus = "no file";
        private SettingsSchema _settings;

        public string Id => "blueprint";
        public string PaletteLabel => "Plan";

        public float PlanScale => planScale;
        public float PlanRotationDeg => planRotationDeg;
        public float PlanOffsetX => planOffsetX;
        public float PlanOffsetZ => planOffsetZ;

        public SettingsSchema GetSettings()
        {
            // Plan parameters are THIS tool's state — no ToolManager store involved.
            _settings ??= new SettingsSchema()
                .Stepper("scale", "Plan scale",
                    () => $"{planScale:0.0} m",
                    () => { planScale = Mathf.Clamp(planScale - 0.25f, 0.5f, 50f); Refresh(); },
                    () => { planScale = Mathf.Clamp(planScale + 0.25f, 0.5f, 50f); Refresh(); })
                .Stepper("rot", "Rotation",
                    () => $"{planRotationDeg:0}°",
                    () => { planRotationDeg = Mathf.Repeat(planRotationDeg - 5f, 360f); Refresh(); },
                    () => { planRotationDeg = Mathf.Repeat(planRotationDeg + 5f, 360f); Refresh(); })
                .Cycle("reload", "Plan file", () => _planStatus, ReloadPlan);
            return _settings;
        }

        public void OnActivate()
        {
            if (_planTex == null) ReloadPlan();
        }

        public void OnDeactivate()
        {
            if (reticle != null) reticle.gameObject.SetActive(false);
        }

        public void Tick(bool blocked)
        {
            if (input == null) return;
            if (blocked)
            {
                if (reticle != null) reticle.gameObject.SetActive(false);
                return;
            }

            // stick = nudge the plan across the floor
            Vector2 s = input.Thumbstick();
            if (s.sqrMagnitude > 0.02f)
            {
                planOffsetX += s.x * offsetSpeed * Time.deltaTime;
                planOffsetZ += s.y * offsetSpeed * Time.deltaTime;
                Refresh();
            }

            // reticle on the working level plane, for orientation while nudging
            if (pointer != null && reticle != null)
            {
                float level = manager != null ? manager.Level : 0f;
                Ray ray = pointer.GetRay();
                if (MeasureMath.RayPlaneY(ray, level, out var cursor))
                {
                    reticle.gameObject.SetActive(true);
                    reticle.position = cursor;
                }
                else reticle.gameObject.SetActive(false);
            }
        }

        private void Refresh()
        {
            if (floors != null) floors.RefreshPlan();
        }

        private void ReloadPlan()
        {
            if (planMaterial == null) return;
            string path = Path.Combine(Application.persistentDataPath, PlanFile);
            if (!File.Exists(path))
            {
                _planStatus = "no file";
                Debug.Log($"[Plan] no plan at {path}");
                return;
            }
            var tex = new Texture2D(2, 2) { wrapMode = TextureWrapMode.Clamp };
            if (tex.LoadImage(File.ReadAllBytes(path)))
            {
                // Free the previous decode — reloading must not leak full-size textures.
                if (_planTex != null) Destroy(_planTex);
                _planTex = tex;
                if (planMaterial.HasProperty("_BaseMap")) planMaterial.SetTexture("_BaseMap", tex);
                planMaterial.mainTexture = tex;
                _planStatus = $"ok {tex.width}x{tex.height}";
                Debug.Log($"[Plan] loaded {tex.width}x{tex.height}");
            }
            else
            {
                Destroy(tex);
                _planStatus = "bad file";
            }
        }
    }
}
