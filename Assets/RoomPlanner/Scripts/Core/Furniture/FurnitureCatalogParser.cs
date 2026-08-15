using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core.Furniture
{
    /// <summary>
    /// Why a manifest entry was dropped — the parser counts and names them instead of
    /// quietly producing a broken item (rules 12 §1.3: no silent degradation).
    /// </summary>
    public enum FurnitureRejectReason
    {
        MissingId, MissingFile, UnsafeFile, BadSize, UnknownAnchor, DuplicateId,
    }

    /// <summary>Outcome of parsing one manifest: what was accepted, what was dropped and why.</summary>
    public struct FurnitureParseReport
    {
        public int Accepted;
        public int Rejected;
        public List<string> Problems;   // "sofa: BadSize" lines, in manifest order

        public bool HasProblems => Rejected > 0;

        public void Note(string itemId, FurnitureRejectReason reason)
        {
            Rejected++;
            Problems ??= new List<string>();
            Problems.Add((string.IsNullOrEmpty(itemId) ? "<no id>" : itemId) + ": " + reason);
        }

        public override string ToString() =>
            Rejected == 0 ? $"{Accepted} items" : $"{Accepted} items, {Rejected} dropped";
    }

    /// <summary>
    /// Reads a pack's <c>collection.json</c> (design/27 §1). JsonUtility-friendly DTOs in the
    /// same style as the project format (Core/Project/ProjectData.cs); enums arrive as
    /// strings so a manifest stays readable and survives enum reordering.
    ///
    /// Tolerance policy: an unreadable FILE means no collection at all (null + reason);
    /// a broken ITEM is dropped and counted, because one bad row must not cost the user
    /// the other 139 models in the pack.
    /// </summary>
    public static class FurnitureCatalogParser
    {
        /// <summary>Manifest file name inside a collection folder.</summary>
        public const string ManifestName = "collection.json";

        /// <summary>Sanity ceiling for a declared size, metres — beyond this the manifest
        /// is wrong (a 40 m sofa would blow up the fit-scale and the placement ghost).</summary>
        public const float MaxSizeMeters = 20f;

        [Serializable]
        private class ItemDto
        {
            public string Id;
            public string Name;
            public string Category;
            public string Subcategory;
            public string Anchor;
            public string File;
            public string Preview;
            public Vector3 Size;
            public string Fit;
            public float YawOffset;
            public int Tris;
        }

        [Serializable]
        private class CollectionDto
        {
            public string Id;
            public string Title;
            public string Author;
            public string License;
            public string LicenseUrl;
            /// <summary>"true"/"false"; absent = commercial use allowed (the CC0 default).</summary>
            public string CommercialUse;
            public ItemDto[] Items;
        }

        /// <summary>
        /// Parse a manifest. Returns null when the JSON itself is unusable (malformed, or
        /// no collection id) — <paramref name="report"/> still carries the reason.
        /// </summary>
        public static FurnitureCollection Parse(string json, FurnitureSource source, string rootPath,
            out FurnitureParseReport report)
        {
            report = default;
            if (string.IsNullOrWhiteSpace(json))
            {
                report.Note("<manifest>", FurnitureRejectReason.MissingFile);
                return null;
            }

            CollectionDto dto;
            try { dto = JsonUtility.FromJson<CollectionDto>(json); }
            catch (Exception) { dto = null; }

            if (dto == null || string.IsNullOrEmpty(dto.Id))
            {
                report.Note("<manifest>", FurnitureRejectReason.MissingId);
                return null;
            }

            var collection = new FurnitureCollection
            {
                Id = dto.Id,
                Title = dto.Title,
                Author = dto.Author,
                License = dto.License,
                LicenseUrl = dto.LicenseUrl,
                CommercialUse = !string.Equals(dto.CommercialUse, "false",
                    StringComparison.OrdinalIgnoreCase),
                Source = source,
                RootPath = rootPath,
            };

            if (dto.Items == null) return collection;

            foreach (var raw in dto.Items)
            {
                if (raw == null) continue;
                if (string.IsNullOrEmpty(raw.Id)) { report.Note(raw.Name, FurnitureRejectReason.MissingId); continue; }
                if (string.IsNullOrEmpty(raw.File)) { report.Note(raw.Id, FurnitureRejectReason.MissingFile); continue; }
                if (!IsSafeFileName(raw.File)) { report.Note(raw.Id, FurnitureRejectReason.UnsafeFile); continue; }
                if (!IsSaneSize(raw.Size)) { report.Note(raw.Id, FurnitureRejectReason.BadSize); continue; }
                if (!TryParseAnchor(raw.Anchor, out var anchor)) { report.Note(raw.Id, FurnitureRejectReason.UnknownAnchor); continue; }
                if (Contains(collection.Items, raw.Id)) { report.Note(raw.Id, FurnitureRejectReason.DuplicateId); continue; }

                collection.Items.Add(new FurnitureItem
                {
                    Id = raw.Id,
                    Name = string.IsNullOrEmpty(raw.Name) ? raw.Id : raw.Name,
                    // An unknown category only affects grouping, so it degrades to Decor;
                    // an unknown anchor changes PHYSICS and is rejected above instead.
                    Category = ParseCategory(raw.Category),
                    Subcategory = string.IsNullOrWhiteSpace(raw.Subcategory) ? null : raw.Subcategory.Trim(),
                    Anchor = anchor,
                    File = raw.File,
                    // An unsafe preview path costs the thumbnail, not the item: a missing
                    // picture degrades to a text label, a bad path would be a hole (#83).
                    Preview = IsSafeFileName(raw.Preview) ? raw.Preview : null,
                    Size = raw.Size,
                    Fit = ParseFit(raw.Fit),
                    YawOffset = Mathf.Repeat(raw.YawOffset, 360f),
                    Tris = Mathf.Max(0, raw.Tris),
                    CollectionId = collection.Id,
                });
                report.Accepted++;
            }

            return collection;
        }

        /// <summary>
        /// A model file is a path RELATIVE TO THE PACK FOLDER. Sub-folders are allowed —
        /// a .gltf keeps its textures next to it, so packs are naturally one folder per
        /// asset — but nothing may leave the pack: no "..", no absolute path, no drive
        /// letter, no backslash. This is what stops a downloaded manifest (#74) from
        /// pointing the loader at the rest of the device.
        /// </summary>
        public static bool IsSafeFileName(string file)
        {
            if (string.IsNullOrEmpty(file)) return false;
            if (file.IndexOf('\\') >= 0) return false;
            if (file.IndexOf(':') >= 0) return false;
            if (file.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            if (file[0] == '/') return false;
            return true;
        }

        public static bool IsSaneSize(Vector3 size) =>
            size.x > 0f && size.y > 0f && size.z > 0f &&
            size.x <= MaxSizeMeters && size.y <= MaxSizeMeters && size.z <= MaxSizeMeters;

        public static bool TryParseAnchor(string text, out FurnitureAnchor anchor)
        {
            // Absent anchor is the common case in a hand-written manifest and means the
            // ordinary thing: it stands on the floor.
            if (string.IsNullOrEmpty(text)) { anchor = FurnitureAnchor.Floor; return true; }
            return Enum.TryParse(text, ignoreCase: true, out anchor) && Enum.IsDefined(typeof(FurnitureAnchor), anchor);
        }

        /// <summary>Unknown or absent fit keeps proportions — the safe answer for a model
        /// whose shape we have not curated.</summary>
        public static FurnitureFit ParseFit(string text)
        {
            if (!string.IsNullOrEmpty(text) &&
                Enum.TryParse<FurnitureFit>(text, ignoreCase: true, out var fit) &&
                Enum.IsDefined(typeof(FurnitureFit), fit))
                return fit;
            return FurnitureFit.Uniform;
        }

        public static FurnitureCategory ParseCategory(string text)
        {
            if (!string.IsNullOrEmpty(text) &&
                Enum.TryParse<FurnitureCategory>(text, ignoreCase: true, out var cat) &&
                Enum.IsDefined(typeof(FurnitureCategory), cat))
                return cat;
            return FurnitureCategory.Decor;
        }

        private static bool Contains(List<FurnitureItem> items, string id)
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i].Id == id) return true;
            return false;
        }
    }
}
