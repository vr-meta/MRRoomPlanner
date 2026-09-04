using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core.Furniture
{
    /// <summary>
    /// Slatted room divider (design/27 §3c, issue #86): vertical slats in a frame-less
    /// screen, generated from parameters rather than shipped as a model — a partition is
    /// sized to the opening it fills, so any fixed mesh would be the wrong width.
    ///
    /// Local space matches the furniture convention: origin at the BOTTOM CENTRE, width
    /// along X, height along Y, depth along Z, front facing +Z. Every triangle winds
    /// outwards (rules 12 §1.1) — a partition is walked around, and an inverted face
    /// would shift the pick point by the slat depth.
    /// </summary>
    public static class PartitionMesh
    {
        public const float MinSlat = 0.01f;
        public const float MinGap = 0.005f;
        public const float MinDepth = 0.01f;

        /// <summary>How many slats fit a width at the given slat/gap pitch (never below 1).</summary>
        public static int SlatCount(float width, float slat, float gap)
        {
            slat = Mathf.Max(MinSlat, slat);
            gap = Mathf.Max(MinGap, gap);
            float pitch = slat + gap;
            if (width <= slat || pitch <= 0f) return 1;
            // n slats leave n-1 gaps: n*slat + (n-1)*gap <= width
            int n = Mathf.FloorToInt((width + gap) / pitch);
            return Mathf.Max(1, n);
        }

        /// <summary>
        /// Build the screen. Degenerate input is clamped rather than allowed to produce
        /// garbage geometry (rules 12 §1.3): zero height, a gap wider than the panel or a
        /// negative slat all still yield one sane slat.
        /// </summary>
        public static void Build(float width, float height, float slat, float gap, float depth,
            List<Vector3> vertices, List<int> triangles, List<Vector3> normals = null)
        {
            if (vertices == null || triangles == null) return;
            vertices.Clear();
            triangles.Clear();
            normals?.Clear();

            width = Mathf.Max(MinSlat, Mathf.Abs(width));
            height = Mathf.Max(MinSlat, Mathf.Abs(height));
            slat = Mathf.Clamp(Mathf.Abs(slat), MinSlat, width);
            gap = Mathf.Max(MinGap, Mathf.Abs(gap));
            depth = Mathf.Max(MinDepth, Mathf.Abs(depth));

            int count = SlatCount(width, slat, gap);
            // Spread the slats across the full width: the outermost ones touch the edges,
            // so the screen measures exactly what the inspector says.
            float span = count > 1 ? (width - slat) / (count - 1) : 0f;
            float x0 = count > 1 ? -width * 0.5f + slat * 0.5f : 0f;

            for (int i = 0; i < count; i++)
            {
                float cx = x0 + i * span;
                AddBox(vertices, triangles, normals,
                    new Vector3(cx, height * 0.5f, 0f),
                    new Vector3(slat * 0.5f, height * 0.5f, depth * 0.5f));
            }
        }

        /// <summary>Axis-aligned box, outward winding, flat normals per face.</summary>
        public static void AddBox(List<Vector3> v, List<int> t, List<Vector3> n,
            Vector3 c, Vector3 e)
        {
            // face order: +X, -X, +Y, -Y, +Z, -Z
            AddFace(v, t, n, c + new Vector3(e.x, 0, 0), new Vector3(0, 0, -e.z), new Vector3(0, e.y, 0), Vector3.right);
            AddFace(v, t, n, c + new Vector3(-e.x, 0, 0), new Vector3(0, 0, e.z), new Vector3(0, e.y, 0), Vector3.left);
            AddFace(v, t, n, c + new Vector3(0, e.y, 0), new Vector3(e.x, 0, 0), new Vector3(0, 0, -e.z), Vector3.up);
            AddFace(v, t, n, c + new Vector3(0, -e.y, 0), new Vector3(e.x, 0, 0), new Vector3(0, 0, e.z), Vector3.down);
            AddFace(v, t, n, c + new Vector3(0, 0, e.z), new Vector3(e.x, 0, 0), new Vector3(0, e.y, 0), Vector3.forward);
            AddFace(v, t, n, c + new Vector3(0, 0, -e.z), new Vector3(-e.x, 0, 0), new Vector3(0, e.y, 0), Vector3.back);
        }

        private static void AddFace(List<Vector3> v, List<int> t, List<Vector3> n,
            Vector3 centre, Vector3 right, Vector3 up, Vector3 normal)
        {
            int b = v.Count;
            v.Add(centre - right - up);
            v.Add(centre + right - up);
            v.Add(centre + right + up);
            v.Add(centre - right + up);
            if (n != null) for (int i = 0; i < 4; i++) n.Add(normal);
            // (0,1,2)+(0,2,3): with right×up == the face normal, this is the outward
            // winding. The mirrored order looks equally plausible and is wrong — hence
            // the normals test (rules 12 §1.2).
            t.Add(b); t.Add(b + 1); t.Add(b + 2);
            t.Add(b); t.Add(b + 2); t.Add(b + 3);
        }
    }
}
