using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>
    /// Angle-based normals for imported meshes (issue #132). `Mesh.RecalculateNormals()`
    /// averages EVERY face meeting at a vertex, and IFC extrusions share their ring
    /// vertices between adjacent side quads — so the 90° corner of a square bar came out
    /// as a smooth gradient and the bar read as a cylinder (headset 2026-08-16).
    ///
    /// Flat-shading everything is equally wrong: Revit tessellates curved surfaces into
    /// flat facets, and those need the averaging or a lamp shade turns into a drum. So:
    /// faces around a vertex are grouped into smoothing clusters, and only edges sharper
    /// than the threshold split it.
    ///
    /// Triangle ORDER and COUNT never change — <see cref="Ifc.MepPart"/> ranges address
    /// that list (design/29 §2). Only the vertex list grows.
    /// </summary>
    public static class MeshSmoothing
    {
        /// <summary>Edges sharper than this split the vertex. 40° keeps tessellated
        /// cylinders smooth (their facets meet at a few degrees) and hardens every real
        /// corner.</summary>
        public const float DefaultAngleDeg = 40f;

        /// <summary>
        /// Rebuild <paramref name="verts"/> / <paramref name="tris"/> in place with
        /// angle-split vertices and fill <paramref name="normals"/>. Returns the number of
        /// vertices after the split.
        /// </summary>
        public static int Apply(List<Vector3> verts, List<int> tris, List<Vector3> normals,
            float angleDeg = DefaultAngleDeg)
        {
            if (verts == null || tris == null || normals == null) return 0;
            normals.Clear();
            if (verts.Count == 0 || tris.Count < 3) return verts.Count;

            int faceCount = tris.Count / 3;
            var faceNormal = new Vector3[faceCount];
            for (int f = 0; f < faceCount; f++)
            {
                int a = tris[f * 3], b = tris[f * 3 + 1], c = tris[f * 3 + 2];
                if ((uint)a >= verts.Count || (uint)b >= verts.Count || (uint)c >= verts.Count) continue;
                faceNormal[f] = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            }

            // faces incident to each ORIGINAL vertex
            var incident = new List<int>[verts.Count];
            for (int f = 0; f < faceCount; f++)
                for (int k = 0; k < 3; k++)
                {
                    int v = tris[f * 3 + k];
                    if ((uint)v >= verts.Count) continue;
                    (incident[v] ??= new List<int>()).Add(f);
                }

            float cosLimit = Mathf.Cos(Mathf.Clamp(angleDeg, 0f, 180f) * Mathf.Deg2Rad);
            var outVerts = new List<Vector3>(verts.Count);
            var outNormals = new List<Vector3>(verts.Count);
            // face → its new vertex index, per corner
            var remap = new int[tris.Count];
            var clusterOf = new Dictionary<int, int>();   // face → cluster index
            var clusterNormal = new List<Vector3>();
            var clusterVertex = new List<int>();

            for (int v = 0; v < verts.Count; v++)
            {
                var faces = incident[v];
                if (faces == null) continue;   // orphan vertex: dropped by the remap

                clusterOf.Clear();
                clusterNormal.Clear();
                clusterVertex.Clear();

                foreach (int f in faces)
                {
                    var n = faceNormal[f];
                    float len = n.magnitude;
                    if (len < 1e-12f)
                    {
                        // degenerate face: park it in its own cluster, normal comes out
                        // of the neighbours later via the zero vector
                        clusterOf[f] = -1;
                        continue;
                    }
                    var unit = n / len;

                    int found = -1;
                    for (int ci = 0; ci < clusterNormal.Count; ci++)
                    {
                        var cn = clusterNormal[ci];
                        if (Vector3.Dot(cn.normalized, unit) >= cosLimit) { found = ci; break; }
                    }
                    if (found < 0)
                    {
                        clusterNormal.Add(n);   // area-weighted: keep the unnormalised one
                        clusterVertex.Add(-1);
                        found = clusterNormal.Count - 1;
                    }
                    else clusterNormal[found] += n;
                    clusterOf[f] = found;
                }

                // emit one vertex per cluster
                for (int ci = 0; ci < clusterNormal.Count; ci++)
                {
                    clusterVertex[ci] = outVerts.Count;
                    outVerts.Add(verts[v]);
                    var n = clusterNormal[ci];
                    outNormals.Add(n.sqrMagnitude > 1e-20f ? n.normalized : Vector3.up);
                }

                // point every corner of every incident face at its cluster's vertex
                foreach (int f in faces)
                {
                    int ci = clusterOf.TryGetValue(f, out int c) ? c : -1;
                    int target;
                    if (ci >= 0) target = clusterVertex[ci];
                    else
                    {
                        // degenerate face: reuse the first cluster, or emit a lone vertex
                        if (clusterVertex.Count > 0) target = clusterVertex[0];
                        else
                        {
                            target = outVerts.Count;
                            outVerts.Add(verts[v]);
                            outNormals.Add(Vector3.up);
                            clusterVertex.Add(target);
                            clusterNormal.Add(Vector3.up);
                        }
                    }
                    for (int k = 0; k < 3; k++)
                        if (tris[f * 3 + k] == v) remap[f * 3 + k] = target;
                }
            }

            verts.Clear();
            verts.AddRange(outVerts);
            for (int i = 0; i < tris.Count; i++) tris[i] = remap[i];
            normals.AddRange(outNormals);
            return verts.Count;
        }
    }
}
