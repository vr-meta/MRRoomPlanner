using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Measure;
using RoomPlanner.Walls;
using RoomPlanner.Floors;
using RoomPlanner.Editing;

namespace RoomPlanner.Tools
{
    /// <summary>Where points land: on scanned geometry, free in the air, or snapped to the floor.</summary>
    public enum PlaceMode { Surface, Free, Floor }

    /// <summary>
    /// «Мозг» инструментов: держит РЕЕСТР инструментов (ITool[], без enum/switch — см.
    /// design/14-modularity.md), каждый кадр решает — указатель над меню (блокируем
    /// инструмент) или в сцене (тикаем активный). Хранит ОБЩИЕ параметры (толщина/высота/
    /// уровень и т.п.) как разделяемый стор; схемы инструментов ссылаются на его методы.
    /// </summary>
    public class ToolManager : MonoBehaviour
    {
        [SerializeField] private PointerProvider pointer;
        [SerializeField] private MeasureInput input;
        [SerializeField] private Transform reticle;
        [SerializeField] private ToolMenu menu;
        [SerializeField] private RadialMenu radial;
        [SerializeField] private InspectorPanel inspector;
        [SerializeField] private SceneModel sceneModel;
        [SerializeField] private SelectController select;
        [SerializeField] private MeasureController measure;
        [SerializeField] private WallController wall;
        [SerializeField] private FloorController floor;
        [SerializeField] private BlueprintController blueprint;
        [SerializeField] private RoomPlanner.Import.ImportController importTool;
        [SerializeField] private RoomPlanner.Electrical.ElectricController electric;
        [SerializeField] private PaintController paint;
        [SerializeField] private TeleportLocomotion locomotion;
        [SerializeField] private float wallThickness = 0.2f;
        [SerializeField] private float wallHeight = 2.7f;
        [SerializeField] private int offsetMode = 0;  // 0 Outer, 1 Center, 2 Inner
        [SerializeField] private int placeMode = 0;   // 0 Surface, 1 Free, 2 Floor
        [SerializeField] private int joinMode = 0;    // 0 Miter, 1 Bevel, 2 Round
        [SerializeField] private bool snapCorner = true;
        [SerializeField] private bool snapEdge = true;
        [SerializeField] private bool snapGrid = false;
        [SerializeField] private bool snapAngle = false;
        [SerializeField] private float gridSize = 0.05f;   // 5 cm
        [SerializeField] private float angleStep = 15f;    // degrees
        [SerializeField] private float level = 0f;         // working floor level Y (storeys); Quest floor ≈ 0
        // Scan OFF is the default since 2026-08-10: the main workflow is walking an
        // imported house in the virtual environment, not scanning the real room.
        [SerializeField] private bool scanOn = false;
        [SerializeField] private Material groundMat;       // virtual ground shown when the scan is off
        [SerializeField] private Material skyMat;          // procedural sky for the scan-off mode
        [SerializeField] private UnityEngine.Rendering.Universal.ScriptableRendererFeature ssaoFeature;
        [SerializeField] private Light sunLight;           // Rendering page: sun-shadows toggle
        // NOTE: plan placement (scale/rotation/offset) lives in BlueprintController — the
        // shared store here holds only genuinely cross-tool parameters.

        private const int MenuLayer = 2;              // IgnoreRaycast

        public float WallThickness => wallThickness;
        public float WallHeight => wallHeight;
        public WallOffsetMode OffsetMode => (WallOffsetMode)offsetMode;
        public WallJoin Join => (WallJoin)joinMode;
        public PlaceMode Place => (PlaceMode)placeMode;
        public bool SnapCorner => snapCorner;
        public bool SnapEdge => snapEdge;
        public bool SnapGrid => snapGrid;
        public bool SnapAngle => snapAngle;
        public float GridSize => gridSize;
        public float AngleStep => angleStep;
        public float Level => level;
        /// <summary>Passthrough scan state — smooth locomotion is gated on it being OFF.</summary>
        public bool ScanOn => scanOn;

