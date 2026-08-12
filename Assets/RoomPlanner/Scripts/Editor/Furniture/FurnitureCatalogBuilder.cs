#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using RoomPlanner.Core.Furniture;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Writes a bundled pack's <c>collection.json</c> from its curation table
    /// (design/27 §1, issue #69).
    ///
    /// Why a curation table at all: the CC0 packs are stylised and NOT proportional to
    /// reality — Kenney's kitchen unit measures 0.43 × 0.45 × 0.45 where the real one is
    /// 0.60 × 0.85 × 0.60, and the error differs per axis. So real-world sizes, category
    /// and anchor are authored by hand, checked into the repo next to this builder, and
    /// re-running the builder never loses that work.
    ///
    /// The builder verifies rather than guesses: every curated id must have its .glb in
    /// StreamingAssets, every model file must be curated or explicitly excluded. Anything
    /// unaccounted for is reported — a silently skipped model would read as "the pack
    /// shipped complete" when it did not.
    /// </summary>
    public static class FurnitureCatalogBuilder
    {
        public const string CurationDir = "Assets/RoomPlanner/Scripts/Editor/Furniture";
        public const string StreamingRoot = "Assets/StreamingAssets/Furniture";
        public const string CurationSuffix = ".curation.json";

        [Serializable]
        private class CurationItem
        {
            public string Id;
            /// <summary>Model file relative to the pack folder; defaults to "&lt;Id&gt;.glb".
            /// Packs whose assets carry textures (Poly Haven) use "&lt;Id&gt;/&lt;file&gt;.gltf".</summary>
            public string File;
            public string Name;
            public string Category;
            public string Anchor;
            public string Fit;
            public Vector3 Size;
            public float YawOffset;
        }

        [Serializable]
        private class CurationFile
        {
            public string Collection;
            public string Title;
            public string Author;
            public string License;
            public string LicenseUrl;
            public string Note;
            public string[] Exclude;
            public CurationItem[] Items;
        }

        [MenuItem("RoomPlanner/Furniture/Build bundled catalogs")]
        public static void BuildAllMenu()
        {
            string report = BuildAll();
            Debug.Log(report);
            EditorUtility.DisplayDialog("Furniture catalogs", report, "OK");
        }

        /// <summary>Headless entry (ci/unity-run.ps1 -Method …FurnitureCatalogBuilder.BuildAllBatch).</summary>
        public static void BuildAllBatch()
        {
            string report = BuildAll();
            Debug.Log(report);
            bool failed = report.IndexOf("MISSING", StringComparison.Ordinal) >= 0 ||
                          report.IndexOf("UNACCOUNTED", StringComparison.Ordinal) >= 0;
            EditorApplication.Exit(failed ? 1 : 0);
        }

        /// <summary>Rebuild every curation table found next to this script.</summary>
        public static string BuildAll()
        {
            var sb = new StringBuilder("[Furniture] catalog build\n");
            var curations = Directory.Exists(CurationDir)
                ? Directory.GetFiles(CurationDir, "*" + CurationSuffix)
                : Array.Empty<string>();

            if (curations.Length == 0) sb.AppendLine("  no curation tables found in " + CurationDir);
            var built = new List<string>();
            foreach (var path in curations)
            {
                sb.Append(Build(path));
                var curation = JsonUtility.FromJson<CurationFile>(File.ReadAllText(path));
                if (curation != null && !string.IsNullOrEmpty(curation.Collection)) built.Add(curation.Collection);
            }

            WriteIndex(built);
            sb.AppendLine($"  index: {built.Count} collection(s) → {IndexPath}");

            AssetDatabase.Refresh();
            return sb.ToString();
        }

        /// <summary>Build one pack; returns its slice of the report.</summary>
        public static string Build(string curationPath)
        {
            var sb = new StringBuilder();
            var curation = JsonUtility.FromJson<CurationFile>(File.ReadAllText(curationPath));
            if (curation == null || string.IsNullOrEmpty(curation.Collection))
                return $"  {Path.GetFileName(curationPath)}: unreadable curation table\n";

            string folder = Path.Combine(StreamingRoot, curation.Collection).Replace('\\', '/');
            if (!Directory.Exists(folder))
                return $"  {curation.Collection}: MISSING model folder {folder}\n";

            var curated = new HashSet<string>();
            var missing = new List<string>();
            var json = new StringBuilder();
            json.Append("{\n");
            json.Append($"  \"Id\": {Quote(curation.Collection)},\n");
            json.Append($"  \"Title\": {Quote(curation.Title)},\n");
            json.Append($"  \"Author\": {Quote(curation.Author)},\n");
            json.Append($"  \"License\": {Quote(curation.License)},\n");
            json.Append($"  \"LicenseUrl\": {Quote(curation.LicenseUrl)},\n");
            json.Append("  \"Items\": [\n");

            int written = 0;
            foreach (var item in curation.Items ?? Array.Empty<CurationItem>())
            {
                if (item == null || string.IsNullOrEmpty(item.Id)) continue;
                curated.Add(item.Id);

                string file = string.IsNullOrEmpty(item.File) ? item.Id + ".glb" : item.File;
                if (!FurnitureCatalogParser.IsSafeFileName(file)) { missing.Add(item.Id + " (unsafe path)"); continue; }
                if (!File.Exists(Path.Combine(folder, file))) { missing.Add(item.Id); continue; }
                if (!FurnitureCatalogParser.IsSaneSize(item.Size)) { missing.Add(item.Id + " (bad size)"); continue; }

                if (written > 0) json.Append(",\n");
                json.Append("    { ");
                json.Append($"\"Id\": {Quote(item.Id)}, ");
                json.Append($"\"Name\": {Quote(string.IsNullOrEmpty(item.Name) ? item.Id : item.Name)}, ");
                json.Append($"\"Category\": {Quote(FurnitureCatalogParser.ParseCategory(item.Category).ToString())}, ");
                json.Append($"\"Anchor\": {Quote(AnchorOrFloor(item.Anchor).ToString())}, ");
                json.Append($"\"Fit\": {Quote(FurnitureCatalogParser.ParseFit(item.Fit).ToString())}, ");
                json.Append($"\"File\": {Quote(file)}, ");
                json.Append("\"Size\": { " +
                            $"\"x\": {item.Size.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}, " +
                            $"\"y\": {item.Size.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}, " +
                            $"\"z\": {item.Size.z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} }}, ");
                json.Append($"\"YawOffset\": {item.YawOffset.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}");
                json.Append(" }");
                written++;
            }

            json.Append("\n  ]\n}\n");

            string manifest = Path.Combine(folder, FurnitureCatalogParser.ManifestName).Replace('\\', '/');
            File.WriteAllText(manifest, json.ToString());

            // Every model file in the folder must be curated or explicitly excluded —
            // otherwise the pack ships models nobody can reach from the panel.
            var excluded = new HashSet<string>(curation.Exclude ?? Array.Empty<string>());
            var unaccounted = new List<string>();
            foreach (var glb in Directory.GetFiles(folder, "*.glb"))
            {
                string id = Path.GetFileNameWithoutExtension(glb);
                if (!curated.Contains(id) && !excluded.Contains(id)) unaccounted.Add(id);
            }

            sb.AppendLine($"  {curation.Collection}: {written} items → {manifest}");
            if (missing.Count > 0) sb.AppendLine($"    MISSING models ({missing.Count}): {string.Join(", ", missing)}");
            if (unaccounted.Count > 0) sb.AppendLine($"    UNACCOUNTED models ({unaccounted.Count}): {string.Join(", ", unaccounted)}");
            return sb.ToString();
        }

        public static string IndexPath => (StreamingRoot + "/" + FurnitureIndex.FileName);

        /// <summary>
        /// StreamingAssets cannot be enumerated at runtime on Android (it lives inside the
        /// APK), so the packs that ship with the build are listed in an index file the
        /// loader reads first.
        /// </summary>
        private static void WriteIndex(List<string> collections)
        {
            var sb = new StringBuilder("{\n  \"Collections\": [ ");
            for (int i = 0; i < collections.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Quote(collections[i]));
            }
            sb.Append(" ]\n}\n");
            File.WriteAllText(IndexPath, sb.ToString());
        }

        private static FurnitureAnchor AnchorOrFloor(string text) =>
            FurnitureCatalogParser.TryParseAnchor(text, out var a) ? a : FurnitureAnchor.Floor;

        private static string Quote(string s) =>
            "\"" + (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
#endif
