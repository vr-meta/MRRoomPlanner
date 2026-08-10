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
    /// skipped). Only the Color map is imported in v1.
    /// </summary>
    public static class TextureDownloader
    {
        public const string TexDir = "Assets/RoomPlanner/Textures";

        /// <summary>(category, ambientCG asset id, catalog id, tile size in meters)</summary>
        public static readonly (string cat, string asset, string id, float tile)[] Curated =
        {
            ("Walls", "Wallpaper001A", "wallpaper-001a", 1.0f),
            ("Walls", "Wallpaper001C", "wallpaper-001c", 1.0f),
            ("Walls", "Wallpaper002A", "wallpaper-002a", 1.0f),
            ("Walls", "Wallpaper002B", "wallpaper-002b", 1.0f),
            ("Walls", "PaintedPlaster001", "plaster-001", 1.5f),
            ("Walls", "PaintedPlaster006", "plaster-006", 1.5f),
            ("Walls", "PaintedPlaster010", "plaster-010", 1.5f),
            ("Walls", "PaintedPlaster017", "plaster-017", 1.5f),
            ("Floors", "WoodFloor051", "parquet-051", 2.0f),
            ("Floors", "WoodFloor040", "parquet-040", 2.0f),
            ("Floors", "WoodFloor007", "parquet-007", 2.0f),
            ("Floors", "WoodFloor064", "laminate-064", 2.0f),
        };

        public static string PathFor(string cat, string id) => $"{TexDir}/{cat}/{id}.jpg";

        [MenuItem("RoomPlanner/Download Textures (CC0, ambientCG)")]
        public static void Download()
        {
            int ok = 0, skipped = 0, failed = 0;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MRRoomPlanner/1.0");

            foreach (var (cat, asset, id, _) in Curated)
            {
                string assetPath = PathFor(cat, id);
                if (File.Exists(assetPath)) { skipped++; continue; }
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
                    string url = $"https://ambientcg.com/get?file={asset}_1K-JPG.zip";
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

            AssetDatabase.Refresh();
            foreach (var (cat, _, id, _) in Curated) ConfigureImporter(PathFor(cat, id));
            AssetDatabase.SaveAssets();

            EnsureGitIgnore();
            Debug.Log($"[Tex] done: {ok} downloaded, {skipped} present, {failed} failed");
            if (Application.isBatchMode && failed > 0) EditorApplication.Exit(1);
        }

        /// <summary>Seamless-tiling import: Repeat wrap, mips, capped at 1K.</summary>
        private static void ConfigureImporter(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter imp) return;
            bool dirty = imp.wrapMode != TextureWrapMode.Repeat
                || imp.maxTextureSize != 1024 || !imp.mipmapEnabled
                || imp.anisoLevel < 4;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 4;
            imp.maxTextureSize = 1024;
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
            foreach (var (cat, _, id, _) in Curated)
                if (!File.Exists(PathFor(cat, id))) return false;
            return true;
        }
    }
}
#endif