        // ---- shared-parameter mutators (referenced by tool schemas) ----

        public void AdjustWallThickness(float d) => wallThickness = Mathf.Clamp(wallThickness + d, 0.02f, 1f);
        public void AdjustWallHeight(float d) => wallHeight = Mathf.Clamp(wallHeight + d, 0.2f, 5f);
        public void AdjustAngleStep(float d) => angleStep = Mathf.Clamp(angleStep + d, 5f, 90f);
        public void AdjustLevel(float d) => level = Mathf.Round((level + d) * 100f) / 100f;

        // absolute setters for the v2 sliders/numpad (design/20 §2.2, §2.6)
        public void SetWallThickness(float v) => wallThickness = Mathf.Clamp(v, 0.02f, 1f);
        public void SetWallHeight(float v) => wallHeight = Mathf.Clamp(v, 0.2f, 5f);
        public void SetLevel(float v) => level = Mathf.Round(v * 100f) / 100f;

        // index accessors for the v2 segmented rows (design/20 §2.3)
        public int OffsetModeIndex { get => offsetMode; set => offsetMode = Mathf.Clamp(value, 0, 2); }
        public int PlaceModeIndex { get => placeMode; set => placeMode = Mathf.Clamp(value, 0, 2); }
        public int JoinModeIndex { get => joinMode; set => joinMode = Mathf.Clamp(value, 0, 2); }

        // ---- tool registry ----

        private ITool[] _tools;
        private int _active;
        private bool _showRenderSettings;   // gear on the snap strip → Rendering page
        private SettingsSchema _renderSchema;

        private ITool ActiveTool() =>
            _tools != null && _active >= 0 && _active < _tools.Length ? _tools[_active] : null;

        /// <summary>
        /// The radial's fixed compass layout (design/20 §1.6): slot → registry tool id,
        /// icon and layer tint. Positions are permanent — spatial memory is the payoff;
        /// null tool id = reserved slot (faint dot until the tool ships).
        /// </summary>
        private static readonly (string toolId, string icon, string label, Color tint)[] RadialSlots =
        {
            ("select", "select-cursor", "Select", new Color(0.91f, 0.93f, 0.96f)),
            ("measure", "tape-measure", "Measure", new Color(0.61f, 0.48f, 1f)),      // Measurements
            ("wall", "wall", "Wall", new Color(0.60f, 0.65f, 0.75f)),                 // Structure
            ("floor", "floor-slab", "Floor", new Color(0.60f, 0.65f, 0.75f)),
            (null, "door-window", "Openings", Color.gray),
            (null, "furniture", "Furniture", Color.gray),
            ("blueprint", "blueprint", "Blueprint", new Color(0.54f, 0.82f, 0.78f)),  // Blueprint
            ("import", "import-file", "Import", new Color(0.91f, 0.93f, 0.96f)),
            ("electric", "electric-plug", "Electric", new Color(1f, 0.79f, 0.30f)),   // Electrical
            (null, "radiator", "Heating", Color.gray),
            (null, "pipe", "Plumbing", Color.gray),
            ("paint", "paint-roller", "Paint", new Color(0.88f, 0.66f, 0.42f)),       // Interior
        };

