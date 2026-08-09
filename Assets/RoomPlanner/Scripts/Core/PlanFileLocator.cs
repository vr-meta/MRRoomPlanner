using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RoomPlanner.Core
{
    /// <summary>
    /// Finds floorplan image candidates on disk (design/15 BP6). Pure System.IO —
    /// unit-testable; the Blueprint tool feeds it the device folders (Downloads, Pictures,
    /// the app's own folder) and cycles the result in the inspector.
    /// </summary>
    public static class PlanFileLocator
    {
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };

        /// <summary>Image files from the given directories, newest first (see FindFiles).</summary>
        public static List<string> FindImages(IEnumerable<string> directories, int max = 20)
            => FindFiles(directories, ImageExtensions, max);

        /// <summary>
        /// Files with one of the given extensions (lowercase, with dot) from the given
        /// directories, newest first, capped at <paramref name="max"/>. Missing/unreadable
        /// directories are skipped silently (device folders may be permission-gated).
        /// </summary>
        public static List<string> FindFiles(IEnumerable<string> directories,
            IReadOnlyList<string> extensions, int max = 20)
        {
            var found = new List<(string path, DateTime time)>();
            foreach (var dir in directories)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        bool match = false;
                        for (int i = 0; i < extensions.Count && !match; i++)
                            match = extensions[i] == ext;
                        if (!match) continue;
                        found.Add((f, File.GetLastWriteTimeUtc(f)));
                    }
                }
                catch (Exception)
                {
                    // unreadable directory — not an error, just nothing to offer from it
                }
            }
            return found
                .OrderByDescending(e => e.time)
                .Take(Math.Max(0, max))
                .Select(e => e.path)
                .ToList();
        }
    }
}
