using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RoomPlanner.Core.Project;

namespace RoomPlanner.Import
{
    /// <summary>
    /// Where named projects live and which one is open (#58). The current name
    /// persists in PlayerPrefs so a relaunch reopens the same project; an empty name
    /// means an unnamed scene backed by the legacy autosave file (persistence v0).
    /// </summary>
    public static class ProjectPaths
    {
        private const string PrefKey = "rp.currentProject";

        public static string Root => Path.Combine(Application.persistentDataPath, "projects");

        public static string AutosavePath =>
            Path.Combine(Application.persistentDataPath,
                ProjectCatalog.FileName(ProjectCatalog.AutosaveName));

        public static string CurrentName
        {
            get => PlayerPrefs.GetString(PrefKey, "");
            set
            {
                PlayerPrefs.SetString(PrefKey, value ?? "");
                PlayerPrefs.Save();   // Quest apps die silently — persist immediately
            }
        }

        public static bool HasCurrent => !string.IsNullOrEmpty(CurrentName);

        /// <summary>Autosave target: the open project's file, or the unnamed autosave.</summary>
        public static string CurrentSavePath =>
            HasCurrent ? PathFor(CurrentName) : AutosavePath;

        public static string PathFor(string name) =>
            Path.Combine(Root, ProjectCatalog.FileName(name));

        /// <summary>Existing project names in numeric-aware order (stable Select list).</summary>
        public static List<string> ListNames()
        {
            var names = new List<string>();
            try
            {
                if (Directory.Exists(Root))
                    foreach (var f in Directory.GetFiles(Root, "*" + ProjectCatalog.Extension))
                    {
                        var n = ProjectCatalog.NameOf(Path.GetFileName(f));
                        if (n != null) names.Add(n);
                    }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Projects] list failed: {e.Message}");
            }
            names.Sort(ProjectCatalog.CompareNames);
            return names;
        }
    }
}