        private void Start()
        {
            // Registration point: adding a tool = wiring its controller + one entry here
            // (the radial's fixed slot table above maps tools to compass positions).
            _tools = new ITool[] { select, measure, wall, floor, blueprint, importTool, electric, paint };

            Debug.Log($"[Tools] v12 registry: {_tools.Length} tools, radial={(radial != null)} scene={(sceneModel != null)} inspector={(inspector != null)}");
            foreach (var t in _tools)
                if (t != null) t.OnDeactivate();
            _active = 0;
            if (ActiveTool() != null) ActiveTool().OnActivate();

            if (radial != null)
            {
                var defs = new RadialSlotDef[RadialSlots.Length];
                for (int i = 0; i < RadialSlots.Length; i++)
                {
                    var s = RadialSlots[i];
                    defs[i] = new RadialSlotDef
                    {
                        IconId = s.icon, Label = s.label, Tint = s.tint,
                        ToolIndex = IndexOfTool(s.toolId),
                    };
                }
                radial.Configure(defs);
                radial.OnPicked = i => { if (i >= 0) SetActiveTool(i); };
            }
            RefreshMenu();

            // Enforce the serialized scan default (OFF since 2026-08-10). MRUK spawns the
            // scan meshes asynchronously, so a single early SetScan would miss them —
            // re-apply a few times while the scene settles.
            SetScan(scanOn);
            if (!scanOn) StartCoroutine(ReapplyScanState());
        }

        private System.Collections.IEnumerator ReapplyScanState()
        {
            foreach (float delay in new[] { 1f, 3f, 6f })
            {
                yield return new WaitForSeconds(delay);
                SetScan(scanOn);
            }
        }

        private int IndexOfTool(string id)
        {
            if (id == null || _tools == null) return -1;
            for (int i = 0; i < _tools.Length; i++)
                if (_tools[i] != null && _tools[i].Id == id) return i;
            return -1;
        }

        private bool _grabbing;
        private float _grabDist = 0.55f;
        private Transform _grabTarget;   // what the current grip-drag moves (InspectorGrab.MoveTarget)

        // radial state: capture window + the 150 ms post-close input debounce (design/20 §1.8)
        private bool _radialWasCapturing;
        private float _uiDebounceUntil;
        private int _prevTool = -1;   // R3 = previous tool (16 P2.3)

        // A button: tap (release < AHoldSeconds) = teleport, hold = tool radial
        private const float AHoldSeconds = 0.35f;
        private float _aPressedAt;
        private bool _aConsumed;

        // slider drag capture (design/20 §2.2)
        private SliderWidget _slider;
        private float _sliderX;
        private float _sliderLastX;

        // destructive hold (design/20 §2.8): fill for 0.5 s, then fire
        private MenuButton _holdBtn;
        private float _holdStart;

        // stepper auto-repeat (UX v2 P0.2): hold trigger on a −/+ row to keep stepping
        private MenuButton _repeatBtn;
        private float _repeatStart;
        private float _repeatNext;
        private MenuButton _hoverBtn;   // for hover-enter haptics

