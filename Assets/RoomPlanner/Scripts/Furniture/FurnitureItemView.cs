using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Core.Furniture;
using RoomPlanner.Editing;

namespace RoomPlanner.Furniture
{
    /// <summary>
    /// A placed catalog item (design/27 §3). The object's own transform sits at the
    /// item's BOTTOM CENTRE — the same convention the placement solver uses — and the
    /// loaded model hangs under it, scaled and recentred to the curated real-world size.
    ///
    /// Everything the inspector shows about the piece comes from here (size, rotation,
    /// where the model came from), so the panel needs no knowledge of the catalog.
    /// </summary>
    public class FurnitureItemView : MonoBehaviour, ISettingsProvider
    {
        [SerializeField] private string collectionId;
        [SerializeField] private string itemId;
        [SerializeField] private string displayName;
        [SerializeField] private Vector3 size = Vector3.one;
        [SerializeField] private float yaw;
        [SerializeField] private int anchor;      // FurnitureAnchor as int (inspector-friendly)
        [SerializeField] private string credit;   // "Kenney · CC0"

        private Transform _model;
        private BoxCollider _box;
        private SettingsSchema _settings;

        // Generated pieces (slat partitions, #86): parameters instead of a model file.
        [SerializeField] private bool procedural;
        [SerializeField] private float slat = 0.04f;
        [SerializeField] private float gap = 0.05f;
        private Mesh _mesh;
        private static readonly List<Vector3> Verts = new();
        private static readonly List<int> Tris = new();
        private static readonly List<Vector3> Norms = new();

        public bool IsProcedural => procedural;

        /// <summary>Stored in the project file as "collection/item" (#73).</summary>
        public string CatalogKey => FurnitureCatalog.MakeKey(collectionId, itemId);
        public string CollectionId => collectionId;
        public string ItemId => itemId;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? itemId : displayName;
        public Vector3 Size => size;
        public float Yaw => yaw;
        public FurnitureAnchor Anchor => (FurnitureAnchor)anchor;
        public string Credit => credit;

        /// <summary>Yaw step for the per-instance stepper — the wall tool's convention.</summary>
        public const float YawStep = 15f;

        /// <summary>
        /// Adopt a freshly loaded model: scale it to the declared size (per the item's
        /// fit rule) and slide it so its bottom centre lands on this transform's origin.
        /// The model's own pivot is arbitrary — catalog packs are not authored to a
        /// convention — so the offset is computed from its actual bounds.
        /// </summary>
        public void Bind(FurnitureItem item, GameObject model, FurnitureCollection collection)
        {
            collectionId = item.CollectionId;
            itemId = item.Id;
            displayName = item.Name;
            size = item.Size;
            anchor = (int)item.Anchor;
            credit = collection == null ? null
                : string.IsNullOrEmpty(collection.Author) ? collection.License
                : $"{collection.Author} · {collection.License}";

            procedural = item.IsProcedural;
            if (procedural)
            {
                Rebuild();
                EnsureCollider();
                return;
            }

            _model = model != null ? model.transform : null;
            if (_model != null)
            {
                var bounds = FurnitureLoader.LocalBounds(model);
                var scale = FurniturePlacement.FitScaleAxes(bounds.size, item.Size, item.Fit);
                _model.localScale = scale;
                _model.localRotation = Quaternion.Euler(0f, item.YawOffset, 0f);

                // bottom-centre alignment, in the scaled model's space
                var centre = Vector3.Scale(bounds.center, scale);
                var half = Vector3.Scale(bounds.extents, scale);
                _model.localPosition = new Vector3(-centre.x, -(centre.y - half.y), -centre.z);
            }

            EnsureCollider();
        }

        /// <summary>Pick target for the Select and Move paths: a box the size of the piece.
        /// A box beats a mesh collider here — it is what the user aims at, it costs
        /// nothing per frame, and the ghost preview uses the same extents.</summary>
        private void EnsureCollider()
        {
            if (_box == null) _box = GetComponent<BoxCollider>();
            if (_box == null) _box = gameObject.AddComponent<BoxCollider>();
            _box.center = new Vector3(0f, size.y * 0.5f, 0f);
            _box.size = size;
        }

