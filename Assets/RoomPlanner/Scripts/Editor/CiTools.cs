#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Batchmode entry points so the rig setup and the APK build can run headless
    /// (ci/setup-rig.ps1, ci/build.ps1) without opening the Editor:
    ///
    ///   Unity.exe -batchmode -quit -projectPath &lt;proj&gt; -executeMethod RoomPlanner.EditorTools.CiTools.SetupRig
    ///   Unity.exe -batchmode -quit -projectPath &lt;proj&gt; -executeMethod RoomPlanner.EditorTools.CiTools.BuildAndroid
    ///
    /// EditorUtility.DisplayDialog auto-returns in batchmode, so MeasureSetup's summary
    /// dialog does not block. Every path exits with a non-zero code on failure so the
    /// calling script can detect problems.
    /// </summary>
    public static class CiTools
    {
        private const string ScenePath = "Assets/Measure.unity";

        /// <summary>Rebuild the rig (RoomPlanner → Setup Measure Rig) and save the scene.</summary>
        public static void SetupRig()
        {
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Debug.Log($"[CI] opened scene {scene.path}");

                MeasureSetup.SetupMeasureRig();

                EditorSceneManager.MarkSceneDirty(scene);
                bool saved = EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log($"[CI] SetupRig done, scene saved={saved}");
                if (!saved) EditorApplication.Exit(1);
            }
            catch (Exception e)
            {
                Debug.LogError($"[CI] SetupRig failed: {e}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Build the Quest APK to Build/MRRoomPlanner.apk. The single build path for both
        /// batchmode and the GUI menu (RoomPlanner → Build APK (Quest) delegates here) — two
        /// diverging pipelines drifted before.
        /// </summary>
        public static void BuildAndroid()
        {
            try
            {
                string outPath = "Build/MRRoomPlanner.apk";
                System.IO.Directory.CreateDirectory("Build");

                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

                var id = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
                if (string.IsNullOrEmpty(id) || id.Contains("DefaultCompany"))
                    PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.RoomPlanner.MRRoomPlanner");

                var scenes = EditorBuildSettings.scenes
                    .Where(s => s.enabled)
                    .Select(s => s.path)
                    .ToArray();
                if (scenes.Length == 0) scenes = new[] { ScenePath };

                var opts = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None
                };

                var report = BuildPipeline.BuildPlayer(opts);
                var summary = report.summary;
                bool ok = summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
                Debug.Log($"[CI] Build {summary.result}, size={summary.totalSize} bytes, errors={summary.totalErrors}");
                if (Application.isBatchMode)
                {
                    if (!ok) EditorApplication.Exit(1);
                }
                else
                {
                    EditorUtility.DisplayDialog("Build APK (Quest)",
                        ok ? $"OK: {outPath}\nSize: {summary.totalSize / (1024f * 1024f):F1} MB"
                           : $"FAILED: {summary.result} ({summary.totalErrors} errors)", "OK");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[CI] BuildAndroid failed: {e}");
                // Exit only headless — from the GUI menu this would close the Editor.
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        /// <summary>Force a script recompile + asset refresh (used to verify compilation headless).</summary>
        public static void Compile()
        {
            AssetDatabase.Refresh();
            Debug.Log("[CI] compile/refresh requested");
        }
    }
}
#endif