        private void Update()
        {
            if (pointer == null || input == null) return;

            // Global Undo/Redo (X/Y) — works regardless of the active tool, but not while a
            // drag is accumulating its delta (replaying history mid-drag corrupts the total).
            if (sceneModel != null && (select == null || !select.IsDragging))
            {
                // Undoing/redoing a teleport moves the model — the virtual ground follows.
                if (input.UndoPressed()) { sceneModel.History.Undo(); UpdateGroundLevel(); RefreshMenu(); }
                else if (input.RedoPressed()) { sceneModel.History.Redo(); UpdateGroundLevel(); RefreshMenu(); }
            }

            // Left-hand navigation (design/21): portal aim + smooth walk. The radial owns
            // the left hand while open — an active aim cancels instead of fighting it.
            if (locomotion != null) locomotion.Tick(radial != null && radial.IsOpen, scanOn);

            Ray ray = pointer.GetRay();

            // ---- tool radial captures ALL input while open (design/20 §1) ----
            // Invocation (device feedback 2026-08-10): HOLD A ~0.35 s (stick clicks felt
            // awkward); a short A tap stays teleport (fires on release). L3 still works.
            var cam = Camera.main != null ? Camera.main.transform : null;
            bool teleportTap = false;
            if (input.TeleportPressed()) { _aPressedAt = Time.time; _aConsumed = false; }
            if (_aPressedAt > 0f && !input.TeleportHeld())
            {
                teleportTap = !_aConsumed && Time.time - _aPressedAt < AHoldSeconds;
                _aPressedAt = 0f;
            }
            if (radial != null)
            {
                bool holdFired = _aPressedAt > 0f && !_aConsumed && !radial.IsOpen
                    && Time.time - _aPressedAt >= AHoldSeconds;
                if (input.RadialPressed() || holdFired)
                {
                    if (radial.IsOpen && !holdFired) radial.Close();
                    else if (cam != null)
                    {
                        radial.Open(cam, _active);
                        input.PulseLeft(0.3f, 0.012f);
                    }
                    if (holdFired) _aConsumed = true;
                }
                bool captures = radial.Tick(input.LeftThumbstick(), ray,
                    input.ConfirmPressed(), input.ClearPressed(), cam, input);
                if (captures)
                {
                    _radialWasCapturing = true;
                    ITool blocked = ActiveTool();
                    if (blocked != null) blocked.Tick(true);
                    if (menu != null) menu.Highlight(null);
                    // the pointer stays VISIBLE on the wheel — picking by cursor + trigger
                    // must read like everywhere else (device feedback 2026-08-10)
                    if (reticle != null)
                    {
                        reticle.gameObject.SetActive(radial.HasRayPoint);
                        if (radial.HasRayPoint) reticle.position = radial.RayPoint;
                    }
                    return;
                }
                if (_radialWasCapturing)
                {
                    // just closed: swallow trailing clicks so a confirm can't leak into the scene
                    _radialWasCapturing = false;
                    _uiDebounceUntil = Time.time + 0.15f;
                }
            }

            // R3 — jump back to the previous tool without aiming at anything
            if (_prevTool >= 0 && input.PrevToolPressed()) SetActiveTool(_prevTool);

            bool debounced = Time.time < _uiDebounceUntil;

            MenuButton mb = null;
            InspectorGrab grab = null;
            RaycastHit hit = default;
            // ANY hit on the menu layer blocks the scene tools — panel backgrounds carry
            // colliders too, so the trigger can't shoot through the gaps between buttons.
            // 1.2 m limit: menus live at arm's length; a menu collider far across the room
            // must not silently freeze the active tool (design/16 P1.2).
            bool overMenu = Physics.Raycast(ray, out hit, 1.2f, 1 << MenuLayer, QueryTriggerInteraction.Ignore);
            if (overMenu)
            {
                mb = hit.collider.GetComponentInParent<MenuButton>();
                grab = hit.collider.GetComponentInParent<InspectorGrab>();
            }

            // ---- modal popups (design/20 §3.8): one layer, B or click-outside closes ----
            var popups = inspector != null ? inspector.Popups : null;
            if (popups != null && popups.IsOpen)
            {
                popups.Tick(input.Thumbstick(), input.ClearPressed());
                if (popups.IsOpen && input.ConfirmPressed()
                    && (!overMenu || !popups.Owns(hit.collider)))
                {
                    popups.CloseAll();
                    RefreshMenu();
                }
                // while (still) modal, scene tools stay blocked even off-panel; on ANY
                // close path (B included) the closing frame stays blocked too + 150 ms
                // debounce — otherwise the same B press reaches the tool and deletes
                // the selection (review 2026-08-10, finding 1)
                overMenu = true;
                if (!popups.IsOpen) _uiDebounceUntil = Time.time + 0.15f;
            }

            // ---- slider drag (design/20 §2.2): trigger captured, grip = fine ×0.1 ----
            if (_slider != null)
            {
                if (!input.ConfirmHeld())
                {
                    _slider.EndDrag();
                    _slider = null;
                    RefreshMenu();
                }
                else
                {
                    var plane = new Plane(-_slider.transform.forward, _slider.transform.position);
                    if (plane.Raycast(ray, out float d))
                    {
                        float x = _slider.transform.InverseTransformPoint(ray.GetPoint(d)).x;
                        float gain = input.SnapHeld() ? Core.PanelLayout.FineGain : 1f;
                        _sliderX += (x - _sliderLastX) * gain;
                        _sliderLastX = x;
                        _slider.PreviewAt(_sliderX);
                    }
                    ITool draggingTool = ActiveTool();
                    if (draggingTool != null) draggingTool.Tick(true);
                    if (menu != null) menu.Highlight(null);
                    return;
                }
            }
            if (overMenu && !debounced && _slider == null && input.ConfirmPressed())
            {
                var sw = hit.collider != null ? hit.collider.GetComponentInParent<SliderWidget>() : null;
                if (sw != null)
                {
                    _slider = sw;
                    sw.BeginDrag();
                    float x = sw.transform.InverseTransformPoint(hit.point).x;
                    _sliderX = x;          // absolute jump on track press (§2.2)
                    _sliderLastX = x;
                    sw.PreviewAt(_sliderX);
                    input.Pulse(0.4f, 0.015f);
                    return;
                }
            }

            // --- Grab-to-move the floating inspector (grip PRESSED on its title bar) ---
            // Requires a fresh grip press over the bar: a grip already held for axis/angle snap
            // that merely sweeps across the panel must not yank it (and must not freeze the tool).
            bool grabStart = !_grabbing && grab != null && input.SnapPressed();
            if ((_grabbing && input.SnapHeld()) || grabStart)
            {
                if (!_grabbing)
                {
                    _grabbing = true;
                    _grabDist = Mathf.Max(0.3f, hit.distance);
                    // the marker says WHAT to move (snap strip root, panel…); legacy null = inspector
                    _grabTarget = grab != null ? grab.MoveTarget : null;
                }
                Vector3 target = ray.origin + ray.direction * _grabDist;
                if (_grabTarget != null) _grabTarget.position = target;
                else if (inspector != null) inspector.MovePanel(target);
                if (reticle != null) reticle.gameObject.SetActive(false);
                if (menu != null) menu.Highlight(null);
                return; // don't tick tools or press buttons while dragging
            }
            _grabbing = false;

            // Global teleport (A TAP, fires on release — the hold opens the radial): bring
            // the aimed slab spot under your feet — the model moves, never the camera
            // (passthrough; design/18 I6). Works from any tool.
            if (!overMenu && !debounced && sceneModel != null && teleportTap
                && (select == null || !select.IsDragging)
                && (locomotion == null || !locomotion.IsAiming))
            {
                // Slabs AND stair treads are teleport targets — aiming a step is how you
                // walk down to a lower storey (design/18 I12).
                if (sceneModel.TryPick(ray, out var picked, out var point)
                    && picked is Selectable sel
                    && (sel.Kind == SelectableKind.Floor || sel.Kind == SelectableKind.Stair))
                {
                    TeleportModelTo(point);
                }
            }

            ITool act = ActiveTool();
            if (act != null) act.Tick(overMenu || debounced);

            if (overMenu)
            {
                if (menu != null) menu.Highlight(mb);
                // short hover-enter tick — the ray needs tactile confirmation, not just visual
                if (mb != _hoverBtn)
                {
                    _hoverBtn = mb;
                    if (mb != null && mb.Interactable) input.Pulse(0.2f, 0.01f);
                }
                // popup-forced overMenu can come without a physics hit — a default hit.point
                // would park the reticle at the world origin (review finding 4)
                if (reticle != null)
                {
                    reticle.gameObject.SetActive(hit.collider != null);
                    if (hit.collider != null) reticle.position = hit.point;
                }

                if (mb != null && mb.Interactable && !debounced && input.ConfirmPressed())
                {
                    if (mb.Destructive && mb.OnClick != null)
                    {
                        // destructive: arm the hold — the plate fills with Danger and only
                        // a completed 0.5 s hold fires (design/20 §2.8)
                        _holdBtn = mb;
                        _holdStart = Time.time;
                    }
                    else
                    {
                        mb.Press();                 // visual click confirmation (dip + flash)
                        Execute(mb);
                        input.Pulse(0.6f, 0.02f);   // crisp click, not a long buzz
                        if (mb.OnClick != null && mb.Repeatable)
                        {
                            _repeatBtn = mb;
                            _repeatStart = Time.time;
                            _repeatNext = Time.time + 0.4f;   // initial delay before repeating
                        }
                    }
                }
                else if (_holdBtn != null)
                {
                    if (!input.ConfirmHeld() || mb != _holdBtn)
                    {
                        _holdBtn.SetDangerFill(0f);   // released early → disarm silently
                        _holdBtn = null;
                    }
                    else
                    {
                        float fill = (Time.time - _holdStart) / Core.UiTokens.DestructiveHoldSeconds;
                        _holdBtn.SetDangerFill(fill);
                        if (fill >= 1f)
                        {
                            var fired = _holdBtn;
                            _holdBtn = null;
                            fired.SetDangerFill(0f);
                            fired.Press();
                            Execute(fired);
                            input.Pulse(0.7f, 0.03f);
                        }
                    }
                }
                else if (_repeatBtn != null)
                {
                    if (!input.ConfirmHeld() || mb != _repeatBtn) _repeatBtn = null;
                    else if (Time.time >= _repeatNext)
                    {
                        // 8 Hz; after 1.5 s of holding each tick steps ×5
                        int steps = Time.time - _repeatStart > 1.5f ? 5 : 1;
                        for (int i = 0; i < steps; i++) _repeatBtn.OnClick?.Invoke();
                        RefreshMenu();
                        input.Pulse(0.3f, 0.008f);   // detent per step
                        _repeatNext = Time.time + 0.125f;
                    }
                }
            }
            else
            {
                if (menu != null) menu.Highlight(null);
                _hoverBtn = null;
                _repeatBtn = null;
                if (_holdBtn != null)
                {
                    _holdBtn.SetDangerFill(0f);
                    _holdBtn = null;
                }
            }
        }

