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
        public float TileMetersY;  // vertical/second-axis tile; 0 = square (use TileMeters)
        public float Smoothness;   // 0 matte … 1 gloss (shader _Smoothness; design/04 v1.2)
        public float RotationDeg;  // texture rotation in the metric UV plane (turn laminate)

        public static readonly SurfaceFinish None = new() { Kind = FinishKind.None };

        public static SurfaceFinish OfColor(Color c, float smoothness = 0f) =>
            new() { Kind = FinishKind.Color, Color = c, Smoothness = Mathf.Clamp01(smoothness) };

        public static SurfaceFinish OfTexture(string id, float tileMeters,
            float tileMetersY = 0f, Color? tint = null, float smoothness = 0f,
            float rotationDeg = 0f) =>
            new()
            {
                Kind = FinishKind.Texture,
                TextureId = id,
                TileMeters = Mathf.Max(0.05f, tileMeters),
                TileMetersY = tileMetersY > 0f ? Mathf.Max(0.05f, tileMetersY) : 0f,
                Color = tint ?? UnityEngine.Color.white,
                Smoothness = Mathf.Clamp01(smoothness),
                RotationDeg = Mathf.Repeat(rotationDeg, 360f),
            };

        public bool IsNone => Kind == FinishKind.None;

        /// <summary>Effective tile along the second UV axis (V): its own value or square.</summary>
        public float EffectiveTileY => TileMetersY > 0f ? TileMetersY : TileMeters;

        /// <summary>UV scale for the metric-UV meshes: 1 world meter ÷ tile meters, per axis.</summary>
        public Vector4 UvScaleOffset()
        {
            float sx = TileMeters > 0f ? 1f / TileMeters : 1f;
            float ty = EffectiveTileY;
            float sy = ty > 0f ? 1f / ty : 1f;
            return new Vector4(sx, sy, 0f, 0f);
        }

        /// <summary>(cos, sin) of the rotation for the shader's _UvRot — applied to the
        /// METRIC uv before the tile scale, so non-square tiles rotate without shear.</summary>
        public Vector4 UvRotation()
        {
            float r = RotationDeg * Mathf.Deg2Rad;
            return new Vector4(Mathf.Cos(r), Mathf.Sin(r), 0f, 0f);
        }

        public override string ToString() => Kind switch
        {
            FinishKind.Color => $"color {Color}",
            FinishKind.Texture => $"tex {TextureId} @{TileMeters}x{EffectiveTileY}m",
            _ => "none",
        };
    }
}
