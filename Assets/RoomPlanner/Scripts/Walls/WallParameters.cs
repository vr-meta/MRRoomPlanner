using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;

namespace RoomPlanner.Walls
{
    /// <summary>
    /// Per-instance parameters of ONE wall, shown in the inspector when it is selected
    /// (docs/design/13-phase-b-wallgraph.md, step B4).
    ///
    /// Sits on the wall view next to <see cref="Wall"/> and <see cref="Selectable"/>; the Select
    /// tool finds it through <see cref="ISettingsProvider"/>, so neither the inspector nor the
    /// Editing assembly needs to know what a wall is (design/14-modularity.md).
    ///
    /// Menu values stay the DEFAULTS for the next wall drawn — editing here changes this wall
    /// only, and every change goes through the undo stack.
    /// </summary>
    [RequireComponent(typeof(Wall))]
    public class WallParameters : MonoBehaviour, ISettingsProvider
    {
        private const float MinThickness = 0.02f, MaxThickness = 1f;
        private const float MinHeight = 0.2f, MaxHeight = 5f;
        private const float MinLength = 0.01f, MaxLength = 100f;

        private Wall _wall;
        private WallGraphRenderer _renderer;
        private SettingsSchema _schema;

        private Wall View => _wall != null ? _wall : _wall = GetComponent<Wall>();
        private WallSegment Segment => View != null ? View.Segment : null;

        private WallGraphRenderer Renderer =>
            _renderer != null ? _renderer : _renderer = GetComponentInParent<WallGraphRenderer>();

        public SettingsSchema GetSettings()
        {
            if (Segment == null) return null;
            // one schema per view: its delegates read the CURRENT segment every time, so the
            // rows stay correct even after the wall is split or rebuilt.
            // v2 widgets: numeric fields commit ONE command per entry (design/20 §2.6),
            // segmented rows replace the blind cycles (§2.3).
            _schema ??= new SettingsSchema()
                .Numeric("wlen", "Length", MinLength, MaxLength,
                    () => Segment?.Length ?? 0f,
                    (_, v) => SetLength(v),
                    () => $"{(Segment?.Length ?? 0f):0.00} m")
                .Numeric("wthk", "Thickness", MinThickness, MaxThickness,
                    () => Segment?.Thickness ?? 0f,
                    (_, v) => SetThickness(v),
                    () => $"{(Segment?.Thickness ?? 0f) * 100f:0} cm", displayScale: 100f)
                .Numeric("wh", "Height", MinHeight, MaxHeight,
                    () => Segment?.Height ?? 0f,
                    (_, v) => SetHeight(v),
                    () => $"{(Segment?.Height ?? 0f) * 100f:0} cm", displayScale: 100f)
                .Segmented("woff", "Offset", new[] { "Outer", "Center", "Inner" },
                    () => Segment != null ? (int)Segment.Offset : 0,
                    i => { if (Segment != null) Apply(WallParamCommand.ForOffset(this, (WallOffsetMode)i)); })
                // NOTE: no "Corner" row. WallMesh always miters (with the ×4 limit) and
                // never read Segment.Join — the row was UI with no effect (audit B8).
                // Bevel/Round live only in the legacy polyline path; bring the row back
                // if they are ever implemented for graph joints.
                .Segmented("wside", "Side", new[] { "Right", "Left" },
                    () => Segment != null && Segment.SideSign < 0f ? 1 : 0,
                    i => { if (Segment != null) Apply(WallParamCommand.ForSide(this, i == 0 ? 1f : -1f)); });
            return _schema;
        }

        // ---- edits (each one undoable) ----

        private void SetThickness(float value)
        {
            var s = Segment;
            if (s == null) return;
            Apply(WallParamCommand.ForThickness(this, Mathf.Clamp(value, MinThickness, MaxThickness)));
        }

        private void SetLength(float value)
        {
            var s = Segment;
            float clamped = Mathf.Clamp(value, MinLength, MaxLength);
            if (s == null || Mathf.Approximately(s.Length, clamped)) return;
            Apply(WallLengthCommand.Create(this, clamped));
        }

        private void SetHeight(float value)
        {
            var s = Segment;
            if (s == null) return;
            Apply(WallParamCommand.ForHeight(this, Mathf.Clamp(value, MinHeight, MaxHeight)));
        }

        private void Apply(ICommand cmd)
        {
            if (cmd == null) return;
            var model = SceneModel.Instance;
            if (model != null) model.History.Execute(cmd);
            else cmd.Do();                 // no history in a bare test rig — still apply
        }

        /// <summary>Push the change into the segment and rebuild this wall and its neighbours.</summary>
        internal void Rebuild()
        {
            var s = Segment;
            if (s == null) return;
            if (Renderer != null) Renderer.RebuildNeighbourhood(s);
            else View.BuildSegment(s);     // standalone view (tests)
        }

        internal string OffsetName() => Segment == null ? "—"
            : Segment.Offset == WallOffsetMode.Outer ? "Outer"
            : Segment.Offset == WallOffsetMode.Center ? "Center" : "Inner";

        internal string JoinName() => Segment == null ? "—"
            : Segment.Join == WallJoin.Miter ? "Miter"
            : Segment.Join == WallJoin.Bevel ? "Bevel" : "Round";

        internal string SideName() => Segment == null ? "—" : (Segment.SideSign >= 0f ? "Right" : "Left");

        internal WallSegment TargetSegment => Segment;
        internal ISelectable Owner => GetComponent<Selectable>();
    }
}