        /// <summary>
        /// Bring the aimed spot under your feet — the model moves, never the camera
        /// (passthrough; design/18 I6). Shared by the A-tap teleport and the left-trigger
        /// portal (design/21): both roads lead into the same undoable TeleportCommand.
        /// </summary>
        public void TeleportModelTo(Vector3 point)
        {
            if (sceneModel == null) return;
            var head = Camera.main != null ? Camera.main.transform.position : point + Vector3.up;
            var delta = BuildingNav.TeleportDelta(point, head);
            sceneModel.History.Execute(new TeleportCommand(
                GetComponent<RoomPlanner.Walls.WallGraphRenderer>(),
                TeleportCommand.CollectFloors(), delta,
                TeleportCommand.CollectStairs(),
                TeleportCommand.CollectMep(),
                TeleportCommand.CollectFixtures(),
                TeleportCommand.CollectRoutes(),
                TeleportCommand.CollectMeasurements()));   // tape stays on the model (feedback 2026-08-10)
            UpdateGroundLevel();
            if (input != null) input.Pulse(0.4f, 0.02f);
        }

        /// <summary>Activate a tool by its stable id (e.g. B-on-empty returns to "select").</summary>
        public void ActivateTool(string id)
        {
            if (_tools == null) return;
            for (int i = 0; i < _tools.Length; i++)
                if (_tools[i] != null && _tools[i].Id == id) { SetActiveTool(i); return; }
        }

