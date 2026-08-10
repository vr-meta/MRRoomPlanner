using System;
using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>What covers a surface (design/04 § «Текстуры v1»).</summary>
    public enum FinishKind
    {
        None,     // the material's own look (concrete)
        Color,    // solid paint
        Texture   // wallpaper / plaster / wood floor, optionally tinted by Color
    }

    /// <summary>
    /// One surface's finish — pure data, the unit both the paint command and the project
    /// file store. Texture lookup (id → Texture2D) happens at the edges (FinishLibrary);
    /// the model never references Unity assets, so it round-trips through JSON as-is.
    /// </summary>
    [Serializable]
    public struct SurfaceFinish
    {
        public FinishKind Kind;
        public Color Color;        // Color mode: the paint; Texture mode: the tint (white = none)
        public string TextureId;   // catalog id ("wallpaper-001a"); null unless Texture
        public float TileMeters;   // metric tile size for the UV scale (1 m UV / TileMeters)

        public static readonly SurfaceFinish None = new() { Kind = FinishKind.None };

        public static SurfaceFinish OfColor(Color c) =>
            new() { Kind = FinishKind.Color, Color = c };

        public static SurfaceFinish OfTexture(string id, float tileMeters, Color? tint = null) =>
            new()
            {
                Kind = FinishKind.Texture,
                TextureId = id,
                TileMeters = Mathf.Max(0.05f, tileMeters),
                Color = tint ?? UnityEngine.Color.white,
            };

        public bool IsNone => Kind == FinishKind.None;

        /// <summary>UV scale for the metric-UV meshes: 1 world meter ÷ tile meters.</summary>
        public Vector4 UvScaleOffset()
        {
            float s = TileMeters > 0f ? 1f / TileMeters : 1f;
            return new Vector4(s, s, 0f, 0f);
        }

        public override string ToString() => Kind switch
        {
            FinishKind.Color => $"color {Color}",
            FinishKind.Texture => $"tex {TextureId} @{TileMeters}m",
            _ => "none",
        };
    }
}
