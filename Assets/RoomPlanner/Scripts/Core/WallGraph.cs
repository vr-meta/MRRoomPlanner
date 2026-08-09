using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Walls
{
    /// <summary>
    /// A junction or endpoint shared by walls. Walls that meet share ONE node, which is what
    /// makes corners and T-junctions come out right without any CSG: move the node and every
    /// wall attached to it follows. See docs/design/02-walls.md.
    /// </summary>
    public class WallNode
    {
        internal readonly List<WallSegment> segments = new();

        public int Id { get; }
        public Vector3 Position { get; internal set; }

        /// <summary>Walls meeting here. 1 = dead end, 2 = corner, 3+ = T / cross junction.</summary>
        public IReadOnlyList<WallSegment> Segments => segments;
        public int Degree => segments.Count;

        /// <summary>
        /// Deliberately move this node. The property setter stays internal so nobody moves a
        /// node by accident: whoever calls this owes a rebuild of every segment on it.
        /// </summary>
        public void MoveTo(Vector3 position) => Position = position;

        internal WallNode(int id, Vector3 position)
        {
            Id = id;
            Position = position;
        }

        public override string ToString() => $"Node#{Id}({Position.x:0.##},{Position.z:0.##}) deg={Degree}";
    }

    /// <summary>
    /// One wall: the stretch between two nodes, plus its own parameters. Parameters live per
    /// segment (not globally) so a selected wall can be edited without touching its neighbours.
    /// </summary>
    public class WallSegment
    {
        public int Id { get; }
        public WallNode A { get; internal set; }
        public WallNode B { get; internal set; }

        public float Thickness = 0.2f;
        public float Height = 2.7f;
        public float BaseHeight = 0f;
        public WallOffsetMode Offset = WallOffsetMode.Outer;
        public WallJoin Join = WallJoin.Miter;

        /// <summary>
        /// Which side of the centerline the thickness grows on: +1 = along the A→B right
        /// normal, -1 = the other way.
        ///
        /// Deliberately a SIGN, not an "interior point": a world-space reference is re-evaluated
        /// on every rebuild, so an L-shaped run would pick opposite sides for its two legs, and
        /// dragging a node could silently turn a wall inside out. The side is decided once, when
        /// the wall is drawn (<see cref="SetSideFromInterior"/>), and then stays put.
        /// </summary>
        public float SideSign = 1f;

        /// <summary>Decide the side once, from where the user stood when drawing this wall.</summary>
        public void SetSideFromInterior(Vector3 interior)
        {
            Vector3 d = B.Position - A.Position;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-8f) return;
            d.Normalize();
            var rightNormal = new Vector3(d.z, 0f, -d.x);
            SideSign = Vector3.Dot(rightNormal, Midpoint - interior) >= 0f ? 1f : -1f;
        }

        internal WallSegment(int id, WallNode a, WallNode b)
        {
            Id = id;
            A = a;
            B = b;
        }

        public float Length => Vector3.Distance(A.Position, B.Position);
        public Vector3 Midpoint => (A.Position + B.Position) * 0.5f;

        /// <summary>The node at the other end, or null if n does not belong to this segment.</summary>
        public WallNode Other(WallNode n) => n == A ? B : n == B ? A : null;

        public bool Has(WallNode n) => n == A || n == B;

        /// <summary>Unit direction pointing away from n along this segment (XZ plane).</summary>
        public Vector3 DirectionFrom(WallNode n)
        {
            var other = Other(n);
            if (other == null) return Vector3.forward;
            Vector3 d = other.Position - n.Position;
            d.y = 0f;
            return d.sqrMagnitude < 1e-8f ? Vector3.forward : d.normalized;
        }

        /// <summary>Copy the editable parameters from another segment (used when splitting).</summary>
        internal void CopyParamsFrom(WallSegment s)
        {
            Thickness = s.Thickness;
            Height = s.Height;
            BaseHeight = s.BaseHeight;
            Offset = s.Offset;
            Join = s.Join;
            SideSign = s.SideSign;
        }

        public override string ToString() => $"Seg#{Id}({A.Id}->{B.Id}) len={Length:0.##}";
    }

    /// <summary>
    /// The wall graph: nodes + segments and the operations that keep them consistent.
    /// Pure data (no MonoBehaviour, no meshes) so it is unit-testable in RoomPlanner.Core —
    /// meshing lives in Wall.cs, editing in RoomPlanner.Editing.
    ///
    /// Ids are stable for the lifetime of the graph, so save/load and openings (Phase D) can
    /// reference a segment without relying on list order.
    /// </summary>
    public class WallGraph
    {
        /// <summary>Default merge radius for nodes — 5 cm, same order as the snap radius.</summary>
        public const float DefaultTolerance = 0.05f;

        private readonly List<WallNode> _nodes = new();
        private readonly List<WallSegment> _segments = new();
        private int _nextNodeId = 1;
        private int _nextSegmentId = 1;

        public IReadOnlyList<WallNode> Nodes => _nodes;
        public IReadOnlyList<WallSegment> Segments => _segments;

        // ---- nodes ----

        /// <summary>Nearest existing node within tolerance, or null.</summary>
        public WallNode FindNode(Vector3 p, float tolerance = DefaultTolerance)
        {
            WallNode best = null;
            float bestSqr = tolerance * tolerance;
            foreach (var n in _nodes)
            {
                float d = (n.Position - p).sqrMagnitude;
                if (d <= bestSqr)
                {
                    bestSqr = d;
                    best = n;
                }
            }
            return best;
        }

        /// <summary>
        /// Reuse the node at p if one is close enough, otherwise create it. This is what turns
        /// "drew a wall ending on another wall's corner" into a genuinely shared node.
        /// </summary>
        public WallNode SnapOrCreateNode(Vector3 p, float tolerance = DefaultTolerance)
        {
            var existing = FindNode(p, tolerance);
            if (existing != null) return existing;
            var node = new WallNode(_nextNodeId++, p);
            _nodes.Add(node);
            return node;
        }

        /// <summary>Move a node; every attached segment implicitly follows (they share it).</summary>
        public void MoveNode(WallNode n, Vector3 p)
        {
            if (n != null) n.Position = p;
        }

        // ---- segments ----

        public WallSegment FindSegment(WallNode a, WallNode b)
        {
            if (a == null || b == null) return null;
            foreach (var s in a.segments)
                if (s.Other(a) == b) return s;
            return null;
        }

        /// <summary>Shortest wall we accept — below this it is MR controller noise, not intent.</summary>
        public const float MinSegmentLength = 0.001f;   // 1 mm, cf. coding rule 1.3

        /// <summary>
        /// Connect two nodes. Returns the existing segment if they are already connected
        /// (so drawing over the same wall twice doesn't duplicate it), and null for a
        /// degenerate segment — same node, or two distinct nodes that sit on top of each
        /// other (double-click on the trigger). Never silently produces junk geometry.
        /// </summary>
        public WallSegment AddSegment(WallNode a, WallNode b)
        {
            if (a == null || b == null || a == b) return null;
            if ((a.Position - b.Position).sqrMagnitude < MinSegmentLength * MinSegmentLength) return null;
            var existing = FindSegment(a, b);
            if (existing != null) return existing;

            var seg = new WallSegment(_nextSegmentId++, a, b);
            _segments.Add(seg);
            a.segments.Add(seg);
            b.segments.Add(seg);
            return seg;
        }

        /// <summary>
        /// Split a segment at p — this is how a T-junction is made: the wall you snapped onto
        /// gets a new node inserted, and the incoming wall attaches to it.
        /// The original segment keeps its identity as the A-side half.
        /// Returns the node at the split (an existing endpoint if p lands on one — no split then).
        /// </summary>
        public WallNode SplitSegmentAt(WallSegment s, Vector3 p, float tolerance = DefaultTolerance)
        {
            if (s == null) return null;

            Vector3 onSeg = ClosestPointOnSegment(s.A.Position, s.B.Position, p);
            if ((onSeg - s.A.Position).sqrMagnitude <= tolerance * tolerance) return s.A;
            if ((onSeg - s.B.Position).sqrMagnitude <= tolerance * tolerance) return s.B;

            var mid = new WallNode(_nextNodeId++, onSeg);
            _nodes.Add(mid);

            var far = s.B;
            // s becomes A--mid
            far.segments.Remove(s);
            s.B = mid;
            mid.segments.Add(s);

            // new segment mid--far, same parameters
            var tail = new WallSegment(_nextSegmentId++, mid, far);
            tail.CopyParamsFrom(s);
            _segments.Add(tail);
            mid.segments.Add(tail);
            far.segments.Add(tail);

            return mid;
        }

        /// <summary>Remove a segment and drop any node left with nothing attached.</summary>
        public bool RemoveSegment(WallSegment s)
        {
            if (s == null || !_segments.Remove(s)) return false;
            s.A.segments.Remove(s);
            s.B.segments.Remove(s);
            PruneIfOrphan(s.A);
            PruneIfOrphan(s.B);
            return true;
        }

        private void PruneIfOrphan(WallNode n)
        {
            if (n.Degree == 0) _nodes.Remove(n);
        }

        // ---- queries (used by the mesh builder to shape joints) ----

        public IReadOnlyList<WallSegment> SegmentsAt(WallNode n) =>
            n != null ? n.Segments : (IReadOnlyList<WallSegment>)System.Array.Empty<WallSegment>();

        /// <summary>
        /// Other segments meeting s at node n — the ones its joint must account for.
        /// Non-allocating: the caller owns the buffer (coding rule 4.1).
        /// </summary>
        public void NeighborsOf(WallSegment s, WallNode n, List<WallSegment> results)
        {
            results.Clear();
            if (s == null || n == null || !s.Has(n)) return;
            foreach (var other in n.segments)
                if (other != s) results.Add(other);
        }

        /// <summary>Allocating convenience overload — tests and one-off calls only.</summary>
        public List<WallSegment> NeighborsOf(WallSegment s, WallNode n)
        {
            var res = new List<WallSegment>();
            NeighborsOf(s, n, res);
            return res;
        }

        public void Clear()
        {
            _nodes.Clear();
            _segments.Clear();
            // ids keep counting up: never reuse an id a saved file might still reference
        }

        internal static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-10f) return a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            return a + ab * t;
        }
    }
}
