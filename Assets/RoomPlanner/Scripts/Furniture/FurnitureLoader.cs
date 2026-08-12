using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using GLTFast;
using RoomPlanner.Core.Furniture;

namespace RoomPlanner.Furniture
{
    /// <summary>
    /// Turns a catalog item into a scene object (design/27 §4). One loader serves every
    /// source: bundled packs inside the APK, downloaded packs in the cache, and later
    /// online models — they all arrive as a URL that glTFast reads.
    ///
    /// Parsed files are cached per catalog key, so placing the tenth chair re-instantiates
    /// an already parsed glTF instead of re-reading and re-uploading its meshes.
    /// </summary>
    public class FurnitureLoader : MonoBehaviour
    {
        [SerializeField] private FurnitureLibrary library;

        /// <summary>Per-model triangle ceiling. The bundled CC0 packs sit around 100–1000,
        /// so this only ever bites for downloaded models (#74) — and then it reports
        /// instead of silently dropping the object.</summary>
        public const int TriangleBudget = 60000;

        private readonly Dictionary<string, GltfImport> _imports = new();

        /// <summary>Why the last load failed, for the panel's readout (null when fine).</summary>
        public string LastError { get; private set; }

        public FurnitureLibrary Library => library;

        public void Bind(FurnitureLibrary lib) => library = lib;

        /// <summary>
        /// Instantiate the model under <paramref name="parent"/>. Returns null on failure
        /// (unreadable file, unsupported glTF) with <see cref="LastError"/> set — the
        /// caller shows that, it never silently places nothing.
        /// </summary>
        public async Task<GameObject> InstantiateAsync(FurnitureItem item, Transform parent)
        {
            LastError = null;
            if (item == null) { LastError = "no item"; return null; }
            if (library == null) { LastError = "library missing"; return null; }

            string url = library.UrlOf(item);
            if (string.IsNullOrEmpty(url)) { LastError = $"{item.Key}: no file"; return null; }

            if (!_imports.TryGetValue(item.Key, out var import) || import == null)
            {
                import = new GltfImport();
                bool ok;
                try { ok = await import.Load(url); }
                catch (System.Exception e) { ok = false; LastError = e.Message; }

                if (!ok)
                {
                    import.Dispose();
                    LastError ??= $"{item.Key}: cannot read {item.File}";
                    Debug.LogWarning($"[Furniture] {LastError}");
                    return null;
                }
                _imports[item.Key] = import;
            }

            var holder = new GameObject("Model");
            holder.transform.SetParent(parent, false);
            bool instantiated = await import.InstantiateMainSceneAsync(holder.transform);
            if (!instantiated)
            {
                Destroy(holder);
                LastError = $"{item.Key}: cannot instantiate";
                Debug.LogWarning($"[Furniture] {LastError}");
                return null;
            }

            int tris = CountTriangles(holder);
            if (tris > TriangleBudget)
                Debug.LogWarning($"[Furniture] {item.Key}: {tris} triangles exceeds the {TriangleBudget} budget");

            return holder;
        }

        /// <summary>Local-space bounds of a loaded model, or a unit box when it has no
        /// renderers (a glTF that carries only lights/cameras is not placeable furniture).</summary>
        public static Bounds LocalBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            bool has = false;
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            var toRoot = root.transform.worldToLocalMatrix;

            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;

                // Mesh bounds are local to their own transform; bring the 8 corners into
                // the root's space so parent transforms inside the glTF hierarchy count.
                var local = mesh.bounds;
                var m = toRoot * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? local.min.x : local.max.x,
                        (i & 2) == 0 ? local.min.y : local.max.y,
                        (i & 4) == 0 ? local.min.z : local.max.z);
                    var p = m.MultiplyPoint3x4(corner);
                    if (!has) { bounds = new Bounds(p, Vector3.zero); has = true; }
                    else bounds.Encapsulate(p);
                }
            }

            return has ? bounds : new Bounds(Vector3.zero, Vector3.one);
        }

        private static int CountTriangles(GameObject root)
        {
            int tris = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
            return tris;
        }

        // glTFast owns the meshes and textures it parsed; dropping the loader without
        // disposing would leak them for the rest of the session (rules 12 §1.5).
        private void OnDestroy()
        {
            foreach (var import in _imports.Values) import?.Dispose();
            _imports.Clear();
        }
    }
}
