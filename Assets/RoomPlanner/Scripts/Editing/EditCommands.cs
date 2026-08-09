using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Editing
{
    /// <summary>Move a whole object by a delta (recorded after a live drag; replayed on redo).</summary>
    public class MoveCommand : ICommand
    {
        private readonly ISelectable _target;
        private readonly Vector3 _delta;

        public MoveCommand(ISelectable target, Vector3 delta)
        {
            _target = target;
            _delta = delta;
        }

        public string Name => "Move";
        public void Do() { if (_target != null) _target.MoveBy(_delta); }
        public void Undo() { if (_target != null) _target.MoveBy(-_delta); }
    }

    /// <summary>Delete = hide (SetActive false) so the object can be restored on undo.</summary>
    public class DeleteCommand : ICommand
    {
        private readonly ISelectable _target;

        public DeleteCommand(ISelectable target) => _target = target;

        public string Name => "Delete";
        public void Do() { if (_target != null) _target.SetHidden(true); }
        public void Undo() { if (_target != null) _target.SetHidden(false); }
    }
}
