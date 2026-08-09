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

        /// <summary>Copy the ring in clockwise order (hole walls face into the hole).</summary>
        public static List<Vector3> ToClockwise(IReadOnlyList<Vector3> pts)
        {
            var res = new List<Vector3>(pts);
            if (!IsClockwise(pts)) res.Reverse();
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
                if (clipped) continue;

                // No ear. A bridged hole leaves zero-area "spikes" at the seam (the same point
                // appears twice), and those are never ears — drop one without emitting a
                // triangle so the clipper can make progress.
                int degenerate = -1;
                for (int i = 0; i < idx.Count && degenerate < 0; i++)
                {
                    Vector3 a = pts[idx[(i + idx.Count - 1) % idx.Count]];
                    Vector3 b = pts[idx[i]];
                    Vector3 c = pts[idx[(i + 1) % idx.Count]];
                    if (Mathf.Abs(Cross2(a, b, c)) <= Eps || Same(a, c)) degenerate = i;
                }
                if (degenerate >= 0) { idx.RemoveAt(degenerate); continue; }

                // genuinely unusable (self-intersecting): refuse rather than spin
                return new List<int>();
            }

            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
            return tris;
        }

        /// <summary>Inside the outline AND outside every hole — the actual solid area.</summary>
        public static bool ContainsWithHoles(IReadOnlyList<Vector3> outer,
            IReadOnlyList<IReadOnlyList<Vector3>> holes, Vector3 p)
        {
            if (!Contains(outer, p)) return false;
            if (holes == null) return true;
            foreach (var h in holes)
                if (h != null && Contains(h, p)) return false;
            return true;
        }

        /// <summary>Area of the outline minus its holes.</summary>
        public static float AreaWithHoles(IReadOnlyList<Vector3> outer,
            IReadOnlyList<IReadOnlyList<Vector3>> holes)
        {
            float a = Area(outer);
            if (holes == null) return a;
            foreach (var h in holes)
                if (h != null) a -= Area(h);
            return Mathf.Max(0f, a);
        }

        /// <summary>
        /// Triangulate an outline with holes (stairwells, shafts).
        ///
        /// Ear clipping cannot see a hole, so each hole is first BRIDGED into the outer ring: a
        /// seam is cut to a mutually visible outer vertex and traversed both ways, which turns
        /// the whole thing into one simple polygon that ear clipping does understand. The seam's
        /// two coincident edges are harmless — they only ever produce zero-area ears, which the
        /// clipper already skips.
        ///
        /// Indices refer to <paramref name="merged"/>, not to the inputs, because bridging
        /// rewrites the vertex order. Empty result (and an empty merged list) if the shape is
        /// unusable — same refusal contract as <see cref="Triangulate"/>.
        /// </summary>
        public static List<int> TriangulateWithHoles(IReadOnlyList<Vector3> outer,
            IReadOnlyList<IReadOnlyList<Vector3>> holes, out List<Vector3> merged)
        {
            merged = new List<Vector3>();
            var outerCcw = ToCounterClockwise(Clean(outer));
            if (outerCcw.Count < 3 || !IsSimple(outerCcw)) return new List<int>();

            var usable = new List<List<Vector3>>();
            if (holes != null)
            {
                foreach (var h in holes)
                {
                    var ring = Clean(h);
                    if (ring.Count < 3 || !IsSimple(ring)) continue;      // ignore junk holes
                    // holes run the OTHER way round, so the spliced ring stays simple
                    if (!IsClockwise(ring)) ring.Reverse();
                    usable.Add(ring);
                }
            }

            merged = outerCcw;
            if (usable.Count == 0) return Triangulate(merged);

            // Bridge the rightmost hole first: its bridge cannot then be blocked by a hole
            // further right that has not been merged yet.
            usable.Sort((a, b) => MaxX(b).CompareTo(MaxX(a)));

            foreach (var hole in usable)
            {
                if (!BridgeHole(merged, hole, out var spliced)) return new List<int>();
                merged = spliced;
            }
            return Triangulate(merged);
        }

        private static float MaxX(IReadOnlyList<Vector3> pts)
        {
            float m = float.MinValue;
            foreach (var p in pts) m = Mathf.Max(m, p.x);
            return m;
        }

        /// <summary>
        /// Cut the seam: from the hole's rightmost vertex to the nearest outer vertex it can
        /// actually see, then walk the hole and come back. Fails (false) if no vertex is
        /// visible, which means the hole is not properly inside the outline.
        /// </summary>
        private static bool BridgeHole(List<Vector3> ring, List<Vector3> hole, out List<Vector3> spliced)
        {
            spliced = null;

            int m = 0;
            for (int i = 1; i < hole.Count; i++) if (hole[i].x > hole[m].x) m = i;
            Vector3 M = hole[m];

            int best = -1;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < ring.Count; i++)
            {
                float d = FlatSqrDistance(M, ring[i]);
                if (d >= bestSqr) continue;
                if (!SeamIsClear(M, ring[i], ring, hole)) continue;
                bestSqr = d;
                best = i;
            }
            if (best < 0) return false;

            // outer[0..best] + hole starting at M + M again + outer[best] again + the rest
            spliced = new List<Vector3>(ring.Count + hole.Count + 2);
            for (int i = 0; i <= best; i++) spliced.Add(ring[i]);
            for (int k = 0; k < hole.Count; k++) spliced.Add(hole[(m + k) % hole.Count]);
            spliced.Add(M);
            for (int i = best; i < ring.Count; i++) spliced.Add(ring[i]);
            return true;
        }

        /// <summary>The seam must not cross any edge of the ring or of the hole itself.</summary>
        private static bool SeamIsClear(Vector3 a, Vector3 b, List<Vector3> ring, List<Vector3> hole)
        {
            for (int i = 0; i < ring.Count; i++)
                if (SegmentsCross(a, b, ring[i], ring[(i + 1) % ring.Count])) return false;
            for (int i = 0; i < hole.Count; i++)
                if (SegmentsCross(a, b, hole[i], hole[(i + 1) % hole.Count])) return false;

            // and it must run through solid material, not across the hole's own interior
            Vector3 mid = (a + b) * 0.5f;
            return Contains(ring, mid) && !Contains(hole, mid);
        }

        // ---- internals ----

        private static bool IsEar(IReadOnlyList<Vector3> pts, List<int> idx, int i0, int i1, int i2)
        {
            Vector3 a = pts[i0], b = pts[i1], c = pts[i2];
            if (Cross2(a, b, c) <= Eps) return false;          // reflex or collinear in CCW ring

            foreach (int k in idx)
            {
                if (k == i0 || k == i1 || k == i2) continue;
                // Bridging a hole duplicates its seam vertices, so the SAME position appears
                // under two indices. Testing such a twin "inside" the ear would reject every
                // candidate and the whole polygon would fail to triangulate.
                var q = pts[k];
                if (Same(q, a) || Same(q, b) || Same(q, c)) continue;
                if (PointInTriangle(q, a, b, c)) return false;
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

        private static bool Same(Vector3 a, Vector3 b) => FlatSqrDistance(a, b) < 1e-10f;

        private static float FlatSqrDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
