using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Measure;
using RoomPlanner.Tools;

namespace RoomPlanner.Walls
{
    /// <summary>
    /// Openings tool ("Open", design/03 v1, audit F1): aim at a wall, the ghost frame
    /// shows where the door/window/garage door will sit and whether it fits (piers,
    /// header, overlaps — Core/OpeningMath); trigger places it as ONE undo entry.
    /// B near an existing opening deletes it (undo-able); B on empty space = Esc to
    /// Select. Per-instance editing of a placed opening is v2 (needs a child
    /// Selectable) — sizes are the tool's defaults, per kind.
    /// </summary>
    public class OpeningsController : MonoBehaviour, ITool
    {
        [SerializeField] private PointerProvider pointer;
        [SerializeField] private MeasureInput input;
        [SerializeField] private ToolManager manager;
        [SerializeField] private SceneModel sceneModel;
        [SerializeField] private WallGraphRenderer walls;
        [SerializeField] private LineRenderer ghost;      // rectangle on the wall face
        [SerializeField] private Transform reticle;       // shared aim point (#47 — aiming was blind)

        /// <summary>Aim-to-delete reach along the wall, metres (design/03 v1).</summary>
        private const float DeleteReach = 0.25f;

        private int _tab;                                  // 0 Door · 1 Window · 2 Garage
        // defaults per kind, metres (design/03): door 85×210, window 120×140 sill 90,
        // garage 250×210. Stored per tab so switching kinds keeps each one's numbers.
        private readonly float[] _width = { 0.85f, 1.2f, 2.5f };
        private readonly float[] _height = { 2.1f, 1.4f, 2.1f };
        private float _sill = 0.9f;                        // window only
        private SettingsSchema _settings;
        private int _nextOpeningId = 1000;                 // hand-placed; import uses low ids

        public string Id => "openings";
        public string PaletteLabel => "Open";
        public string IconId => "door-window";

        private static readonly (float minW, float maxW, float minH, float maxH)[] Limits =
        {
            (0.6f, 1.2f, 1.8f, 2.4f),    // door
            (0.4f, 3.0f, 0.4f, 2.4f),    // window
            (1.8f, 5.0f, 1.8f, 3.0f),    // garage
        };

        private OpeningKind TabKind => _tab == 0 ? OpeningKind.Door
            : _tab == 1 ? OpeningKind.Window : OpeningKind.Garage;

        public SettingsSchema GetSettings()
        {
            if (_settings == null)
            {
                SettingsSchema Page(int t)
                {
                    var p = new SettingsSchema()
                        .Readout($"how{t}", "How to", () => "aim a wall · Trigger = place · B = delete")
                        .Numeric($"w{t}", "Width", Limits[t].minW, Limits[t].maxW,
                            () => _width[t],
                            (_, v) => _width[t] = Mathf.Clamp(v, Limits[t].minW, Limits[t].maxW),
                            () => $"{_width[t] * 100f:0} cm", displayScale: 100f)
                        .Numeric($"h{t}", "Height", Limits[t].minH, Limits[t].maxH,
                            () => _height[t],
                            (_, v) => _height[t] = Mathf.Clamp(v, Limits[t].minH, Limits[t].maxH),
                            () => $"{_height[t] * 100f:0} cm", displayScale: 100f);
                    if (t == 1)
                        p.Numeric("sill", "Sill", 0f, 2f,
                            () => _sill,
                            (_, v) => _sill = Mathf.Clamp(v, 0f, 2f),
                            () => $"{_sill * 100f:0} cm", displayScale: 100f);
                    return p;
                }
                _settings = SettingsSchema.Tabbed(
                    new[] { "Door", "Window", "Garage" },
                    () => _tab, i => _tab = Mathf.Clamp(i, 0, 2),
                    Page(0), Page(1), Page(2));
            }
            return _settings;
        }

        public void OnActivate() { }

        // Dragging an existing opening along its wall (headset feedback: "no idea how
        // to move a placed door"). One undoable command per gesture, recorded on release.
        private WallSegment _dragSeg;
        private Selectable _dragTarget;
        private WallOpening _dragOpening;
        private float _dragStartAlong;

        public void OnDeactivate()
        {
            EndOpeningDrag(record: true);
            if (ghost != null) ghost.enabled = false;
            if (reticle != null) reticle.gameObject.SetActive(false);
        }

