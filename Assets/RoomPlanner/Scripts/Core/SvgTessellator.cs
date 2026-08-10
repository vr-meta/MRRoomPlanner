using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>Flat icon mesh data: vertices in the XY plane, front face toward −Z
    /// (the side UI children face — text sits at negative z offsets on panels).</summary>
    public struct SvgMesh
    {
        public Vector3[] Vertices;
        public int[] Triangles;

        public bool IsEmpty => Vertices == null || Vertices.Length == 0;
    }

    /// <summary>
    /// Turns parsed SVG subpaths into a renderable mesh (design/20 §5). Fill rule is
    /// even-odd: containment depth decides who is solid — even-depth rings are outlines,
    /// odd-depth rings are holes in their immediate parent, a ring inside a hole is an
    /// island (solid again). Triangulation is <see cref="Polygon.TriangulateWithHoles"/>,
    /// the same ear clipping the floor slabs use.
    ///
    /// Output is normalized: a viewBox-sized icon maps to [−0.5, 0.5]² centered at the
    /// origin with SVG's downward y flipped up, so callers scale by the wanted metric
    /// size and nothing else.
    /// </summary>
    public static class SvgTessellator
    {
        /// <summary>Parse + tessellate in one call. Empty mesh on unusable input.</summary>
        public static SvgMesh Build(string pathData, float viewBox = 24f)
            => Tessellate(SvgPath.Parse(pathData), viewBox);

        public static SvgMesh Tessellate(List<List<Vector2>> rings, float viewBox = 24f)
        {
            var empty = new SvgMesh { Vertices = new Vector3[0], Triangles = new int[0] };
            if (rings == null || rings.Count == 0 || viewBox <= 0f) return empty;

            // SVG (x, y-down) → plan-view XZ where Polygon operates: z carries SVG y.
            // The final mapping flips z into +Y, which also flips the triangle winding
            // exactly once — CCW-in-XZ from Polygon lands as front-face-toward-−Z.
            var xz = new List<List<Vector3>>(rings.Count);
            foreach (var ring in rings)
            {
                var r = new List<Vector3>(ring.Count);
                foreach (var p in ring) r.Add(new Vector3(p.x, 0f, p.y));
                xz.Add(r);
            }

            // Containment tests need a point STRICTLY inside each ring: ring vertices may lie
            // exactly on another ring's edge (icons with edge-sharing islands — the guide
            // allows those), where ray-crossing is undefined. The centroid of a ring's first
            // ear triangle is always interior; a ring with no ears is degenerate → refuse.
            int n = xz.Count;
            var inner = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var own = Polygon.Triangulate(xz[i]);
                if (own.Count < 3) return empty;
                inner[i] = (xz[i][own[0]] + xz[i][own[1]] + xz[i][own[2]]) / 3f;
            }

            // Ring i is nested in ring j iff an interior point of i lies in j AND i is the
            // smaller one. The area guard is required: a big ring's interior sample can land
            // inside a small ring nested within it (a sheet's ear centroid falling into its
            // own window cutout), which would misread the sheet as a hole.
            var area = new float[n];
            for (int i = 0; i < n; i++) area[i] = Polygon.Area(xz[i]);

            var depth = new int[n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (i != j && area[i] < area[j] && Polygon.Contains(xz[j], inner[i]))
                        depth[i]++;

            var verts = new List<Vector3>();
            var tris = new List<int>();

            for (int i = 0; i < n; i++)
            {
                if ((depth[i] & 1) != 0) continue;   // holes are consumed by their parent

                // direct children: one level deeper, smaller, and actually inside this ring
                var holes = new List<IReadOnlyList<Vector3>>();
                for (int j = 0; j < n; j++)
                    if (depth[j] == depth[i] + 1 && area[j] < area[i]
                        && Polygon.Contains(xz[i], inner[j]))
                        holes.Add(xz[j]);

                var indices = Polygon.TriangulateWithHoles(xz[i], holes, out var merged);
                if (indices.Count == 0) return empty;   // refuse, don't emit half an icon

                int offset = verts.Count;
                foreach (var v in merged)
                    verts.Add(new Vector3(v.x / viewBox - 0.5f, 0.5f - v.z / viewBox, 0f));
                for (int t = 0; t + 2 < indices.Count; t += 3)
                {
                    // The clipper's unchecked tail triangle can be zero-area (collinear
                    // leftovers) — dead weight on the GPU and a winding-test tripwire.
                    // Threshold 1e-3 in viewBox units²: float noise on a degenerate tail
                    // reaches ~1e-6, while the smallest REAL icon triangle is ~8.8 —
                    // three orders of margin on both sides.
                    Vector3 a = merged[indices[t]], b = merged[indices[t + 1]], c = merged[indices[t + 2]];
                    float cross = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
                    if (Mathf.Abs(cross) < 1e-3f) continue;
                    tris.Add(offset + indices[t]);
                    tris.Add(offset + indices[t + 1]);
                    tris.Add(offset + indices[t + 2]);
                }
            }

            if (tris.Count == 0) return empty;
            return new SvgMesh { Vertices = verts.ToArray(), Triangles = tris.ToArray() };
        }

        /// <summary>Filled area of the mesh in normalized units (1 = full viewBox square).
        /// Test hook: a 24×24 square icon must come out as exactly 1.</summary>
        public static float Area(SvgMesh mesh)
        {
            if (mesh.IsEmpty) return 0f;
            float sum = 0f;
            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                Vector3 a = mesh.Vertices[mesh.Triangles[t]];
                Vector3 b = mesh.Vertices[mesh.Triangles[t + 1]];
                Vector3 c = mesh.Vertices[mesh.Triangles[t + 2]];
                sum += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;
            }
            return sum;
        }
    }
}
