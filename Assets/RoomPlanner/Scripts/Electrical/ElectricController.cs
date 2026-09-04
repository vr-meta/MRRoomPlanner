using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Measure;
using RoomPlanner.Tools;
using RoomPlanner.Editing;

namespace RoomPlanner.Electrical
{
    /// <summary>
    /// Electric tool (docs/design/19-electrical.md): one palette button, five sub-modes
    /// as inspector tabs — Outlet / Switch / Wire / Box / Panel.
    ///
    /// Fixtures go on wall faces with the mounting height snapped to the sub-mode preset
    /// (grip = free vertical for the odd counter-top outlet); the junction box (v2)
    /// mounts on walls AND ceilings at any height. Wires are routed BY HAND, point by
    /// point on walls and the ceiling — Ortho inserts a single elbow with the horizontal
    /// travel at the higher of the two clicks, never an automatic detour to the ceiling
    /// (headset feedback 2026-08-10). Ends within reach of a fixture terminal attach
    /// logically — clicking any terminal finishes the run into it.
    /// </summary>
    public class ElectricController : MonoBehaviour, ITool
    {
        [SerializeField] private PointerProvider pointer;
        [SerializeField] private MeasureInput input;
        [SerializeField] private SceneRaycaster raycaster;
        [SerializeField] private Transform reticle;
        [SerializeField] private ElectricFixture fixturePrefab;
        [SerializeField] private WireRoute wirePrefab;
        [SerializeField] private LineRenderer previewLine;
        [SerializeField] private ToolManager manager;
        [SerializeField] private SceneModel sceneModel;

        public enum SubMode { Outlet, Switch, Wire, Junction, Panel }

        // ---- tool defaults (the inspector rows edit these; instances copy them on creation) ----
        private SubMode _mode = SubMode.Outlet;
        private int _posts = 2;
        private int _keys = 1;
        private float _outletHeight = ElectricalDefaults.OutletHeight;
        private float _switchHeight = ElectricalDefaults.SwitchHeight;
        private CableType _cable = CableType.C3x25;
        private bool _ortho = true;
        private int _reserve = ElectricalDefaults.DefaultReservePercent;
        private bool _blackVariant;
        private bool _panelOpen;

        private static readonly Color[] FixtureVariants =
        {
            ElectricFixture.WhitePlastic,
            ElectricFixture.BlackPlastic,
        };

        // ---- wire-drawing state ----
        private readonly List<Vector3> _pts = new();
        private readonly List<Vector3> _orthoScratch = new();
        private readonly List<Vector3> _previewPts = new();
        private string _startFixtureId;
        private float _nextPlaceAllowed;

        // ---- route continuation (#81): picked-up route being extended from its free end ----
        private WireRoute _continued;
        private List<Vector3> _continuedBefore;
        private string _continuedStartId, _continuedEndId;
        private CableType _continuedCable;

        // ---- per-frame caches (rule 4.1: no allocations in Tick) ----
        private readonly RaycastHit[] _ownHits = new RaycastHit[16];
        private const int SelectableLayer = 6;

        private ElectricFixture _ghost;          // placement preview, never registered
        private SubMode _ghostMode = (SubMode)(-1);
        private int _ghostPosts = -1, _ghostKeys = -1;
        private bool _ghostBlack, _ghostPanelOpen;

        private SettingsSchema _schema;          // tabbed root (design/20 §2.12) — one instance

        public string Id => "electric";
        public string PaletteLabel => "Elec";
        public string IconId => "electric-plug";

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
        }

        public void Tick(bool blocked)
        {
            if (pointer == null || input == null || raycaster == null) return;
            if (blocked)
            {
                if (reticle != null) reticle.gameObject.SetActive(false);
                if (previewLine != null) previewLine.enabled = false;
                if (_ghost != null) _ghost.gameObject.SetActive(false);
                return;
            }

            Ray ray = pointer.GetRay();
            bool hasHit = TryHitSurface(ray, out Vector3 point, out Vector3 normal, out ElectricFixture hitFixture);

            if (_mode == SubMode.Wire) TickWire(hasHit, point, normal, hitFixture);
            else TickFixture(hasHit, point, normal);
        }

        // ---- surface query: scanned room + own selectables (walls/fixtures live on layer 6,
        // which the shared SceneRaycaster deliberately skips), nearest of the two wins ----

