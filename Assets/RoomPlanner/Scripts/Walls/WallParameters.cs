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
            // rows stay correct even after the wall is split or rebuilt
            _schema ??= new SettingsSchema()
                .Stepper("wthk", "Thickness",
                    () => $"{(Segment?.Thickness ?? 0f) * 100f:0} cm",
                    () => AdjustThickness(-0.02f), () => AdjustThickness(0.02f))
                .Stepper("wh", "Height",
                    () => $"{(Segment?.Height ?? 0f) * 100f:0} cm",
                    () => AdjustHeight(-0.1f), () => AdjustHeight(0.1f))
                .Cycle("woff", "Offset", () => OffsetName(), CycleOffset)
                .Cycle("wjoin", "Corner", () => JoinName(), CycleJoin)
                .Cycle("wside", "Side", () => SideName(), FlipSide);
            return _schema;
        }

        // ---- edits (each one undoable) ----

        private void AdjustThickness(float delta)
        {
            var s = Segment;
            if (s == null) return;
            Apply(WallParamCommand.ForThickness(this, Mathf.Clamp(s.Thickness + delta, MinThickness, MaxThickness)));
        }

        private void AdjustHeight(float delta)
        {
            var s = Segment;
            if (s == null) return;
            Apply(WallParamCommand.ForHeight(this, Mathf.Clamp(s.Height + delta, MinHeight, MaxHeight)));
        }

        private void CycleOffset()
        {
            var s = Segment;
            if (s == null) return;
            Apply(WallParamCommand.ForOffset(this, (WallOffsetMode)(((int)s.Offset + 1) % 3)));
        }

        private void CycleJoin()
        {
            var s = Segment;
            if (s == null) return;
            Apply(WallParamCommand.ForJoin(this, (WallJoin)(((int)s.Join + 1) % 3)));
        }

        /// <summary>Flip which face the thickness grows on — the "I drew it from the wrong side" fix.</summary>
        private void FlipSide()
        {
            var s = Segment;
            if (s == null) return;
            Apply(WallParamCommand.ForSide(this, -s.SideSign));
        }

        private void Apply(WallParamCommand cmd)
        {
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
