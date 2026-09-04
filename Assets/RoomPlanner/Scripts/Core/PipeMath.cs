using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Electrical;

namespace RoomPlanner.Plumbing
{
    /// <summary>
    /// Pure polyline math of the Plumbing layer (docs/design/30-plumbing.md). Tube
    /// meshes and length reuse WireMath — this file adds what drainage needs on top:
    /// the LOW ortho elbow (mains run along the floor, not the ceiling), the riser-axis
    /// snap and elbow classification for the BOM. No scene dependencies.
    /// </summary>
    public static class PipeMath
    {
        /// <summary>
        /// The single ortho elbow between two placed points, drainage flavor: the
        /// horizontal travel runs at the LOWER of the two heights — mains lie on the
        /// floor and rises stay vertical, the mirror of WireMath.OrthoElbow which
        /// travels along the top. Result EXCLUDES prev and next.
        /// </summary>
        public static void OrthoElbowLow(Vector3 prev, Vector3 next, List<Vector3> result)
        {
            result.Clear();
            float dx = next.x - prev.x, dz = next.z - prev.z;
            if (Mathf.Sqrt(dx * dx + dz * dz) < WireMath.MergeDistance) return;  // vertical pair
            if (Mathf.Abs(next.y - prev.y) < WireMath.MergeDistance) return;     // level pair
            result.Add(next.y < prev.y
                ? new Vector3(prev.x, next.y, prev.z)    // drop first, then travel along the bottom
                : new Vector3(next.x, prev.y, next.z));  // travel along the bottom, then rise
        }

        /// <summary>Closest point on segment [a, b] to p — the riser-axis snap: a pipe
        /// may tee into a riser at any height along it.</summary>
        public static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-10f) return a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            return a + ab * t;
        }

        /// <summary>Turn-angle thresholds: below Angle45Min the bend is treated as
        /// straight, up to Angle45Max it counts as a 45-degree elbow, beyond — 90.</summary>
        public const float Angle45Min = 22.5f;
        public const float Angle45Max = 67.5f;

        /// <summary>Classifies interior bends of a polyline into 45- and 90-degree
        /// elbows by the turn angle between adjacent segments (degenerate segments
        /// are skipped, near-collinear bends count as neither).</summary>
        public static void CountElbows(IReadOnlyList<Vector3> points, out int deg90, out int deg45)
        {
            deg90 = 0; deg45 = 0;
            if (points == null || points.Count < 3) return;
            for (int i = 1; i < points.Count - 1; i++)
            {
                Vector3 dirIn = points[i] - points[i - 1];
                Vector3 dirOut = points[i + 1] - points[i];
                if (dirIn.sqrMagnitude < 1e-10f || dirOut.sqrMagnitude < 1e-10f) continue;
                float turn = Vector3.Angle(dirIn, dirOut);
                if (turn < Angle45Min) continue;
                if (turn <= Angle45Max) deg45++;
                else deg90++;
            }
        }
    }
}
