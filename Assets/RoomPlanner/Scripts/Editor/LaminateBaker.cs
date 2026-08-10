#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Bakes seamless laminate tiles (design/22) from the single-plank source set in
    /// D:\Maps (18 oak boards 4096×684, diffuse + normal; NOT CC0 — never committed,
    /// override the dir with RP_LAMINATE_SRC). CPU compositing, deterministic: every
    /// output pixel maps through LaminateLayout to one plank, sampled bilinearly;
    /// normals get their tangent XY rotated with the plank. Color variants are graded
    /// at bake time (tint / desaturation / brightness / contrast), the normal map is
    /// shared per pattern. Outputs are gitignored like the CC0 set — this menu is the
    /// reproducible way to restore them.
    /// </summary>
    public static class LaminateBaker
    {
        public const string OutDir = "Assets/RoomPlanner/Textures/Laminate";
        private const string DefaultSourceDir = @"D:\Maps";
        private const int OutSize = 2048;
        private const int LayoutSeedBase = 20260810;   // bake date — NOT time-dependent

        // V-groove seam between boards (device feedback: without it the deck rows read
        // as one solid slab of wood): a ~2 mm darkened chamfer whose normal tilts
        // outward, plus a subtle per-board brightness variation.
        private const float BevelMeters = 0.0018f;
        private const float BevelDarken = 0.74f;   // diffuse multiplier at the seam line
        private const float BevelTilt = 0.55f;     // normal XY push at the seam line
        private const float BoardVariation = 0.05f; // ± brightness per board

        /// <summary>(colorKey, tint, tintLerp, desaturate, brightness, contrast) —
        /// applied in that order, in sRGB space (good enough for grading wood).</summary>
        private static readonly (string key, Color tint, float lerp, float desat, float bright, float contrast)[] Grades =
        {
            ("natural", Color.white, 0f, 0f, 1f, 1f),
            ("grey", new Color(0.722f, 0.706f, 0.675f), 0.35f, 0.65f, 1.00f, 1.00f),
            ("dark", new Color(0.420f, 0.290f, 0.184f), 0.45f, 0.10f, 0.90f, 1.10f),
            ("bleached", new Color(0.910f, 0.886f, 0.847f), 0.50f, 0.50f, 1.05f, 0.95f),
        };

        public static string DiffusePath(LaminateCatalog.Entry e)
            => $"{OutDir}/{LaminateCatalog.DiffuseFileName(e)}";
        public static string NormalPath(LaminatePattern p)
            => $"{OutDir}/{LaminateCatalog.NormalFileName(p)}";

        /// <summary>True when every baked file is on disk (SetupPaintTool gate).</summary>
        public static bool AllPresent()
        {
            foreach (var e in LaminateCatalog.Entries)
                if (!File.Exists(DiffusePath(e)) || !File.Exists(NormalPath(e.Pattern)))
                    return false;
            return true;
        }

        [MenuItem("RoomPlanner/Bake Laminate (from D:\\Maps)")]
        public static void Bake()
        {
            string src = Environment.GetEnvironmentVariable("RP_LAMINATE_SRC");
            if (string.IsNullOrEmpty(src)) src = DefaultSourceDir;
            if (!Directory.Exists(src))
            {
                Debug.LogWarning($"[Laminate] source dir '{src}' not found — skipping " +
                    "(machine without the plank set; set RP_LAMINATE_SRC to override)");
                return;
            }

            var diffuse = LoadPlanks(src, "diffuse");
            var normal = LoadPlanks(src, "normal");
            if (diffuse == null || normal == null) return;

            try
            {
                Directory.CreateDirectory(OutDir);
                foreach (var (pattern, key) in LaminateCatalog.Patterns)
                    BakePattern(pattern, key, diffuse, normal);
            }
            finally
            {
                foreach (var t in diffuse) UnityEngine.Object.DestroyImmediate(t);
                foreach (var t in normal) UnityEngine.Object.DestroyImmediate(t);
            }

            AssetDatabase.Refresh();
            foreach (var e in LaminateCatalog.Entries)
                ConfigureImporter(DiffusePath(e), normalMap: false);
            foreach (var (pattern, _) in LaminateCatalog.Patterns)
                ConfigureImporter(NormalPath(pattern), normalMap: true);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Laminate] baked {LaminateCatalog.Entries.Count} diffuse + " +
                $"{LaminateCatalog.Patterns.Length} normal maps into {OutDir}");
        }

        private static Texture2D[] LoadPlanks(string src, string map)
        {
            var planks = new Texture2D[LaminateCatalog.SourcePlanks];
            for (int i = 0; i < planks.Length; i++)
            {
                string path = Path.Combine(src, $"01_Oak_texture_{map}_{i + 1:00}.jpg");
                if (!File.Exists(path))
                {
                    Debug.LogError($"[Laminate] missing source plank: {path}");
                    return null;
                }
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(File.ReadAllBytes(path));   // readable, exact pixels
                planks[i] = tex;
            }
            return planks;
        }

        private static void BakePattern(LaminatePattern pattern, string key,
            Texture2D[] diffuse, Texture2D[] normal)
        {
            var quads = LaminateLayout.Generate(pattern,
                LaminateCatalog.PlankLengthMeters, LaminateCatalog.PlankWidthMeters,
                LaminateCatalog.SourcePlanks, LayoutSeedBase + (int)pattern);

            float tileMeters = LaminateLayout.TileMeters(pattern,
                LaminateCatalog.PlankLengthMeters, LaminateCatalog.PlankWidthMeters);
            var baseColor = new Color[OutSize * OutSize];
            var normalColor = new Color[OutSize * OutSize];
            var covered = new bool[OutSize * OutSize];
            foreach (var q in quads)
                RasterizeQuad(q, tileMeters, diffuse, normal, baseColor, normalColor, covered);

            // float slop at plank borders may leave a lone pixel unwritten — heal from
            // the left neighbour instead of shipping a black dot
            int holes = 0;
            for (int i = 0; i < covered.Length; i++)
                if (!covered[i])
                {
                    int from = i % OutSize > 0 ? i - 1 : i + 1;
                    baseColor[i] = baseColor[from];
                    normalColor[i] = normalColor[from];
                    holes++;
                }
            if (holes > OutSize)   // a hole LINE means the layout math is wrong — fail loud
                Debug.LogError($"[Laminate] {pattern}: {holes} uncovered pixels");

            WritePng(NormalPath(pattern), normalColor);
            foreach (var e in LaminateCatalog.Entries)
            {
                if (e.Pattern != pattern) continue;
                WritePng(DiffusePath(e), Grade(baseColor, e.ColorKey));
            }
            Debug.Log($"[Laminate] {key}: {quads.Count} planks, {holes} healed px");
        }

        /// <summary>Fill every output pixel whose centre falls inside the quad footprint,
        /// including the wrap copies (quads may cross the seamless tile edge).</summary>
        private static void RasterizeQuad(PlankQuad q, float tileMeters,
            Texture2D[] diffuse, Texture2D[] normal,
            Color[] baseColor, Color[] normalColor, bool[] covered)
        {
            var srcD = diffuse[q.SourceIndex];
            var srcN = normal[q.SourceIndex];
            float hw = q.Size.x * 0.5f, hh = q.Size.y * 0.5f;
            float sizeMx = q.Size.x * tileMeters, sizeMy = q.Size.y * tileMeters;
            // deterministic per-board brightness (boards on a real floor never match)
            int hash = q.SourceIndex * 73856093
                ^ (int)(q.Center.x * 4096f) * 19349663 ^ (int)(q.Center.y * 4096f) * 83492791;
            float board = 1f + (((hash & 0xffff) / 65535f) - 0.5f) * 2f * BoardVariation;

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
                            SamplePlank(q, fx, fy, out float u, out float v, out float ca, out float sa);

                            // V-groove chamfer: distance to the nearest board edge, metres
                            float distX = Mathf.Min(fx, 1f - fx) * sizeMx;
                            float distY = Mathf.Min(fy, 1f - fy) * sizeMy;
                            float t = Mathf.Clamp01(Mathf.Min(distX, distY) / BevelMeters);

                            Color c = srcD.GetPixelBilinear(u, v);
                            float dk = board * Mathf.Lerp(BevelDarken, 1f, t);
                            c.r *= dk; c.g *= dk; c.b *= dk;

                            Color n = RotateNormal(srcN.GetPixelBilinear(u, v), ca, sa);
                            if (t < 1f)
                            {
                                float nx = n.r * 2f - 1f, ny = n.g * 2f - 1f, nz = n.b * 2f - 1f;
                                float push = (1f - t) * BevelTilt;   // tilt toward the seam
                                if (distX <= distY) nx += (fx < 0.5f ? -1f : 1f) * push;
                                else ny += (fy < 0.5f ? -1f : 1f) * push;
                                float inv = 1f / Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                                n = new Color(nx * inv * 0.5f + 0.5f, ny * inv * 0.5f + 0.5f,
                                    nz * inv * 0.5f + 0.5f, 1f);
                            }

                            int idx = py * OutSize + px;
                            baseColor[idx] = c;
                            normalColor[idx] = n;
                            covered[idx] = true;
                        }
                }
        }

        /// <summary>Footprint-local (fx, fy) → plank UV. The plank texture is a
        /// horizontal strip (U along the board). Rotation 90 = the board runs along
        /// tile V; Flip adds a 180° turn. (ca, sa) = cos/sin of the total rotation the
        /// normal XY must follow.</summary>
        private static void SamplePlank(PlankQuad q, float fx, float fy,
            out float u, out float v, out float ca, out float sa)
        {
            float along, across;
            if (q.RotationDeg == 0f) { along = fx; across = fy; ca = 1f; sa = 0f; }
            else { along = fy; across = 1f - fx; ca = 0f; sa = 1f; }   // +90°: U→+Y, V→−X
            if (q.Flip) { along = 1f - along; across = 1f - across; ca = -ca; sa = -sa; }
            u = Mathf.Lerp(q.CropU0, q.CropU1, along);
            v = across;
        }

        private static Color RotateNormal(Color packed, float ca, float sa)
        {
            float nx = packed.r * 2f - 1f, ny = packed.g * 2f - 1f;
            float rx = ca * nx - sa * ny, ry = sa * nx + ca * ny;
            return new Color(rx * 0.5f + 0.5f, ry * 0.5f + 0.5f, packed.b, 1f);
        }

        private static Color[] Grade(Color[] src, string colorKey)
        {
            foreach (var (key, tint, lerp, desat, bright, contrast) in Grades)
            {
                if (key != colorKey) continue;
                if (lerp == 0f && desat == 0f && bright == 1f && contrast == 1f) return src;
                var dst = new Color[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    Color c = src[i];
                    float lum = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                    c = Color.Lerp(c, new Color(lum, lum, lum), desat);
                    c = Color.Lerp(c, c * tint, lerp);
                    c *= bright;
                    c.r = (c.r - 0.5f) * contrast + 0.5f;
                    c.g = (c.g - 0.5f) * contrast + 0.5f;
                    c.b = (c.b - 0.5f) * contrast + 0.5f;
                    c.r = Mathf.Clamp01(c.r); c.g = Mathf.Clamp01(c.g); c.b = Mathf.Clamp01(c.b);
                    c.a = 1f;
                    dst[i] = c;
                }
                return dst;
            }
            throw new ArgumentException($"unknown laminate color '{colorKey}'");
        }

        private static void WritePng(string assetPath, Color[] pixels)
        {
            var tex = new Texture2D(OutSize, OutSize, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply(false);
            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        /// <summary>Same seamless-tiling import as the CC0 set; normals typed properly
        /// so Unity packs/unpacks them for mobile. Shared with TileBaker (design/23).</summary>
        internal static void ConfigureImporter(string assetPath, bool normalMap)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter imp) return;
            var wantType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            bool dirty = imp.wrapMode != TextureWrapMode.Repeat
                || imp.maxTextureSize != 2048 || !imp.mipmapEnabled
                || imp.anisoLevel < 4 || imp.textureType != wantType;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 4;
            imp.maxTextureSize = 2048;
            imp.textureType = wantType;
            if (dirty) imp.SaveAndReimport();
        }
    }
}
#endif
