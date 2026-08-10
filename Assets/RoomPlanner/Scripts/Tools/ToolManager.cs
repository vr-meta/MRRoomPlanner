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
        [SerializeField] private InspectorPanel inspector;
        [SerializeField] private SceneModel sceneModel;
        [SerializeField] private SelectController select;
        [SerializeField] private MeasureController measure;
        [SerializeField] private WallController wall;
        [SerializeField] private FloorController floor;
        [SerializeField] private BlueprintController blueprint;
        [SerializeField] private RoomPlanner.Import.ImportController importTool;
        [SerializeField] private RoomPlanner.Electrical.ElectricController electric;
        [SerializeField] private RoomPlanner.Paint.PaintController paint;
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
        [SerializeField] private bool scanOn = true;       // show/hide the scanned room mesh (EffectMesh)
        [SerializeField] private Material groundMat;       // virtual ground shown when the scan is off
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

        // ---- shared-parameter mutators (referenced by tool schemas) ----

        public void AdjustWallThickness(float d) => wallThickness = Mathf.Clamp(wallThickness + d, 0.02f, 1f);
        public void AdjustWallHeight(float d) => wallHeight = Mathf.Clamp(wallHeight + d, 0.2f, 5f);
        public void AdjustAngleStep(float d) => angleStep = Mathf.Clamp(angleStep + d, 5f, 90f);
        public void AdjustLevel(float d) => level = Mathf.Round((level + d) * 100f) / 100f;
        public void CycleOffsetMode() => offsetMode = (offsetMode + 1) % 3;
        public void CyclePlaceMode() => placeMode = (placeMode + 1) % 3;
        public void CycleJoinMode() => joinMode = (joinMode + 1) % 3;

        public string OffsetName() => offsetMode == 0 ? "Outer" : offsetMode == 1 ? "Center" : "Inner";
        public string PlaceName() => placeMode == 0 ? "Surface" : placeMode == 1 ? "Free" : "Floor";
        public string JoinName() => joinMode == 0 ? "Miter" : joinMode == 1 ? "Bevel" : "Round";

        // ---- tool registry ----

        private ITool[] _tools;
        private int _active;

        private ITool ActiveTool() =>
            _tools != null && _active >= 0 && _active < _tools.Length ? _tools[_active] : null;

        private void Start()
        {
            // Registration point: adding a tool = wiring its controller + one entry here
            // (palette buttons are generated from the same order by SetupPalette).
            _tools = new ITool[] { select, measure, wall, floor, blueprint, importTool, electric, paint };

            Debug.Log($"[Tools] v11 registry: {_tools.Length} tools, scene={(sceneModel != null)} inspector={(inspector != null)}");
            foreach (var t in _tools)
                if (t != null) t.OnDeactivate();
            _active = 0;
            if (ActiveTool() != null) ActiveTool().OnActivate();
            RefreshMenu();
        }

        private bool _grabbing;
        private float _grabDist = 0.55f;

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
                if (input.UndoPressed()) { sceneModel.History.Undo(); RefreshMenu(); }
                else if (input.RedoPressed()) { sceneModel.History.Redo(); RefreshMenu(); }
            }

            Ray ray = pointer.GetRay();
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

            // --- Grab-to-move the floating inspector (grip PRESSED on its title bar) ---
            // Requires a fresh grip press over the bar: a grip already held for axis/angle snap
            // that merely sweeps across the panel must not yank it (and must not freeze the tool).
            bool grabStart = !_grabbing && grab != null && input.SnapPressed();
            if ((_grabbing && input.SnapHeld()) || grabStart)
            {
                if (!_grabbing) { _grabbing = true; _grabDist = Mathf.Max(0.3f, hit.distance); }
                if (inspector != null) inspector.MovePanel(ray.origin + ray.direction * _grabDist);
                if (reticle != null) reticle.gameObject.SetActive(false);
                if (menu != null) menu.Highlight(null);
                return; // don't tick tools or press buttons while dragging
            }
            _grabbing = false;

            // Global teleport (A): bring the aimed slab spot under your feet — the model
            // moves, never the camera (passthrough; design/18 I6). Works from any tool.
            if (!overMenu && sceneModel != null && input.TeleportPressed()
                && (select == null || !select.IsDragging))
            {
                if (sceneModel.TryPick(ray, out var picked, out var point)
                    && picked is Selectable sel && sel.Kind == SelectableKind.Floor)
                {
                    var head = Camera.main != null ? Camera.main.transform.position : ray.origin;
                    var delta = BuildingNav.TeleportDelta(point, head);
                    sceneModel.History.Execute(new TeleportCommand(
                        GetComponent<RoomPlanner.Walls.WallGraphRenderer>(),
                        TeleportCommand.CollectFloors(), delta,
                        TeleportCommand.CollectStairs()));
                    input.Pulse(0.4f, 0.02f);
                }
            }

            ITool act = ActiveTool();
            if (act != null) act.Tick(overMenu);

            if (overMenu)
            {
                if (menu != null) menu.Highlight(mb);
                // short hover-enter tick — the ray needs tactile confirmation, not just visual
                if (mb != _hoverBtn)
                {
                    _hoverBtn = mb;
                    if (mb != null && mb.Interactable) input.Pulse(0.2f, 0.01f);
                }
                if (reticle != null) { reticle.gameObject.SetActive(true); reticle.position = hit.point; }

                if (mb != null && mb.Interactable && input.ConfirmPressed())
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
            }
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
                else if (RenderSettings.skybox != null)
                {
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
        }

        public void SetActiveTool(int index)
        {
            if (_tools == null || index < 0 || index >= _tools.Length) return;
            if (index != _active)
            {
                ITool prev = ActiveTool();
                if (prev != null) prev.OnDeactivate();
            }
            _active = index;
            ITool next = ActiveTool();
            if (next != null) next.OnActivate();
            RefreshMenu();
        }

        /// <summary>Public hook for the Select tool to refresh the inspector when selection changes.</summary>
        public void RefreshInspector() => RefreshMenu();

        private void RefreshMenu()
        {
            if (menu != null)
                menu.Refresh(_active, snapCorner, snapEdge, snapGrid, snapAngle, scanOn);
            if (inspector != null)
            {
                ITool act = ActiveTool();
                bool hasSel = select != null && select.HasSelection;
                bool showSelection = act != null && ReferenceEquals(act, select) && hasSel;
                if (select != null) inspector.SetSelection(select.SelectionTitle, select.SelectionInfo);
                inspector.ShowFor(act?.GetSettings(), showSelection);
            }
        }
    }
}