        /// <summary>Material for generated pieces; wired by the tool from the rig assets.</summary>
        public Material ProceduralMaterial { get; set; }

        /// <summary>
        /// Regenerate a procedural piece (slat partition). One mesh per instance, replaced
        /// in place and released in OnDestroy — Destroy(gameObject) does not free meshes
        /// (rules 12 §1.5).
        /// </summary>
        public void Rebuild()
        {
            if (!procedural) return;
            PartitionMesh.Build(size.x, size.y, slat, gap, size.z, Verts, Tris, Norms);

            var filter = GetComponent<MeshFilter>();
            if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
            var renderer = GetComponent<MeshRenderer>();
            if (renderer == null) renderer = gameObject.AddComponent<MeshRenderer>();
            if (ProceduralMaterial != null && renderer.sharedMaterial == null)
                renderer.sharedMaterial = ProceduralMaterial;

            if (_mesh == null) { _mesh = new Mesh { name = "Partition" }; }
            _mesh.Clear();
            _mesh.SetVertices(Verts);
            _mesh.SetTriangles(Tris, 0);
            _mesh.SetNormals(Norms);
            _mesh.RecalculateBounds();
            filter.sharedMesh = _mesh;
            EnsureCollider();
        }

        private void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
            _mesh = null;
        }

        public void ApplyPose(in FurniturePose pose)
        {
            if (!pose.Valid) return;
            transform.position = pose.Position;
            SetYaw(pose.Yaw);
        }

        public void SetYaw(float value)
        {
            yaw = FurniturePlacement.Normalize360(value);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        public void MoveBy(Vector3 delta) => transform.position += delta;

        public string Describe() =>
            $"{DisplayName} · {size.x * 100f:0}×{size.z * 100f:0}×{size.y * 100f:0} cm";

        /// <summary>Per-instance rows shown when the piece is selected (design/20 §2).</summary>
        public SettingsSchema GetSettings()
        {
            if (_settings != null) return _settings;

            // A generated piece is sized by its parameters — that is the whole point of
            // generating it (design/27 §3c), so the inspector edits them directly.
            if (procedural)
                return _settings = new SettingsSchema()
                    .Slider("w", "Width", 0.3f, 4f, 0.05f, () => size.x,
                        v => { size.x = v; Rebuild(); }, (_, v) => { size.x = v; Rebuild(); },
                        () => $"{size.x * 100f:0} cm", displayScale: 100f)
                    .Slider("h", "Height", 0.4f, 3f, 0.05f, () => size.y,
                        v => { size.y = v; Rebuild(); }, (_, v) => { size.y = v; Rebuild(); },
                        () => $"{size.y * 100f:0} cm", displayScale: 100f)
                    .Slider("slat", "Slat", 0.02f, 0.15f, 0.005f, () => slat,
                        v => { slat = v; Rebuild(); }, (_, v) => { slat = v; Rebuild(); },
                        () => $"{slat * 100f:0.#} cm", displayScale: 100f)
                    .Slider("gap", "Gap", 0.01f, 0.30f, 0.005f, () => gap,
                        v => { gap = v; Rebuild(); }, (_, v) => { gap = v; Rebuild(); },
                        () => $"{gap * 100f:0.#} cm", displayScale: 100f)
                    .Stepper("yaw", "Rotate", () => $"{yaw:0}°",
                        () => SetYaw(yaw - YawStep), () => SetYaw(yaw + YawStep))
                    .Readout("slats", "Slats",
                        () => PartitionMesh.SlatCount(size.x, slat, gap).ToString());

            return _settings = new SettingsSchema()
                .Readout("size", "Size", () =>
                    $"{size.x * 100f:0} × {size.z * 100f:0} × {size.y * 100f:0} cm")
                .Stepper("yaw", "Rotate", () => $"{yaw:0}°",
                    () => SetYaw(yaw - YawStep), () => SetYaw(yaw + YawStep))
                .Readout("source", "Source", () => string.IsNullOrEmpty(credit) ? "—" : credit);
        }
    }
}
