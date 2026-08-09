using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Editing
{
    /// <summary>
    /// A command that edits one scene object. Lets <see cref="SceneModel"/> purge commands
    /// whose target has been destroyed for good (their replay would touch a dead component).
    /// </summary>
    public interface ISelectableCommand
    {
        ISelectable Target { get; }
    }

    /// <summary>Move a whole object by a delta (recorded after a live drag; replayed on redo).</summary>
    public class MoveCommand : ICommand, ISelectableCommand
    {
        private readonly ISelectable _target;
        private readonly Vector3 _delta;

        public MoveCommand(ISelectable target, Vector3 delta)
        {
            _target = target;
            _delta = delta;
        }

        public ISelectable Target => _target;

        // _target is interface-typed, so `!= null` alone would pass a destroyed component
        // (Unity's fake-null semantics don't apply through an interface reference).
        private bool Alive => _target != null && _target.IsAlive;

        public string Name => "Move";
        public void Do() { if (Alive) _target.MoveBy(_delta); }
        public void Undo() { if (Alive) _target.MoveBy(-_delta); }
    }

    /// <summary>Delete = hide (SetActive false) so the object can be restored on undo.</summary>
    public class DeleteCommand : ICommand, ISelectableCommand
    {
        private readonly ISelectable _target;

        public DeleteCommand(ISelectable target) => _target = target;

        public ISelectable Target => _target;

        private bool Alive => _target != null && _target.IsAlive;

        public string Name => "Delete";
        public void Do() { if (Alive) _target.SetHidden(true); }
        public void Undo() { if (Alive) _target.SetHidden(false); }
    }
}
