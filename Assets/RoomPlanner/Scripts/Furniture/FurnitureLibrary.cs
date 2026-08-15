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

            AddPartitions();
            packs++;

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

        /// <summary>Id of the generated collection (design/27 §3c).</summary>
        public const string PartitionsId = "partitions";

        /// <summary>
        /// The generated pack: slat screens are parameters, not files, so they are always
        /// available — even with no packs installed at all — and always fit the opening
        /// they are sized to (#86).
        /// </summary>
        private void AddPartitions()
        {
            var collection = new FurnitureCollection
            {
                Id = PartitionsId,
                Title = "Partitions",
                Author = "MRRoomPlanner",
                License = "CC0",
                Source = FurnitureSource.Bundled,
                RootPath = null,
            };

            void Add(string id, string name, string sub, Vector3 size) =>
                collection.Items.Add(new FurnitureItem
                {
                    Id = id, Name = name, Category = FurnitureCategory.Storage,
                    Subcategory = sub, Anchor = FurnitureAnchor.Floor,
                    Fit = FurnitureFit.Stretch, Size = size, CollectionId = collection.Id,
                });

            Add("slat-room", "Slat divider", "Partition", new Vector3(1.20f, 2.20f, 0.05f));
            Add("slat-half", "Slat screen (half)", "Partition", new Vector3(1.20f, 1.20f, 0.05f));
            Add("slat-wide", "Slat divider (wide)", "Partition", new Vector3(2.40f, 2.20f, 0.05f));
            Add("slat-head", "Slat headboard", "Partition", new Vector3(1.60f, 1.00f, 0.05f));

            Catalog.Add(collection);
        }

        private readonly Dictionary<string, Texture2D> _previews = new();
        private readonly HashSet<string> _previewsMissing = new();

        /// <summary>
        /// Thumbnail for a catalog item, or null when the pack ships none (the picker then
        /// falls back to text). Cached per item and loaded lazily — the picker asks for a
        /// few dozen at a time and must not read the disk twice for the same chip.
        /// </summary>
        public Texture2D PreviewOf(FurnitureItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Preview)) return null;
            string key = item.Key;
            if (_previews.TryGetValue(key, out var cached)) return cached;
            if (_previewsMissing.Contains(key)) return null;

            var collection = Catalog.Find(item.CollectionId);
            if (collection == null) return null;
            string path = Path.Combine(collection.RootPath, item.Preview);

            byte[] bytes = null;
            if (File.Exists(path)) bytes = File.ReadAllBytes(path);
            else if (collection.Source == FurnitureSource.Bundled)
            {
                // Inside the APK: StreamingAssets is not a file system on Android.
                using var request = UnityWebRequest.Get(ToUrl(path));
                request.SendWebRequest();
                while (!request.isDone) { }          // previews are tiny and load-time only
                if (request.result == UnityWebRequest.Result.Success)
                    bytes = request.downloadHandler.data;
            }

            if (bytes == null || bytes.Length == 0)
            {
                _previewsMissing.Add(key);
                return null;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            if (!tex.LoadImage(bytes))
            {
                Destroy(tex);
                _previewsMissing.Add(key);
                return null;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            _previews[key] = tex;
            return tex;
        }

        // Textures created here are ours to release (rules 12 §1.5).
        private void OnDestroy()
        {
            foreach (var t in _previews.Values) if (t != null) Destroy(t);
            _previews.Clear();
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
