using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Electrical;
using RoomPlanner.Measure;
using RoomPlanner.Tools;
using RoomPlanner.Editing;

namespace RoomPlanner.Plumbing
{
    /// <summary>
    /// Plumb tool (docs/design/28-plumbing.md): rough-in drainage in four sub-modes —
    /// Riser / Pipe / Outlet / Drain.
    ///
    /// A riser is one click on the floor: a vertical D110 pipe auto-extends to the
    /// ceiling. Pipes are routed by hand on walls, FLOORS and ceilings (drainage lies
    /// on the floor — the one surface electrical refuses); Ortho inserts the LOW elbow,
    /// with the horizontal travel at the lower of the two clicks. Ends snap to fixture
    /// terminals, to a riser AXIS at any height (the tee), and to free ends of same-
    /// diameter pipes (continuation, the #81 pattern). Stub-outs mount on wall faces at
    /// preset heights; the floor drain sits flush in the floor.
    /// </summary>
    public class PlumbController : MonoBehaviour, ITool
    {
        [SerializeField] private PointerProvider pointer;
        [SerializeField] private MeasureInput input;
        [SerializeField] private SceneRaycaster raycaster;
        [SerializeField] private Transform reticle;
        [SerializeField] private PlumbFixture fixturePrefab;
        [SerializeField] private PipeRoute pipePrefab;
        [SerializeField] private LineRenderer previewLine;
        [SerializeField] private ToolManager manager;
        [SerializeField] private SceneModel sceneModel;
        [SerializeField] private RoomPlanner.Walls.WallGraphRenderer walls;
        [SerializeField] private PlacementGuideOverlay guideOverlay;

        public enum SubMode { Riser, Pipe, Outlet, Drain }

        // ---- tool defaults (the inspector rows edit these; instances copy them on creation) ----
        private SubMode _mode = SubMode.Riser;
        private PlumbFixtureKind _outletKind = PlumbFixtureKind.ToiletOutlet;
        private OutletAngle _angle = OutletAngle.Deg90;
        private float _toiletHeight = PlumbingDefaults.ToiletOutletHeight;
        private float _sinkHeight = PlumbingDefaults.SinkOutletHeight;
        private PipeDiameter _diameter = PipeDiameter.D50;
        private bool _ortho = true;
        private int _reserve = PlumbingDefaults.DefaultReservePercent;

        // ---- pipe-drawing state ----
        private readonly List<Vector3> _pts = new();
        private readonly List<Vector3> _orthoScratch = new();
        private readonly List<Vector3> _previewPts = new();
        private string _startFixtureId;
        private float _nextPlaceAllowed;

        // ---- route continuation (#81 pattern): picked-up pipe being extended ----
        private PipeRoute _continued;
        private List<Vector3> _continuedBefore;
        private string _continuedStartId, _continuedEndId;
        private PipeDiameter _continuedDiameter;

        // ---- per-frame caches (rule 4.1: no allocations in Tick) ----
        private readonly RaycastHit[] _ownHits = new RaycastHit[16];
        private readonly List<Vector3> _axes = new();
        private const int SelectableLayer = 6;

        // stick-yaw during placement (#115): angled stub-outs and turned drains —
        // the furniture-rotation gesture (90°/s, a haptic detent every 15°)
        private float _yawOffset;
        private float _lastYawDetent;

        private PlumbFixture _ghost;             // placement preview, never registered
        private PipeRoute _riserGhost;           // riser column preview (#112), never registered
        private Vector3 _riserGhostFoot = new(float.NaN, 0f, 0f);
        private SubMode _ghostMode = (SubMode)(-1);
        private PlumbFixtureKind _ghostKind = (PlumbFixtureKind)(-1);
        private OutletAngle _ghostAngle = (OutletAngle)(-1);

        private SettingsSchema _schema;

        public string Id => "plumb";
        public string PaletteLabel => "Plumb";
        public string IconId => "pipe";

        // ---- ITool ----

        public SettingsSchema GetSettings()
        {
            _schema ??= BuildSchema();
            return _schema;
        }

        public void OnActivate() { }

        public void OnDeactivate()
        {
            FinishOrCancelRoute();
            if (reticle != null) reticle.gameObject.SetActive(false);
            if (previewLine != null) previewLine.enabled = false;
            if (_ghost != null) _ghost.gameObject.SetActive(false);
            if (_riserGhost != null) _riserGhost.gameObject.SetActive(false);
            if (guideOverlay != null) guideOverlay.Hide();
        }

        /// <summary>Dimension guides during placement (#113): show distances to the
        /// nearest wall(s) and, with grid snap on, land them on 5 cm multiples. The
        /// mount normal keeps a wall fixture from being dimensioned against its own
        /// wall. Redrawn after quantizing so the labels show the snapped values.</summary>
        private Vector3 ApplyGuides(Vector3 place, Vector3 mountNormal)
        {
            if (guideOverlay == null) return place;
            _axes.Clear();
            var g = walls != null ? walls.Graph : null;
            if (g != null)
                foreach (var s in g.Segments)
                {
                    if (!walls.IsVisible(s)) continue;
                    _axes.Add(s.A.Position);
                    _axes.Add(s.B.Position);
                }
            guideOverlay.ShowAt(place, _axes, mountNormal);
            if (manager != null && manager.SnapGrid && guideOverlay.Count > 0)
            {
                place = guideOverlay.Quantize(place, 0.05f);
                guideOverlay.ShowAt(place, _axes, mountNormal);
            }
            return place;
        }

