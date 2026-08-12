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
            return _settings ??= new SettingsSchema()
                .Readout("size", "Size", () =>
                    $"{size.x * 100f:0} × {size.z * 100f:0} × {size.y * 100f:0} cm")
                .Stepper("yaw", "Rotate", () => $"{yaw:0}°",
                    () => SetYaw(yaw - YawStep), () => SetYaw(yaw + YawStep))
                .Readout("source", "Source", () => string.IsNullOrEmpty(credit) ? "—" : credit);
        }
    }
}
