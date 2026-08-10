#if UNITY_EDITOR
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using UnityEditor;
using UnityEngine;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Downloads the curated CC0 texture set from ambientCG (design/04 § «Текстуры v1»)
    /// into Assets/RoomPlanner/Textures/&lt;category&gt;/. CC0 = safe to ship in the APK,
    /// no attribution required. The files are BINARY and therefore gitignored — this
    /// menu is the reproducible way to restore them (idempotent: present files are
    /// skipped). Only the Color map is imported. v1.2: 2K quality — a marker file
    /// records the downloaded quality and a mismatch re-downloads the whole set.
    /// </summary>
    public static class TextureDownloader
    {
        public const string TexDir = "Assets/RoomPlanner/Textures";
        private const string Quality = "2K";
        private static string MarkerPath => $"{TexDir}/.quality";

        /// <summary>(category, ambientCG asset id, catalog id, tile size m, gloss 0..1).
        /// v1.2 «много материалов»: 4 категории, ~37 текстур; Tiles применимы к стенам
        /// И полу, Ceiling — к плитам (низ плиты = потолок этажа ниже).</summary>
        public static readonly (string cat, string asset, string id, float tile, float gloss)[] Curated =
        {
            // ---- Walls: wallpaper, painted plaster, brick, concrete ----
            ("Walls", "Wallpaper001A", "wallpaper-001a", 1.0f, 0.05f),
            ("Walls", "Wallpaper001B", "wallpaper-001b", 1.0f, 0.05f),
            ("Walls", "Wallpaper001C", "wallpaper-001c", 1.0f, 0.05f),
            ("Walls", "Wallpaper002A", "wallpaper-002a", 1.0f, 0.05f),
            ("Walls", "Wallpaper002B", "wallpaper-002b", 1.0f, 0.05f),
            ("Walls", "Wallpaper002C", "wallpaper-002c", 1.0f, 0.05f),
            ("Walls", "PaintedPlaster001", "plaster-001", 1.5f, 0.12f),
            ("Walls", "PaintedPlaster002", "plaster-002", 1.5f, 0.12f),
            ("Walls", "PaintedPlaster006", "plaster-006", 1.5f, 0.12f),
            ("Walls", "PaintedPlaster009", "plaster-009", 1.5f, 0.12f),
            ("Walls", "PaintedPlaster010", "plaster-010", 1.5f, 0.12f),
            ("Walls", "PaintedPlaster016", "plaster-016", 1.5f, 0.12f),
            ("Walls", "PaintedPlaster017", "plaster-017", 1.5f, 0.12f),
            ("Walls", "Bricks059", "bricks-059", 1.2f, 0.10f),
            ("Walls", "Bricks066", "bricks-066", 1.2f, 0.10f),
            ("Walls", "Concrete034", "concrete-034", 2.0f, 0.15f),
            // «гипсокартон»: smooth skimmed plasterboard look (ambientCG has no
            // seamed-drywall asset — Plaster002/003 are the finished-GKL surface)
            ("Walls", "Plaster002", "drywall-002", 1.5f, 0.10f),
            ("Walls", "Plaster003", "drywall-003", 1.5f, 0.10f),
            // ---- Floors: wood, planks, stone, carpet ----
            ("Floors", "WoodFloor051", "parquet-051", 2.0f, 0.35f),
            ("Floors", "WoodFloor040", "parquet-040", 2.0f, 0.35f),
            ("Floors", "WoodFloor007", "parquet-007", 2.0f, 0.35f),
            ("Floors", "WoodFloor064", "laminate-064", 2.0f, 0.30f),
            ("Floors", "WoodFloor043", "parquet-043", 2.0f, 0.35f),
            ("Floors", "Planks012", "planks-012", 2.0f, 0.30f),
            ("Floors", "Marble006", "marble-006", 1.5f, 0.70f),
            ("Floors", "Marble012", "marble-012", 1.5f, 0.70f),
            ("Floors", "Carpet004", "carpet-004", 2.0f, 0.00f),
            ("Floors", "Carpet008", "carpet-008", 2.0f, 0.00f),
            // ---- Tiles: walls AND floors (bathroom / kitchen) ----
            ("Tiles", "Tiles012", "tiles-012", 1.0f, 0.65f),
            ("Tiles", "Tiles032", "tiles-032", 1.0f, 0.65f),
            ("Tiles", "Tiles038", "tiles-038", 1.2f, 0.65f),
            ("Tiles", "Tiles050", "tiles-050", 1.0f, 0.65f),
            ("Tiles", "Tiles074", "tiles-074", 1.0f, 0.65f),
            ("Tiles", "Tiles077", "tiles-077", 1.0f, 0.65f),
            ("Tiles", "Tiles101", "tiles-101", 1.2f, 0.65f),
            // ---- Ceiling: acoustic office tiles + plain plaster ----
            ("Ceiling", "OfficeCeiling003", "ceiling-003", 1.2f, 0.05f),
            ("Ceiling", "OfficeCeiling005", "ceiling-005", 1.2f, 0.05f),
            ("Ceiling", "Plaster001", "ceiling-plaster-001", 1.5f, 0.10f),
            ("Ceiling", "Plaster004", "ceiling-plaster-004", 1.5f, 0.10f),
        };

        public static string PathFor(string cat, string id) => $"{TexDir}/{cat}/{id}.jpg";

        [MenuItem("RoomPlanner/Download Textures (CC0, ambientCG)")]
        public static void Download()
        {
            // quality upgrade (1K → 2K): wipe the old files so they re-download
            if (Directory.Exists(TexDir)
                && (!File.Exists(MarkerPath) || File.ReadAllText(MarkerPath).Trim() != Quality))
            {
                Debug.Log($"[Tex] quality marker != {Quality} — re-downloading the set");
                foreach (var (cat, _, id, _, _) in Curated)
                {
                    string old = PathFor(cat, id);
                    if (File.Exists(old)) AssetDatabase.DeleteAsset(old);
                }
            }

            int ok = 0, skipped = 0, failed = 0;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(240) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MRRoomPlanner/1.0");

            foreach (var (cat, asset, id, _, _) in Curated)
            {
                string assetPath = PathFor(cat, id);
                if (File.Exists(assetPath)) { skipped++; continue; }
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
                    string url = $"https://ambientcg.com/get?file={asset}_{Quality}-JPG.zip";
                    Debug.Log($"[Tex] downloading {asset} …");
                    byte[] zipBytes = http.GetByteArrayAsync(url).GetAwaiter().GetResult();

                    using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
                    ZipArchiveEntry color = null;
                    foreach (var e in zip.Entries)
                        if (e.Name.EndsWith("_Color.jpg", StringComparison.OrdinalIgnoreCase))
                            color = e;
                    if (color == null) throw new Exception("no *_Color.jpg in archive");

                    using var src = color.Open();
                    using var dst = File.Create(assetPath);
                    src.CopyTo(dst);
                    ok++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Tex] {asset} failed: {e.Message}");
                    failed++;
                }
            }

            if (failed == 0)
            {
                Directory.CreateDirectory(TexDir);
                File.WriteAllText(MarkerPath, Quality);
            }

            AssetDatabase.Refresh();
            foreach (var (cat, _, id, _, _) in Curated) ConfigureImporter(PathFor(cat, id));
            AssetDatabase.SaveAssets();

            EnsureGitIgnore();
            Debug.Log($"[Tex] done: {ok} downloaded, {skipped} present, {failed} failed");
            if (Application.isBatchMode && failed > 0) EditorApplication.Exit(1);
        }

        /// <summary>Seamless-tiling import: Repeat wrap, mips, capped at 2K (v1.2).</summary>
        private static void ConfigureImporter(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter imp) return;
            bool dirty = imp.wrapMode != TextureWrapMode.Repeat
                || imp.maxTextureSize != 2048 || !imp.mipmapEnabled
                || imp.anisoLevel < 4;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 4;
            imp.maxTextureSize = 2048;
            if (dirty) imp.SaveAndReimport();
        }

        /// <summary>The binaries stay OUT of git — only this downloader reproduces them.</summary>
        private static void EnsureGitIgnore()
        {
            string gi = Path.Combine(Directory.GetCurrentDirectory(), ".gitignore");
            if (!File.Exists(gi)) return;
            string text = File.ReadAllText(gi);
            if (text.Contains("Assets/RoomPlanner/Textures/")) return;
            File.AppendAllText(gi,
                "\n# CC0 textures — restored via RoomPlanner > Download Textures\n" +
                "Assets/RoomPlanner/Textures/**/*.jpg\n" +
                "Assets/RoomPlanner/Textures/**/*.jpg.meta\n");
        }

        /// <summary>True when the whole curated set is on disk (SetupRig gate).</summary>
        public static bool AllPresent()
        {
            foreach (var (cat, _, id, _, _) in Curated)
                if (!File.Exists(PathFor(cat, id))) return false;
            return true;
        }
    }
}
#endif
