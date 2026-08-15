using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core.Ifc
{
    /// <summary>
    /// Issue #117: IFC slab outlines follow wall AXES (centerlines), so an imported
    /// slab visibly stops in the middle of the wall it borders — which side depends on
    /// nothing but the direction the spline was drawn in. This normalization extends
    /// every outline edge that lies on a wall axis outward to that wall's FAR face
    /// (half a thickness), deterministically, by offsetting the edge lines and
    /// re-intersecting neighbours. Pure math over the imported model.
    /// </summary>
    public static class SlabWallAlignment
    {
        /// <summary>An edge counts as "on the axis" when both endpoints sit this close.</summary>
        public const float AxisTolerance = 0.03f;

        public static void Apply(ImportedBuilding b)
        {
            if (b == null) return;
            foreach (var slab in b.Slabs)
                AlignOutline(slab.Outline, b.Walls);
        }

        private static void AlignOutline(List<Vector3> outline, List<ImportedWall> walls)
        {
            int n = outline != null ? outline.Count : 0;
            if (n < 3 || walls == null || walls.Count == 0) return;

            // winding sign decides which perpendicular points OUT of the polygon
            float area2 = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = outline[i], c = outline[(i + 1) % n];
                area2 += a.x * c.z - c.x * a.z;
            }
            // CCW (positive shoelace in XZ) keeps the interior LEFT of each directed
            // edge, so outward is the RIGHT perpendicular (d.z, -d.x)
            float outSign = area2 >= 0f ? 1f : -1f;

            var offsets = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 a = outline[i], c = outline[(i + 1) % n];
                foreach (var w in walls)
                {
                    if (w.Path.Count < 2 || w.Thickness <= 0f) continue;
                    Vector3 wa = w.Path[0], wb = w.Path[w.Path.Count - 1];
                    if (DistXZ(a, wa, wb) > AxisTolerance) continue;
                    if (DistXZ(c, wa, wb) > AxisTolerance) continue;
                    offsets[i] = Mathf.Max(offsets[i], w.Thickness * 0.5f);
                }
            }

            bool any = false;
            foreach (var o in offsets) if (o > 0f) { any = true; break; }
            if (!any) return;

            // offset each edge line by its amount, then every vertex is the
            // intersection of its two adjacent (possibly un-moved) edge lines
            var result = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;
                result[i] = IntersectOffsetEdges(outline, n, prev, i, offsets, outSign);
            }
            for (int i = 0; i < n; i++) outline[i] = result[i];
        }

        private static Vector3 IntersectOffsetEdges(List<Vector3> pts, int n,
            int e1, int e2, float[] offsets, float outSign)
        {
            Vector3 p1 = Offset(pts[e1], pts[(e1 + 1) % n], offsets[e1], outSign, out Vector3 d1);
            Vector3 p2 = Offset(pts[e2], pts[(e2 + 1) % n], offsets[e2], outSign, out Vector3 d2);

            // solve p1 + t*d1 = p2 + s*d2 in XZ; near-parallel edges just take the shift
            float denom = d1.x * d2.z - d1.z * d2.x;
            Vector3 shared = pts[e2];
            if (Mathf.Abs(denom) < 1e-6f)
                return shared + OutNormal(d2, outSign) * offsets[e2]
                              + OutNormal(d1, outSign) * offsets[e1];
            Vector3 diff = p2 - p1;
            float t = (diff.x * d2.z - diff.z * d2.x) / denom;
            Vector3 hit = p1 + d1 * t;
            hit.y = shared.y;
            return hit;
        }

        private static Vector3 Offset(Vector3 a, Vector3 b, float amount, float outSign,
            out Vector3 dir)
        {
            dir = b - a;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-10f) dir = Vector3.right;
            dir.Normalize();
            return a + OutNormal(dir, outSign) * amount;
        }

        private static Vector3 OutNormal(Vector3 dir, float outSign) =>
            new Vector3(dir.z, 0f, -dir.x) * outSign;

        private static float DistXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            var p2 = new Vector3(p.x, 0f, p.z);
            a.y = 0f; b.y = 0f;
            Vector3 ab = b - a;
            if (ab.sqrMagnitude < 1e-10f) return Vector3.Distance(p2, a);
            float t = Mathf.Clamp01(Vector3.Dot(p2 - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(a + ab * t, p2);
        }
    }
}
