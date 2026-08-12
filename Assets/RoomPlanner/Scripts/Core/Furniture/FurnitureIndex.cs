using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core.Furniture
{
    /// <summary>
    /// The list of packs that ship inside the build. StreamingAssets cannot be enumerated
    /// at runtime on Android (it lives compressed inside the APK), so the builder writes
    /// this index next to the pack folders and the loader reads it first.
    /// </summary>
    public static class FurnitureIndex
    {
        public const string FileName = "collections.json";

        [Serializable]
        private class IndexDto
        {
            public string[] Collections;
        }

        /// <summary>
        /// Parse the index; unreadable or empty content yields an empty list rather than
        /// an exception — a missing index means "no bundled packs", which the panel can
        /// report, not a crash on startup.
        /// </summary>
        public static List<string> Parse(string json)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(json)) return result;

            IndexDto dto;
            try { dto = JsonUtility.FromJson<IndexDto>(json); }
            catch (Exception) { return result; }
            if (dto?.Collections == null) return result;

            foreach (var id in dto.Collections)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (!IsSafeCollectionId(id)) continue;
                if (!result.Contains(id)) result.Add(id);
            }
            return result;
        }

        /// <summary>A collection id is a folder name — never a path.</summary>
        public static bool IsSafeCollectionId(string id) =>
            !string.IsNullOrEmpty(id) &&
            id.IndexOf('/') < 0 && id.IndexOf('\\') < 0 &&
            id.IndexOf(':') < 0 && id.IndexOf("..", StringComparison.Ordinal) < 0;
    }
}