        private bool TryHitSurface(Ray ray, out Vector3 point, out Vector3 normal, out ElectricFixture hitFixture)
        {
            point = default; normal = default; hitFixture = null;

            bool haveScan = raycaster.TryRaycast(ray, out Vector3 sp, out Vector3 sn, out _);
            float scanDist = haveScan ? Vector3.Distance(ray.origin, sp) : float.MaxValue;

            float ownDist = float.MaxValue;
            Vector3 op = default, on = default;
            ElectricFixture of = null;
            int n = Physics.RaycastNonAlloc(ray, _ownHits, 10f, 1 << SelectableLayer, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = _ownHits[i];
                if (h.collider == null || h.distance >= ownDist) continue;
                var go = h.collider.gameObject;
                if (!go.activeInHierarchy) continue;                       // hidden ≠ alive (rule 2.4)
                // only registered scene objects count as mounting surfaces — layer 6 also
                // carries loose edit-handle spheres, and a handle must not host an outlet
                var sel = h.collider.GetComponentInParent<Selectable>();
                if (sel == null) continue;
                if (sel.Kind == SelectableKind.Wire) continue;             // wires are not a surface
                var fx = sel.Fixture;
                if (fx != null && _ghost != null && fx == _ghost) continue; // never hit our own preview
                ownDist = h.distance; op = h.point; on = h.normal; of = fx;
            }

            if (!haveScan && ownDist == float.MaxValue) return false;
            if (ownDist < scanDist) { point = op; normal = on; hitFixture = of; }
            else { point = sp; normal = sn; }
            return true;
        }

        private static bool IsWall(Vector3 n) => Mathf.Abs(n.y) < 0.3f;
        private static bool IsCeiling(Vector3 n) => n.y < -0.7f;

        private float Level() => manager != null ? manager.Level : 0f;

        // ---- fixtures (Outlet / Switch / Panel) ----

        private float PresetHeight() => _mode switch
        {
            SubMode.Outlet => _outletHeight,
            SubMode.Switch => _switchHeight,
            _ => ElectricalDefaults.PanelHeight,
        };

        private FixtureKind ModeKind() => _mode switch
        {
            SubMode.Outlet => FixtureKind.Outlet,
            SubMode.Switch => FixtureKind.Switch,
            SubMode.Junction => FixtureKind.Junction,
            _ => FixtureKind.Panel,
        };

        private void TickFixture(bool hasHit, Vector3 point, Vector3 normal)
        {
            if (previewLine != null) previewLine.enabled = false;

            // junction boxes (v2) mount on walls AND ceilings; everything else is wall-only
            bool junction = _mode == SubMode.Junction;
            bool valid = hasHit && (IsWall(normal) || (junction && IsCeiling(normal)));
            if (!valid)
            {
                // no air fixtures, ever: a miss shows no ghost and a click places nothing
                if (reticle != null) reticle.gameObject.SetActive(false);
                if (_ghost != null) _ghost.gameObject.SetActive(false);
                if (input.ConfirmPressed()) input.Pulse(0.2f, 0.01f);   // refusal tick
                if (input.ClearPressed() && manager != null) manager.ActivateTool("select");
                return;
            }

            // height locks to the sub-mode preset — a hand tremor of ±3–5 cm at ray distance
            // must not decide mounting heights; grip frees the vertical for odd cases.
            // The junction box has no preset: it sits wherever the run needs it.
            Vector3 place = point;
            if (!junction && !input.SnapHeld()) place.y = Level() + PresetHeight();

            Quaternion rot;
            if (IsWall(normal))
            {
                Vector3 outward = new Vector3(normal.x, 0f, normal.z);
                if (outward.sqrMagnitude < 1e-6f) outward = Vector3.forward;
                outward.Normalize();
                rot = Quaternion.LookRotation(outward);
                place += outward * 0.002f;   // keep the back plate off the wall plane
            }
            else
            {
                // ceiling mount: local +Z (the lid) looks down into the room
                Vector3 n = normal.normalized;
                rot = Quaternion.LookRotation(n, Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up);
                place += n * 0.002f;
            }

            if (reticle != null) { reticle.gameObject.SetActive(true); reticle.position = place; }
            UpdateGhost(place, rot);

            if (input.ConfirmPressed() && Time.time >= _nextPlaceAllowed)
            {
                _nextPlaceAllowed = Time.time + ElectricalDefaults.PlaceDebounceSeconds;
                TryPlaceFixture(place, rot);
            }
            if (input.ClearPressed() && manager != null) manager.ActivateTool("select");
        }

