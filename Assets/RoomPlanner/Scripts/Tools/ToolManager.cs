using UnityEngine;
using RoomPlanner.Measure;
using RoomPlanner.Walls;
using RoomPlanner.Floors;

namespace RoomPlanner.Tools
{
    /// <summary>Where points land: on scanned geometry, free in the air, or snapped to the floor.</summary>
    public enum PlaceMode { Surface, Free, Floor }

    /// <summary>
    /// «Мозг» инструментов: держит активный инструмент, каждый кадр решает — указатель над
    /// меню (тогда обрабатываем выбор в меню и блокируем инструмент) или в сцене (тикаем
    /// активный инструмент). Хранит глобальные параметры стен (толщина/высота/смещение).
    /// </summary>
    public class ToolManager : MonoBehaviour
    {
        [SerializeField] private PointerProvider pointer;
        [SerializeField] private MeasureInput input;
        [SerializeField] private Transform reticle;
        [SerializeField] private ToolMenu menu;
        [SerializeField] private InspectorPanel inspector;
        [SerializeField] private MeasureController measure;
        [SerializeField] private WallController wall;
        [SerializeField] private FloorController floor;
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
        [SerializeField] private float planScale = 5f;     // meters across the floorplan image width
        [SerializeField] private float planOffsetX = 0f;   // plan origin in world (positioning)
        [SerializeField] private float planOffsetZ = 0f;

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
        public float PlanScale => planScale;
        public float PlanOffsetX => planOffsetX;
        public float PlanOffsetZ => planOffsetZ;

        public void NudgePlan(float dx, float dz) { planOffsetX += dx; planOffsetZ += dz; }

        private enum Tool { Measure, Wall, Floor }
        private Tool _active = Tool.Measure;

        private ITool ActiveTool() =>
            _active == Tool.Measure ? (ITool)measure : _active == Tool.Wall ? wall : floor;

        private void Start()
        {
            Debug.Log($"[Tools] v9 started. measure={(measure != null)} wall={(wall != null)} floor={(floor != null)} inspector={(inspector != null)}");
            if (wall != null) wall.OnDeactivate();
            if (floor != null) floor.OnDeactivate();
            if (measure != null) measure.OnActivate();
            _active = Tool.Measure;
            RefreshMenu();
        }

        private bool _grabbing;
        private float _grabDist = 0.55f;

        private void Update()
        {
            if (pointer == null || input == null) return;

            Ray ray = pointer.GetRay();
            MenuButton mb = null;
            InspectorGrab grab = null;
            RaycastHit hit = default;
            if (Physics.Raycast(ray, out hit, 10f, 1 << MenuLayer, QueryTriggerInteraction.Ignore))
            {
                mb = hit.collider.GetComponentInParent<MenuButton>();
                grab = hit.collider.GetComponentInParent<InspectorGrab>();
            }

            // --- Grab-to-move the floating inspector (grip on its title bar) ---
            if (input.SnapHeld() && (grab != null || _grabbing))
            {
                if (!_grabbing) { _grabbing = true; _grabDist = Mathf.Max(0.3f, hit.distance); }
                if (inspector != null) inspector.MovePanel(ray.origin + ray.direction * _grabDist);
                if (reticle != null) reticle.gameObject.SetActive(false);
                if (menu != null) menu.Highlight(null);
                return; // don't tick tools or press buttons while dragging
            }
            _grabbing = false;

            bool overMenu = mb != null || grab != null;

            ITool act = ActiveTool();
            if (act != null) act.Tick(overMenu);

            if (overMenu)
            {
                if (menu != null) menu.Highlight(mb);
                if (reticle != null) { reticle.gameObject.SetActive(true); reticle.position = hit.point; }
                if (mb != null && input.ConfirmPressed()) { Execute(mb.Action); input.Pulse(); }
            }
            else if (menu != null) menu.Highlight(null);
        }

        private void Execute(MenuAction a)
        {
            switch (a)
            {
                case MenuAction.SelectMeasure: SetActive(Tool.Measure); break;
                case MenuAction.SelectWall: SetActive(Tool.Wall); break;
                case MenuAction.SelectFloor: SetActive(Tool.Floor); break;
                case MenuAction.PlanDown: planScale = Mathf.Clamp(planScale - 0.25f, 0.5f, 50f); break;
                case MenuAction.PlanUp: planScale = Mathf.Clamp(planScale + 0.25f, 0.5f, 50f); break;
                case MenuAction.ThicknessDown: wallThickness = Mathf.Clamp(wallThickness - 0.02f, 0.02f, 1f); break;
                case MenuAction.ThicknessUp: wallThickness = Mathf.Clamp(wallThickness + 0.02f, 0.02f, 1f); break;
                case MenuAction.HeightDown: wallHeight = Mathf.Clamp(wallHeight - 0.1f, 0.2f, 5f); break;
                case MenuAction.HeightUp: wallHeight = Mathf.Clamp(wallHeight + 0.1f, 0.2f, 5f); break;
                case MenuAction.CycleOffset: offsetMode = (offsetMode + 1) % 3; break;
                case MenuAction.CyclePlace: placeMode = (placeMode + 1) % 3; break;
                case MenuAction.ToggleSnapCorner: snapCorner = !snapCorner; break;
                case MenuAction.ToggleSnapEdge: snapEdge = !snapEdge; break;
                case MenuAction.ToggleSnapGrid: snapGrid = !snapGrid; break;
                case MenuAction.ToggleSnapAngle: snapAngle = !snapAngle; break;
                case MenuAction.AngleDown: angleStep = Mathf.Clamp(angleStep - 5f, 5f, 90f); break;
                case MenuAction.AngleUp: angleStep = Mathf.Clamp(angleStep + 5f, 5f, 90f); break;
                case MenuAction.CycleJoin: joinMode = (joinMode + 1) % 3; break;
                case MenuAction.LevelDown: level = Mathf.Round((level - 0.1f) * 100f) / 100f; break;
                case MenuAction.LevelUp: level = Mathf.Round((level + 0.1f) * 100f) / 100f; break;
                case MenuAction.ToggleScan: scanOn = !scanOn; SetScan(scanOn); break;
            }
            RefreshMenu();
        }

        // Show/hide the scanned room mesh so we can work purely from a plan.
        private static void SetScan(bool on)
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb != null && mb.GetType().Name == "EffectMesh")
                    mb.gameObject.SetActive(on);
        }

        private void SetActive(Tool t)
        {
            if (t != _active)
            {
                ITool prev = ActiveTool();
                if (prev != null) prev.OnDeactivate();
            }
            _active = t;
            ITool next = ActiveTool();
            if (next != null) next.OnActivate();
            RefreshMenu();
        }

        private void RefreshMenu()
        {
            if (menu != null)
                menu.Refresh((int)_active, snapCorner, snapEdge, snapGrid, snapAngle, scanOn);
            if (inspector != null)
            {
                inspector.RefreshValues(wallThickness, wallHeight, angleStep, level, planScale,
                    OffsetName(), JoinName(), PlaceName());
                inspector.ShowFor((int)_active);
            }
        }

        private string OffsetName() => offsetMode == 0 ? "Outer" : offsetMode == 1 ? "Center" : "Inner";
        private string PlaceName() => placeMode == 0 ? "Surface" : placeMode == 1 ? "Free" : "Floor";
        private string JoinName() => joinMode == 0 ? "Miter" : joinMode == 1 ? "Bevel" : "Round";
    }
}