        public void Tick(bool blocked)
        {
            if (pointer == null || input == null || raycaster == null) return;
            if (blocked)
            {
                if (reticle != null) reticle.gameObject.SetActive(false);
                if (previewLine != null) previewLine.enabled = false;
                if (_ghost != null) _ghost.gameObject.SetActive(false);
                if (_riserGhost != null) _riserGhost.gameObject.SetActive(false);
                if (guideOverlay != null) guideOverlay.Hide();
                return;
            }

            Ray ray = pointer.GetRay();
            bool hasHit = TryHitSurface(ray, out Vector3 point, out Vector3 normal,
                out PlumbFixture hitFixture, out PipeRoute hitPipe);

            switch (_mode)
            {
                case SubMode.Pipe: TickPipe(hasHit, point, normal, hitFixture, hitPipe); break;
                case SubMode.Outlet: TickOutlet(hasHit, point, normal); break;
                case SubMode.Drain: TickDrain(hasHit, point, normal); break;
                default: TickRiser(hasHit, point, normal); break;
            }
        }

        // ---- surface query: scanned room + own selectables; pipes and wires are not a
        // mounting surface, but a directly-hit pipe is reported for the riser snap ----

        private bool TryHitSurface(Ray ray, out Vector3 point, out Vector3 normal,
            out PlumbFixture hitFixture, out PipeRoute hitPipe)
        {
            point = default; normal = default; hitFixture = null; hitPipe = null;

            bool haveScan = raycaster.TryRaycast(ray, out Vector3 sp, out Vector3 sn, out _);
            float scanDist = haveScan ? Vector3.Distance(ray.origin, sp) : float.MaxValue;

            float ownDist = float.MaxValue;
            Vector3 op = default, on = default;
            PlumbFixture of = null;
            PipeRoute opipe = null;
            int n = Physics.RaycastNonAlloc(ray, _ownHits, 10f, 1 << SelectableLayer, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = _ownHits[i];
                if (h.collider == null || h.distance >= ownDist) continue;
                var go = h.collider.gameObject;
                if (!go.activeInHierarchy) continue;                       // hidden ≠ alive (rule 2.4)
                var sel = h.collider.GetComponentInParent<Selectable>();
                if (sel == null) continue;
                if (sel.Kind == SelectableKind.Wire) continue;             // wires are not a surface
                var pipe = sel.Pipe;
                var fx = sel.Plumb;
                if (fx != null && _ghost != null && fx == _ghost) continue; // never hit our own preview
                ownDist = h.distance; op = h.point; on = h.normal; of = fx; opipe = pipe;
            }

            if (!haveScan && ownDist == float.MaxValue) return false;
            if (ownDist < scanDist) { point = op; normal = on; hitFixture = of; hitPipe = opipe; }
            else { point = sp; normal = sn; }
            return true;
        }

        private static bool IsWall(Vector3 n) => Mathf.Abs(n.y) < 0.3f;
        private static bool IsCeiling(Vector3 n) => n.y < -0.7f;
        private static bool IsFloor(Vector3 n) => n.y > 0.7f;

        private float Level() => manager != null ? manager.Level : 0f;

        // ---- riser: one click on the floor, floor -> ceiling ----

        private void TickRiser(bool hasHit, Vector3 point, Vector3 normal)
        {
            if (previewLine != null) previewLine.enabled = false;
            if (_ghost != null) _ghost.gameObject.SetActive(false);

            bool valid = hasHit && IsFloor(normal);
            if (valid) point = ApplyGuides(point, Vector3.zero);
            else if (guideOverlay != null) guideOverlay.Hide();
            if (reticle != null)
            {
                reticle.gameObject.SetActive(valid);
                if (valid) reticle.position = point;
            }
            if (!valid)
            {
                if (_riserGhost != null) _riserGhost.gameObject.SetActive(false);
                if (input.ConfirmPressed()) input.Pulse(0.2f, 0.01f);   // refusal tick
                if (input.ClearPressed() && manager != null) manager.ActivateTool("select");
                return;
            }
            UpdateRiserGhost(point);

            if (input.ConfirmPressed() && Time.time >= _nextPlaceAllowed)
            {
                _nextPlaceAllowed = Time.time + PlumbingDefaults.PlaceDebounceSeconds;
                PlaceRiser(point);
            }
            if (input.ClearPressed() && manager != null) manager.ActivateTool("select");
        }

