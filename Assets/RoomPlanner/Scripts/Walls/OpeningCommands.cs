using RoomPlanner.Core;
using RoomPlanner.Editing;

namespace RoomPlanner.Walls
{
    /// <summary>
    /// Add/remove one opening on a wall segment (Openings tool v1, design/03).
    /// The opening object itself is reused across undo/redo, so its identity (and any
    /// future per-instance edits) survive the round-trip. Target is the wall's
    /// Selectable — SceneModel.Unregister purges these commands with the wall.
    /// </summary>
    public class CreateOpeningCommand : ICommand, ISelectableCommand
    {
        private readonly WallGraphRenderer _walls;
        private readonly WallSegment _segment;
        private readonly WallOpening _opening;
        private readonly ISelectable _target;

        public CreateOpeningCommand(WallGraphRenderer walls, WallSegment segment,
            WallOpening opening, ISelectable target)
        {
            _walls = walls;
            _segment = segment;
            _opening = opening;
            _target = target;
        }

        public ISelectable Target => _target;

        private bool Alive => _target != null && _target.IsAlive && _segment != null;

        public string Name => "Add opening";

        public void Do()
        {
            if (!Alive || _segment.Openings.Contains(_opening)) return;
            _segment.Openings.Add(_opening);
            if (_walls != null) _walls.RebuildSegment(_segment);
        }

        public void Undo()
        {
            if (!Alive) return;
            _segment.Openings.Remove(_opening);
            if (_walls != null) _walls.RebuildSegment(_segment);
        }
    }

    /// <summary>One drag of an opening along its wall = one undo entry (absolute
    /// before/after fractions — replay cannot drift).</summary>
    public class OpeningMoveCommand : ICommand, ISelectableCommand
    {
        private readonly WallGraphRenderer _walls;
        private readonly WallSegment _segment;
        private readonly WallOpening _opening;
        private readonly float _before, _after;
        private readonly ISelectable _target;

        public OpeningMoveCommand(WallGraphRenderer walls, WallSegment segment,
            WallOpening opening, float before, float after, ISelectable target)
        {
            _walls = walls;
            _segment = segment;
            _opening = opening;
            _before = before;
            _after = after;
            _target = target;
        }

        public ISelectable Target => _target;

        private bool Alive => _target != null && _target.IsAlive
            && _segment != null && _segment.Openings.Contains(_opening);

        public string Name => "Move opening";
        public void Do() => Apply(_after);
        public void Undo() => Apply(_before);

        private void Apply(float fraction)
        {
            if (!Alive) return;
            _opening.AlongFraction = fraction;
            if (_walls != null) _walls.RebuildSegment(_segment);
        }
    }

    /// <summary>One inspector edit of an opening's size (issue #50): absolute
    /// before/after triples — replay cannot drift, invalid values never enter
    /// (the caller validates via OpeningMath.CanPlace with ignore: self).</summary>
    public class OpeningEditCommand : ICommand, ISelectableCommand
    {
        private readonly WallGraphRenderer _walls;
        private readonly WallSegment _segment;
        private readonly WallOpening _opening;
        private readonly float _wBefore, _hBefore, _sBefore;
        private readonly float _wAfter, _hAfter, _sAfter;
        private readonly ISelectable _target;

        public OpeningEditCommand(WallGraphRenderer walls, WallSegment segment, WallOpening opening,
            float widthAfter, float heightAfter, float sillAfter, ISelectable target)
        {
            _walls = walls;
            _segment = segment;
            _opening = opening;
            _wBefore = opening.Width; _hBefore = opening.Height; _sBefore = opening.SillHeight;
            _wAfter = widthAfter; _hAfter = heightAfter; _sAfter = sillAfter;
            _target = target;
        }

        public ISelectable Target => _target;

        private bool Alive => _target != null && _target.IsAlive
            && _segment != null && _segment.Openings.Contains(_opening);

        public string Name => "Edit opening";
        public void Do() => Apply(_wAfter, _hAfter, _sAfter);
        public void Undo() => Apply(_wBefore, _hBefore, _sBefore);

        private void Apply(float w, float h, float sill)
        {
            if (!Alive) return;
            _opening.Width = w;
            _opening.Height = h;
            _opening.SillHeight = sill;
            if (_walls != null) _walls.RebuildSegment(_segment);
        }
    }

    /// <summary>Swing/hinge side flip of a door (issue #50) — same absolute pattern.</summary>
    public class OpeningSwingCommand : ICommand, ISelectableCommand
    {
        private readonly WallGraphRenderer _walls;
        private readonly WallSegment _segment;
        private readonly WallOpening _opening;
        private readonly UnityEngine.Vector3 _swingBefore, _hingeBefore, _swingAfter, _hingeAfter;
        private readonly ISelectable _target;

        public OpeningSwingCommand(WallGraphRenderer walls, WallSegment segment, WallOpening opening,
            UnityEngine.Vector3 swingAfter, UnityEngine.Vector3 hingeAfter, ISelectable target)
        {
            _walls = walls;
            _segment = segment;
            _opening = opening;
            _swingBefore = opening.SwingDir; _hingeBefore = opening.HingeDir;
            _swingAfter = swingAfter; _hingeAfter = hingeAfter;
            _target = target;
        }

        public ISelectable Target => _target;

        private bool Alive => _target != null && _target.IsAlive
            && _segment != null && _segment.Openings.Contains(_opening);

        public string Name => "Door swing";
        public void Do() => Apply(_swingAfter, _hingeAfter);
        public void Undo() => Apply(_swingBefore, _hingeBefore);

        private void Apply(UnityEngine.Vector3 swing, UnityEngine.Vector3 hinge)
        {
            if (!Alive) return;
            _opening.SwingDir = swing;
            _opening.HingeDir = hinge;
            if (_walls != null) _walls.RebuildSegment(_segment);
        }
    }

    /// <summary>The mirror: B near an opening removes it, undo puts it back.</summary>
    public class DeleteOpeningCommand : ICommand, ISelectableCommand
    {
        private readonly CreateOpeningCommand _create;

        public DeleteOpeningCommand(WallGraphRenderer walls, WallSegment segment,
            WallOpening opening, ISelectable target)
            => _create = new CreateOpeningCommand(walls, segment, opening, target);

        public ISelectable Target => _create.Target;
        public string Name => "Delete opening";
        public void Do() => _create.Undo();
        public void Undo() => _create.Do();
    }
}
