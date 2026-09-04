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

        /// <summary>(category, ambientCG asset id, catalog id, tile size m, gloss 0..1,
        /// relief). v1.2 «много материалов»: 4 категории, ~37 текстур; Tiles применимы к
        /// стенам И полу, Ceiling — к плитам (низ плиты = потолок этажа ниже). v1.3
        /// (design/29): категория **Objects** — материалы предметов (мебель из IFC, каталог)
        /// с объектными тайлами, и флаг `bump` — для кого тянем карту нормалей
        /// (кирпич/плитка/паркет/ткань читаются рельефом, гладкий пластик и полированный
        /// металл — нет).</summary>
        public static readonly (string cat, string asset, string id, float tile, float gloss, bool bump)[] Curated =
        {
            // ---- Walls: wallpaper, painted plaster, brick, concrete ----
            ("Walls", "Wallpaper001A", "wallpaper-001a", 1.0f, 0.05f, false),
            ("Walls", "Wallpaper001B", "wallpaper-001b", 1.0f, 0.05f, false),
            ("Walls", "Wallpaper001C", "wallpaper-001c", 1.0f, 0.05f, false),
            ("Walls", "Wallpaper002A", "wallpaper-002a", 1.0f, 0.05f, false),
            ("Walls", "Wallpaper002B", "wallpaper-002b", 1.0f, 0.05f, false),
            ("Walls", "Wallpaper002C", "wallpaper-002c", 1.0f, 0.05f, false),
            ("Walls", "PaintedPlaster001", "plaster-001", 1.5f, 0.12f, true),
            ("Walls", "PaintedPlaster002", "plaster-002", 1.5f, 0.12f, true),
            ("Walls", "PaintedPlaster006", "plaster-006", 1.5f, 0.12f, true),
            ("Walls", "PaintedPlaster009", "plaster-009", 1.5f, 0.12f, true),
            ("Walls", "PaintedPlaster010", "plaster-010", 1.5f, 0.12f, true),
            ("Walls", "PaintedPlaster016", "plaster-016", 1.5f, 0.12f, true),
            ("Walls", "PaintedPlaster017", "plaster-017", 1.5f, 0.12f, true),
            ("Walls", "Bricks059", "bricks-059", 1.2f, 0.10f, true),
            ("Walls", "Bricks066", "bricks-066", 1.2f, 0.10f, true),
            ("Walls", "Concrete034", "concrete-034", 2.0f, 0.15f, true),
            // «гипсокартон»: smooth skimmed plasterboard look (ambientCG has no
            // seamed-drywall asset — Plaster002/003 are the finished-GKL surface)
            ("Walls", "Plaster002", "drywall-002", 1.5f, 0.10f, false),
            ("Walls", "Plaster003", "drywall-003", 1.5f, 0.10f, false),
            // ---- Floors: wood, planks, stone, carpet ----
            ("Floors", "WoodFloor051", "parquet-051", 2.0f, 0.35f, true),
            ("Floors", "WoodFloor040", "parquet-040", 2.0f, 0.35f, true),
            ("Floors", "WoodFloor007", "parquet-007", 2.0f, 0.35f, true),
            ("Floors", "WoodFloor064", "laminate-064", 2.0f, 0.30f, true),
            ("Floors", "WoodFloor043", "parquet-043", 2.0f, 0.35f, true),
            ("Floors", "Planks012", "planks-012", 2.0f, 0.30f, true),
            ("Floors", "Marble006", "marble-006", 1.5f, 0.70f, false),
            ("Floors", "Marble012", "marble-012", 1.5f, 0.70f, false),
            ("Floors", "Carpet004", "carpet-004", 2.0f, 0.00f, true),
            ("Floors", "Carpet008", "carpet-008", 2.0f, 0.00f, true),
            // ---- Tiles: walls AND floors (bathroom / kitchen) ----
            ("Tiles", "Tiles012", "tiles-012", 1.0f, 0.65f, true),
            ("Tiles", "Tiles032", "tiles-032", 1.0f, 0.65f, true),
            ("Tiles", "Tiles038", "tiles-038", 1.2f, 0.65f, true),
            ("Tiles", "Tiles050", "tiles-050", 1.0f, 0.65f, true),
            ("Tiles", "Tiles074", "tiles-074", 1.0f, 0.65f, true),
            ("Tiles", "Tiles077", "tiles-077", 1.0f, 0.65f, true),
            ("Tiles", "Tiles101", "tiles-101", 1.2f, 0.65f, true),
            // ---- Ceiling: acoustic office tiles + plain plaster ----
            ("Ceiling", "OfficeCeiling003", "ceiling-003", 1.2f, 0.05f, true),
            ("Ceiling", "OfficeCeiling005", "ceiling-005", 1.2f, 0.05f, true),
            ("Ceiling", "Plaster001", "ceiling-plaster-001", 1.5f, 0.10f, false),
            ("Ceiling", "Plaster004", "ceiling-plaster-004", 1.5f, 0.10f, false),
            // ---- Objects (design/29): what furniture is made of. Tiles are OBJECT
            //      scale — wood grain ~0.8 m, fabric 0.4 m, plastic 0.3 m — and the ids
            //      are the ones IfcMaterialMap resolves IFC material names to. ----
            ("Objects", "Wood049", "wood-oak", 0.8f, 0.30f, true),
            ("Objects", "Wood051", "wood-walnut", 0.8f, 0.35f, true),
            ("Objects", "Wood048", "wood-birch", 0.8f, 0.25f, true),
            ("Objects", "Wood028", "wood-dark", 0.8f, 0.30f, true),
            ("Objects", "Plastic010", "panel-white", 0.6f, 0.25f, false),
            ("Objects", "Fabric082A", "fabric-grey", 0.4f, 0.05f, true),
            ("Objects", "Fabric081A", "fabric-blue", 0.4f, 0.05f, true),
            ("Objects", "Leather031", "leather-black", 0.45f, 0.25f, true),
            ("Objects", "Leather028", "leather-brown", 0.45f, 0.25f, true),
            ("Objects", "Metal012", "metal-brushed", 0.5f, 0.55f, true),
            ("Objects", "Metal049A", "metal-steel", 0.5f, 0.65f, false),
            ("Objects", "Metal050A", "metal-aluminium", 0.5f, 0.55f, false),
            ("Objects", "Metal028", "metal-painted-black", 0.5f, 0.30f, false),
            ("Objects", "Plastic013A", "plastic-white", 0.3f, 0.35f, false),
            ("Objects", "Plastic011", "plastic-grey", 0.3f, 0.35f, false),
            ("Objects", "Plastic006", "plastic-black", 0.3f, 0.45f, false),
            ("Objects", "Porcelain001", "ceramic-white", 0.5f, 0.75f, false),
            // v1.4 (2026-08-16): «размер репо не пугает» — расширяем ШИРИНУ набора
            // (VRAM остаётся границей: 2K ASTC ≈ 1.9 МБ на карту, весь каталог висит
            // в сцене), поэтому берём материалы, которых не хватало под мебель и кухню
            ("Objects", "Wood095", "wood-ash", 0.8f, 0.30f, true),
            ("Objects", "Wood092", "wood-teak", 0.8f, 0.30f, true),
            ("Objects", "Wood067", "wood-veneer-dark", 0.4f, 0.30f, true),
            ("Objects", "Fabric018", "fabric-green", 0.4f, 0.05f, true),
            ("Objects", "Fabric031", "fabric-weave", 0.4f, 0.05f, true),
            ("Objects", "Fabric034", "fabric-felt", 0.4f, 0.02f, true),
            ("Objects", "Leather035D", "leather-white", 0.3f, 0.25f, true),
            ("Objects", "Metal034", "metal-brass", 0.5f, 0.70f, false),
            ("Objects", "Metal035", "metal-copper", 0.5f, 0.65f, false),
            ("Objects", "Terrazzo019L", "stone-terrazzo", 0.8f, 0.45f, true),
            ("Objects", "Marble021", "stone-white", 1.0f, 0.75f, false),
            ("Objects", "Wicker007A", "wicker-natural", 0.4f, 0.10f, true),
            ("Objects", "Cork002", "cork-natural", 0.5f, 0.05f, true),
        };

        public static string PathFor(string cat, string id) => $"{TexDir}/{cat}/{id}.jpg";

        /// <summary>Relief map next to the colour map; only entries with `bump` have one.
        /// Stored at 1K (design/29 §5) — relief does not need 2K and Quest VRAM does.</summary>
        public static string NormalPathFor(string cat, string id) => $"{TexDir}/{cat}/{id}_n.jpg";

        [MenuItem("RoomPlanner/Download Textures (CC0, ambientCG)")]
        public static void Download()
        {
            // quality upgrade (1K → 2K): wipe the old files so they re-download
            if (Directory.Exists(TexDir)
                && (!File.Exists(MarkerPath) || File.ReadAllText(MarkerPath).Trim() != Quality))
            {
                Debug.Log($"[Tex] quality marker != {Quality} — re-downloading the set");
                foreach (var (cat, _, id, _, _, _) in Curated)
                {
                    string old = PathFor(cat, id);
                    if (File.Exists(old)) AssetDatabase.DeleteAsset(old);
                }
            }

            int ok = 0, skipped = 0, failed = 0, normals = 0;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(240) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MRRoomPlanner/1.0");

            foreach (var (cat, asset, id, _, _, bump) in Curated)
            {
                string assetPath = PathFor(cat, id);
                string normalPath = NormalPathFor(cat, id);
                bool needColor = !File.Exists(assetPath);
                bool needNormal = bump && !File.Exists(normalPath);
                if (!needColor && !needNormal) { skipped++; continue; }
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
                    string url = $"https://ambientcg.com/get?file={asset}_{Quality}-JPG.zip";
                    Debug.Log($"[Tex] downloading {asset} …");
                    byte[] zipBytes = http.GetByteArrayAsync(url).GetAwaiter().GetResult();

                    using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
                    ZipArchiveEntry color = null, normal = null;
                    foreach (var e in zip.Entries)
                    {
                        if (e.Name.EndsWith("_Color.jpg", StringComparison.OrdinalIgnoreCase)) color = e;
                        // GL = OpenGL green channel, what Unity expects
                        else if (e.Name.EndsWith("_NormalGL.jpg", StringComparison.OrdinalIgnoreCase)) normal = e;
                    }
                    if (needColor)
                    {
                        if (color == null) throw new Exception("no *_Color.jpg in archive");
                        using var src = color.Open();
                        using var dst = File.Create(assetPath);
                        src.CopyTo(dst);
                        ok++;
                    }
                    if (needNormal)
                    {
                        if (normal == null) Debug.LogWarning($"[Tex] {asset}: no *_NormalGL.jpg");
                        else
                        {
                            // shrink to 1K ON DISK: the relief map is committed to a
                            // public repo, and 2K of it buys nothing (design/29 §5)
                            using var src = normal.Open();
                            using var buf = new MemoryStream();
                            src.CopyTo(buf);
                            File.WriteAllBytes(normalPath, Downscale(buf.ToArray(), 1024));
                            normals++;
                        }
                    }
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
            long bytes = 0;
            foreach (var (cat, _, id, _, _, bump) in Curated)
            {
                ConfigureImporter(PathFor(cat, id), normalMap: false, maxSize: 2048);
                bytes += SizeOf(PathFor(cat, id));
                if (!bump) continue;
                ConfigureImporter(NormalPathFor(cat, id), normalMap: true, maxSize: 1024);
                bytes += SizeOf(NormalPathFor(cat, id));
            }
            AssetDatabase.SaveAssets();

            EnsureGitIgnore();
            Debug.Log($"[Tex] done: {ok} colour + {normals} normal downloaded, "
                + $"{skipped} present, {failed} failed; set on disk {bytes / (1024 * 1024)} MB");
            if (Application.isBatchMode && failed > 0) EditorApplication.Exit(1);
        }

        private static long SizeOf(string path) =>
            File.Exists(path) ? new FileInfo(path).Length : 0L;

        /// <summary>Re-encode a JPEG down to <paramref name="maxSize"/> on the long side;
        /// returns the input untouched when it is already small enough or undecodable.</summary>
        private static byte[] Downscale(byte[] jpeg, int maxSize)
        {
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!src.LoadImage(jpeg)) return jpeg;
                if (src.width <= maxSize && src.height <= maxSize) return jpeg;
                int w = maxSize, h = Mathf.Max(1, Mathf.RoundToInt(src.height * (maxSize / (float)src.width)));
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                var prev = RenderTexture.active;
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
                dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                dst.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                byte[] outBytes = dst.EncodeToJPG(92);
                UnityEngine.Object.DestroyImmediate(dst);
                return outBytes;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(src);
            }
        }

        /// <summary>
        /// Seamless-tiling import: Repeat wrap, mips, 2K colour / 1K relief (v1.2,
        /// design/29 §5). Explicit Android override — ASTC 6×6 at full compression
        /// quality and aniso 8 — instead of whatever the build target defaults to:
        /// grazing angles on a tiled floor are exactly where the default gave up.
        /// </summary>
        private static void ConfigureImporter(string assetPath, bool normalMap, int maxSize)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter imp) return;
            var wantType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            var android = imp.GetPlatformTextureSettings("Android");
            bool dirty = imp.wrapMode != TextureWrapMode.Repeat
                || imp.textureType != wantType
                || imp.maxTextureSize != maxSize || !imp.mipmapEnabled
                || imp.anisoLevel != 8
                || !android.overridden
                || android.maxTextureSize != maxSize
                || android.format != TextureImporterFormat.ASTC_6x6
                || android.compressionQuality != 100;
            imp.textureType = wantType;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 8;
            imp.maxTextureSize = maxSize;
            android.overridden = true;
            android.maxTextureSize = maxSize;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.compressionQuality = 100;
            imp.SetPlatformTextureSettings(android);
            if (dirty) imp.SaveAndReimport();
        }

        /// <summary>Historic guard from when texture binaries stayed out of git. Since
        /// 2026-08-13 the CC0 set is committed (with .meta — the scene pins the GUIDs;
        /// CI builds from a clean checkout) and .gitignore only excludes the laminate
        /// bakes, whose sources are not redistributable. The Contains() check below
        /// matches that laminate section, so nothing is ever re-appended.</summary>
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
            foreach (var (cat, _, id, _, _, _) in Curated)
                if (!File.Exists(PathFor(cat, id))) return false;
            return true;
        }
    }
}
#endif
