using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;

namespace RoomPlanner.Floors
{
    /// <summary>
    /// Makes the corners of a slab draggable (step C4, docs/design/17-floor-outline.md).
    ///
    /// Reuses the handle machinery built for wall vertices in Phase B: the Select tool spawns
    /// the visuals, picks them and owns the undo record — this only knows how to move a corner.
    /// </summary>
    [RequireComponent(typeof(Floor))]
    public class FloorHandles : MonoBehaviour, IHandleProvider
    {
        private Floor _floor;
        private Floor Slab => _floor != null ? _floor : _floor = GetComponent<Floor>();

        public int HandleCount => Slab != null ? Slab.Outline.Count : 0;

        public Vector3 GetHandlePosition(int index)
        {
            var slab = Slab;
            if (slab == null || index < 0 || index >= slab.Outline.Count) return Vector3.zero;
            return slab.Outline[index];
        }

        public void PreviewHandle(int index, Vector3 worldPosition)
        {
            // The slab rebuilds fully (it is a handful of triangles); the collider re-cook is
            // the expensive part and Floor already reassigns it only on a real rebuild.
            Slab?.MoveCorner(index, worldPosition);
        }

        public ICommand CommitHandle(int index, Vector3 from, Vector3 to)
        {
            var slab = Slab;
            if (slab == null) return null;
            slab.MoveCorner(index, to);
            if ((to - from).sqrMagnitude < 1e-8f) return null;   // a click, not a drag
            return new FloorCornerMoveCommand(this, index, from, to);
        }

        internal void MoveCorner(int index, Vector3 position) => Slab?.MoveCorner(index, position);
        internal ISelectable Owner => GetComponent<Selectable>();
    }

    /// <summary>One corner drag, start to finish — a single undo entry for the whole gesture.</summary>
    public sealed class FloorCornerMoveCommand : ICommand, ISelectableCommand
    {
        private readonly FloorHandles _handles;
        private readonly int _index;
        private readonly Vector3 _from, _to;

        public FloorCornerMoveCommand(FloorHandles handles, int index, Vector3 from, Vector3 to)
        {
            _handles = handles; _index = index; _from = from; _to = to;
        }

        public string Name => "Move floor corner";
        public ISelectable Target => _handles != null ? _handles.Owner : null;

        public void Do() => Apply(_to);
        public void Undo() => Apply(_from);

        private void Apply(Vector3 p)
        {
            if (_handles == null) return;      // destroyed since the command was recorded
            _handles.MoveCorner(_index, p);
        }
    }
}