        private void Execute(MenuButton mb)
        {
            // Runtime-bound rows (inspector schema) take precedence over the global enum.
            if (mb.OnClick != null)
            {
                mb.OnClick();
                RefreshMenu();
                return;
            }

            switch (mb.Action)
            {
                case MenuAction.SelectTool: if (mb.ToolIndex >= 0) SetActiveTool(mb.ToolIndex); break;
                case MenuAction.ToggleSnapCorner: snapCorner = !snapCorner; break;
                case MenuAction.ToggleSnapEdge: snapEdge = !snapEdge; break;
                case MenuAction.ToggleSnapGrid: snapGrid = !snapGrid; break;
                case MenuAction.ToggleSnapAngle: snapAngle = !snapAngle; break;
                case MenuAction.ToggleScan: scanOn = !scanOn; SetScan(scanOn); break;
                case MenuAction.ToggleRenderSettings: _showRenderSettings = !_showRenderSettings; break;
            }
            RefreshMenu();
        }

        // Show/hide the scanned room so we can work purely from a plan: the EffectMesh AND the
        // Virtual Home primitives (prefabs spawned under MRUK anchors). Renderers+colliders are
        // toggled under anchors (not the anchor objects) so MRUK internals keep working.
        // Reflection by type name — no hard MRUK assembly dependency.
        private void SetScan(bool on)
        {
            // FindObjectsInactive.Include: a hidden EffectMesh is inactive — without it the
            // toggle could switch the scan off but never back on.
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null) continue;
                string t = mb.GetType().Name;
                if (t == "EffectMesh")
                {
                    mb.gameObject.SetActive(on);
                }
                else if (t == "MRUKAnchor")
                {
                    foreach (var r in mb.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
                    foreach (var c in mb.GetComponentsInChildren<Collider>(true)) c.enabled = on;
                }
            }
            ApplyEnvironment(on);
        }

