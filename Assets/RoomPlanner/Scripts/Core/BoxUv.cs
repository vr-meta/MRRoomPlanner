using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>
    /// Metric box projection for meshes that arrive without an unwrap — baked IFC Breps
    /// (design/29 §4). The project rule is «1 m of the world = a fixed piece of texture»
    /// (design/04): the UV of a vertex is its position in METRES projected onto the plane
    /// perpendicular to the dominant axis of its normal, so the same finish tiles at the
    /// same scale on a wall, a floor and an imported cabinet.
    ///
    /// Per-VERTEX (not per-triangle) on purpose: splitting every triangle to give it an
    /// exact projection would multiply the vertex count of an already heavy import. Baked
    /// Breps carry their own vertices per face anyway, and extrusion side rings are shared
    /// only between faces of the same orientation class — the averaged normal picks the
    /// same axis for all of them.
    /// </summary>
    public static class BoxUv
    {
        /// <summary>
        /// Fill <paramref name="uvs"/> (cleared first) with the metric box projection of
        /// <paramref name="verts"/>, using area-weighted vertex normals derived from
        /// <paramref name="tris"/>. Degenerate input yields a zero UV, never NaN.
        /// </summary>
        public static void Fill(IReadOnlyList<Vector3> verts, IReadOnlyList<int> tris,
            List<Vector2> uvs)
        {
            if (uvs == null) return;
            uvs.Clear();
            if (verts == null || verts.Count == 0) return;

            var normals = new Vector3[verts.Count];
            if (tris != null)
                for (int i = 0; i + 2 < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    if ((uint)a >= verts.Count || (uint)b >= verts.Count || (uint)c >= verts.Count)
                        continue;
                    // un-normalised cross = 2 × area, so bigger faces weigh more
                    var n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                    normals[a] += n; normals[b] += n; normals[c] += n;
                }

            for (int i = 0; i < verts.Count; i++)
                uvs.Add(Project(verts[i], normals[i]));
        }

        /// <summary>UV of one point under the box projection chosen by its normal.
        /// Y-up faces read the XZ plane, X-facing read ZY, Z-facing read XY — the same
        /// hand as the wall/floor unwraps, so a wood grain never mirrors between them.</summary>
        public static Vector2 Project(Vector3 p, Vector3 normal)
        {
            float ax = Mathf.Abs(normal.x), ay = Mathf.Abs(normal.y), az = Mathf.Abs(normal.z);
            if (ay >= ax && ay >= az) return new Vector2(p.x, p.z);   // top / bottom
            if (ax >= az) return new Vector2(p.z, p.y);               // left / right
            return new Vector2(p.x, p.y);                             // front / back
        }
    }
}
