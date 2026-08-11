#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor.Android;
using UnityEngine;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Keep exactly ONE libopenxr_loader.so in the APK. Unity's OpenXR package and Meta's
    /// OVRPlugin each ship one, and Gradle dies in :launcher:mergeReleaseNativeLibs with
    /// "2 files found with path lib/arm64-v8a/libopenxr_loader.so".
    ///
    /// CiTools.ExcludeDuplicateOpenXRLoaders tries to solve this the tidy way, through the
    /// PluginImporter — but com.unity.xr.openxr lives in Library/PackageCache, and importer
    /// settings on an IMMUTABLE package do not stick: SaveAndReimport is a no-op there, so
    /// the exported Gradle project still declares the aar (verified 2026-08-11, this is why
    /// the importer-only workaround stopped working in a fresh worktree).
    ///
    /// So we cut it out of the GENERATED project instead — after the export, before Gradle:
    /// drop libs/openxr_loader.aar and its dependency line. Meta's loader (OVRPlugin.aar) is
    /// the one Quest needs; without OVRPlugin present nothing is touched, so a plain OpenXR
    /// build keeps its loader.
    /// </summary>
    public class AndroidOpenXRLoaderFix : IPostGenerateGradleAndroidProject
    {
        /// <summary>After Meta's own OVRGradleGeneration, so we see its final project.</summary>
        public int callbackOrder => 100;

        private const string LoaderAar = "openxr_loader";
        private const string MetaAar = "OVRPlugin.aar";

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // Normal build: path IS unityLibrary. Exported project: path is its root.
            if (!Directory.Exists(Path.Combine(path, "libs"))
                && Directory.Exists(Path.Combine(path, "unityLibrary", "libs")))
                path = Path.Combine(path, "unityLibrary");
            string libs = Path.Combine(path, "libs");
            string aar = Path.Combine(libs, LoaderAar + ".aar");
            if (!File.Exists(aar)) return;                                  // already gone
            if (!File.Exists(Path.Combine(libs, MetaAar))) return;          // no duplicate to resolve

            File.Delete(aar);

            string gradle = Path.Combine(path, "build.gradle");
            if (File.Exists(gradle))
            {
                var kept = new StringBuilder();
                foreach (string line in File.ReadAllLines(gradle))
                    if (!line.Contains($"name: '{LoaderAar}'")) kept.AppendLine(line);
                File.WriteAllText(gradle, kept.ToString());
            }

            Debug.Log($"[CI] dropped the duplicate OpenXR loader from the Gradle project: {aar}");
        }
    }
}
#endif
