using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>
    /// Plan-view polygon maths for floor slabs (docs/design/17-floor-outline.md, step C1).
    ///
    /// Works in the XZ plane — everything here is a floor plan seen from above; Y is carried
    /// along but never decides anything. Pure C#, so it is unit-testable without a scene.
    ///
    /// The triangulator is ear clipping: floors are small polygons (tens of points at most) that
    /// rebuild on edit, not per frame, so simplicity beats an O(n log n) sweep here.
    /// </summary>
    public static class Polygon
    {
        private const float Eps = 1e-6f;

        /// <summary>Twice the signed area in XZ. Positive = counter-clockwise seen from above.</summary>
        public static float SignedArea2(IReadOnlyList<Vector3> pts)
        {
            if (pts == null || pts.Count < 3) return 0f;
            float sum = 0f;
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
                sum += (pts[j].x * pts[i].z) - (pts[i].x * pts[j].z);
            return sum;
        }

        /// <summary>Area in square metres (always positive).</summary>
        public static float Area(IReadOnlyList<Vector3> pts) => Mathf.Abs(SignedArea2(pts)) * 0.5f;

        public static bool IsClockwise(IReadOnlyList<Vector3> pts) => SignedArea2(pts) < 0f;

        /// <summary>
        /// Copy the outline in counter-clockwise order. Winding decides which way the generated
        /// faces point, and a floor drawn clockwise would end up with its top face looking down —
        /// a pick would then land on the underside (coding rule 1.1).
        /// </summary>
        public static List<Vector3> ToCounterClockwise(IReadOnlyList<Vector3> pts)
        {
            var res = new List<Vector3>(pts);
            if (IsClockwise(pts)) res.Reverse();
            return res;
        }

        /// <summary>
        /// Drop points that repeat or sit on a straight run — MR controllers produce both, and
        /// a duplicate point makes ear clipping produce degenerate triangles (rule 1.3).
        /// </summary>
        public static List<Vector3> Clean(IReadOnlyList<Vector3> pts, float mergeDistance = 0.005f)
        {
            var res = new List<Vector3>();
            if (pts == null) return res;
            float mergeSqr = mergeDistance * mergeDistance;

            foreach (var p in pts)
            {
                if (res.Count > 0 && FlatSqrDistance(res[res.Count - 1], p) < mergeSqr) continue;
                res.Add(p);
            }
            // the closing edge can also be a duplicate
            while (res.Count > 1 && FlatSqrDistance(res[0], res[res.Count - 1]) < mergeSqr)
                res.RemoveAt(res.Count - 1);
            return res;
        }

        /// <summary>Is the point inside the outline? (ray crossing, XZ)</summary>
        public static bool Contains(IReadOnlyList<Vector3> pts, Vector3 p)
        {
            if (pts == null || pts.Count < 3) return false;
            bool inside = false;
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            {
                bool straddles = (pts[i].z > p.z) != (pts[j].z > p.z);
                if (!straddles) continue;
                float x = (pts[j].x - pts[i].x) * (p.z - pts[i].z) / (pts[j].z - pts[i].z) + pts[i].x;
                if (p.x < x) inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// True when no two non-adjacent edges cross. A figure-of-eight outline cannot be
        /// triangulated sensibly, and silently producing garbage is exactly what rule 1.3
        /// forbids — the caller refuses the shape instead.
        /// </summary>
        public static bool IsSimple(IReadOnlyList<Vector3> pts)
        {
            if (pts == null || pts.Count < 3) return false;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 a1 = pts[i], a2 = pts[(i + 1) % n];
                for (int j = i + 1; j < n; j++)
                {
                    // skip edges that share a vertex
                    if (j == i || (j + 1) % n == i || (i + 1) % n == j) continue;
                    Vector3 b1 = pts[j], b2 = pts[(j + 1) % n];
                    if (SegmentsCross(a1, a2, b1, b2)) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Triangulate a simple polygon by ear clipping. Returns index triples into the given
        /// list, wound counter-clockwise in XZ. Empty list if the outline is unusable.
        /// </summary>
        public static List<int> Triangulate(IReadOnlyList<Vector3> pts)
        {
            var tris = new List<int>();
            if (pts == null || pts.Count < 3) return tris;
            // Ear clipping happily "succeeds" on a crossed outline and returns triangles that
            // overlap. Refuse here rather than relying on every caller to pre-check, or the
            // contract above ("empty if unusable") would be a lie.
            if (!IsSimple(pts)) return tris;

            // work on an index ring in CCW order
            int n = pts.Count;
            var idx = new List<int>(n);
            if (IsClockwise(pts)) for (int i = n - 1; i >= 0; i--) idx.Add(i);
            else for (int i = 0; i < n; i++) idx.Add(i);

            int guard = 0, maxGuard = n * n + 16;
            while (idx.Count > 3 && guard++ < maxGuard)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int i0 = idx[(i + idx.Count - 1) % idx.Count];
                    int i1 = idx[i];
                    int i2 = idx[(i + 1) % idx.Count];

                    if (!IsEar(pts, idx, i0, i1, i2)) continue;

                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                // no ear found: the outline is self-intersecting or degenerate — stop rather
                // than spin, and let the caller see the incomplete result as a refusal
                if (!clipped) return new List<int>();
            }

            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
            return tris;
        }

        // ---- internals ----

        private static bool IsEar(IReadOnlyList<Vector3> pts, List<int> idx, int i0, int i1, int i2)
        {
            Vector3 a = pts[i0], b = pts[i1], c = pts[i2];
            if (Cross2(a, b, c) <= Eps) return false;          // reflex or collinear in CCW ring

            foreach (int k in idx)
            {
                if (k == i0 || k == i1 || k == i2) continue;
                if (PointInTriangle(pts[k], a, b, c)) return false;
            }
            return true;
        }

        /// <summary>Z-component of the cross product in XZ — sign tells the turn direction.</summary>
        private static float Cross2(Vector3 a, Vector3 b, Vector3 c) =>
            (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);

        private static bool PointInTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            float d1 = Cross2(a, b, p), d2 = Cross2(b, c, p), d3 = Cross2(c, a, p);
            bool hasNeg = d1 < -Eps || d2 < -Eps || d3 < -Eps;
            bool hasPos = d1 > Eps || d2 > Eps || d3 > Eps;
            return !(hasNeg && hasPos);
        }

        private static bool SegmentsCross(Vector3 a1, Vector3 a2, Vector3 b1, Vector3 b2)
        {
            float d1 = Cross2(b1, b2, a1), d2 = Cross2(b1, b2, a2);
            float d3 = Cross2(a1, a2, b1), d4 = Cross2(a1, a2, b2);
            return ((d1 > Eps && d2 < -Eps) || (d1 < -Eps && d2 > Eps)) &&
                   ((d3 > Eps && d4 < -Eps) || (d3 < -Eps && d4 > Eps));
        }

        private static float FlatSqrDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