        private GameObject _ground;

        /// <summary>
        /// Scan OFF now also leaves passthrough: the model stands on a virtual ground in a
        /// plain sky — the "came to design from a plan, not to scan a room" mode
        /// (docs/design/18-ifc-import.md I10). Scan ON restores passthrough MR.
        /// </summary>
        private void ApplyEnvironment(bool on)
        {
            foreach (var layer in FindObjectsByType<OVRPassthroughLayer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                layer.enabled = on;

            var cam = Camera.main;
            if (cam != null)
            {
                if (on)
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = Color.clear;               // passthrough shows through
                }
                else if (skyMat != null)
                {
                    RenderSettings.skybox = skyMat;                  // real environment: daylight sky
                    cam.clearFlags = CameraClearFlags.Skybox;
                }
                else
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.13f, 0.18f, 0.24f, 1f);   // calm dusk blue
                }
            }

            if (!on && _ground == null && groundMat != null)
            {
                _ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                _ground.name = "VirtualGround";
                // just under Level 0 so slab tops never z-fight with it
                _ground.transform.position = new Vector3(0f, -0.02f, 0f);
                _ground.transform.localScale = new Vector3(30f, 1f, 30f);       // 300 × 300 m
                _ground.GetComponent<Renderer>().sharedMaterial = groundMat;
            }
            if (_ground != null) _ground.SetActive(!on);
            UpdateGroundLevel();
        }

        /// <summary>
        /// Keep the virtual ground UNDER the whole model: after teleporting up a storey the
        /// lower floors sink below zero, and a ground plane parked at −2 cm would hide them —
        /// which is exactly "can't see storeys through the stairwell hole" (design/18 I12).
        /// </summary>
        private void UpdateGroundLevel()
        {
            if (_ground == null) return;
            float min = -0.02f;
            foreach (var f in TeleportCommand.CollectFloors())
                min = Mathf.Min(min, f.Level - f.Thickness);
            foreach (var s in TeleportCommand.CollectStairs())
                min = Mathf.Min(min, s.Base.y);
            var p = _ground.transform.position;
            _ground.transform.position = new Vector3(p.x, min - 0.3f, p.z);
        }

        public void SetActiveTool(int index)
        {
            if (_tools == null || index < 0 || index >= _tools.Length) return;
            _showRenderSettings = false;   // picking a tool always returns its own settings
            if (index != _active)
            {
                _prevTool = _active;   // R3 jumps back here
                ITool prev = ActiveTool();
                if (prev != null) prev.OnDeactivate();
            }
            _active = index;
            ITool next = ActiveTool();
            if (next != null) next.OnActivate();
            if (inspector != null) inspector.NoteToolChanged();
            RefreshMenu();
        }

        /// <summary>Public hook for the Select tool to refresh the inspector when selection changes.</summary>
        public void RefreshInspector() => RefreshMenu();

        private void RefreshMenu()
        {
            if (menu != null)
            {
                menu.Refresh(_active, snapCorner, snapEdge, snapGrid, snapAngle, scanOn);
                ITool a = ActiveTool();
                if (a != null)
                    foreach (var s in RadialSlots)
                        if (s.toolId == a.Id)
                        {
                            menu.SetToolChip(s.icon, s.label, s.tint);
                            break;
                        }
            }
            if (inspector != null)
            {
                ITool act = ActiveTool();
                bool hasSel = select != null && select.HasSelection;
                bool showSelection = act != null && ReferenceEquals(act, select) && hasSel;
                if (select != null) inspector.SetSelection(select.SelectionTitle, select.SelectionInfo);
                // gear page replaces the tool schema until toggled off or a tool is picked
                var schema = _showRenderSettings ? RenderSettingsSchema() : act?.GetSettings();
                inspector.ShowFor(schema, showSelection && !_showRenderSettings);
            }
        }

        // ---- Rendering page (gear on the snap strip; moved out of Paint 2026-08-11) ----

        private SettingsSchema RenderSettingsSchema()
        {
            _renderSchema ??= new SettingsSchema()
                .Header("rhead", "Rendering")
                .Toggle("vao", "Vertex AO", () => Core.MeshShading.VertexAO, _ => ToggleVertexAO())
                .Toggle("ssao", "SSAO",
                    () => ssaoFeature != null && ssaoFeature.isActive, _ => ToggleSsao())
                .Toggle("sunsh", "Sun shadows",
                    () => sunLight != null && sunLight.shadows != LightShadows.None,
                    _ => ToggleSunShadows())
                .Toggle("objsh", "Cast shadows (all)",
                    () => RoomPlanner.Import.MepView.CastShadows, _ => ToggleCastShadows());
            return _renderSchema;
        }

        /// <summary>One switch for EVERY content caster — walls, floors, stairs,
        /// furniture, fixtures (feedback 2026-08-11). Imports go two-sided (arbitrary
        /// Brep winding); our procedural meshes cast normally. UI never casts.</summary>
        private void ToggleCastShadows()
        {
            bool on = !RoomPlanner.Import.MepView.CastShadows;
            RoomPlanner.Import.MepView.CastShadows = on;   // future imports follow
            foreach (var sel in FindObjectsByType<Selectable>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                bool import = sel.GetComponent<RoomPlanner.Import.MepView>() != null;
                foreach (var r in sel.GetComponentsInChildren<MeshRenderer>(true))
                    r.shadowCastingMode = !on ? UnityEngine.Rendering.ShadowCastingMode.Off
                        : import ? UnityEngine.Rendering.ShadowCastingMode.TwoSided
                                 : UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        /// <summary>Sun patches through the windows (feedback 2026-08-11) — toggleable
        /// because a 2K shadow map is not free on Quest.</summary>
        private void ToggleSunShadows()
        {
            if (sunLight == null) return;
            sunLight.shadows = sunLight.shadows == LightShadows.None
                ? LightShadows.Soft : LightShadows.None;
        }

        /// <summary>Baked-AO switch: flips the global flag and rebuilds every mesh once.</summary>
        private void ToggleVertexAO()
        {
            Core.MeshShading.VertexAO = !Core.MeshShading.VertexAO;
            var wallsRenderer = GetComponent<WallGraphRenderer>();
            if (wallsRenderer != null && wallsRenderer.Graph != null)
                foreach (var s in wallsRenderer.Graph.Segments) wallsRenderer.RebuildSegment(s);
            foreach (var f in TeleportCommand.CollectFloors()) f.Rebuild();
            foreach (var st in TeleportCommand.CollectStairs()) st.Rebuild();
        }

        private void ToggleSsao()
        {
            if (ssaoFeature != null) ssaoFeature.SetActive(!ssaoFeature.isActive);
        }
    }
}