        public void Tick(bool blocked)
        {
            if (pointer == null || input == null || sceneModel == null || walls == null) return;
            if (blocked)
            {
                EndOpeningDrag(record: true);
                if (ghost != null) ghost.enabled = false;
                if (reticle != null) reticle.gameObject.SetActive(false);
                return;
            }

            bool aimed = TryAimWall(out var seg, out var target, out float along, out var hitPoint);
            if (reticle != null)
            {
                reticle.gameObject.SetActive(aimed);
                if (aimed) reticle.position = hitPoint;
            }

            // --- gesture in progress: slide the opening along its wall ---
            if (_dragOpening != null)
            {
                if (input.ConfirmHeld())
                {
                    if (aimed && seg == _dragSeg)
                    {
                        float len = seg.Length;
                        float half = _dragOpening.Width * 0.5f;
                        float c = Mathf.Clamp(along,
                            half + OpeningMath.MinPier, len - half - OpeningMath.MinPier);
                        _dragOpening.AlongFraction = c / len;
                        walls.RebuildSegment(seg);
                        ShowGhost(seg, c, _dragOpening.Width, _dragOpening.Height,
                            _dragOpening.SillHeight, UiTokens.Selected, hitPoint);
                    }
                    return;
                }
                EndOpeningDrag(record: true);
                return;
            }

            int near = aimed ? OpeningMath.NearestOpening(seg, along, DeleteReach) : -1;

            if (near >= 0)
            {
                // Existing opening under the aim: outline IT (mint) — trigger drags it,
                // B deletes it. Placement only happens clear of existing openings.
                var op = seg.Openings[near];
                ShowGhost(seg, op.AlongFraction * seg.Length, op.Width, op.Height,
                    op.SillHeight, UiTokens.Selected, hitPoint);

                if (input.ConfirmPressed())
                {
                    _dragSeg = seg;
                    _dragTarget = target;
                    _dragOpening = op;
                    _dragStartAlong = op.AlongFraction;
                    input.Pulse(0.3f, 0.01f);
                }
                if (input.ClearPressed())
                {
                    sceneModel.History.Execute(new DeleteOpeningCommand(walls, seg, op, target));
                    input.Pulse(0.6f, 0.02f);
                }
                return;
            }

            float sill = TabKind == OpeningKind.Window ? _sill : 0f;
            bool fits = aimed && OpeningMath.CanPlace(seg, along, _width[_tab], sill + _height[_tab]);
            if (aimed)
                ShowGhost(seg, along, _width[_tab], _height[_tab], sill,
                    fits ? UiTokens.Selected : UiTokens.Danger, hitPoint);
            else if (ghost != null) ghost.enabled = false;

            if (input.ConfirmPressed())
            {
                if (fits)
                {
                    var opening = new WallOpening
                    {
                        Id = _nextOpeningId++,
                        AlongFraction = along / seg.Length,
                        Width = _width[_tab],
                        Height = _height[_tab],
                        SillHeight = sill,
                        Kind = TabKind,
                    };
                    sceneModel.History.Execute(new CreateOpeningCommand(walls, seg, opening, target));
                    input.Pulse(0.6f, 0.02f);
                }
                else input.Pulse(0.2f, 0.01f);   // refusal tick — the ghost is already red
            }

            if (input.ClearPressed() && manager != null) manager.ActivateTool("select");
        }

        /// <summary>Release/settle the opening drag; revert when the new spot is invalid.</summary>
        private void EndOpeningDrag(bool record)
        {
            if (_dragOpening == null) return;
            var op = _dragOpening;
            var seg = _dragSeg;
            var target = _dragTarget;
            _dragOpening = null;
            _dragSeg = null;
            _dragTarget = null;
            if (seg == null) return;

            float len = seg.Length;
            bool valid = OpeningMath.CanPlace(seg, op.AlongFraction * len, op.Width,
                op.SillHeight + op.Height, ignore: op);
            if (!valid || !record || Mathf.Abs(op.AlongFraction - _dragStartAlong) < 1e-4f)
            {
                if (!valid) input.Pulse(0.2f, 0.01f);
                op.AlongFraction = _dragStartAlong;   // revert (or nothing really moved)
                walls.RebuildSegment(seg);
                return;
            }
            sceneModel.History.Record(
                new OpeningMoveCommand(walls, seg, op, _dragStartAlong, op.AlongFraction, target));
        }

        /// <summary>The wall under the ray, plus the aim position in metres along it.</summary>
        private bool TryAimWall(out WallSegment seg, out Selectable target,
            out float along, out Vector3 hitPoint)
        {
            seg = null; target = null; along = 0f; hitPoint = default;
            if (!sceneModel.TryPick(pointer.GetRay(), out var picked, out hitPoint)) return false;
            if (picked is not Selectable s || s.Kind != SelectableKind.Wall) return false;
            var view = s.GetComponent<Wall>();
            if (view == null || view.Segment == null) return false;
            seg = view.Segment;
            target = s;
            Vector3 a = seg.A.Position;
            Vector3 dir = seg.B.Position - a; dir.y = 0f;
            float len = dir.magnitude;
            if (len < 1e-4f) return false;
            along = Mathf.Clamp(Vector3.Dot(hitPoint - a, dir / len), 0f, len);
            return true;
        }

        /// <summary>Rectangle outline of an opening — the future one (mint/red by
        /// validity) or an existing one under the aim (mint). Drawn on the FACE the ray
        /// hit, nudged 8 mm toward the user: on the centreline plane it sat embedded
        /// inside Center-offset walls and only peeked past the end caps (headset
        /// feedback — "frames only visible at wall edges").</summary>
        private void ShowGhost(WallSegment seg, float centerAlong, float width, float height,
            float sill, Color c, Vector3 hitPoint)
        {
            if (ghost == null || seg == null) return;
            Vector3 a = seg.A.Position;
            Vector3 dir = (seg.B.Position - a).normalized;
            Vector3 toHit = hitPoint - (a + dir * Vector3.Dot(hitPoint - a, dir));
            toHit.y = 0f;
            Vector3 lift = toHit.sqrMagnitude > 1e-6f
                ? toHit + toHit.normalized * 0.008f : Vector3.zero;
            float half = width * 0.5f;
            float baseY = a.y + seg.BaseHeight;
            Vector3 p0 = a + dir * (centerAlong - half) + lift + Vector3.up * (baseY + sill);
            Vector3 p1 = a + dir * (centerAlong + half) + lift + Vector3.up * (baseY + sill);
            Vector3 p2 = p1 + Vector3.up * height;
            Vector3 p3 = p0 + Vector3.up * height;

            ghost.enabled = true;
            ghost.positionCount = 5;
            ghost.SetPosition(0, p0); ghost.SetPosition(1, p1);
            ghost.SetPosition(2, p2); ghost.SetPosition(3, p3);
            ghost.SetPosition(4, p0);
            ghost.startColor = c;
            ghost.endColor = c;
        }
    }
}
