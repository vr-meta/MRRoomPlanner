using UnityEngine;

namespace RoomPlanner.Walls
{
    /// <summary>
    /// Footprint of ONE wall segment, with each end shaped by whatever else meets it at that
    /// node. This is what lets walls be per-segment (own thickness/height, own openings later)
    /// while corners and T-junctions still come out clean — the joint is derived from the
    /// graph, not from a polyline (docs/design/13-phase-b-wallgraph.md, step B2).
    ///
    /// How a joint is built, uniformly for every node degree:
    ///   • sort the walls meeting at the node by azimuth;
    ///   • between two angularly adjacent walls there is a wedge, bounded by one face of each;
    ///   • those two face lines intersect — that point is the corner shared by both walls.
    /// Degree 1 falls out as a flat cap, degree 2 as the familiar miter, degree 3+ as a T/cross
    /// where each wall is cut against its own neighbours.
    ///
    /// Pure geometry: no MonoBehaviour, no Mesh — unit-testable in RoomPlanner.Core.
    /// </summary>
    public static class WallMesh
    {
        /// <summary>Cap the miter this many times the offset, then fall back to a flat cut.</summary>
        public const float MiterLimit = 4f;

        private const float Eps = 1e-5f;

        /// <summary>
        /// The four ground corners of a segment, always in the segment’s OWN A→B frame:
        /// "Right" is the +right-normal side, "Left" the other — at both ends. (Do not name these
        /// by the outward direction at each node: that flips at B and is a reliable way to get
        /// the sides backwards.)
        /// </summary>
        public struct Footprint
        {
            public Vector3 ARight, ALeft;   // at node A
            public Vector3 BRight, BLeft;   // at node B — SAME frame: "right" is always +rightNormal(A→B)
        }

        /// <summary>Right normal of a flat direction — Cross(up, dir), matching Wall.cs.</summary>
        public static Vector3 RightNormal(Vector3 dir) => new Vector3(dir.z, 0f, -dir.x);

        /// <summary>Unit A→B direction, flattened.</summary>
        public static Vector3 Direction(WallSegment s)
        {
            Vector3 d = s.B.Position - s.A.Position;
            d.y = 0f;
            return d.sqrMagnitude < 1e-8f ? Vector3.forward : d.normalized;
        }

        /// <summary>
        /// How far the two faces sit from the centerline, measured along the A→B right normal
        /// (plus) and against it (minus). Negative/NaN thickness is normalised away rather than
        /// producing junk geometry (coding rule 1.3).
        /// </summary>
        public static void FaceOffsets(WallSegment s, out float plus, out float minus)
        {
            float t = Mathf.Abs(s.Thickness);
            if (float.IsNaN(t) || float.IsInfinity(t)) t = 0f;

            float outer, inner;
            switch (s.Offset)
            {
                case WallOffsetMode.Center: outer = t * 0.5f; inner = t * 0.5f; break;
                case WallOffsetMode.Inner:  outer = 0f;       inner = t;        break;
                default:                    outer = t;        inner = 0f;       break; // Outer
            }

            bool towardPlus = s.SideSign >= 0f;
            plus = towardPlus ? outer : inner;
            minus = towardPlus ? inner : outer;
        }

        /// <summary>
        /// Face offsets relative to the OUTWARD direction at node n (i.e. looking from n along
        /// the wall). At node B the outward direction is reversed, so left and right swap.
        /// </summary>
        public static void OffsetsAt(WallSegment s, WallNode n, out float left, out float right)
        {
            FaceOffsets(s, out float plus, out float minus);
            if (n == s.A) { right = plus; left = minus; }
            else          { right = minus; left = plus; }
        }

