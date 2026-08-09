using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;

namespace RoomPlanner.Walls
{
    /// <summary>
    /// Makes a wall's two ends draggable (docs/design/13-phase-b-wallgraph.md, step B5).
    ///
    /// The handles are the graph NODES, so dragging one moves every wall attached to it — the
    /// payoff of the whole phase: pull a corner and both walls follow, pull a T-junction and
    /// all three do.
    /// </summary>
    [RequireComponent(typeof(Wall))]
    public class WallHandles : MonoBehaviour, IHandleProvider
    {
        private Wall _wall;
        private WallGraphRenderer _renderer;

        private Wall View => _wall != null ? _wall : _wall = GetComponent<Wall>();
        private WallSegment Segment => View != null ? View.Segment : null;

        private WallGraphRenderer Renderer =>
            _renderer != null ? _renderer : _renderer = GetComponentInParent<WallGraphRenderer>();

        public int HandleCount => Segment != null ? 2 : 0;

        private WallNode NodeAt(int index)
        {
            var s = Segment;
            if (s == null) return null;
            return index == 0 ? s.A : s.B;
        }

        public Vector3 GetHandlePosition(int index) => NodeAt(index)?.Position ?? Vector3.zero;

        public void PreviewHandle(int index, Vector3 worldPosition)
        {
            var node = NodeAt(index);
            if (node == null) return;
            // keep the node on its own level: dragging is a plan-view operation
            worldPosition.y = node.Position.y;
            MoveNodeAndRebuild(node, worldPosition, deferCollider: true);
        }

        public ICommand CommitHandle(int index, Vector3 from, Vector3 to)
        {
            var node = NodeAt(index);
            if (node == null) return null;
            to.y = from.y;
            MoveNodeAndRebuild(node, to, deferCollider: false);
            if ((to - from).sqrMagnitude < 1e-8f) return null;   // nothing actually moved
            return new WallNodeMoveCommand(this, node, from, to);
        }

        /// <summary>
        /// Move a node and rebuild every wall that hangs off it. During a drag the physics mesh
        /// is left stale on purpose and re-cooked once at the end (coding rule 4.2).
        /// </summary>
        internal void MoveNodeAndRebuild(WallNode node, Vector3 position, bool deferCollider)
        {
            if (node == null) return;
            node.MoveTo(position);

            var r = Renderer;
            for (int i = 0; i < node.Segments.Count; i++)
            {
                var seg = node.Segments[i];
                var view = r != null ? r.ViewOf(seg) : (seg == Segment ? View : null);
                if (view == null) continue;
                view.DeferCollider = deferCollider;
                view.BuildSegment(seg);
                if (!deferCollider)
                {
                    view.DeferCollider = false;
                    view.RefreshCollider();
                }
            }
        }

        internal ISelectable Owner => GetComponent<Selectable>();
    }

    /// <summary>
    /// One node drag, start to finish — a single undo entry for the whole gesture, not one per
    /// frame. Safe on a dead target (coding rules 2.1 / 3.2).
    /// </summary>
    public sealed class WallNodeMoveCommand : ICommand, ISelectableCommand
    {
        private readonly WallHandles _handles;
        private readonly WallNode _node;
        private readonly Vector3 _from, _to;

        public WallNodeMoveCommand(WallHandles handles, WallNode node, Vector3 from, Vector3 to)
        {
            _handles = handles;
            _node = node;
            _from = from;
            _to = to;
        }

        public string Name => "Move wall node";
        public ISelectable Target => _handles != null ? _handles.Owner : null;

        public void Do() => Apply(_to);
        public void Undo() => Apply(_from);

        private void Apply(Vector3 p)
        {
            if (_handles == null || _node == null) return;
            _handles.MoveNodeAndRebuild(_node, p, deferCollider: false);
        }
    }
}
