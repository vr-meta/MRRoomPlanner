using System.Collections.Generic;

namespace RoomPlanner.Core.Project
{
    /// <summary>
    /// Pure naming rules for the named-project catalog (design/06 «Проекты v1», #58).
    /// Quest has no keyboard, so names are generated — "Project N" with the smallest
    /// free N. File I/O lives in Import/ProjectPaths; this is testable string logic.
    /// </summary>
    public static class ProjectCatalog
    {
        public const string Extension = ".rp.json";
        public const string Prefix = "Project ";

        /// <summary>Reserved internal file name — never shown as a project.</summary>
        public const string AutosaveName = "autosave";

        public static string FileName(string name) => name + Extension;

        /// <summary>Project name for a catalog file, or null when the file is not a
        /// project (wrong extension, empty, or the reserved autosave).</summary>
        public static string NameOf(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(Extension)) return null;
            string name = fileName.Substring(0, fileName.Length - Extension.Length);
            if (name.Length == 0 || name == AutosaveName) return null;
            return name;
        }

        /// <summary>"Project N" with the smallest N not taken — a deleted slot is reused.</summary>
        public static string NextName(IReadOnlyList<string> existing)
        {
            var taken = new HashSet<string>();
            if (existing != null)
                foreach (var e in existing) taken.Add(e);
            for (int n = 1; ; n++)
            {
                string candidate = Prefix + n;
                if (!taken.Contains(candidate)) return candidate;
            }
        }

        /// <summary>Numeric-aware order: "Project 2" before "Project 10"; non-generated
        /// names (future import/rename sources) follow ordinally after.</summary>
        public static int CompareNames(string a, string b)
        {
            bool na = TryNumber(a, out int ia), nb = TryNumber(b, out int ib);
            if (na && nb) return ia.CompareTo(ib);
            if (na != nb) return na ? -1 : 1;
            return string.CompareOrdinal(a, b);
        }

        private static bool TryNumber(string name, out int n)
        {
            n = 0;
            return name != null && name.StartsWith(Prefix)
                && int.TryParse(name.Substring(Prefix.Length), out n);
        }
    }
}
