#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Сборка APK под Quest. Работает и из GUI (меню RoomPlanner → Build APK (Quest)),
    /// и из командной строки:
    ///   Unity.exe -batchmode -quit -projectPath &lt;proj&gt; -buildTarget Android \
    ///     -executeMethod RoomPlanner.EditorTools.BuildTool.BuildQuest -logFile &lt;log&gt;
    /// APK: &lt;proj&gt;/Build/RoomPlanner.apk
    /// </summary>
    public static class BuildTool
    {
        private const string OutDir = "Build";
        private const string OutApk = "Build/RoomPlanner.apk";

        [MenuItem("RoomPlanner/Build APK (Quest)")]
        public static void BuildQuest()
        {
            // Целевая платформа — Android
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            // Идентификатор приложения (если ещё дефолтный)
            var id = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
            if (string.IsNullOrEmpty(id) || id.Contains("DefaultCompany"))
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.RoomPlanner.MRRoomPlanner");

            // Сцены: из Build Settings, иначе — все сцены из Assets/RoomPlanner
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                scenes = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/RoomPlanner" })
                    .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            }

            if (scenes.Length == 0)
            {
                Report("Нет сцен для сборки. Сохрани сцену в Assets/RoomPlanner/Scenes/ и добавь в Build Settings.", true);
                return;
            }

            Directory.CreateDirectory(OutDir);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                locationPathName = OutApk,
                options = BuildOptions.None,
            };

            Debug.Log($"[BuildTool] Building {scenes.Length} scene(s) → {OutApk}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;

            bool ok = s.result == BuildResult.Succeeded;
            string msg = ok
                ? $"OK: {OutApk}\nРазмер: {(s.totalSize / (1024f * 1024f)):F1} MB\nВремя: {s.totalTime}"
                : $"FAILED: {s.result}\nОшибок: {s.totalErrors}";
            Report(msg, !ok);

            if (Application.isBatchMode)
                EditorApplication.Exit(ok ? 0 : 1);
        }

        private static void Report(string msg, bool error)
        {
            if (error) Debug.LogError("[BuildTool] " + msg); else Debug.Log("[BuildTool] " + msg);
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("Build APK (Quest)", msg, "OK");
        }
    }
}
#endif