        /// <summary>
        /// The two ground corners of segment s at node n, expressed relative to the outward
        /// direction at that node. With no other wall attached this is a flat cap.
        /// </summary>
        public static void CornersAt(WallSegment s, WallNode n, out Vector3 right, out Vector3 left)
        {
            Vector3 u = s.DirectionFrom(n);
            Vector3 r = RightNormal(u);
            OffsetsAt(s, n, out float leftOff, out float rightOff);

            Vector3 rightBase = n.Position + r * rightOff;
            Vector3 leftBase = n.Position - r * leftOff;

            FindAngularNeighbors(s, n, out WallSegment ccw, out WallSegment cw);

            // the wedge on our LEFT is shared with the CCW neighbour's RIGHT face
            left = leftBase;
            if (ccw != null)
            {
                Vector3 un = ccw.DirectionFrom(n);
                OffsetsAt(ccw, n, out _, out float nRight);
                Vector3 nBase = n.Position + RightNormal(un) * nRight;
                left = JoinPoint(n.Position, leftBase, u, nBase, un, leftOff, nRight);
            }

            // ...and the wedge on our RIGHT with the CW neighbour's LEFT face
            right = rightBase;
            if (cw != null)
            {
                Vector3 un = cw.DirectionFrom(n);
                OffsetsAt(cw, n, out float nLeft, out _);
                Vector3 nBase = n.Position - RightNormal(un) * nLeft;
                right = JoinPoint(n.Position, rightBase, u, nBase, un, rightOff, nLeft);
            }
        }

        /// <summary>Full footprint of a segment, both ends joined to their neighbours.</summary>
        public static Footprint BuildFootprint(WallSegment s)
        {
            var f = new Footprint();
            // at A the outward direction is A→B, so "right" is the +right-normal side
            CornersAt(s, s.A, out f.ARight, out f.ALeft);
            // at B the outward direction is reversed, so its local right IS the segment’s left
            CornersAt(s, s.B, out f.BLeft, out f.BRight);
            return f;
        }

        // ---- internals ----

        /// <summary>
        /// The walls immediately counter-clockwise and clockwise from s around node n.
        /// With only two walls present both come out as the same wall — which is exactly
        /// right: a corner is mitred on both of its faces.
        /// </summary>
        private static void FindAngularNeighbors(WallSegment s, WallNode n, out WallSegment ccw, out WallSegment cw)
        {
            ccw = null;
            cw = null;
            if (n == null || n.Degree < 2) return;

            float a0 = Azimuth(s.DirectionFrom(n));
            float bestCcw = float.MaxValue, bestCw = float.MaxValue;

            foreach (var other in n.Segments)
            {
                if (other == s) continue;
                float delta = Mathf.Repeat(Azimuth(other.DirectionFrom(n)) - a0, 360f);
                if (delta < Eps) delta = 360f;      // sitting exactly on us — treat as a full turn
                if (delta < bestCcw) { bestCcw = delta; ccw = other; }
                float back = 360f - delta;
                if (back < bestCw) { bestCw = back; cw = other; }
            }
        }

        private static float Azimuth(Vector3 dir) => Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;

        /// <summary>
        /// Where our face line meets the neighbour's face line. Falls back to a flat cut when
        /// the two are parallel (a straight run-through) or when the miter would spike out past
        /// the limit — a ~180° doubling back must not produce a kilometre-long spike.
        /// </summary>
        private static Vector3 JoinPoint(Vector3 node, Vector3 ourBase, Vector3 ourDir,
            Vector3 theirBase, Vector3 theirDir, float ourOffset, float theirOffset)
        {
            float cross = ourDir.x * theirDir.z - ourDir.z * theirDir.x;
            if (Mathf.Abs(cross) < Eps)
            {
                // parallel faces: a straight continuation (equal thickness → same point anyway)
                return (ourBase + theirBase) * 0.5f;
            }

            Vector3 delta = theirBase - ourBase;
            float t = (delta.x * theirDir.z - delta.z * theirDir.x) / cross;
            Vector3 hit = ourBase + ourDir * t;

            float limit = MiterLimit * Mathf.Max(Mathf.Max(ourOffset, theirOffset), Eps);
            if ((hit - node).sqrMagnitude > limit * limit) return ourBase;
            return hit;
        }
    }
}
