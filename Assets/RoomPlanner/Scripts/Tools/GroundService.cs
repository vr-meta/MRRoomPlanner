using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Floors;
using RoomPlanner.Stairs;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// The ground datum and gravity (docs/design/26-ground.md, issue #59). Two jobs:
    ///
    /// • <b>Ground</b> — the level the building stands on, DERIVED from the model (the
    ///   lowest live slab/flight), never stored: teleporting moves the model, so a stored
    ///   level would have to chase it. The virtual ground plane (scan off) is drawn just
    ///   under it; the walkable datum itself is infinite in XZ, so nobody falls into the
    ///   void past the plane's rim.
    /// • <b>Gravity</b> — the surface under your feet must end up AT your feet. The MODEL
    ///   is shifted to the feet in BOTH modes, in discrete settles (threshold + cooldown,
    ///   never per-frame — rule 4.2). The rig is NEVER moved: the feet are the fixed
    ///   Y = 0 datum. The pre-fix version moved the rig vertically with the scan off,
    ///   which turned the foot level into a moving target — every teleport then
    ///   re-anchored the model to the drifted level and the user ended up trapped
    ///   between storeys (#65).
    ///
    /// Gravity stays OUT of the undo history on purpose: recorded, undoing a settle would
    /// hang the user in the air again and gravity would settle back in the same frame.
    /// It is navigation, like the smooth walk — not an edit.
    /// </summary>
    public class GroundService : MonoBehaviour
    {
        [SerializeField] private SceneModel sceneModel;
        [SerializeField] private Material groundMat;

        /// <summary>Below the datum so slab tops never z-fight with the plane.</summary>
        public const float PlaneDrop = 0.02f;
        public const float PlaneSize = 300f;

        /// <summary>A ledge taller than a step but shorter than a person blocks walking —
        /// that is what keeps you out of the solid body of a stair flight.</summary>
        public const float BlockHeadroom = 1.8f;

        /// <summary>Passthrough settle: ignore anything smaller, and never twice in a row
        /// faster than this (tracking jitter at a slab rim must not start a ping-pong).</summary>
        public const float SettleThreshold = 0.05f;
        public const float SettleCooldown = 0.3f;

        private const float RefreshSeconds = 0.5f;

        private readonly List<WalkSlab> _slabs = new();
        private readonly List<WalkFlight> _flights = new();
        private float _groundY;
        private float _nextRefresh;
        private float _settleAt;
        private int _sig = -1;
        private Vector2 _contentCenter;

        private GameObject _plane;
        private bool _visible;
        private OVRCameraRig _rig;

        /// <summary>The ground level: the walking surface of the model's lowest storey
        /// (0 while there is no content yet).</summary>
        public float GroundY => _groundY;

        /// <summary>Test seam — PlayMode has no OVRCameraRig, so tests hand us the
        /// transform that plays its part (feet level = its Y).</summary>
        public Transform RigOverride { get; set; }

        /// <summary>
        /// Foot level — the fixed datum (#65). The SCANNED floor when the room has one:
        /// the tracking origin can sit tens of cm off the real floor (headset floor
        /// calibration — seen live: controller on the real floor rendered ~20 cm above
        /// our slabs while Meta's Layout, which trusts the scan, sat perfectly), so the
        /// scan wins over the tracking zero. Without a scan — the tracking floor (0).
        /// </summary>
        public float FootY => ScanFloorOverride ?? _scanFloorY ?? 0f;

        /// <summary>Test seam: PlayMode has no MRUK scan to reflect over.</summary>
        public float? ScanFloorOverride { get; set; }

        /// <summary>Show or hide the virtual ground (scan off / passthrough).</summary>
        public void SetVisible(bool on)
        {
            _visible = on;
            EnsurePlane();
            if (_plane != null) _plane.SetActive(on);
            Refresh();
        }

        /// <summary>Force a re-scan of the walkable surfaces on the next tick (import,
        /// project load — anything that rebuilds the scene wholesale).</summary>
        public void Invalidate() => _nextRefresh = 0f;

        /// <summary>
        /// One gravity step, ticked by ToolManager. Returns true with the model shift the
        /// CALLER has to apply (it owns the model and history) — the surface under the
        /// feet comes TO the feet in both modes. The rig is never moved (#65): the feet
        /// are the fixed Y = 0 datum, and a rig some earlier code drifted is snapped back.
        /// </summary>
        public bool Tick(out Vector3 modelShift)
        {
            modelShift = Vector3.zero;
            ProbeScanFloor();
            MaybeRefresh();

            Transform rig = ResolveRig();
            var cam = Camera.main;
            if (cam == null) return false;

            // Restore the datum invariant: nothing may move the rig vertically. A drifted
            // rig (the pre-#65 gravity did that) poisons every later teleport, so it is
            // snapped back the moment it is seen; the settle below re-seats the model.
            if (rig != null && Mathf.Abs(rig.position.y) > 1e-4f)
            {
                Debug.Log($"[Ground] rig Y drifted to {rig.position.y:0.###} — snapped back to 0");
                Vector3 p = rig.position;
                rig.position = new Vector3(p.x, 0f, p.z);
            }

            // Feet are under the HEAD: in passthrough the user walks their real room, so
            // the rig's XZ says nothing about where they are standing.
            Vector3 head = cam.transform.position;
            float footY = FootY;
            float support = GroundMath.Support(_slabs, _flights, _groundY, head.x, head.z, footY);
            LogStanding(head, support);

            float gap = footY - support;          // >0 = hanging in the air, <0 = a step up
            if (Mathf.Abs(gap) < SettleThreshold || Time.time < _settleAt) return false;
            modelShift = new Vector3(0f, gap, 0f);
            _settleAt = Time.time + SettleCooldown;
            Debug.Log($"[Ground] settle dy={gap:0.###} support={support:0.###} " +
                $"ground={_groundY:0.###} at ({head.x:0.#}, {head.z:0.#})");
            return true;
        }

        /// <summary>
        /// May a walker move to this spot? No when a walkable top there sits higher than a
        /// step but lower than a person — the solid flank of a stair flight or a low slab.
        /// Overhead slabs you can duck under are not blocking (their top is out of range).
        /// </summary>
        public bool CanStepTo(float x, float z, float footY)
        {
            float top = GroundMath.Support(_slabs, _flights, _groundY, x, z, footY, BlockHeadroom);
            return top <= footY + GroundMath.StepUp + 1e-3f;
        }

        // ---- the scanned floor: the real-floor datum (#65 follow-up) ----

        private Transform _scanFloor;      // MRUKAnchor with a FLOOR label, once found
        private float? _scanFloorY;
        private float _nextFloorProbe;

        /// <summary>
        /// Find the scanned floor anchor. Reflection by type name — the same soft MRUK
        /// dependency as ToolManager.SetScan. Cheap once found (just reads its Y); probed
        /// every few seconds until then, because the scan spawns asynchronously.
        /// v1 takes the first FLOOR anchor — multi-room selection can come later.
        /// </summary>
        private void ProbeScanFloor()
        {
            if (_scanFloor != null)
            {
                float y = _scanFloor.position.y;
                if (_scanFloorY == null || Mathf.Abs(y - _scanFloorY.Value) > 1e-4f)
                {
                    _scanFloorY = y;
                    Debug.Log($"[Ground] scan floor at Y={y:0.###} — foot datum");
                }
                return;
            }
            if (Time.time < _nextFloorProbe) return;
            _nextFloorProbe = Time.time + 3f;

            foreach (var mb in FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null || mb.GetType().Name != "MRUKAnchor") continue;
                var labelProp = mb.GetType().GetProperty("Label");
                var label = labelProp != null ? labelProp.GetValue(mb) : null;
                if (label == null || !label.ToString().Contains("FLOOR")) continue;
                _scanFloor = mb.transform;
                _scanFloorY = _scanFloor.position.y;
                Debug.Log($"[Ground] scan floor found at Y={_scanFloorY:0.###} — foot datum");
                return;
            }
        }

        /// <summary>The walking surface at a spot — the arc's landing check uses it too.</summary>
        public float SupportAt(float x, float z, float footY)
            => GroundMath.Support(_slabs, _flights, _groundY, x, z, footY);

        // ---- surface cache ----

        private void MaybeRefresh()
        {
            int sig = Signature();
            if (Time.time >= _nextRefresh || sig != _sig)
            {
                _sig = sig;
                Refresh();
            }
        }

        /// <summary>Cheap change detector: every command moves one of these counters, and
        /// the timed refresh below covers live edits that do not (dragging a slab).</summary>
        private int Signature()
        {
            if (sceneModel == null) return 0;
            var h = sceneModel.History;
            return (sceneModel.Items.Count * 397) ^ (h.UndoCount * 31 + h.RedoCount);
        }

        private void Refresh()
        {
            _nextRefresh = Time.time + RefreshSeconds;
            _slabs.Clear();
            _flights.Clear();

            float lowest = float.PositiveInfinity;
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;

            // Active objects only: a slab hidden by DeleteCommand must stop carrying you
            // (rule 2.4) — that IS the "delete what you stand on and settle down" case.
            foreach (var f in FindObjectsByType<Floor>(FindObjectsSortMode.None))
            {
                if (f == null || f.Outline.Count < 3) continue;   // parked prefab template
                _slabs.Add(new WalkSlab(f.Level, f.Outline, f.Holes));
                if (f.Level < lowest) lowest = f.Level;
                foreach (var p in f.Outline)
                {
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.z < minZ) minZ = p.z;
                    if (p.z > maxZ) maxZ = p.z;
                }
            }
            foreach (var s in FindObjectsByType<Stair>(FindObjectsSortMode.None))
            {
                if (s == null || s.Risers <= 0) continue;
                _flights.Add(new WalkFlight(s.Base, s.YawDeg, s.Width, s.Risers,
                    s.RiserHeight, s.TreadDepth));
                if (s.Base.y < lowest) lowest = s.Base.y;
            }

            // No content: keep the ground where it was (0 at startup) instead of letting an
            // empty scene drag the datum around.
            if (!float.IsPositiveInfinity(lowest)) _groundY = lowest;
            if (minX <= maxX) _contentCenter = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);

            UpdatePlane();
        }

        // ---- the visible ground plane (scan off) ----

        private void EnsurePlane()
        {
            if (_plane != null || groundMat == null) return;
            _plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _plane.name = "VirtualGround";
            _plane.transform.localScale = new Vector3(PlaneSize * 0.1f, 1f, PlaneSize * 0.1f);
            _plane.GetComponent<Renderer>().sharedMaterial = groundMat;
            _plane.SetActive(_visible);
        }

        /// <summary>The plane is deliberately parentless — parented to the rig it would
        /// walk along with the user — so it needs an explicit owner (rule 1.5).</summary>
        private void OnDestroy()
        {
            if (_plane == null) return;
            if (Application.isPlaying) Destroy(_plane);
            else DestroyImmediate(_plane);
            _plane = null;
        }

        private void UpdatePlane()
        {
            if (_plane == null) return;
            float y = GroundMath.PlaneLevel(_groundY, FootY, PlaneDrop);
            // IFC files arrive in their own coordinates and can sit far from the origin —
            // follow the content so the 300 m plane is actually under the building.
            _plane.transform.position = new Vector3(_contentCenter.x, y, _contentCenter.y);
        }

        // ---- standing diagnostics (#65 follow-up: "floating ~20 cm above the floor") ----

        private float _lastLoggedHeadOverSupport = float.MinValue;
        private float _nextStandLog;

        /// <summary>Throttled stance log: the head height over the support is effectively
        /// the user's eye height — a value far from human (~1.6–1.9 m) means the TRACKING
        /// floor and the real floor disagree (headset floor calibration), not our math.
        /// Logged on change so logcat tells the story without spamming.</summary>
        private void LogStanding(Vector3 head, float support)
        {
            float over = head.y - support;
            if (Time.time < _nextStandLog
                && Mathf.Abs(over - _lastLoggedHeadOverSupport) < 0.03f) return;
            _nextStandLog = Time.time + 2f;
            _lastLoggedHeadOverSupport = over;
            string origin = "?";
            if (OVRManager.instance != null)
                origin = OVRManager.instance.trackingOriginType.ToString();
            Debug.Log($"[Ground] stand headY={head.y:0.###} over-support={over:0.###} " +
                $"support={support:0.###} ground={_groundY:0.###} foot={FootY:0.###} " +
                $"scanFloor={(_scanFloorY.HasValue ? _scanFloorY.Value.ToString("0.###") : "none")} " +
                $"origin={origin} at ({head.x:0.#}, {head.z:0.#})");
        }

        private Transform ResolveRig()
        {
            if (RigOverride != null) return RigOverride;
            if (_rig == null) _rig = Object.FindFirstObjectByType<OVRCameraRig>();
            return _rig != null ? _rig.transform : null;
        }
    }
}