        /// <summary>Riser placement used to be blind — a flat reticle on the floor
        /// (#112). The ghost is a full-height D110 column; the tube is only re-cooked
        /// when the cursor moves more than 2 cm (a rebuild is a mesh cook, rule 4.1).</summary>
        private void UpdateRiserGhost(Vector3 foot)
        {
            if (pipePrefab == null) return;
            if (_riserGhost == null)
            {
                _riserGhost = Instantiate(pipePrefab, transform);
                _riserGhost.name = "RiserPreview";
            }
            if ((foot - _riserGhostFoot).sqrMagnitude > 0.0004f)
            {
                float topY = CeilingYAt(foot);
                _riserGhost.Build(new List<Vector3> { foot, new(foot.x, topY, foot.z) },
                    PipeDiameter.D110);
                var col = _riserGhost.GetComponent<MeshCollider>();
                if (col != null) col.enabled = false;   // a preview must not catch rays
                _riserGhostFoot = foot;
            }
            _riserGhost.gameObject.SetActive(true);
        }

        private void PlaceRiser(Vector3 foot)
        {
            if (pipePrefab == null || sceneModel == null) return;
            float topY = CeilingYAt(foot);
            if (topY < foot.y + 0.5f) { input.Pulse(0.2f, 0.01f); return; }

            var riser = Instantiate(pipePrefab, transform);
            if (!riser.gameObject.activeSelf) riser.gameObject.SetActive(true);
            riser.IsRiser = true;
            riser.ReservePercent = _reserve;
            if (riser.Build(new List<Vector3> { foot, new(foot.x, topY, foot.z) }, PipeDiameter.D110))
            {
                sceneModel.Register(riser.GetComponent<Selectable>());
                input.Pulse(0.6f, 0.02f);
            }
            else
            {
                Destroy(riser.gameObject);
            }
        }

        /// <summary>Ceiling above a floor point: an upward ray against the scan and own
        /// geometry, the nearest down-facing surface at least 2 m up (furniture bottoms
        /// and own tubes are filtered out); fallback — storey level + wall height.</summary>
        private float CeilingYAt(Vector3 foot)
        {
            float minY = Level() + 2f;
            var up = new Ray(new Vector3(foot.x, foot.y + 0.3f, foot.z), Vector3.up);

            float best = float.MaxValue;
            if (raycaster.TryRaycast(up, out Vector3 sp, out Vector3 sn, out _)
                && sn.y < -0.5f && sp.y >= minY)
                best = sp.y;

            int n = Physics.RaycastNonAlloc(up, _ownHits, 10f, 1 << SelectableLayer, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = _ownHits[i];
                if (h.collider == null || h.point.y >= best || h.point.y < minY) continue;
                if (h.normal.y >= -0.5f) continue;
                var sel = h.collider.GetComponentInParent<Selectable>();
                if (sel == null) continue;
                if (sel.Kind == SelectableKind.Wire || sel.Kind == SelectableKind.Pipe) continue;
                best = h.point.y;
            }
            return best < float.MaxValue ? best
                : Level() + (manager != null ? manager.WallHeight : 2.7f);
        }

        // ---- stub-outs (Outlet) and the floor drain ----

        private float PresetHeight() =>
            _outletKind == PlumbFixtureKind.ToiletOutlet ? _toiletHeight : _sinkHeight;

        private void TickOutlet(bool hasHit, Vector3 point, Vector3 normal)
        {
            if (previewLine != null) previewLine.enabled = false;
            if (_riserGhost != null) _riserGhost.gameObject.SetActive(false);

            bool valid = hasHit && IsWall(normal);
            if (!valid)
            {
                // no air fixtures, ever (the electrical rule)
                if (reticle != null) reticle.gameObject.SetActive(false);
                if (_ghost != null) _ghost.gameObject.SetActive(false);
                if (guideOverlay != null) guideOverlay.Hide();
                if (input.ConfirmPressed()) input.Pulse(0.2f, 0.01f);
                if (input.ClearPressed() && manager != null) manager.ActivateTool("select");
                return;
            }

            // height locks to the preset of the outlet type; grip frees the vertical
            Vector3 place = point;
            if (!input.SnapHeld()) place.y = Level() + PresetHeight();

            Vector3 outward = new Vector3(normal.x, 0f, normal.z);
            if (outward.sqrMagnitude < 1e-6f) outward = Vector3.forward;
            outward.Normalize();
            // stick turns the stub around the vertical, clamped so the seat stays on
            // the wall; grip quantizes to 15° (the furniture gesture, #115)
            TickYaw();
            float wallYaw = Mathf.Clamp(_yawOffset, -75f, 75f);
            var rot = Quaternion.AngleAxis(wallYaw, Vector3.up) * Quaternion.LookRotation(outward);
            place += outward * 0.002f;   // keep the seat off the wall plane
            place = ApplyGuides(place, outward);

            if (reticle != null) { reticle.gameObject.SetActive(true); reticle.position = place; }
            UpdateGhost(_outletKind, place, rot);

            if (input.ConfirmPressed() && Time.time >= _nextPlaceAllowed)
            {
                _nextPlaceAllowed = Time.time + PlumbingDefaults.PlaceDebounceSeconds;
                TryPlaceFixture(_outletKind, place, rot);
            }
            if (input.ClearPressed() && manager != null) manager.ActivateTool("select");
        }

