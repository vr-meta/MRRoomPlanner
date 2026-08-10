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

        public void OnDeactivate()
        {
            if (ghost != null) ghost.enabled = false;
        }

        public void Tick(bool blocked)
        {
            if (pointer == null || input == null || sceneModel == null || walls == null) return;
            if (blocked)
            {
                if (ghost != null) ghost.enabled = false;
                return;
            }

            bool aimed = TryAimWall(out var seg, out var target, out float along, out var hitPoint);
            float sill = TabKind == OpeningKind.Window ? _sill : 0f;
            bool fits = aimed && OpeningMath.CanPlace(seg, along, _width[_tab], sill + _height[_tab]);
            UpdateGhost(aimed, fits, seg, along, sill);

            if (input.ConfirmPressed() && fits)
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
            else if (input.ConfirmPressed())
            {
                input.Pulse(0.2f, 0.01f);   // refusal tick — the ghost is already red
            }

            if (input.ClearPressed())
            {
                int near = aimed ? OpeningMath.NearestOpening(seg, along, DeleteReach) : -1;
                if (near >= 0)
                {
                    sceneModel.History.Execute(
                        new DeleteOpeningCommand(walls, seg, seg.Openings[near], target));
                    input.Pulse(0.6f, 0.02f);
                }
                else if (manager != null) manager.ActivateTool("select");
            }
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

        /// <summary>Rectangle of the future opening on the wall centreline plane.</summary>
        private void UpdateGhost(bool aimed, bool fits, WallSegment seg, float along, float sill)
        {
            if (ghost == null) return;
            if (!aimed) { ghost.enabled = false; return; }

            Vector3 a = seg.A.Position;
            Vector3 dir = (seg.B.Position - a).normalized;
            float half = _width[_tab] * 0.5f;
            float baseY = a.y + seg.BaseHeight;
            Vector3 p0 = a + dir * (along - half) + Vector3.up * (baseY + sill);
            Vector3 p1 = a + dir * (along + half) + Vector3.up * (baseY + sill);
            Vector3 p2 = p1 + Vector3.up * _height[_tab];
            Vector3 p3 = p0 + Vector3.up * _height[_tab];

            ghost.enabled = true;
            ghost.positionCount = 5;
            ghost.SetPosition(0, p0); ghost.SetPosition(1, p1);
            ghost.SetPosition(2, p2); ghost.SetPosition(3, p3);
            ghost.SetPosition(4, p0);
            var c = fits ? UiTokens.Selected : UiTokens.Danger;
            ghost.startColor = c;
            ghost.endColor = c;
        }
    }
}
