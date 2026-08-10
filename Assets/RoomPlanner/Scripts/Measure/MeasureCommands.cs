using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;

namespace RoomPlanner.Measure
{
    /// <summary>
    /// One vertex drag of a measurement = one undo entry (audit 2026-08-10, 01 §Б2:
    /// dragging used to bypass the history entirely, so X after a drag undid the wrong
    /// thing). Recorded on release with before/after positions — absolute, not a delta,
    /// so replay cannot drift.
    /// </summary>
    public class MeasurePointMoveCommand : ICommand, ISelectableCommand
    {
        private readonly ISelectable _selectable;   // purge anchor (SceneModel.Unregister)
        private readonly Measurement _measurement;
        private readonly bool _endA;
        private readonly Vector3 _before, _after;

        public MeasurePointMoveCommand(ISelectable selectable, Measurement measurement,
            bool endA, Vector3 before, Vector3 after)
        {
            _selectable = selectable;
            _measurement = measurement;
            _endA = endA;
            _before = before;
            _after = after;
        }

        public ISelectable Target => _selectable;

        private bool Alive => _measurement != null && _selectable != null && _selectable.IsAlive;

        public string Name => "Move point";
        public void Do() => Apply(_after);
        public void Undo() => Apply(_before);

        private void Apply(Vector3 p)
        {
            if (!Alive) return;
            if (_endA) _measurement.Set(p, _measurement.PointB);
            else _measurement.Set(_measurement.PointA, p);
        }
    }
}
