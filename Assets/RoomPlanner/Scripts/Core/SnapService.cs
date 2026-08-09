using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>What a snap latched onto — useful both for feedback and for what to do next.</summary>
    public enum SnapKind { None, Corner, Edge }

    /// <summary>
    /// The shared snapping policy (docs/design/13-phase-b-wallgraph.md, step B6).
    ///
    /// Each tool used to carry its own copy of "find the nearest corner, else the nearest edge",
    /// which drifted apart. The policy lives here once; the CALLER feeds the candidates, so this
    /// stays free of walls, floors and measurements and works for all three.
    ///
    /// A struct with no allocations: it is used inside per-frame cursor code (coding rule 4.1).
    ///
    /// Order: corners beat edges. An endpoint is what people aim for, and without a bias the edge
    /// of the very same wall wins by a millimetre right next to its corner.
    /// </summary>
    public struct SnapFinder
    {
        /// <summary>A corner this much closer-feeling than an edge still wins (metres).</summary>
        public const float CornerBias = 0.02f;

        private float _radius;
        private float _bestScore;

        public bool Found { get; private set; }
        public Vector3 Point { get; private set; }
        public SnapKind Kind { get; private set; }

        /// <summary>Index the caller passed with the winning candidate (-1 if none given).</summary>
        public int Index { get; private set; }

        public static SnapFinder WithRadius(float radius) => new SnapFinder
        {
            _radius = Mathf.Max(0f, radius),
            _bestScore = float.MaxValue,
            Found = false,
            Kind = SnapKind.None,
            Index = -1,
            Point = default,
        };

        /// <summary>Offer a corner/endpoint candidate.</summary>
        public void TryCorner(Vector3 cursor, Vector3 candidate, int index = -1)
        {
            float d = Vector3.Distance(cursor, candidate);
            if (d > _radius) return;
            Consider(d - CornerBias, candidate, SnapKind.Corner, index);
        }

        /// <summary>Offer a segment; the closest point on it becomes the candidate.</summary>
        public void TryEdge(Vector3 cursor, Vector3 a, Vector3 b, int index = -1)
        {
            Vector3 p = MeasureMath.ClosestPointOnSegment(a, b, cursor);
            float d = Vector3.Distance(cursor, p);
            if (d > _radius) return;
            Consider(d, p, SnapKind.Edge, index);
        }

        private void Consider(float score, Vector3 point, SnapKind kind, int index)
        {
            if (Found && score >= _bestScore) return;
            _bestScore = score;
            Point = point;
            Kind = kind;
            Index = index;
            Found = true;
        }

        /// <summary>
        /// Apply the fallbacks a tool wants when nothing was magnetised: angle first (it needs
        /// the previous point), then grid. Returns the point to actually use.
        /// </summary>
        public static Vector3 ApplyFallbacks(Vector3 cursor, bool hasPrevious, Vector3 previous,
            bool angleOn, float angleStepDeg, bool gridOn, float gridSize)
        {
            Vector3 p = cursor;
            if (angleOn && hasPrevious) p = MeasureMath.SnapToAngleXZ(previous, p, angleStepDeg);
            if (gridOn) p = MeasureMath.SnapToGridXZ(p, gridSize);
            return p;
        }
    }
}
