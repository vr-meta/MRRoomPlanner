#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Bakes the procedural ceramic tile set (design/23): subway ("кабанчик"), square
    /// grid and tile herringbone in preset glaze colors. No photo sources — a face is
    /// glaze color + per-tile brightness hash + subtle glaze noise; the character comes
    /// from the grout line and the bevel in the normal map (subway's wide chamfer).
    /// Everything is deterministic and reproduces from nothing via this menu; outputs
    /// are gitignored with the rest of Textures/.
    /// </summary>
    public static class TileBaker
    {
        public const string OutDir = "Assets/RoomPlanner/Textures/TilesBaked";
        private const int OutSize = 1024;
        private const int LayoutSeedBase = 20260812;   // bake date — NOT time-dependent

        private const float GroutMeters = 0.0012f;     // half-width of the grout line
        private const float GroutDarken = 0.86f;       // grout sits below the glaze — no gloss pop
        private const float BevelTilt = 0.5f;          // normal XY push at the grout edge
        private const float BevelLighten = 0.05f;      // faked glaze curvature highlight
        private const float TileVariation = 0.03f;     // ± brightness per tile
        private const float GlazeNoise = 0.02f;        // ± low-frequency glaze mottle

        private static readonly Color GroutColor = new(0.72f, 0.70f, 0.66f);

        public static string DiffusePath(TileCatalog.Entry e)
            => $"{OutDir}/{TileCatalog.DiffuseFileName(e)}";
        public static string NormalPath(TileCatalog.Pattern p)
            => $"{OutDir}/{TileCatalog.NormalFileName(p)}";

        /// <summary>True when every baked file is on disk (SetupPaintTool gate).</summary>
        public static bool AllPresent()
        {
            foreach (var e in TileCatalog.Entries)
                if (!File.Exists(DiffusePath(e)) || !File.Exists(NormalPath(e.Pattern)))
                    return false;
            return true;
        }

        [MenuItem("RoomPlanner/Bake Tiles (procedural ceramic)")]
        public static void Bake()
        {
            Directory.CreateDirectory(OutDir);
            for (int i = 0; i < TileCatalog.Patterns.Length; i++)
                BakePattern(TileCatalog.Patterns[i], LayoutSeedBase + i);

            AssetDatabase.Refresh();
            foreach (var e in TileCatalog.Entries)
                LaminateBaker.ConfigureImporter(DiffusePath(e), normalMap: false);
            foreach (var p in TileCatalog.Patterns)
                LaminateBaker.ConfigureImporter(NormalPath(p), normalMap: true);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Tiles] baked {TileCatalog.Entries.Count} diffuse + " +
                $"{TileCatalog.Patterns.Length} normal maps into {OutDir}");
        }

        private static void BakePattern(TileCatalog.Pattern p, int seed)
        {
            var quads = LaminateLayout.Generate(p.Layout, p.TileL, p.TileW,
                TileCatalog.SourceSlots, seed,
                p.DeckOffset > 0f ? p.DeckOffset : 1f / 3f);
            float tileMeters = LaminateLayout.TileMeters(p.Layout, p.TileL, p.TileW);

            // shade = glaze multiplier per pixel; grout pixels take the grout color
            var shade = new float[OutSize * OutSize];
            var grout = new bool[OutSize * OutSize];
            var normal = new Color[OutSize * OutSize];
            var covered = new bool[OutSize * OutSize];
            foreach (var q in quads)
                RasterizeQuad(q, p, tileMeters, shade, grout, normal, covered);

            int holes = 0;
            for (int i = 0; i < covered.Length; i++)
                if (!covered[i])
                {
                    int from = i % OutSize > 0 ? i - 1 : i + 1;
                    shade[i] = shade[from]; grout[i] = grout[from]; normal[i] = normal[from];
                    holes++;
                }
            if (holes > OutSize)
                Debug.LogError($"[Tiles] {p.Key}: {holes} uncovered pixels");

            WritePng(NormalPath(p), normal);
            foreach (var e in TileCatalog.Entries)
            {
                if (e.Pattern.Key != p.Key) continue;
                var pixels = new Color[shade.Length];
                for (int i = 0; i < shade.Length; i++)
                {
                    Color c = grout[i] ? GroutColor * (shade[i] * GroutDarken) : e.Glaze * shade[i];
                    c.a = 1f;
                    pixels[i] = c;
                }
                WritePng(DiffusePath(e), pixels);
            }
            Debug.Log($"[Tiles] {p.Key}: {quads.Count} tiles, {holes} healed px");
        }

        private static void RasterizeQuad(PlankQuad q, TileCatalog.Pattern p, float tileMeters,
            float[] shade, bool[] grout, Color[] normal, bool[] covered)
        {
            float hw = q.Size.x * 0.5f, hh = q.Size.y * 0.5f;
            float sizeMx = q.Size.x * tileMeters, sizeMy = q.Size.y * tileMeters;
            int hash = q.SourceIndex * 73856093
                ^ (int)(q.Center.x * 4096f) * 19349663 ^ (int)(q.Center.y * 4096f) * 83492791;
            float tile = 1f + (((hash & 0xffff) / 65535f) - 0.5f) * 2f * TileVariation;

            for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    float left = q.Center.x + ox - hw, bottom = q.Center.y + oy - hh;
                    int px0 = Mathf.Max(0, Mathf.CeilToInt(left * OutSize - 0.5f));
                    int px1 = Mathf.Min(OutSize - 1, Mathf.FloorToInt((left + q.Size.x) * OutSize - 0.5f));
                    int py0 = Mathf.Max(0, Mathf.CeilToInt(bottom * OutSize - 0.5f));
                    int py1 = Mathf.Min(OutSize - 1, Mathf.FloorToInt((bottom + q.Size.y) * OutSize - 0.5f));
                    for (int py = py0; py <= py1; py++)
                        for (int px = px0; px <= px1; px++)
                        {
                            float fx = ((px + 0.5f) / OutSize - left) / q.Size.x;
                            float fy = ((py + 0.5f) / OutSize - bottom) / q.Size.y;
                            float distX = Mathf.Min(fx, 1f - fx) * sizeMx;
                            float distY = Mathf.Min(fy, 1f - fy) * sizeMy;
                            float d = Mathf.Min(distX, distY);

                            int idx = py * OutSize + px;
                            float mottle = 1f + (ValueNoise(px, py) - 0.5f) * 2f * GlazeNoise;

                            if (d < GroutMeters)
                            {
                                grout[idx] = true;
                                shade[idx] = mottle;
                                normal[idx] = new Color(0.5f, 0.5f, 1f, 1f);   // flat
                            }
                            else
                            {
                                float t = Mathf.Clamp01((d - GroutMeters) / p.BevelMeters);
                                grout[idx] = false;
                                shade[idx] = tile * mottle * (1f + (1f - t) * BevelLighten);
                                float nx = 0f, ny = 0f, nz = 1f;
                                if (t < 1f)
                                {
                                    // half-sine chamfer: steepest right at the grout
                                    float push = Mathf.Cos(t * Mathf.PI * 0.5f) * BevelTilt;
                                    if (distX <= distY) nx = (fx < 0.5f ? -1f : 1f) * push;
                                    else ny = (fy < 0.5f ? -1f : 1f) * push;
                                    float inv = 1f / Mathf.Sqrt(nx * nx + ny * ny + 1f);
                                    nx *= inv; ny *= inv; nz = inv;
                                }
                                normal[idx] = new Color(nx * 0.5f + 0.5f, ny * 0.5f + 0.5f,
                                    nz * 0.5f + 0.5f, 1f);
                            }
                            covered[idx] = true;
                        }
                }
        }

        /// <summary>Deterministic low-frequency value noise (lattice 32 px, bilinear) —
        /// the faint mottle of fired glaze. No UnityEngine.Random / time involved.</summary>
        private static float ValueNoise(int px, int py)
        {
            const int cell = 32;
            int x0 = px / cell, y0 = py / cell;
            float fx = (px % cell) / (float)cell, fy = (py % cell) / (float)cell;
            float a = Hash01(x0, y0), b = Hash01(x0 + 1, y0);
            float c = Hash01(x0, y0 + 1), d = Hash01(x0 + 1, y0 + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Hash01(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffffu) / 16777215f;
            }
        }

        private static void WritePng(string assetPath, Color[] pixels)
        {
            var tex = new Texture2D(OutSize, OutSize, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply(false);
            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
#endif
