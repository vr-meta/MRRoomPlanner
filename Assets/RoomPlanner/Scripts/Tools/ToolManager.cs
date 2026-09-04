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

    /// <summary>Stable action ids carried by the selection-context radial slots.</summary>
    public enum SelectionAction { Duplicate, QuickMeasure, OffsetWall, Delete }

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
        [SerializeField] private RoomPlanner.Walls.OpeningsController openings;
        [SerializeField] private RoomPlanner.Import.ProjectsController projects;
        [SerializeField] private RoomPlanner.Furniture.FurnitureController furniture;
        [SerializeField] private RoomPlanner.Plumbing.PlumbController plumb;
        [SerializeField] private TeleportLocomotion locomotion;
        [SerializeField] private GroundService ground;
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
        public void AdjustGridSize(float d) =>
            gridSize = Mathf.Round(Mathf.Clamp(gridSize + d, 0.01f, 0.50f) * 100f) / 100f;
        public void SetAngleStep(float value) => angleStep = Mathf.Clamp(Mathf.Round(value), 5f, 90f);
        public void SetGridSize(float value) =>
            gridSize = Mathf.Round(Mathf.Clamp(value, 0.01f, 0.50f) * 100f) / 100f;
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
        private ReticleVisual _reticleVisual;

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
            ("openings", "door-window", "Openings", new Color(0.60f, 0.65f, 0.75f)),   // Structure
            ("furniture", "furniture", "Furniture", new Color(0.72f, 0.63f, 0.86f)),  // Interior
            ("blueprint", "blueprint", "Blueprint", new Color(0.54f, 0.82f, 0.78f)),  // Blueprint
            ("import", "import-file", "Import", new Color(0.91f, 0.93f, 0.96f)),
            ("electric", "electric-plug", "Electric", new Color(1f, 0.79f, 0.30f)),   // Electrical
            // Heating's reserve slot went to Projects (#58) — heating will ship as a
            // tab of a future MEP tool (07-mep-layers), not as its own radial sector.
            ("projects", "folder", "Projects", new Color(0.91f, 0.93f, 0.96f)),
            ("plumb", "pipe", "Plumbing", new Color(0.30f, 0.65f, 1f)),               // Plumbing
            ("paint", "paint-roller", "Paint", new Color(0.88f, 0.66f, 0.42f)),       // Interior
        };

        /// <summary>
        /// Registry order as ids — the same order <see cref="Start"/> builds the array in.
        /// Setup code (which has no live registry) resolves palette shortcuts through this,
        /// and Start asserts the two stay in step.
        /// </summary>
        public static readonly string[] RegistryIds =
        {
            "select", "measure", "wall", "floor", "blueprint", "import",
            "electric", "paint", "openings", "projects", "furniture",
        };

        /// <summary>Index of a tool in the registry, or -1 — for Editor-side wiring.</summary>
        public static int RegistryIndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < RegistryIds.Length; i++)
                if (RegistryIds[i] == id) return i;
            return -1;
        }

        /// <summary>Stable controller order used by the rig, radial preview and inventory tests.</summary>
        public static System.Collections.Generic.IReadOnlyList<string> RegisteredToolIds => RegistryIds;

        public static int DefaultToolIndex(string id) => RegistryIndexOf(id);

        /// <summary>One source for runtime and editor-preview radial definitions.</summary>
        public static RadialSlotDef[] CreateRadialDefinitions(System.Func<string, int> indexOfTool)
        {
            var defs = new RadialSlotDef[RadialSlots.Length];
            for (int i = 0; i < RadialSlots.Length; i++)
            {
                var slot = RadialSlots[i];
                defs[i] = new RadialSlotDef
                {
                    IconId = slot.icon,
                    Label = slot.label,
                    Tint = slot.tint,
                    ToolIndex = slot.toolId == null || indexOfTool == null
                        ? -1 : indexOfTool(slot.toolId),
                };
            }
            return defs;
        }

        /// <summary>
        /// One-level selection wheel. The four verbs keep fixed cardinal positions; actions
        /// unsupported by the selected object stay visible and explain why they are disabled.
        /// </summary>
        public static RadialSlotDef[] CreateSelectionContextDefinitions(
            bool canDuplicate, bool canQuickMeasure, bool canOffset)
        {
            var defs = new RadialSlotDef[Core.RadialMath.Slots];
            for (int i = 0; i < defs.Length; i++)
                defs[i] = new RadialSlotDef
                {
                    IconId = "plus", Label = "", Tint = UiTokens.LabelDim, ToolIndex = -1,
                };

            defs[0] = ContextSlot(SelectionAction.Duplicate, "copy", "Duplicate",
                new Color(0.61f, 0.78f, 1f), !canDuplicate, "not supported for this object");
            defs[3] = ContextSlot(SelectionAction.QuickMeasure, "tape-measure", "Quick measure",
                new Color(0.61f, 0.48f, 1f), !canQuickMeasure, "measurement already has dimensions");
            defs[6] = ContextSlot(SelectionAction.OffsetWall, "wall", "Exact offset",
                new Color(0.60f, 0.65f, 0.75f), !canOffset, "walls only");
            defs[9] = ContextSlot(SelectionAction.Delete, "trash", "Delete",
                UiTokens.Danger, false, null);
            return defs;
        }

        private static RadialSlotDef ContextSlot(SelectionAction action, string icon,
            string label, Color tint, bool disabled, string disabledHint) => new()
        {
            IconId = icon,
            Label = label,
            Tint = tint,
            ToolIndex = (int)action,
            Disabled = disabled,
            DisabledHint = disabledHint,
        };

        private void Start()
        {
            // Registration point: adding a tool = wiring its controller + one entry here
            // (the radial's fixed slot table above maps tools to compass positions).
            _tools = new ITool[] { select, measure, wall, floor, blueprint, importTool, electric, paint, openings, projects, furniture, plumb };

            for (int i = 0; i < _tools.Length && i < RegistryIds.Length; i++)
                if (_tools[i] != null && _tools[i].Id != RegistryIds[i])
                    Debug.LogError($"[Tools] registry order drifted: slot {i} is " +
                                   $"{_tools[i].Id}, RegistryIds says {RegistryIds[i]}");

            Debug.Log($"[Tools] v13 registry: {_tools.Length} tools, radial={(radial != null)} scene={(sceneModel != null)} inspector={(inspector != null)}");
            foreach (var t in _tools)
                if (t != null) t.OnDeactivate();
            _active = 0;
            if (ActiveTool() != null) ActiveTool().OnActivate();
            ConfigureReticle(ActiveTool(), showGesture: true);

            if (radial != null)
                ConfigureToolRadial();
            RefreshMenu();

            // SSAO ships ACTIVE in the URP asset (so its shaders survive build stripping)
            // but the runtime default is OFF — the Rendering-page toggle opts in.
            if (ssaoFeature != null) ssaoFeature.SetActive(false);

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

        // A opens a context wheel for the selected object; otherwise it opens the tool wheel.
        // Teleport remains exclusively on the left-trigger portal (#87).
        private float _selectionOffsetMeters = 0.20f;

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
        private float _nextChromeRefreshAt;

        private void Update()
        {
            if (pointer == null || input == null) return;

            if (Time.unscaledTime >= _nextChromeRefreshAt)
            {
                _nextChromeRefreshAt = Time.unscaledTime + Core.UiTokens.LiveRefreshSeconds;
                if (select != null)
                {
                    select.RefreshSelectionInfo();
                    inspector?.SetSelection(select.SelectionTitle, select.SelectionInfo);
                }
                if (menu != null)
                    menu.Refresh(_active, snapCorner, snapEdge, snapGrid, snapAngle, scanOn,
                        _showRenderSettings,
                        sceneModel != null && sceneModel.History.CanUndo,
                        sceneModel != null && sceneModel.History.CanRedo,
                        gridSize);
            }

            // Global Undo/Redo (X/Y) — works regardless of the active tool, but not while a
            // drag is accumulating its delta (replaying history mid-drag corrupts the total).
            if (sceneModel != null && (select == null || !select.IsDragging))
            {
                // Undoing/redoing a teleport moves the model — GroundService notices the
                // history depth change and re-derives the ground on its next tick.
                if (input.UndoPressed()) { sceneModel.History.Undo(); RefreshMenu(); }
                else if (input.RedoPressed()) { sceneModel.History.Redo(); RefreshMenu(); }
            }

            // Left-hand navigation (design/21): portal aim + smooth walk. The radial owns
            // the left hand while open — an active aim cancels instead of fighting it.
            if (locomotion != null) locomotion.Tick(radial != null && radial.IsOpen, scanOn);

            // Gravity (design/26, #65): the MODEL comes to the feet in both modes — the
            // rig never moves, the feet are the fixed Y = 0 datum.
            // Never recorded — gravity is navigation, not an edit (undo would fight it).
            if (ground != null && ground.Tick(out Vector3 settle))
                ShiftModel(settle, record: false);

            Ray ray = pointer.GetRay();

            // ---- radial captures ALL input while open (design/20 §1) ----
            // A is contextual in Select: a selected object opens its action wheel; without
            // a selection it opens the tool wheel. L3/Menu always opens the tool wheel.
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (radial != null)
            {
                bool aPressed = input.TeleportPressed();
                bool toolsPressed = input.RadialPressed();
                if (toolsPressed || aPressed)
                {
                    if (radial.IsOpen) radial.Close();
                    else if (cam != null)
                    {
                        if (aPressed && HasSelectionContext)
                            OpenSelectionRadial(cam);
                        else
                            OpenToolRadial(cam);
                        input.PulseLeft(0.3f, 0.012f);
                    }
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
                    _uiDebounceUntil = Time.time + Core.UiTokens.PostCloseDebounceSeconds;
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
            bool overMenu = Physics.Raycast(ray, out hit, Core.UiTokens.MenuRayDistance,
                1 << MenuLayer, QueryTriggerInteraction.Ignore);
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
                if (!popups.IsOpen)
                    _uiDebounceUntil = Time.time + Core.UiTokens.PostCloseDebounceSeconds;
            }

            // ---- slider drag (design/20 §2.2): trigger captured, grip = fine ×0.1 ----
            if (overMenu && inspector != null && inspector.Owns(hit.collider))
                inspector.TickScroll(input.Thumbstick());

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

            // The aim-and-tap teleport that used to live on A is gone (#87): the portal arc
            // (design/21) is the only way to travel. TeleportModelTo stays as the portal's
            // shared command entry point.

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
        /// (passthrough; design/18 I6). Called by the left-trigger portal (design/21)
        /// and recorded as one undoable TeleportCommand.
        /// </summary>
        public void TeleportModelTo(Vector3 point)
        {
            if (sceneModel == null) return;
            var head = Camera.main != null ? Camera.main.transform.position : point + Vector3.up;
            // Feet, not a hard zero: after walking up a flight with the scan off the rig
            // stands above zero, and the aimed spot must arrive at THAT level (design/26).
            float footY = ground != null ? ground.FootY : 0f;
            ShiftModel(BuildingNav.TeleportDelta(point, head, footY), record: true);
            if (input != null) input.Pulse(0.4f, 0.02f);
        }

        /// <summary>
        /// Move the whole model by a delta. <paramref name="record"/> = the act belongs in
        /// history (teleport — X must always take you home); gravity settles pass false,
        /// see design/26 «Гравитация НЕ пишется в историю».
        /// </summary>
        public void ShiftModel(Vector3 delta, bool record)
        {
            if (sceneModel == null || delta == Vector3.zero) return;
            var cmd = new TeleportCommand(
                GetComponent<RoomPlanner.Walls.WallGraphRenderer>(),
                TeleportCommand.CollectFloors(), delta,
                TeleportCommand.CollectStairs(),
                TeleportCommand.CollectMep(),
                TeleportCommand.CollectFixtures(),
                TeleportCommand.CollectRoutes(),
                TeleportCommand.CollectMeasurements(),    // tape stays on the model (feedback 2026-08-10)
                TeleportCommand.CollectFurniture(),       // and so does furniture (feedback 2026-08-12)
                TeleportCommand.CollectPlumbFixtures(),   // and the plumbing layer (design/30)
                TeleportCommand.CollectPipes(),
                // half-drawn wire/pipe runs shift too (the tape-measure lesson)
                d =>
                {
                    if (electric != null) electric.ShiftDraft(d);
                    if (plumb != null) plumb.ShiftDraft(d);
                });
            if (record) sceneModel.History.Execute(cmd);
            else cmd.Do();
            if (ground != null) ground.Invalidate();
        }

        /// <summary>Activate a tool by its stable id (e.g. B-on-empty returns to "select").</summary>
        public void ActivateTool(string id)
        {
            if (_tools == null) return;
            for (int i = 0; i < _tools.Length; i++)
                if (_tools[i] != null && _tools[i].Id == id) { SetActiveTool(i); return; }
        }

        /// <summary>Finish-create handoff: activate Select and focus the object just created.</summary>
        public void SelectObject(ISelectable selectable)
        {
            int index = IndexOfTool("select");
            if (index >= 0) SetActiveTool(index);
            select?.SelectObject(selectable);
            RefreshMenu();
        }

        private bool HasSelectionContext => ReferenceEquals(ActiveTool(), select)
            && select != null && select.HasSelection;

        private Wall SelectedWall()
        {
            var current = select != null ? select.CurrentSelection : null;
            return current != null && current.Kind == SelectableKind.Wall && current.Transform != null
                ? current.Transform.GetComponent<Wall>()
                : null;
        }

        private bool CanDuplicateSelection()
        {
            var current = select != null ? select.CurrentSelection : null;
            if (current == null) return false;
            if (current.Kind == SelectableKind.Wall)
            {
                var view = SelectedWall();
                return view != null && view.Segment != null
                    && view.GetComponentInParent<WallGraphRenderer>() != null;
            }
            return current.Kind == SelectableKind.Measurement && measure != null;
        }

        private void ConfigureToolRadial()
        {
            if (radial == null) return;
            radial.Configure(CreateRadialDefinitions(IndexOfTool));
            radial.OnPicked = i => { if (i >= 0) SetActiveTool(i); };
        }

        private void OpenToolRadial(Transform cam)
        {
            ConfigureToolRadial();
            radial?.Open(cam, _active);
        }

        private void OpenSelectionRadial(Transform cam)
        {
            if (radial == null || !HasSelectionContext) return;
            var current = select.CurrentSelection;
            radial.Configure(CreateSelectionContextDefinitions(
                CanDuplicateSelection(),
                measure != null && current != null && current.Kind != SelectableKind.Measurement,
                SelectedWall() != null));
            radial.OnPicked = ExecuteSelectionAction;
            radial.Open(cam, -1);
        }

        private void ExecuteSelectionAction(int raw)
        {
            if (!System.Enum.IsDefined(typeof(SelectionAction), raw)) return;
            switch ((SelectionAction)raw)
            {
                case SelectionAction.Duplicate:
                    DuplicateSelection();
                    break;
                case SelectionAction.QuickMeasure:
                    if (measure != null && select?.CurrentSelection != null)
                        measure.QuickMeasure(select.CurrentSelection);
                    break;
                case SelectionAction.OffsetWall:
                    OpenExactWallOffset();
                    break;
                case SelectionAction.Delete:
                    select?.DeleteSelection();
                    break;
            }
            RefreshMenu();
        }

        private bool DuplicateSelection()
        {
            var current = select != null ? select.CurrentSelection : null;
            if (current == null) return false;
            if (current.Kind == SelectableKind.Wall)
            {
                float nudge = Mathf.Max(0.10f, gridSize);
                return DuplicateWall(SelectedWall(), new Vector3(nudge, 0f, nudge));
            }
            if (current.Kind == SelectableKind.Measurement && measure != null)
            {
                float nudge = Mathf.Max(0.10f, gridSize);
                var copy = measure.DuplicateMeasurement(current, new Vector3(nudge, 0f, nudge));
                if (copy == null) return false;
                select.SelectObject(copy);
                RefreshMenu();
                return true;
            }
            return false;
        }

        private bool DuplicateWall(Wall view, Vector3 delta)
        {
            if (view == null || view.Segment == null || delta.sqrMagnitude < 1e-10f) return false;
            var renderer = view.GetComponentInParent<WallGraphRenderer>();
            if (renderer == null) return false;
            var command = new WallDuplicateCommand(renderer, view, delta);
            if (sceneModel != null) sceneModel.History.Execute(command);
            else command.Do();
            if (command.Result == null) return false;
            select?.SelectObject(command.Result);
            RefreshMenu();
            return true;
        }

        private void OpenExactWallOffset()
        {
            var view = SelectedWall();
            var popup = inspector != null ? inspector.Popups : null;
            if (view == null || view.Segment == null || popup == null) return;
            var field = new SettingField
            {
                Id = "selection-wall-offset",
                Caption = "Wall offset",
                Kind = SettingKind.Numeric,
                Min = -10f,
                Max = 10f,
                DisplayScale = 100f,
                GetNumber = () => _selectionOffsetMeters,
                Value = () => $"{_selectionOffsetMeters * 100f:0} cm",
                CommitNumber = (_, after) =>
                {
                    _selectionOffsetMeters = after;
                    DuplicateWall(view, WallDuplicateCommand.OffsetDelta(view.Segment, after));
                },
            };
            popup.OpenNumpad(field, RefreshMenu);
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
                case MenuAction.SelectStripTab:
                    if (menu != null) menu.SetTab(mb.ToolIndex);
                    break;
                case MenuAction.Undo: sceneModel?.History.Undo(); break;
                case MenuAction.Redo: sceneModel?.History.Redo(); break;
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

            // The virtual ground belongs to GroundService now (design/26): it derives the
            // level from the model, so it stays under the whole building after a teleport
            // up a storey AND after an import — the old copy here only refreshed on
            // teleport/undo, which is why an imported house hung above the plane.
            if (ground != null) ground.SetVisible(!on);
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
            ConfigureReticle(next, showGesture: true);
            if (inspector != null) inspector.NoteToolChanged();
            // The strip follows the tool: drawing walls needs snapping, everything else
            // needs the shortcuts (#85).
            if (menu != null && next != null) menu.OnToolChanged(next.Id);
            RefreshMenu();
        }

        /// <summary>Public hook for the Select tool to refresh the inspector when selection changes.</summary>
        public void RefreshInspector() => RefreshMenu();

        private void RefreshMenu()
        {
            if (menu != null)
            {
                menu.Refresh(_active, snapCorner, snapEdge, snapGrid, snapAngle, scanOn,
                    _showRenderSettings,
                    sceneModel != null && sceneModel.History.CanUndo,
                    sceneModel != null && sceneModel.History.CanRedo,
                    gridSize);
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
                string title = _showRenderSettings ? "Rendering"
                    : showSelection ? select.SelectionTitle
                    : ToolTitle(act);
                inspector.ShowFor(schema, showSelection && !_showRenderSettings, title);
            }
        }

        private static string ToolTitle(ITool tool)
        {
            if (tool == null) return "Settings";
            foreach (var slot in RadialSlots)
                if (slot.toolId == tool.Id) return slot.label;
            return string.IsNullOrWhiteSpace(tool.PaletteLabel) ? "Settings" : tool.PaletteLabel;
        }

        private void ConfigureReticle(ITool tool, bool showGesture)
        {
            if (reticle == null || tool == null) return;
            _reticleVisual ??= ReticleVisual.Ensure(reticle);
            string icon = tool.IconId;
            Color tint = UiTokens.LabelLight;
            foreach (var slot in RadialSlots)
                if (slot.toolId == tool.Id)
                {
                    icon = slot.icon;
                    tint = slot.tint;
                    break;
                }
            _reticleVisual.ConfigureTool(tool.Id, icon, tint, GestureHint(tool.Id), showGesture);
        }

        private static string GestureHint(string toolId) => toolId switch
        {
            "select" => "Trigger: select / drag · A: actions · B: delete",
            "measure" => "Trigger: pin · B: clear",
            "wall" => "Trigger: point · B: finish",
            "floor" => "Trigger: corner · B: close",
            "openings" => "Trigger: place · B: delete",
            "blueprint" => "Trigger: calibrate point",
            "electric" => "Trigger: place / route · B: finish",
            "paint" => "Trigger: apply finish",
            "furniture" => "Trigger: place / drag · stick: rotate",
            _ => "Trigger: use tool · B: back",
        };

        // ---- Rendering page (gear on the snap strip; moved out of Paint 2026-08-11) ----

        private SettingsSchema RenderSettingsSchema()
        {
            // NOTE: no SSAO row. Three attempts on device (2026-08-11) all smeared —
            // even with our DepthNormals pass the AO flies with the head on Quest
            // Multiview (frame-late depth for fullscreen effects). Findings + retry
            // conditions live in the gh issue; Vertex AO is the shipped AO story.
            _renderSchema ??= new SettingsSchema()
                .Header("rhead", "Rendering")
                .Toggle("vao", "Vertex AO", () => Core.MeshShading.VertexAO, _ => ToggleVertexAO())
                .Toggle("sunsh", "Sun shadows",
                    () => sunLight != null && sunLight.shadows != LightShadows.None,
                    _ => ToggleSunShadows())
                .Toggle("objsh", "Cast shadows (all)",
                    () => RoomPlanner.Import.MepView.CastShadows, _ => ToggleCastShadows())
                .Toggle("edges", "Edges", () => Core.MeshShading.ShowEdges, _ => ToggleEdges());
            return _renderSchema;
        }

        /// <summary>Editor gallery seam; the gear page is still owned by this manager.</summary>
        public SettingsSchema GetRenderingSettings() => RenderSettingsSchema();

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

        /// <summary>Edge-lines overlay on every element that has one (walls today).
        /// Hidden by default: outlined walls stood out against floors/stairs, which
        /// draw no edges (headset feedback 2026-08-13).</summary>
        private void ToggleEdges()
        {
            Core.MeshShading.ShowEdges = !Core.MeshShading.ShowEdges;
            foreach (var w in FindObjectsByType<Walls.Wall>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
                w.RefreshEdgesVisibility();
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