        private void UpdateGhost(Vector3 place, Quaternion rot)
        {
            if (fixturePrefab == null) return;
            if (_ghost == null)
            {
                _ghost = Instantiate(fixturePrefab, transform);
                _ghost.name = "FixturePreview";
                _ghost.gameObject.SetActive(true);
            }
            if (_ghostMode != _mode || _ghostPosts != _posts || _ghostKeys != _keys
                || _ghostBlack != _blackVariant || _ghostPanelOpen != _panelOpen)
            {
                _ghost.Build(ModeKind(), _posts, _keys, _blackVariant, _panelOpen);
                var col = _ghost.GetComponent<MeshCollider>();
                if (col != null) col.enabled = false;   // a preview must not catch rays
                _ghostMode = _mode; _ghostPosts = _posts; _ghostKeys = _keys;
                _ghostBlack = _blackVariant; _ghostPanelOpen = _panelOpen;
            }
            _ghost.gameObject.SetActive(true);
            _ghost.transform.SetPositionAndRotation(place, rot);
        }

        private void TryPlaceFixture(Vector3 place, Quaternion rot)
        {
            if (fixturePrefab == null || sceneModel == null) return;

            if (_mode == SubMode.Panel)
            {
                if (FindPanel() != null)
                {
                    input.Pulse(0.2f, 0.01f);   // one panel per scene in v1
                    return;
                }
                // A DELETED panel is only hidden (undo-able). Placing a replacement used
                // to leave it resurrectable — undo after the new placement produced TWO
                // panels (audit 08 §Б2). The replacement makes the deletion permanent:
                // Unregister purges the old panel's commands, so undo stays consistent.
                var hiddenPanel = FindHiddenPanel();
                if (hiddenPanel != null)
                {
                    var hiddenSel = hiddenPanel.GetComponent<Selectable>();
                    if (hiddenSel != null && sceneModel != null) sceneModel.Unregister(hiddenSel);
                    Destroy(hiddenPanel.gameObject);
                }
            }
            if (OverlapsExistingFixture(place)) { input.Pulse(0.2f, 0.01f); return; }

            var fx = Instantiate(fixturePrefab, transform);
            if (!fx.gameObject.activeSelf) fx.gameObject.SetActive(true);
            fx.Build(ModeKind(), _posts, _keys, _blackVariant, _panelOpen);
            fx.transform.SetPositionAndRotation(place, rot);
            fx.BaseLevel = Level();   // heights stay storey-relative on upper floors
            if (_mode == SubMode.Panel) fx.ReservePercent = _reserve;
            sceneModel.Register(fx.GetComponent<Selectable>());
            input.Pulse(0.6f, 0.02f);
        }

