using System.Collections.Generic;
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

        // File picking (design/15 BP6): images discovered on device, cycled in the inspector.
        private readonly List<string> _files = new();
        private int _fileIndex = -1;

        // Two-point calibration (design/15 BP5): two pairs «point on the projected plan →
        // where it really is» solve scale+rotation+offset in one step.
        private int _calibStep = -1;   // -1 idle; 0..3 = fromA, toA, fromB, toB
        private Vector3 _calibFromA, _calibToA, _calibFromB;
        private Vector3 _cursor;
        private bool _cursorValid;

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
                .Cycle("calib", "Calibrate", CalibrationLabel,
                    () => { if (_calibStep < 0) BeginCalibration(); else CancelCalibration(); })
                .Cycle("file", "Plan file", SelectedFileLabel, NextFile)
                .Cycle("load", "Load", () => _planStatus, ReloadPlan);
            return _settings;
        }

        // ---- file picking ----

        /// <summary>Candidate folders: the app's own dir plus the device's common image drops
        /// (a plan downloaded in the headset browser lands in Download).</summary>
        private IEnumerable<string> CandidateDirs()
        {
            yield return Application.persistentDataPath;
#if UNITY_ANDROID && !UNITY_EDITOR
            yield return "/sdcard/Download";
            yield return "/sdcard/Pictures";
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

        /// <summary>Rescan and advance to the next found image (inspector "Plan file" row).</summary>
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
            _files.AddRange(PlanFileLocator.FindImages(CandidateDirs()));
            if (_fileIndex >= _files.Count) _fileIndex = _files.Count - 1;
        }

        private static void RequestImagePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            int sdk;
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                sdk = version.GetStatic<int>("SDK_INT");
            string perm = sdk >= 33 ? "android.permission.READ_MEDIA_IMAGES"
                                    : "android.permission.READ_EXTERNAL_STORAGE";
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(perm))
                UnityEngine.Android.Permission.RequestUserPermission(perm);
#endif
        }

        // ---- two-point calibration ----

        public int CalibrationStep => _calibStep;

        public void BeginCalibration() => _calibStep = 0;

        public void CancelCalibration() => _calibStep = -1;

        private string CalibrationLabel() => _calibStep switch
        {
            0 => "A: on plan",
            1 => "A: in room",
            2 => "B: on plan",
            3 => "B: in room",
            _ => "start",
        };

        /// <summary>Feed the next calibration point (trigger while calibrating); on the 4th
        /// point the placement snaps so both pairs coincide. Public for PlayMode tests.</summary>
        public void CalibratePoint(Vector3 p)
        {
            switch (_calibStep)
            {
                case 0: _calibFromA = p; _calibStep = 1; break;
                case 1: _calibToA = p; _calibStep = 2; break;
                case 2: _calibFromB = p; _calibStep = 3; break;
                case 3:
                    var current = new BlueprintPlacement
                    {
                        Scale = planScale, RotationDeg = planRotationDeg,
                        OriginX = planOffsetX, OriginZ = planOffsetZ,
                    };
                    var solved = BlueprintMath.FromPointPairs(_calibFromA, _calibToA, _calibFromB, p, current);
                    planScale = solved.Scale;
                    planRotationDeg = Mathf.Repeat(solved.RotationDeg, 360f);
                    planOffsetX = solved.OriginX;
                    planOffsetZ = solved.OriginZ;
                    _calibStep = -1;
                    Refresh();
                    break;
            }
        }

        public void OnActivate()
        {
            RequestImagePermission();
            RefreshFileList();
            // prefer the app-folder plan.png if present, else the newest found image
            if (_fileIndex < 0 && _files.Count > 0)
            {
                int legacy = _files.FindIndex(f => Path.GetFileName(f) == PlanFile);
                _fileIndex = legacy >= 0 ? legacy : 0;
            }
            if (_planTex == null) ReloadPlan();
        }

        public void OnDeactivate()
        {
            CancelCalibration();
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

            // cursor on the working level plane (nudge orientation + calibration points)
            _cursorValid = false;
            if (pointer != null)
            {
                float level = manager != null ? manager.Level : 0f;
                Ray ray = pointer.GetRay();
                _cursorValid = MeasureMath.RayPlaneY(ray, level, out _cursor);
            }
            if (reticle != null)
            {
                reticle.gameObject.SetActive(_cursorValid);
                if (_cursorValid) reticle.position = _cursor;
            }

            // B: cancel calibration, else Esc back to Select (UX v2 P0.3)
            if (input.ClearPressed())
            {
                if (_calibStep >= 0) CancelCalibration();
                else if (manager != null) { manager.ActivateTool("select"); return; }
            }

            // trigger commits calibration points while calibrating
            if (_calibStep >= 0)
            {
                if (input.ConfirmPressed() && _cursorValid)
                {
                    CalibratePoint(_cursor);
                    input.Pulse(0.4f, 0.015f);   // snap tick
                }
                return;   // no plan nudging mid-calibration
            }

            // stick = nudge the plan across the floor
            Vector2 s = input.Thumbstick();
            if (s.sqrMagnitude > 0.02f)
            {
                planOffsetX += s.x * offsetSpeed * Time.deltaTime;
                planOffsetZ += s.y * offsetSpeed * Time.deltaTime;
                Refresh();
            }
        }

        private void Refresh()
        {
            if (floors != null) floors.RefreshPlan();
        }

        private void ReloadPlan()
        {
            if (planMaterial == null) return;
            // selected file first; legacy fallback — plan.png in the app folder
            string path = SelectedFile ?? Path.Combine(Application.persistentDataPath, PlanFile);
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
