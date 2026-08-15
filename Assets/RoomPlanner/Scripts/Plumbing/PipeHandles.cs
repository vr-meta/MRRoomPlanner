using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;

namespace RoomPlanner.Plumbing
{
    /// <summary>
    /// Draggable control points of a pipe run — the RouteHandles pattern on PipeRoute.
    /// The Select tool spawns visuals, picks and records undo; this only knows how to
    /// move a bend. Preview skips the collider re-cook (rule 4.2).
    /// </summary>
    [RequireComponent(typeof(PipeRoute))]
    public class PipeHandles : MonoBehaviour, IHandleProvider
    {
        private PipeRoute _route;
        private PipeRoute Route => _route != null ? _route : _route = GetComponent<PipeRoute>();

        public int HandleCount => Route != null ? Route.PointCount : 0;

        public Vector3 GetHandlePosition(int index)
        {
            var r = Route;
            if (r == null || index < 0 || index >= r.PointCount) return Vector3.zero;
            return r.GetPoint(index);
        }

        public void PreviewHandle(int index, Vector3 worldPosition)
        {
            var r = Route;   // explicit Unity null-check, not `?.` (rule 2.1 style)
            if (r != null) r.MovePoint(index, worldPosition, refreshCollider: false);
        }

        public ICommand CommitHandle(int index, Vector3 from, Vector3 to)
        {
            var r = Route;
            if (r == null) return null;
            r.MovePoint(index, to);
            if ((to - from).sqrMagnitude < 1e-8f) return null;   // a click, not a drag
            return new PipeBendMoveCommand(this, index, from, to);
        }

        internal void MoveBend(int index, Vector3 position)
        {
            var r = Route;
            if (r != null) r.MovePoint(index, position);
        }
        internal ISelectable Owner => GetComponent<Selectable>();
    }

    /// <summary>One bend drag, start to finish — a single undo entry for the whole gesture.</summary>
    public sealed class PipeBendMoveCommand : ICommand, ISelectableCommand
    {
        private readonly PipeHandles _handles;
        private readonly int _index;
        private readonly Vector3 _from, _to;

        public PipeBendMoveCommand(PipeHandles handles, int index, Vector3 from, Vector3 to)
        {
            _handles = handles; _index = index; _from = from; _to = to;
        }

        public string Name => "Move pipe bend";
        public ISelectable Target => _handles != null ? _handles.Owner : null;

        public void Do() => Apply(_to);
        public void Undo() => Apply(_from);

        private void Apply(Vector3 p)
        {
            if (_handles == null) return;      // destroyed since the command was recorded
            _handles.MoveBend(_index, p);
        }
    }
}