        private void TickDrain(bool hasHit, Vector3 point, Vector3 normal)
        {
            if (previewLine != null) previewLine.enabled = false;
            if (_riserGhost != null) _riserGhost.gameObject.SetActive(false);

            bool valid = hasHit && IsFloor(normal);
            if (!valid)
            {
                if (reticle != null) reticle.gameObject.SetActive(false);
                if (_ghost != null) _ghost.gameObject.SetActive(false);
                if (guideOverlay != null) guideOverlay.Hide();
                if (input.ConfirmPressed()) input.Pulse(0.2f, 0.01f);
                if (input.ClearPressed() && manager != null) manager.ActivateTool("select");
                return;
            }

            // the grate sits flush in the floor plane, upright whatever the hit normal;
            // stick turns it freely so the D50 port faces the run (#115)
            TickYaw();
            var rot = Quaternion.Euler(0f, _yawOffset, 0f);
            point = ApplyGuides(point, Vector3.zero);
            if (reticle != null) { reticle.gameObject.SetActive(true); reticle.position = point; }
            UpdateGhost(PlumbFixtureKind.FloorDrain, point, rot);

            if (input.ConfirmPressed() && Time.time >= _nextPlaceAllowed)
            {
                _nextPlaceAllowed = Time.time + PlumbingDefaults.PlaceDebounceSeconds;
                TryPlaceFixture(PlumbFixtureKind.FloorDrain, point, rot);
            }
            if (input.ClearPressed() && manager != null) manager.ActivateTool("select");
        }

        private void UpdateGhost(PlumbFixtureKind kind, Vector3 place, Quaternion rot)
        {
            if (fixturePrefab == null) return;
            if (_ghost == null)
            {
                _ghost = Instantiate(fixturePrefab, transform);
                _ghost.name = "PlumbPreview";
                _ghost.gameObject.SetActive(true);
            }
            if (_ghostMode != _mode || _ghostKind != kind || _ghostAngle != _angle)
            {
                _ghost.Build(kind, _angle);
                var col = _ghost.GetComponent<MeshCollider>();
                if (col != null) col.enabled = false;   // a preview must not catch rays
                _ghostMode = _mode; _ghostKind = kind; _ghostAngle = _angle;
            }
            _ghost.gameObject.SetActive(true);
            _ghost.transform.SetPositionAndRotation(place, rot);
        }

        private void TryPlaceFixture(PlumbFixtureKind kind, Vector3 place, Quaternion rot)
        {
            if (fixturePrefab == null || sceneModel == null) return;
            if (OverlapsExistingFixture(kind, place)) { input.Pulse(0.2f, 0.01f); return; }

            var fx = Instantiate(fixturePrefab, transform);
            if (!fx.gameObject.activeSelf) fx.gameObject.SetActive(true);
            fx.Build(kind, _angle);
            fx.transform.SetPositionAndRotation(place, rot);
            fx.BaseLevel = Level();
            sceneModel.Register(fx.GetComponent<Selectable>());
            input.Pulse(0.6f, 0.02f);
        }