        private bool OverlapsExistingFixture(Vector3 place)
        {
            if (sceneModel == null) return false;
            var items = sceneModel.Items;
            float newHalf = HalfWidth(ModeKind(), _posts);
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || !item.IsAlive || item.IsHidden) continue;
                if (item is not Selectable s || s.Fixture == null) continue;
                float minGap = newHalf + s.Fixture.BlockWidth * 0.5f + ElectricalDefaults.FixtureClearance;
                if (Vector3.Distance(s.Fixture.transform.position, place) < minGap) return true;
            }
            return false;
        }

        private static float HalfWidth(FixtureKind kind, int posts) => kind switch
        {
            FixtureKind.Outlet => posts * ElectricalDefaults.PostModule * 0.5f,
            FixtureKind.Switch => ElectricalDefaults.PostModule * 0.5f,
            FixtureKind.Junction => ElectricalDefaults.JunctionBoxSize * 0.5f,
            _ => ElectricalDefaults.PanelBoxWidth * 0.5f,
        };

        private ElectricFixture FindPanel()
        {
            if (sceneModel == null) return null;
            var items = sceneModel.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || !item.IsAlive || item.IsHidden) continue;
                if (item is Selectable s && s.Fixture != null && s.Fixture.Kind == FixtureKind.Panel)
                    return s.Fixture;
            }
            return null;
        }

        // ---- project restore (format v2, audit B1) ----

        /// <summary>The model to register restored objects in: the rig-wired one, or the
        /// scene instance — restore must work on rigs saved before the field existed.</summary>
        private SceneModel RestoreModel => sceneModel != null ? sceneModel : SceneModel.Instance;

        /// <summary>
        /// Recreate a saved fixture. The saved id is kept verbatim (Register respects
        /// pre-set ids), so wire ends re-attach by id for free. Falls back to assembling
        /// the components bare when the prefab is not wired.
        /// </summary>
        public ElectricFixture RestoreFixture(RoomPlanner.Core.Project.ProjectFixture f)
        {
            var model = RestoreModel;
            if (f == null || model == null) return null;

            ElectricFixture fx;
            if (fixturePrefab != null)
            {
                fx = Instantiate(fixturePrefab, transform);
                if (!fx.gameObject.activeSelf) fx.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject("Fixture (restored)") { layer = gameObject.layer };
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                fx = go.AddComponent<ElectricFixture>();
                go.AddComponent<ElectricFixtureParameters>();
                go.AddComponent<Selectable>();
            }
            fx.Build((FixtureKind)f.Kind, f.Posts, f.Keys, f.Black, f.PanelOpen);
            fx.transform.SetPositionAndRotation(f.Position, f.Rotation);
            fx.BaseLevel = f.BaseLevel;
            if (f.Reserve >= 0) fx.ReservePercent = f.Reserve;
            var sel = fx.GetComponent<Selectable>();
            if (sel != null && !string.IsNullOrEmpty(f.Id)) sel.Id = f.Id;
            model.Register(sel);
            return fx;
        }

        /// <summary>Recreate a saved wire run with its cable type and attachments.</summary>
        public WireRoute RestoreWire(RoomPlanner.Core.Project.ProjectWire w)
        {
            var model = RestoreModel;
            if (w == null || model == null || w.Points == null || w.Points.Count < 2) return null;

            WireRoute route;
            if (wirePrefab != null)
            {
                route = Instantiate(wirePrefab, transform);
                if (!route.gameObject.activeSelf) route.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject("Wire (restored)") { layer = gameObject.layer };
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                route = go.AddComponent<WireRoute>();
                go.AddComponent<WireRouteParameters>();
                go.AddComponent<Selectable>();
            }
            if (!route.Build(w.Points, (CableType)w.Cable))
            {
                Destroy(route.gameObject);
                return null;
            }
            route.StartFixtureId = w.StartId;
            route.EndFixtureId = w.EndId;
            model.Register(route.GetComponent<Selectable>());
            return route;
        }

        /// <summary>A deleted-but-undoable panel (hidden in the model), if any.</summary>
        private ElectricFixture FindHiddenPanel()
        {
            if (sceneModel == null) return null;
            var items = sceneModel.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || !item.IsAlive || !item.IsHidden) continue;
                if (item is Selectable s && s.Fixture != null && s.Fixture.Kind == FixtureKind.Panel)
                    return s.Fixture;
            }
            return null;
        }

        // ---- wires ----

        private void TickWire(bool hasHit, Vector3 point, Vector3 normal, ElectricFixture hitFixture)
        {
            if (_ghost != null) _ghost.gameObject.SetActive(false);

            // terminal magnet: within reach of a fixture's cable entry the cursor jumps to it
            ElectricFixture terminal = FindNearestTerminal(hasHit ? point : Vector3.zero, hasHit);
            if (hitFixture != null) terminal = hitFixture;

            // free-end magnet (#81): with no run active, the loose end of an existing
            // route is a pickup spot — clicking it CONTINUES that route (still one wire)
            WireRoute pickup = null;
            bool pickupFromStart = false;
            Vector3 pickupPoint = default;
            if (_pts.Count == 0 && terminal == null)
                pickup = FindNearestFreeEnd(hasHit ? point : Vector3.zero, hasHit,
                    out pickupFromStart, out pickupPoint);

            bool valid = hasHit && (terminal != null || pickup != null
                || IsWall(normal) || IsCeiling(normal));
            Vector3 cursor = terminal != null ? terminal.TerminalWorld
                : pickup != null ? pickupPoint : point;

            if (reticle != null)
            {
                reticle.gameObject.SetActive(valid);
                if (valid) reticle.position = cursor;
            }
            DrawWirePreview(cursor, valid);

            if (valid && input.ConfirmPressed() && Time.time >= _nextPlaceAllowed)
            {
                _nextPlaceAllowed = Time.time + ElectricalDefaults.PlaceDebounceSeconds;
                if (pickup != null) BeginContinuation(pickup, pickupFromStart);
                else AddWirePoint(cursor, terminal);
            }
            else if (!valid && input.ConfirmPressed())
            {
                input.Pulse(0.2f, 0.01f);   // no point in the air / on the floor
            }

            if (input.ClearPressed())
            {
                // B finishes a viable run first; Esc-to-Select only fires on an empty run —
                // an accidental B must not throw away a half-drawn circuit (UX report, h4)
                if (_pts.Count >= 2) FinishRoute(null);
                else if (_pts.Count == 1) CancelRoute();
                else if (manager != null) manager.ActivateTool("select");
            }
        }

        private ElectricFixture FindNearestTerminal(Vector3 nearPoint, bool hasHit)
        {
            if (!hasHit || sceneModel == null) return null;
            ElectricFixture best = null;
            float bestDist = ElectricalDefaults.TerminalSnapRadius;
            var items = sceneModel.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || !item.IsAlive || item.IsHidden) continue;
                if (item is not Selectable s || s.Fixture == null) continue;
                float d = Vector3.Distance(s.Fixture.TerminalWorld, nearPoint);
                if (d < bestDist) { bestDist = d; best = s.Fixture; }
            }
            return best;
        }

        /// <summary>Loose end of an existing route within the terminal-snap radius —
        /// the pickup spot for continuing it (#81). An end attached to a fixture is
        /// not free; hidden (deleted) routes never magnet (rule 2.4).</summary>
        private WireRoute FindNearestFreeEnd(Vector3 nearPoint, bool hasHit,
            out bool fromStart, out Vector3 endPoint)
        {
            fromStart = false;
            endPoint = default;
            if (!hasHit || sceneModel == null) return null;
            WireRoute best = null;
            float bestDist = ElectricalDefaults.TerminalSnapRadius;
            var items = sceneModel.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || !item.IsAlive || item.IsHidden) continue;
                if (item is not Selectable s) continue;
                var route = s.GetComponent<WireRoute>();
                if (route == null || route.Points.Count < 2) continue;

                if (string.IsNullOrEmpty(route.StartFixtureId))
                {
                    float d = Vector3.Distance(route.Points[0], nearPoint);
                    if (d < bestDist)
                    {
                        bestDist = d; best = route;
                        fromStart = true; endPoint = route.Points[0];
                    }
                }
                if (string.IsNullOrEmpty(route.EndFixtureId))
                {
                    var last = route.Points[route.Points.Count - 1];
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

        /// <summary>Pick an existing route up by its free end (#81): its polyline becomes
        /// the active run and drawing resumes — the finish rewrites the SAME route as one
        /// undoable edit. Grabbing the START reverses the polyline so drawing always
        /// appends at the tail; the start/end attachments swap with it.</summary>
        private void BeginContinuation(WireRoute route, bool fromStart)
        {
            _continued = route;
            _continuedBefore = new List<Vector3>(route.Points);
            _continuedStartId = route.StartFixtureId;
            _continuedEndId = route.EndFixtureId;
            _continuedCable = route.Cable;

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
            _cable = route.Cable;
            input.Pulse(0.6f, 0.02f);
        }

        private void AddWirePoint(Vector3 cursor, ElectricFixture terminal)
        {
            if (_pts.Count == 0)
            {
                _pts.Add(cursor);
                if (terminal != null)
                {
                    _startFixtureId = FixtureId(terminal);
                    // circuit default follows the fixture it starts from: switches feed
                    // lighting 1.5 mm², outlets 2.5 mm² — the Cable row can still override
                    _cable = Cable.DefaultFor(terminal.Kind);
                }
                input.Pulse(0.6f, 0.02f);
                return;
            }

            Vector3 last = _pts[_pts.Count - 1];
            if (Vector3.Distance(cursor, last) < ElectricalDefaults.MinPointStep)
            {
                input.Pulse(0.2f, 0.01f);   // double click — ignored, not garbage geometry
                return;
            }

            if (_ortho)
            {
                WireMath.OrthoElbow(last, cursor, _orthoScratch);
                _pts.AddRange(_orthoScratch);
            }
            _pts.Add(cursor);
            input.Pulse(0.6f, 0.02f);

            // any terminal ends the run — panel, junction box or another outlet
            if (terminal != null) FinishRoute(terminal);
        }

        private void FinishRoute(ElectricFixture endTerminal)
        {
            if (_continued != null)
            {
                // Continuation stays the SAME route (#81) — one undoable edit; finishing
                // with no new points is a no-op, the route is simply put back down.
                var contSel = _continued.GetComponent<Selectable>();
                if (_pts.Count > _continuedBefore.Count && sceneModel != null
                    && contSel != null && contSel.IsAlive)
                {
                    sceneModel.History.Execute(new RouteExtendCommand(_continued,
                        _continuedBefore, _continuedStartId, _continuedEndId, _continuedCable,
                        new List<Vector3>(_pts), _startFixtureId,
                        endTerminal != null ? FixtureId(endTerminal) : null, _cable));
                    input.Pulse(0.6f, 0.03f);
                }
                CancelRoute();
                return;
            }

            if (_pts.Count >= 2 && wirePrefab != null && sceneModel != null)
            {
                var route = Instantiate(wirePrefab, transform);
                if (!route.gameObject.activeSelf) route.gameObject.SetActive(true);
                if (route.Build(_pts, _cable))
                {
                    route.StartFixtureId = _startFixtureId;
                    route.EndFixtureId = endTerminal != null ? FixtureId(endTerminal) : null;
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

        /// <summary>A run of two or more points survives any exit (mode switch, tool switch)
        /// as a real route — accidental deactivation must not eat drawn work.</summary>
        private void FinishOrCancelRoute()
        {
            if (_pts.Count >= 2) FinishRoute(null);
            else CancelRoute();
        }

        private void DrawWirePreview(Vector3 cursor, bool valid)
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
                    WireMath.OrthoElbow(last, cursor, _orthoScratch);
                    _previewPts.AddRange(_orthoScratch);
                }
                _previewPts.Add(cursor);
            }
            previewLine.enabled = true;
            previewLine.positionCount = _previewPts.Count;
            for (int i = 0; i < _previewPts.Count; i++) previewLine.SetPosition(i, _previewPts[i]);
        }

        /// <summary>Teleport moves the model; a half-drawn wire run is model data too —
        /// the tape-measure lesson. Called from the TeleportCommand.</summary>
        public void ShiftDraft(Vector3 delta)
        {
            for (int i = 0; i < _pts.Count; i++) _pts[i] += delta;
            if (_continuedBefore != null)
                for (int i = 0; i < _continuedBefore.Count; i++) _continuedBefore[i] += delta;
        }

        private static string FixtureId(ElectricFixture fx)
        {
            var sel = fx.GetComponent<Selectable>();
            return sel != null ? sel.Id : null;
        }

        // ---- settings: ONE tabbed schema (design/20 §2.12) — the schema-swap hack is gone,
        // sub-modes are visible stateful tabs with the outlet/switch/wire/breaker icons ----

        private SettingsSchema BuildSchema()
        {
            var outlet = new SettingsSchema()
                .Stepper("posts", "Posts", () => $"{_posts}",
                    () => _posts = Mathf.Max(1, _posts - 1),
                    () => _posts = Mathf.Min(ElectricalDefaults.MaxPosts, _posts + 1))
                .Slider("oh", "Height", ElectricalDefaults.MinOutletHeight,
                    ElectricalDefaults.MaxMountHeight, ElectricalDefaults.HeightStep,
                    () => _outletHeight,
                    v => _outletHeight = ClampHeight(v, ElectricalDefaults.MinOutletHeight),
                    (_, v) => _outletHeight = ClampHeight(v, ElectricalDefaults.MinOutletHeight),
                    () => $"{_outletHeight * 100f:0} cm", displayScale: 100f)
                .Swatch("ofinish", "Finish", FixtureVariants,
                    () => _blackVariant ? 1 : 0, i => _blackVariant = i == 1);
            var sw = new SettingsSchema()
                .Stepper("keys", "Keys", () => $"{_keys}",
                    () => _keys = Mathf.Max(1, _keys - 1),
                    () => _keys = Mathf.Min(ElectricalDefaults.MaxKeys, _keys + 1))
                .Slider("sh", "Height", ElectricalDefaults.MinSwitchHeight,
                    ElectricalDefaults.MaxMountHeight, ElectricalDefaults.HeightStep,
                    () => _switchHeight,
                    v => _switchHeight = ClampHeight(v, ElectricalDefaults.MinSwitchHeight),
                    (_, v) => _switchHeight = ClampHeight(v, ElectricalDefaults.MinSwitchHeight),
                    () => $"{_switchHeight * 100f:0} cm", displayScale: 100f)
                .Swatch("sfinish", "Finish", FixtureVariants,
                    () => _blackVariant ? 1 : 0, i => _blackVariant = i == 1);
            // v2: no "Ceiling off" row — runs are routed by hand, the wire goes where
            // the user clicks and nowhere else (headset feedback 2026-08-10)
            var wire = new SettingsSchema()
                .Segmented("cable", "Cable",
                    new[] { Cable.Label(CableType.C3x15), Cable.Label(CableType.C3x25) },
                    () => (int)_cable, i => _cable = (CableType)i)
                .Segmented("routing", "Route", new[] { "Ortho", "Free" },
                    () => _ortho ? 0 : 1, i => _ortho = i == 0);
            var box = new SettingsSchema()
                .Readout("jmount", "Mount", () => "Wall / ceiling")
                .Swatch("jfinish", "Finish", FixtureVariants,
                    () => _blackVariant ? 1 : 0, i => _blackVariant = i == 1);
            var panel = new SettingsSchema()
                .Slider("res", "Reserve", 0f, ElectricalDefaults.MaxReservePercent,
                    ElectricalDefaults.ReserveStep,
                    () => _reserve,
                    v => _reserve = Mathf.Clamp(Mathf.RoundToInt(v), 0, ElectricalDefaults.MaxReservePercent),
                    (_, v) => _reserve = Mathf.Clamp(Mathf.RoundToInt(v), 0, ElectricalDefaults.MaxReservePercent),
                    () => $"{_reserve} %")
                .Swatch("pfinish", "Finish", FixtureVariants,
                    () => _blackVariant ? 1 : 0, i => _blackVariant = i == 1)
                .Toggle("popen", "Door open", () => _panelOpen, v => _panelOpen = v);

            return SettingsSchema.Tabbed(
                new[] { "Outlet", "Switch", "Wire", "Box", "Panel" },
                () => (int)_mode, SetMode, outlet, sw, wire, box, panel);
        }

        private void SetMode(int mode)
        {
            FinishOrCancelRoute();               // switching modes never eats a drawn run
            _mode = (SubMode)Mathf.Clamp(mode, 0, 4);
        }

        private static float ClampHeight(float value, float min) =>
            Mathf.Clamp(value, min, ElectricalDefaults.MaxMountHeight);
    }

    /// <summary>
    /// Continuing a route from its free end (#81): the whole continuation — appended
    /// points, new end attachment, possibly a cable change — is ONE undo entry that
    /// puts the previous polyline and attachments back.
    /// </summary>
    public sealed class RouteExtendCommand : ICommand, ISelectableCommand
    {
        private readonly WireRoute _route;
        private readonly List<Vector3> _before, _after;
        private readonly string _beforeStart, _beforeEnd, _afterStart, _afterEnd;
        private readonly CableType _beforeCable, _afterCable;

        public RouteExtendCommand(WireRoute route,
            List<Vector3> before, string beforeStart, string beforeEnd, CableType beforeCable,
            List<Vector3> after, string afterStart, string afterEnd, CableType afterCable)
        {
            _route = route;
            _before = before; _beforeStart = beforeStart; _beforeEnd = beforeEnd;
            _beforeCable = beforeCable;
            _after = after; _afterStart = afterStart; _afterEnd = afterEnd;
            _afterCable = afterCable;
        }

        public string Name => "Extend wire";
        public ISelectable Target => _route != null ? _route.GetComponent<Selectable>() : null;

        public void Do() => Apply(_after, _afterStart, _afterEnd, _afterCable);
        public void Undo() => Apply(_before, _beforeStart, _beforeEnd, _beforeCable);

        private void Apply(List<Vector3> pts, string startId, string endId, CableType cable)
        {
            if (_route == null) return;
            var sel = _route.GetComponent<Selectable>();
            if (sel == null || !sel.IsAlive) return;
            _route.Build(new List<Vector3>(pts), cable);
            _route.StartFixtureId = startId;
            _route.EndFixtureId = endId;
        }
    }
}
