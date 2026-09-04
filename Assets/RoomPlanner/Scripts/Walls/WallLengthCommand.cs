using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;

namespace RoomPlanner.Walls
{
    /// <summary>
    /// Changes a segment length by moving its least-connected endpoint. Because the endpoint
    /// is a graph node, junctions remain shared; hosted openings stay on the same segment with
    /// the same along fractions. Before/after node positions make the edit exactly undoable.
    /// </summary>
    public sealed class WallLengthCommand : ICommand, ISelectableCommand
    {
        private readonly WallParameters _parameters;
        private readonly WallNode _node;
        private readonly Vector3 _before;
        private readonly Vector3 _after;

        private WallLengthCommand(WallParameters parameters, WallNode node,
            Vector3 before, Vector3 after)
        {
            _parameters = parameters;
            _node = node;
            _before = before;
            _after = after;
        }

        public static WallLengthCommand Create(WallParameters parameters, float length)
        {
            var segment = parameters != null ? parameters.TargetSegment : null;
            if (segment == null) return null;
            WallNode moving = EndpointToMove(segment);
            WallNode fixedNode = segment.Other(moving);
            if (moving == null || fixedNode == null) return null;
            Vector3 direction = moving.Position - fixedNode.Position;
            if (direction.sqrMagnitude < WallGraph.MinSegmentLength * WallGraph.MinSegmentLength)
                direction = Vector3.right;
            else
                direction.Normalize();
            float safeLength = Mathf.Max(WallGraph.MinSegmentLength, length);
            return new WallLengthCommand(parameters, moving, moving.Position,
                fixedNode.Position + direction * safeLength);
        }

        public static WallNode EndpointToMove(WallSegment segment)
        {
            if (segment == null) return null;
            // Preserve the busier junction. Equal-degree standalone walls extend from A to B.
            return segment.B.Degree <= segment.A.Degree ? segment.B : segment.A;
        }

        public string Name => "Wall Length";
        public ISelectable Target => _parameters != null ? _parameters.Owner : null;
        public void Do() => Set(_after);
        public void Undo() => Set(_before);

        private void Set(Vector3 position)
        {
            if (_parameters == null || _node == null || _parameters.TargetSegment == null) return;
            _node.MoveTo(position);
            _parameters.Rebuild();
        }
    }
}
