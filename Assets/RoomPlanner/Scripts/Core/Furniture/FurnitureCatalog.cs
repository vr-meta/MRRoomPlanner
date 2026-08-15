using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core.Furniture
{
    /// <summary>
    /// What a catalog item is constrained by (design/27 §2). The anchor — not the
    /// category — decides how <see cref="FurniturePlacement"/> solves a pose: a wall
    /// cabinet hangs on a wall face, a sofa stands on the floor.
    /// </summary>
    public enum FurnitureAnchor { Floor, Wall, Ceiling, Counter }

    /// <summary>Closed grouping vocabulary — the panel's Category select is built from it,
    /// so free-form strings never leak into the UI.</summary>
    public enum FurnitureCategory { Seating, Table, Storage, Bed, Kitchen, Bath, Appliance, Decor }

    /// <summary>Where a collection's files live. The tool never branches on this — the
    /// loader does (StreamingAssets vs the download cache).</summary>
    public enum FurnitureSource { Bundled, Cached, Remote }

    /// <summary>
    /// How a model is matched to its declared real-world size. Stylised CC0 packs are not
    /// proportional to reality (Kenney's kitchen unit measures 0.43 × 0.45 where the real
    /// one is 0.60 × 0.85), so boxy carcasses — units, wardrobes, appliances — are
    /// stretched per axis, while silhouette pieces (sofas, chairs, plants) keep their
    /// proportions and only scale uniformly.
    /// </summary>
    public enum FurnitureFit { Uniform, Stretch }

    /// <summary>
    /// One catalog entry. <see cref="Size"/> is the REAL-WORLD size in metres declared by
    /// the manifest, not the mesh's own extent: CC0 packs are stylised, and a planner that
    /// lies about sizes is useless — the loaded model is scaled to match (design/27 §1).
    /// </summary>
    public class FurnitureItem
    {
        /// <summary>Item id, unique inside its collection.</summary>
        public string Id;
        public string Name;
        public FurnitureCategory Category;
        public FurnitureAnchor Anchor;
        /// <summary>File name inside the collection folder (no directories — see the parser).</summary>
        public string File;
        /// <summary>Real-world size in metres: X width, Y height, Z depth.</summary>
        public Vector3 Size;
        /// <summary>How the model is matched to <see cref="Size"/> (default: keep proportions).</summary>
        public FurnitureFit Fit;
        /// <summary>Extra yaw applied to the model so its FRONT faces +Z, degrees.</summary>
        public float YawOffset;
        /// <summary>Triangle count reported by the catalog builder; 0 = unknown.</summary>
        public int Tris;
        /// <summary>Owning collection id — the item half of <see cref="Key"/>.</summary>
        public string CollectionId;

        /// <summary>Catalog-wide address "collection/item" (project files store this).</summary>
        public string Key => FurnitureCatalog.MakeKey(CollectionId, Id);
    }

    /// <summary>
    /// A pack: manifest metadata plus its items. Bundled packs, downloaded packs and
    /// (later) online packs all have this shape, so the tool cannot tell them apart —
    /// only <see cref="Source"/> and <see cref="RootPath"/> differ.
    /// </summary>
    public class FurnitureCollection
    {
        public string Id;
        public string Title;
        public string Author;
        public string License;
        public string LicenseUrl;
        public FurnitureSource Source;
        /// <summary>Folder holding the manifest and the model files.</summary>
        public string RootPath;
        public readonly List<FurnitureItem> Items = new();

        /// <summary>True when the license obliges us to name the author on a credits screen.</summary>
        public bool NeedsAttribution =>
            !string.IsNullOrEmpty(License) &&
            License.IndexOf("CC0", System.StringComparison.OrdinalIgnoreCase) < 0 &&
            License.IndexOf("public domain", System.StringComparison.OrdinalIgnoreCase) < 0;

        public string DisplayTitle => string.IsNullOrEmpty(Title) ? Id : Title;
    }

    /// <summary>
    /// The registry of loaded collections (design/27 §1). Order is the order collections
    /// were added — the panel's Collection select shows exactly that, and it must not
    /// reshuffle between sessions.
    /// </summary>
    public class FurnitureCatalog
    {
        private readonly List<FurnitureCollection> _collections = new();

        public IReadOnlyList<FurnitureCollection> Collections => _collections;
        public int Count => _collections.Count;

        public static string MakeKey(string collectionId, string itemId) =>
            string.IsNullOrEmpty(collectionId) ? itemId : collectionId + "/" + itemId;

        /// <summary>
        /// Register a collection. A second collection with the same id is rejected
        /// (returns false) rather than shadowing the first — two packs claiming one id
        /// would make every stored "collection/item" key ambiguous.
        /// </summary>
        public bool Add(FurnitureCollection collection)
        {
            if (collection == null || string.IsNullOrEmpty(collection.Id)) return false;
            if (Find(collection.Id) != null) return false;
            _collections.Add(collection);
            return true;
        }

        public bool Remove(string collectionId)
        {
            var c = Find(collectionId);
            if (c == null) return false;
            _collections.Remove(c);
            return true;
        }

        public void Clear() => _collections.Clear();

        public FurnitureCollection Find(string collectionId)
        {
            if (string.IsNullOrEmpty(collectionId)) return null;
            for (int i = 0; i < _collections.Count; i++)
                if (_collections[i].Id == collectionId) return _collections[i];
            return null;
        }

        /// <summary>Resolve a stored "collection/item" key; null when either half is gone
        /// (the project loader turns that into a labelled placeholder, never a silent drop).</summary>
        public FurnitureItem FindItem(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int slash = key.LastIndexOf('/');
            if (slash <= 0 || slash == key.Length - 1) return null;
            return FindItem(key.Substring(0, slash), key.Substring(slash + 1));
        }

        public FurnitureItem FindItem(string collectionId, string itemId)
        {
            var c = Find(collectionId);
            if (c == null || string.IsNullOrEmpty(itemId)) return null;
            for (int i = 0; i < c.Items.Count; i++)
                if (c.Items[i].Id == itemId) return c.Items[i];
            return null;
        }

        /// <summary>
        /// Items of one collection, optionally filtered by category, appended to
        /// <paramref name="result"/> in manifest order. Caller-owned list — the aiming
        /// path must not allocate per frame (rules 12 §4.1).
        /// </summary>
        public void ItemsOf(string collectionId, FurnitureCategory? category, List<FurnitureItem> result)
        {
            if (result == null) return;
            result.Clear();
            var c = Find(collectionId);
            if (c == null) return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                var item = c.Items[i];
                if (category.HasValue && item.Category != category.Value) continue;
                result.Add(item);
            }
        }

        /// <summary>Categories actually present in a collection, in enum order — the
        /// select must not offer empty categories.</summary>
        public void CategoriesOf(string collectionId, List<FurnitureCategory> result)
        {
            if (result == null) return;
            result.Clear();
            var c = Find(collectionId);
            if (c == null) return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                var cat = c.Items[i].Category;
                if (!result.Contains(cat)) result.Add(cat);
            }
            result.Sort();
        }
    }
}
