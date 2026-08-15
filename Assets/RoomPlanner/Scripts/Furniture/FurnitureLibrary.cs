using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using RoomPlanner.Core.Furniture;

namespace RoomPlanner.Furniture
{
    /// <summary>
    /// Owns the runtime <see cref="FurnitureCatalog"/> (design/27 §1): reads the bundled
    /// packs out of StreamingAssets and the downloaded ones out of the cache folder, so
    /// the tool sees one list of collections and never learns where a model came from.
    ///
    /// StreamingAssets is a compressed blob inside the APK on Android — it cannot be
    /// enumerated and cannot be read with File IO, hence the index file plus
    /// UnityWebRequest. The download cache is an ordinary directory, so it is read
    /// directly and simply stays empty until #74 ships.
    /// </summary>
    public class FurnitureLibrary : MonoBehaviour
    {
        public const string BundledFolder = "Furniture";
        public const string CacheFolder = "FurnitureCache";

        public FurnitureCatalog Catalog { get; } = new();

        /// <summary>False until the bundled packs finished loading — the panel shows a
        /// progress row instead of an empty (and misleading) collection list.</summary>
        public bool Ready { get; private set; }

        /// <summary>Human-readable outcome for the panel: counts, or the reason nothing loaded.</summary>
        public string Status { get; private set; } = "loading…";

        private readonly List<string> _problems = new();

        /// <summary>Problems found while loading manifests (dropped rows, missing packs).</summary>
        public IReadOnlyList<string> Problems => _problems;

        public static string BundledRoot => Path.Combine(Application.streamingAssetsPath, BundledFolder);
        public static string CacheRoot => Path.Combine(Application.persistentDataPath, CacheFolder);

        private void Awake() => StartCoroutine(LoadAll());

        private IEnumerator LoadAll()
        {
            int packs = 0, items = 0;

            // ---- bundled packs (index → manifests) ----
            string indexUrl = ToUrl(Path.Combine(BundledRoot, FurnitureIndex.FileName));
            yield return ReadText(indexUrl, text =>
            {
                foreach (string id in FurnitureIndex.Parse(text))
                    _pending.Enqueue(id);
            });

            while (_pending.Count > 0)
            {
                string id = _pending.Dequeue();
                string folder = Path.Combine(BundledRoot, id);
                string url = ToUrl(Path.Combine(folder, FurnitureCatalogParser.ManifestName));
                string manifest = null;
                yield return ReadText(url, t => manifest = t);

                if (string.IsNullOrEmpty(manifest))
                {
                    _problems.Add($"{id}: manifest unreadable");
                    continue;
                }
                if (TryAdd(manifest, FurnitureSource.Bundled, folder, id, out int n)) { packs++; items += n; }
            }

            // ---- downloaded packs (plain filesystem) ----
            if (Directory.Exists(CacheRoot))
            {
                foreach (string folder in Directory.GetDirectories(CacheRoot))
                {
                    string manifest = Path.Combine(folder, FurnitureCatalogParser.ManifestName);
                    if (!File.Exists(manifest)) continue;
                    if (TryAdd(File.ReadAllText(manifest), FurnitureSource.Cached, folder,
                        Path.GetFileName(folder), out int n)) { packs++; items += n; }
                }
            }

            Ready = true;
            Status = packs == 0
                ? "no furniture packs found"
                : $"{packs} collection{(packs == 1 ? "" : "s")} · {items} items";
            Debug.Log($"[Furniture] library: {Status}" +
                      (_problems.Count > 0 ? $" · {_problems.Count} problem(s): {string.Join("; ", _problems)}" : ""));
        }

        private readonly Queue<string> _pending = new();

        private bool TryAdd(string manifest, FurnitureSource source, string folder, string id, out int count)
        {
            count = 0;
            var collection = FurnitureCatalogParser.Parse(manifest, source, folder, out var report);
            if (collection == null)
            {
                _problems.Add($"{id}: {report}");
                return false;
            }
            if (report.HasProblems && report.Problems != null)
                _problems.Add($"{id}: dropped {report.Rejected} ({string.Join(", ", report.Problems)})");
            if (!Catalog.Add(collection))
            {
                _problems.Add($"{id}: duplicate collection id");
                return false;
            }
            count = collection.Items.Count;
            return true;
        }

        /// <summary>Absolute path of an item's model file (a URL on Android).</summary>
        public string UrlOf(FurnitureItem item)
        {
            var collection = item != null ? Catalog.Find(item.CollectionId) : null;
            if (collection == null || string.IsNullOrEmpty(item.File)) return null;
            return ToUrl(Path.Combine(collection.RootPath, item.File));
        }

        /// <summary>
        /// UnityWebRequest needs a URI; on Android StreamingAssets already IS one
        /// ("jar:file://…/base.apk!/assets/…"), everywhere else a plain path needs the
        /// file:// scheme.
        /// </summary>
        public static string ToUrl(string path)
        {
            string p = path.Replace('\\', '/');
            return p.Contains("://") ? p : "file://" + p;
        }

        private static IEnumerator ReadText(string url, System.Action<string> onText)
        {
            using var request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();
            onText(request.result == UnityWebRequest.Result.Success ? request.downloadHandler.text : null);
        }
    }
}
