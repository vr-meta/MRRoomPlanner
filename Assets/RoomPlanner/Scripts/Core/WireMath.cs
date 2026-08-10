using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Electrical
{
    /// <summary>
    /// Pure polyline math for wire routes (docs/design/19-electrical.md): ortho elbows
    /// between clicks, length, and the procedural tube mesh. No scene dependencies —
    /// unit-testable in EditMode.
    /// </summary>
    public static class WireMath
    {
        /// <summary>Geometry-level merge threshold — collapses double clicks (rule 1.3).</summary>
        public const float MergeDistance = 0.01f;

        public static float PolylineLength(IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count < 2) return 0f;
            float sum = 0f;
            for (int i = 1; i < points.Count; i++)
                sum += Vector3.Distance(points[i - 1], points[i]);
            return sum;
        }

        /// <summary>
        /// Intermediate points for the ortho layout between two placed points: rise/drop
        /// to the run height, travel horizontally, rise/drop to the target. Degenerate
        /// elbows (shorter than MergeDistance) and points collinear with their neighbors
        /// are dropped, so a same-height pair yields a plain straight segment.
        /// The result EXCLUDES prev and next.
        /// </summary>
        public static void OrthoWaypoints(Vector3 prev, Vector3 next, float runY, List<Vector3> result)
        {
            result.Clear();
            // no horizontal travel → a plain vertical drop, never a detour to the run height
            float dx = next.x - prev.x, dz = next.z - prev.z;
            if (Mathf.Sqrt(dx * dx + dz * dz) < MergeDistance) return;
            result.Add(prev);
            result.Add(new Vector3(prev.x, runY, prev.z));
            result.Add(new Vector3(next.x, runY, next.z));
            result.Add(next);
            CollapsePoints(result, MergeDistance);

            // strip interior points that add no travel (lie on the neighbor-to-neighbor line)
            for (int i = result.Count - 2; i > 0; i--)
            {
                Vector3 onSeg = MeasureMath.ClosestPointOnSegment(result[i - 1], result[i + 1], result[i]);
                if (Vector3.Distance(onSeg, result[i]) < MergeDistance) result.RemoveAt(i);
            }

            // hand back interior points only
            if (result.Count > 0) result.RemoveAt(result.Count - 1);
            if (result.Count > 0) result.RemoveAt(0);
        }

        /// <summary>In-place removal of consecutive points closer than mergeDistance
        /// (keeps the first of each cluster, always keeps the last endpoint).</summary>
        public static void CollapsePoints(List<Vector3> points, float mergeDistance)
        {
            if (points == null) return;
            for (int i = points.Count - 1; i > 0; i--)
            {
                if (Vector3.Distance(points[i], points[i - 1]) >= mergeDistance) continue;
                // prefer dropping the interior twin so route endpoints survive
                if (i == points.Count - 1) points.RemoveAt(i - 1);
                else points.RemoveAt(i);
            }
        }

        /// <summary>
        /// Tube mesh along a polyline: every segment is a closed capped cylinder with
        /// <paramref name="sides"/> faces. Segments do not share rings — elbows stay hard
        /// and every quad faces outward regardless of turn angle (rule 1.1). Caps overlap
        /// inside neighboring segments, which is invisible and keeps the solid closed.
        /// UV: u = meters along the route, v = meters around the circumference.
        /// </summary>
        public static void BuildTube(IReadOnlyList<Vector3> points, float radius, int sides,
            List<Vector3> verts, List<int> tris, List<Vector2> uvs)
        {
            verts.Clear(); tris.Clear(); uvs.Clear();
            if (points == null || points.Count < 2 || radius <= 0f || sides < 3) return;

            float circumference = 2f * Mathf.PI * radius;
            float along = 0f;
            for (int s = 1; s < points.Count; s++)
            {
                Vector3 a = points[s - 1], b = points[s];
                Vector3 dir = b - a;
                float len = dir.magnitude;
                if (len < MergeDistance) continue;
                dir /= len;

                // stable frame: any helper not parallel to the segment
                Vector3 helper = Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up;
                Vector3 u = Vector3.Normalize(Vector3.Cross(helper, dir));
                Vector3 w = Vector3.Cross(dir, u);   // u × w == dir (right-handed)

                int ring0 = verts.Count;
                for (int k = 0; k < sides; k++)
                {
                    float ang = k * 2f * Mathf.PI / sides;
                    Vector3 radial = (Mathf.Cos(ang) * u + Mathf.Sin(ang) * w) * radius;
                    verts.Add(a + radial);
                    uvs.Add(new Vector2(along, k / (float)sides * circumference));
                }
                int ring1 = verts.Count;
                for (int k = 0; k < sides; k++)
                {
                    float ang = k * 2f * Mathf.PI / sides;
                    Vector3 radial = (Mathf.Cos(ang) * u + Mathf.Sin(ang) * w) * radius;
                    verts.Add(b + radial);
                    uvs.Add(new Vector2(along + len, k / (float)sides * circumference));
                }
                for (int k = 0; k < sides; k++)
                {
                    int k1 = (k + 1) % sides;
                    // (a[k], a[k1], b[k1], b[k]) faces outward: cross(tangent, dir) = radial
                    tris.Add(ring0 + k); tris.Add(ring0 + k1); tris.Add(ring1 + k1);
                    tris.Add(ring0 + k); tris.Add(ring1 + k1); tris.Add(ring1 + k);
                }

                // caps: start fan faces -dir, end fan faces +dir
                int centerA = verts.Count;
                verts.Add(a); uvs.Add(new Vector2(along, 0f));
                int centerB = verts.Count;
                verts.Add(b); uvs.Add(new Vector2(along + len, 0f));
                for (int k = 0; k < sides; k++)
                {
                    int k1 = (k + 1) % sides;
                    tris.Add(centerA); tris.Add(ring0 + k1); tris.Add(ring0 + k);
                    tris.Add(centerB); tris.Add(ring1 + k); tris.Add(ring1 + k1);
                }
                along += len;
            }
        }
    }
}
