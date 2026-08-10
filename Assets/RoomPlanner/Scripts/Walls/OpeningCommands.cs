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
