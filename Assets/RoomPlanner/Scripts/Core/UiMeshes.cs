using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>
    /// Procedural flat UI meshes (design/20 §6): radial sectors, discs, the standard
    /// rounded-rect button plate and the radial's vertex-alpha scrim. All meshes live in
    /// the XY plane and face −Z (toward the viewer), matching <see cref="SvgTessellator"/>.
    /// Pure geometry — components wrap the arrays into UnityEngine.Mesh.
    /// </summary>
    public static class UiMeshes
    {
        /// <summary>Annular sector between two compass angles (0° = up, clockwise).</summary>
        public static SvgMesh Sector(float innerR, float outerR, float a0Deg, float a1Deg,
            int segments = 16)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int s = 0; s <= segments; s++)
            {
                float a = Mathf.Lerp(a0Deg, a1Deg, (float)s / segments) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);
                verts.Add(dir * innerR);
                verts.Add(dir * outerR);
            }
            for (int s = 0; s < segments; s++)
            {
                int i = s * 2;
                // clockwise-in-XY when seen from −Z → front faces the viewer
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 3);
                tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
            }
            return new SvgMesh { Vertices = verts.ToArray(), Triangles = tris.ToArray() };
        }

        /// <summary>Filled disc (radial hub).</summary>
        public static SvgMesh Disc(float radius, int segments = 48)
        {
            var verts = new List<Vector3> { Vector3.zero };
            var tris = new List<int>();
            for (int s = 0; s <= segments; s++)
            {
                float a = (float)s / segments * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Sin(a) * radius, Mathf.Cos(a) * radius, 0f));
            }
            for (int s = 1; s <= segments; s++)
            {
                // the rim runs clockwise on screen (x = sin), so center→current→next faces −Z
                tris.Add(0); tris.Add(s); tris.Add(s + 1);
            }
            return new SvgMesh { Vertices = verts.ToArray(), Triangles = tris.ToArray() };
        }

        /// <summary>
        /// Rounded-rect plate (radius-M buttons, fields, §2.2 of the guide): 4 corner fans
        /// of <paramref name="cornerSegments"/> segments. The v2 standard replacing flat quads.
        /// </summary>
        public static SvgMesh RoundedRect(float width, float height, float radius,
            int cornerSegments = 4)
        {
            float r = Mathf.Min(radius, Mathf.Min(width, height) * 0.5f);
            float hw = width * 0.5f, hh = height * 0.5f;
            var outline = new List<Vector2>();
            // corner centers, walked clockwise (as seen by the viewer at −Z) from top-left
            var centers = new[]
            {
                new Vector2(-hw + r, hh - r),   // TL: 270°→360°
                new Vector2(hw - r, hh - r),    // TR: 0°→90°
                new Vector2(hw - r, -hh + r),   // BR: 90°→180°
                new Vector2(-hw + r, -hh + r),  // BL: 180°→270°
            };
            for (int c = 0; c < 4; c++)
            {
                float start = (270f + c * 90f) * Mathf.Deg2Rad;
                for (int s = 0; s <= cornerSegments; s++)
                {
                    float a = start + (float)s / cornerSegments * Mathf.PI * 0.5f;
                    outline.Add(centers[c] + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * r);
                }
            }
            var verts = new List<Vector3> { Vector3.zero };
            var tris = new List<int>();
            foreach (var p in outline) verts.Add(new Vector3(p.x, p.y, 0f));
            int n = outline.Count;
            for (int i = 1; i <= n; i++)
            {
                int j = i % n + 1;
                // outline is clockwise on screen → center→current→next faces −Z
                tris.Add(0); tris.Add(i); tris.Add(j);
            }
            // 0..1 UVs so texture swatch chips actually SHOW their texture
            var uvs = new Vector2[verts.Count];
            for (int i = 0; i < verts.Count; i++)
                uvs[i] = new Vector2(verts[i].x / width + 0.5f, verts[i].y / height + 0.5f);
            return new SvgMesh { Vertices = verts.ToArray(), Triangles = tris.ToArray(), UVs = uvs };
        }

        /// <summary>Radial falloff for the scrim texture: 1 at the center, 0 at the rim.
        /// (The scrim ships as a procedural texture on a quad — vertex-color shaders proved
        /// fragile to variant stripping on device, textures did not.)</summary>
        public static float ScrimAlpha(float normalizedRadius)
            => Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(normalizedRadius));
    }
}
