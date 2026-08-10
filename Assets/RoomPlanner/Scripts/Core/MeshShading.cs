using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>
    /// Baked per-vertex ambient occlusion for the procedural meshes (design/04 realism
    /// pass): real GI cannot bake against runtime-built geometry, but WE build every
    /// mesh — so the builders write cheap contact shading into vertex colors and the
    /// LitVertexAO shader multiplies it into the ambient term. Free at render time.
    /// The toggle is global; flipping it requires a rebuild of the meshes (one-off).
    /// </summary>
    public static class MeshShading
    {
        /// <summary>Vertex-AO master switch (Paint tool settings row).</summary>
        public static bool VertexAO = true;

        /// <summary>Skirting shadow: surfaces darken toward the floor contact line.</summary>
        public static float HeightAO(float metersAboveBase) =>
            VertexAO ? Mathf.Lerp(0.70f, 1f, Mathf.Clamp01(metersAboveBase / 0.45f)) : 1f;

        /// <summary>Floor-top corner shadow: darker toward the outline (walls stand there).</summary>
        public static float EdgeAO(float metersToEdge) =>
            VertexAO ? Mathf.Lerp(0.76f, 1f, Mathf.Clamp01(metersToEdge / 0.4f)) : 1f;

        /// <summary>Distance from a point to the closest edge of a ring (XZ plane).</summary>
        public static float DistanceToRingXZ(System.Collections.Generic.IReadOnlyList<Vector3> ring, Vector3 p)
        {
            float best = float.MaxValue;
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = ring[i], b = ring[(i + 1) % n];
                var ap = new Vector2(p.x - a.x, p.z - a.z);
                var ab = new Vector2(b.x - a.x, b.z - a.z);
                float len2 = ab.sqrMagnitude;
                float t = len2 < 1e-10f ? 0f : Mathf.Clamp01(Vector2.Dot(ap, ab) / len2);
                float d = (ap - ab * t).magnitude;
                if (d < best) best = d;
            }
            return best;
        }
    }
}