        private bool OverlapsExistingFixture(PlumbFixtureKind kind, Vector3 place)
        {
            if (sceneModel == null) return false;
            var items = sceneModel.Items;
            float newHalf = kind == PlumbFixtureKind.FloorDrain
                ? PlumbingDefaults.DrainSize * 0.5f
                : PipeSpec.Radius(kind == PlumbFixtureKind.ToiletOutlet ? PipeDiameter.D110 : PipeDiameter.D50)
                    * PlumbingDefaults.SocketFlare;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || !item.IsAlive || item.IsHidden) continue;
                if (item is not Selectable s || s.Plumb == null) continue;
                float minGap = newHalf + s.Plumb.BlockWidth * 0.5f + PlumbingDefaults.FixtureClearance;
                if (Vector3.Distance(s.Plumb.transform.position, place) < minGap) return true;
            }
            return false;
        }

        // ---- pipes ----

        private void TickPipe(bool hasHit, Vector3 point, Vector3 normal,
            PlumbFixture hitFixture, PipeRoute hitPipe)
        {
            if (_ghost != null) _ghost.gameObject.SetActive(false);
            if (_riserGhost != null) _riserGhost.gameObject.SetActive(false);
            if (guideOverlay != null) guideOverlay.Hide();   // guides are for fixtures (v1)

            // terminal magnet: within reach of a fixture's socket the cursor jumps to it
            PlumbFixture terminal = FindNearestTerminal(hasHit ? point : Vector3.zero, hasHit);
            if (hitFixture != null) terminal = hitFixture;

            // free-end magnet (#81 pattern): with no run active, the loose end of a
            // same-diameter pipe is a pickup spot — clicking it CONTINUES that pipe.
            // Checked BEFORE the tee magnet: near an end, continuing beats teeing.
            PipeRoute pickup = null;
            bool pickupFromStart = false;
            Vector3 pickupPoint = default;
            if (_pts.Count == 0 && terminal == null)
                pickup = FindNearestFreeEnd(hasHit ? point : Vector3.zero, hasHit,
                    out pickupFromStart, out pickupPoint);

            // tee magnet (#115): pipes tee into a riser at ANY height along its axis —
            // and into any EXISTING run's body at the closest point on its polyline
            PipeRoute riser = null;
            Vector3 riserPoint = default;
            if (terminal == null && pickup == null)
            {
                riser = FindNearestPipeTee(hasHit ? point : Vector3.zero, hasHit, out riserPoint);
                if (riser == null && hitPipe != null && hitPipe != _continued
                    && hitPipe.PointCount >= 2)
                {
                    riser = hitPipe;
                    riserPoint = ClosestOnPolyline(hitPipe, point);
                }
            }

            bool valid = hasHit && (terminal != null || riser != null || pickup != null
                || IsWall(normal) || IsCeiling(normal) || IsFloor(normal));
            Vector3 cursor = terminal != null ? terminal.TerminalWorld
                : riser != null ? riserPoint
                : pickup != null ? pickupPoint : point;

            if (reticle != null)
            {
                reticle.gameObject.SetActive(valid);
                if (valid) reticle.position = cursor;
            }
            DrawPipePreview(cursor, valid);

            if (valid && input.ConfirmPressed() && Time.time >= _nextPlaceAllowed)
            {
                _nextPlaceAllowed = Time.time + PlumbingDefaults.PlaceDebounceSeconds;
                if (pickup != null) BeginContinuation(pickup, pickupFromStart);
                else AddPipePoint(cursor, terminal, riser);
            }
            else if (!valid && input.ConfirmPressed())
            {
                input.Pulse(0.2f, 0.01f);   // no point in the air
            }

            if (input.ClearPressed())
            {
                // B finishes a viable run first; Esc-to-Select only on an empty run
                if (_pts.Count >= 2) FinishRoute(null, null);
                else if (_pts.Count == 1) CancelRoute();
                else if (manager != null) manager.ActivateTool("select");
            }
        }

        private PlumbFixture FindNearestTerminal(Vector3 nearPoint, bool hasHit)
        {
            if (!hasHit || sceneModel == null) return null;
            PlumbFixture best = null;
            float bestScore = 1f;
            var items = sceneModel.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || !item.IsAlive || item.IsHidden) continue;
                if (item is not Selectable s || s.Plumb == null) continue;
                // the drain port gets a wider magnet (#115) — normalize by each
                // fixture's own radius so the strongest RELATIVE pull wins
                float radius = s.Plumb.Kind == PlumbFixtureKind.FloorDrain
                    ? PlumbingDefaults.DrainSnapRadius
                    : PlumbingDefaults.TerminalSnapRadius;
                float score = Vector3.Distance(s.Plumb.TerminalWorld, nearPoint) / radius;
                if (score < bestScore) { bestScore = score; best = s.Plumb; }
            }
            return best;
        }

        /// <summary>The tee magnet (#115): the closest point on ANY existing pipe's
        /// polyline — a riser axis above all, but horizontal runs tee too. The pipe
        /// being continued never magnets itself.</summary>
        private PipeRoute FindNearestPipeTee(Vector3 nearPoint, bool hasHit, out Vector3 axisPoint)
        {
            axisPoint = default;
            if (!hasHit || sceneModel == null) return null;
            PipeRoute best = null;
            float bestDist = PlumbingDefaults.TerminalSnapRadius;
            var items = sceneModel.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || !item.IsAlive || item.IsHidden) continue;
                if (item is not Selectable s || s.Pipe == null) continue;
                var r = s.Pipe;
                if (r.PointCount < 2 || r == _continued) continue;
                for (int k = 1; k < r.PointCount; k++)
                {
                    Vector3 p = PipeMath.ClosestOnSegment(r.GetPoint(k - 1), r.GetPoint(k), nearPoint);
                    float d = Vector3.Distance(p, nearPoint);
                    if (d < bestDist) { bestDist = d; best = r; axisPoint = p; }
                }
            }
            return best;
        }

        private static Vector3 ClosestOnPolyline(PipeRoute r, Vector3 point)
        {
            Vector3 best = r.GetPoint(0);
            float bestDist = float.MaxValue;
            for (int k = 1; k < r.PointCount; k++)
            {
                Vector3 p = PipeMath.ClosestOnSegment(r.GetPoint(k - 1), r.GetPoint(k), point);
                float d = Vector3.Distance(p, point);
                if (d < bestDist) { bestDist = d; best = p; }
            }
            return best;
        }

        /// <summary>Loose end of an existing same-diameter pipe within the snap radius —
        /// the pickup spot for continuing it. Risers never continue (their two points ARE
        /// the stack); an end attached to anything is not free; hidden pipes never magnet.</summary>
        private PipeRoute FindNearestFreeEnd(Vector3 nearPoint, bool hasHit,
            out bool fromStart, out Vector3 endPoint)
        {
            fromStart = false;
            endPoint = default;
            if (!hasHit || sceneModel == null) return null;
            PipeRoute best = null;
            float bestDist = PlumbingDefaults.TerminalSnapRadius;
            var items = sceneModel.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || !item.IsAlive || item.IsHidden) continue;
                if (item is not Selectable s || s.Pipe == null) continue;
                var route = s.Pipe;
                if (route.IsRiser || route.PointCount < 2 || route.Diameter != _diameter) continue;

                if (string.IsNullOrEmpty(route.StartFixtureId))
                {
                    float d = Vector3.Distance(route.GetPoint(0), nearPoint);
                    if (d < bestDist)
                    {
                        bestDist = d; best = route;
                        fromStart = true; endPoint = route.GetPoint(0);
                    }
                }
                if (string.IsNullOrEmpty(route.EndFixtureId))
                {
                    var last = route.GetPoint(route.PointCount - 1);
                    float d = Vector3.Distance(last, nearPoint);
                    if (d < bestDist)
                    {
                        bestDist = d; best = route;
                        fromStart = false; endPoint = last;
                    }
                }
            }
            return best;
        }

        /// <summary>Pick an existing pipe up by its free end: its polyline becomes the
        /// active run and drawing resumes — the finish rewrites the SAME pipe as one
        /// undoable edit. Grabbing the START reverses the polyline so drawing always
        /// appends at the tail; the attachments swap with it.</summary>
        private void BeginContinuation(PipeRoute route, bool fromStart)
        {
            _continued = route;
            _continuedBefore = new List<Vector3>(route.Points);
            _continuedStartId = route.StartFixtureId;
            _continuedEndId = route.EndFixtureId;
            _continuedDiameter = route.Diameter;

            _pts.Clear();
            _pts.AddRange(route.Points);
            if (fromStart)
            {
                _pts.Reverse();
                _startFixtureId = route.EndFixtureId;
            }
            else
            {
                _startFixtureId = route.StartFixtureId;
            }
            _diameter = route.Diameter;
            input.Pulse(0.6f, 0.02f);
        }

        private void AddPipePoint(Vector3 cursor, PlumbFixture terminal, PipeRoute riser)
        {
            if (_pts.Count == 0)
            {
                _pts.Add(cursor);
                if (terminal != null)
                {
                    _startFixtureId = SelectableId(terminal.gameObject);
                    // run size follows the fixture it starts from: toilet D110, sinks D50
                    _diameter = terminal.Diameter;
                }
                else if (riser != null)
                {
                    _startFixtureId = SelectableId(riser.gameObject);
                }
                input.Pulse(0.6f, 0.02f);
                return;
            }

            Vector3 last = _pts[_pts.Count - 1];
            if (Vector3.Distance(cursor, last) < PlumbingDefaults.MinPointStep)
            {
                input.Pulse(0.2f, 0.01f);   // double click — ignored
                return;
            }

            if (_ortho)
            {
                PipeMath.OrthoElbowLow(last, cursor, _orthoScratch);
                _pts.AddRange(_orthoScratch);
            }
            _pts.Add(cursor);
            input.Pulse(0.6f, 0.02f);

            // any terminal or riser ends the run into it
            if (terminal != null || riser != null) FinishRoute(terminal, riser);
        }

        private void FinishRoute(PlumbFixture endTerminal, PipeRoute endRiser)
        {
            string endId = endTerminal != null ? SelectableId(endTerminal.gameObject)
                : endRiser != null ? SelectableId(endRiser.gameObject) : null;

            if (_continued != null)
            {
                // continuation stays the SAME pipe — one undoable edit; finishing with
                // no new points is a no-op, the pipe is simply put back down
                var contSel = _continued.GetComponent<Selectable>();
                if (_pts.Count > _continuedBefore.Count && sceneModel != null
                    && contSel != null && contSel.IsAlive)
                {
                    sceneModel.History.Execute(new PipeExtendCommand(_continued,
                        _continuedBefore, _continuedStartId, _continuedEndId, _continuedDiameter,
                        new List<Vector3>(_pts), _startFixtureId, endId, _diameter));
                    input.Pulse(0.6f, 0.03f);
                }
                CancelRoute();
                return;
            }

            if (_pts.Count >= 2 && pipePrefab != null && sceneModel != null)
            {
                var route = Instantiate(pipePrefab, transform);
                if (!route.gameObject.activeSelf) route.gameObject.SetActive(true);
                if (route.Build(_pts, _diameter))
                {
                    route.StartFixtureId = _startFixtureId;
                    route.EndFixtureId = endId;
                    sceneModel.Register(route.GetComponent<Selectable>());
                    input.Pulse(0.6f, 0.03f);
                }
                else
                {
                    Destroy(route.gameObject);   // degenerate after cleanup — refuse silently
                }
            }
            CancelRoute();
        }

        private void CancelRoute()
        {
            _pts.Clear();
            _startFixtureId = null;
            _continued = null;
            _continuedBefore = null;
            if (previewLine != null) previewLine.enabled = false;
        }

        /// <summary>A run of two or more points survives any exit (mode switch, tool
        /// switch) as a real pipe — accidental deactivation must not eat drawn work.</summary>
        private void FinishOrCancelRoute()
        {
            if (_pts.Count >= 2) FinishRoute(null, null);
            else CancelRoute();
        }

        private void DrawPipePreview(Vector3 cursor, bool valid)
        {
            if (previewLine == null) return;
            if (_pts.Count == 0) { previewLine.enabled = false; return; }

            _previewPts.Clear();
            _previewPts.AddRange(_pts);
            if (valid)
            {
                Vector3 last = _pts[_pts.Count - 1];
                if (_ortho)
                {
                    PipeMath.OrthoElbowLow(last, cursor, _orthoScratch);
                    _previewPts.AddRange(_orthoScratch);
                }
                _previewPts.Add(cursor);
            }
            previewLine.enabled = true;
            previewLine.positionCount = _previewPts.Count;
            for (int i = 0; i < _previewPts.Count; i++) previewLine.SetPosition(i, _previewPts[i]);
        }

        private static string SelectableId(GameObject go)
        {
            var sel = go.GetComponent<Selectable>();
            return sel != null ? sel.Id : null;
        }

        // ---- project restore (format v5) ----

        private SceneModel RestoreModel => sceneModel != null ? sceneModel : SceneModel.Instance;

        /// <summary>Recreate a saved plumb fixture. The saved id is kept verbatim, so
        /// pipe ends re-attach by id for free. Falls back to assembling the components
        /// bare when the prefab is not wired.</summary>
        public PlumbFixture RestorePlumbFixture(RoomPlanner.Core.Project.ProjectPlumbFixture f)
        {
            var model = RestoreModel;
            if (f == null || model == null) return null;

            PlumbFixture fx;
            if (fixturePrefab != null)
            {
                fx = Instantiate(fixturePrefab, transform);
                if (!fx.gameObject.activeSelf) fx.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject("PlumbFixture (restored)") { layer = gameObject.layer };
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                fx = go.AddComponent<PlumbFixture>();
                go.AddComponent<PlumbFixtureParameters>();
                go.AddComponent<Selectable>();
            }
            fx.Build((PlumbFixtureKind)f.Kind, (OutletAngle)f.Angle);
            fx.transform.SetPositionAndRotation(f.Position, f.Rotation);
            fx.BaseLevel = f.BaseLevel;
            var sel = fx.GetComponent<Selectable>();
            if (sel != null && !string.IsNullOrEmpty(f.Id)) sel.Id = f.Id;
            model.Register(sel);
            return fx;
        }

        /// <summary>Recreate a saved pipe run with its diameter, riser flag and attachments.</summary>
        public PipeRoute RestorePipe(RoomPlanner.Core.Project.ProjectPipe p)
        {
            var model = RestoreModel;
            if (p == null || model == null || p.Points == null || p.Points.Count < 2) return null;

            PipeRoute route;
            if (pipePrefab != null)
            {
                route = Instantiate(pipePrefab, transform);
                if (!route.gameObject.activeSelf) route.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject("Pipe (restored)") { layer = gameObject.layer };
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                route = go.AddComponent<PipeRoute>();
                go.AddComponent<PipeRouteParameters>();
                go.AddComponent<Selectable>();
            }
            route.IsRiser = p.IsRiser;
            if (p.Reserve >= 0) route.ReservePercent = p.Reserve;
            if (!route.Build(p.Points, (PipeDiameter)p.Diameter))
            {
                Destroy(route.gameObject);
                return null;
            }
            route.StartFixtureId = p.StartId;
            route.EndFixtureId = p.EndId;
            var sel = route.GetComponent<Selectable>();
            if (sel != null && !string.IsNullOrEmpty(p.Id)) sel.Id = p.Id;
            model.Register(sel);
            return route;
        }

        // ---- settings: ONE tabbed schema (design/20 §2.12) ----

        private SettingsSchema BuildSchema()
        {
            var riser = new SettingsSchema()
                .Readout("rsize", "Stack", () => "D110 · floor to ceiling")
                .Slider("res", "Reserve", 0f, PlumbingDefaults.MaxReservePercent,
                    PlumbingDefaults.ReserveStep,
                    () => _reserve,
                    v => _reserve = Mathf.Clamp(Mathf.RoundToInt(v), 0, PlumbingDefaults.MaxReservePercent),
                    (_, v) => _reserve = Mathf.Clamp(Mathf.RoundToInt(v), 0, PlumbingDefaults.MaxReservePercent),
                    () => $"{_reserve} %");
            var pipe = new SettingsSchema()
                .Segmented("dia", "Diameter",
                    new[] { PipeSpec.Label(PipeDiameter.D110), PipeSpec.Label(PipeDiameter.D50), PipeSpec.Label(PipeDiameter.D40) },
                    () => (int)_diameter, i => _diameter = (PipeDiameter)i)
                .Segmented("routing", "Route", new[] { "Ortho", "Free" },
                    () => _ortho ? 0 : 1, i => _ortho = i == 0);
            var outlet = new SettingsSchema()
                .Segmented("okind", "Type", new[] { "Toilet", "Sink" },
                    () => _outletKind == PlumbFixtureKind.ToiletOutlet ? 0 : 1,
                    i => _outletKind = i == 0 ? PlumbFixtureKind.ToiletOutlet : PlumbFixtureKind.SinkOutlet)
                .Segmented("oangle", "Angle", new[] { "90°", "45°↓", "45°↑" },
                    () => (int)_angle,
                    i => _angle = (OutletAngle)Mathf.Clamp(i, 0, 2))
                .Slider("oh", "Height", PlumbingDefaults.MinOutletHeight,
                    PlumbingDefaults.MaxOutletHeight, PlumbingDefaults.HeightStep,
                    () => ActiveHeight(),
                    v => SetActiveHeight(v),
                    (_, v) => SetActiveHeight(v),
                    () => $"{ActiveHeight() * 100f:0} cm", displayScale: 100f);
            var drain = new SettingsSchema()
                .Readout("dsize", "Drain",
                    () => $"{PlumbingDefaults.DrainSize * 100f:0}×{PlumbingDefaults.DrainSize * 100f:0} cm · D50 port");

            return SettingsSchema.Tabbed(
                new[] { "Riser", "Pipe", "Outlet", "Drain" },
                () => (int)_mode, SetMode, riser, pipe, outlet, drain);
        }

        private float ActiveHeight() =>
            _outletKind == PlumbFixtureKind.ToiletOutlet ? _toiletHeight : _sinkHeight;

        private void SetActiveHeight(float v)
        {
            float clamped = Mathf.Clamp(v, PlumbingDefaults.MinOutletHeight, PlumbingDefaults.MaxOutletHeight);
            if (_outletKind == PlumbFixtureKind.ToiletOutlet) _toiletHeight = clamped;
            else _sinkHeight = clamped;
        }

        private void SetMode(int mode)
        {
            FinishOrCancelRoute();               // switching modes never eats a drawn run
            _mode = (SubMode)Mathf.Clamp(mode, 0, 3);
            _yawOffset = 0f;
            _lastYawDetent = 0f;
        }

        private void TickYaw()
        {
            float x = input.Thumbstick().x;
            if (Mathf.Abs(x) < 0.3f) return;
            _yawOffset += x * 90f * Time.deltaTime;
            _yawOffset = Mathf.Repeat(_yawOffset + 180f, 360f) - 180f;
            if (input.SnapHeld()) _yawOffset = Mathf.Round(_yawOffset / 15f) * 15f;
            if (Mathf.Abs(Mathf.DeltaAngle(_yawOffset, _lastYawDetent)) >= 15f)
            {
                _lastYawDetent = _yawOffset;
                input.Pulse(0.2f, 0.01f);        // detent tick, the furniture feel
            }
        }
    }

    /// <summary>
    /// Continuing a pipe from its free end (the #81 pattern): the whole continuation —
    /// appended points, new end attachment, possibly a diameter change — is ONE undo
    /// entry that puts the previous polyline and attachments back.
    /// </summary>
    public sealed class PipeExtendCommand : ICommand, ISelectableCommand
    {
        private readonly PipeRoute _route;
        private readonly List<Vector3> _before, _after;
        private readonly string _beforeStart, _beforeEnd, _afterStart, _afterEnd;
        private readonly PipeDiameter _beforeDiameter, _afterDiameter;

        public PipeExtendCommand(PipeRoute route,
            List<Vector3> before, string beforeStart, string beforeEnd, PipeDiameter beforeDiameter,
            List<Vector3> after, string afterStart, string afterEnd, PipeDiameter afterDiameter)
        {
            _route = route;
            _before = before; _beforeStart = beforeStart; _beforeEnd = beforeEnd;
            _beforeDiameter = beforeDiameter;
            _after = after; _afterStart = afterStart; _afterEnd = afterEnd;
            _afterDiameter = afterDiameter;
        }

        public string Name => "Extend pipe";
        public ISelectable Target => _route != null ? _route.GetComponent<Selectable>() : null;

        public void Do() => Apply(_after, _afterStart, _afterEnd, _afterDiameter);
        public void Undo() => Apply(_before, _beforeStart, _beforeEnd, _beforeDiameter);

        private void Apply(List<Vector3> pts, string startId, string endId, PipeDiameter diameter)
        {
            if (_route == null) return;
            var sel = _route.GetComponent<Selectable>();
            if (sel == null || !sel.IsAlive) return;
            _route.Build(new List<Vector3>(pts), diameter);
            _route.StartFixtureId = startId;
            _route.EndFixtureId = endId;
        }
    }
}
