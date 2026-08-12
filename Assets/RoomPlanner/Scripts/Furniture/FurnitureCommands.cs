using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;

namespace RoomPlanner.Furniture
{
    /// <summary>
    /// Rotation of a placed piece, recorded once per gesture (design/27 §3). Position and
    /// yaw are separate commands on purpose: MoveCommand already exists and works for any
    /// selectable, and a drag that only turned the sofa should undo as a turn.
    /// </summary>
    public class FurnitureYawCommand : ICommand, ISelectableCommand
    {
        private readonly ISelectable _target;
        private readonly FurnitureItemView _view;
        private readonly float _from;
        private readonly float _to;

        public FurnitureYawCommand(ISelectable target, FurnitureItemView view, float from, float to)
        {
            _target = target;
            _view = view;
            _from = from;
            _to = to;
        }

        public ISelectable Target => _target;

        // The view is a MonoBehaviour, so the overloaded null-check applies directly here;
        // the interface-typed target still needs IsAlive (rules 12 §2.1).
        private bool Alive => _view != null && (_target == null || _target.IsAlive);

        public string Name => "Rotate";
        public void Do() { if (Alive) _view.SetYaw(_to); }
        public void Undo() { if (Alive) _view.SetYaw(_from); }
    }
}
