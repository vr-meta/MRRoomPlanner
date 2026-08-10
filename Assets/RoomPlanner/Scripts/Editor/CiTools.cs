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

                ExcludeDuplicateOpenXRLoaders();

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

        /// <summary>
        /// Keep exactly ONE libopenxr_loader.so in the APK — OVRPlugin's. Meta's own
        /// build preprocessor (OVRGradleGeneration) does this when its MetaXRFeature gate
        /// fires, but in a freshly imported Library (new worktree) the gate silently
        /// misses and Gradle dies on the duplicate in mergeReleaseNativeLibs. Same
        /// delegate as Meta sets → idempotent when both run.
        /// </summary>
        private static void ExcludeDuplicateOpenXRLoaders()
        {
            foreach (var importer in PluginImporter.GetAllImporters())
            {
                string p = importer.assetPath;
                if (p.Contains("OVRPlugin")) continue;   // the loader that must win
                if (p.Contains("libopenxr_loader.so") || p.Contains("openxr_loader.aar"))
                {
                    Debug.Log($"[CI] excluding duplicate OpenXR loader: {p}");
                    importer.SetIncludeInBuildDelegate(path => false);
                    // The delegate alone is NOT honored by a from-scratch Bee gradle
                    // export (fresh worktree Library, 2026-08-12: gradle died on the
                    // duplicate again) — main only built because its incremental Bee
                    // state predated the aar. Hard-disable the Android platform so the
                    // exclusion persists in the importer meta.
                    if (importer.GetCompatibleWithPlatform(BuildTarget.Android))
                    {
                        importer.SetCompatibleWithPlatform(BuildTarget.Android, false);
                        importer.SaveAndReimport();
                    }
                }
            }
        }

        /// <summary>
        /// Headless render of an imported scene to PNGs (Build/shots/*.png) — visual QA for
        /// lighting/AO without a headset. Batchmode must run WITHOUT -nographics. The IFC
        /// comes from the RP_SHOT_IFC env var, falling back to the test fixture. The scene
        /// is NOT saved — the imported content lives only in this batch session.
        /// </summary>
        public static void RenderShots()
        {
            try
            {
                var scene = EditorSceneManager.OpenScene("Assets/Measure.unity", OpenSceneMode.Single);
                Debug.Log($"[CI] opened scene {scene.path}");

                var import = UnityEngine.Object.FindFirstObjectByType<RoomPlanner.Import.ImportController>(
                    FindObjectsInactive.Include);
                if (import == null) { Debug.LogError("[CI] no ImportController in scene"); EditorApplication.Exit(1); return; }

                string ifc = Environment.GetEnvironmentVariable("RP_SHOT_IFC");
                if (string.IsNullOrEmpty(ifc) || !System.IO.File.Exists(ifc))
                    ifc = "Assets/RoomPlanner/Tests/Fixtures/MiniRevit.ifc";
                Debug.Log($"[CI] importing {ifc}");
                var building = RoomPlanner.Core.Ifc.IfcImporter.Import(
                    RoomPlanner.Core.Ifc.StepFile.Parse(System.IO.File.ReadAllText(ifc)));
                import.BuildScene(building);

                // frame the model: bounds over pickable geometry + MEP fixtures
                bool has = false;
                var bounds = new Bounds(Vector3.zero, Vector3.one);
                foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (r.gameObject.layer != 6 && r.GetComponent<RoomPlanner.Import.MepView>() == null) continue;
                    if (!has) { bounds = r.bounds; has = true; } else bounds.Encapsulate(r.bounds);
                }
                if (!has) { Debug.LogError("[CI] nothing to frame"); EditorApplication.Exit(1); return; }

                var camGo = new GameObject("ShotCam");
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 62f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 500f;

                // environment: procedural sky + a ground plane under the model — the same
                // world the scan-off mode shows on device
                var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/RoomPlanner/Materials/Env_Sky.mat");
                if (sky != null)
                {
                    RenderSettings.skybox = sky;
                    cam.clearFlags = CameraClearFlags.Skybox;
                }
                else
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.13f, 0.18f, 0.24f);
                }

                System.IO.Directory.CreateDirectory("Build/shots");
                Vector3 c = bounds.center;
                float size = Mathf.Max(bounds.size.x, bounds.size.z);
                float eyeY = bounds.min.y + 1.65f;

                var groundMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/RoomPlanner/Materials/Env_Ground.mat");
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "ShotGround";
                ground.transform.position = new Vector3(c.x, bounds.min.y - 0.02f, c.z);
                ground.transform.localScale = new Vector3(60f, 1f, 60f);
                if (groundMat != null) ground.GetComponent<Renderer>().sharedMaterial = groundMat;

                Shoot(cam, c + new Vector3(0.8f, 0.9f, -1f).normalized * size * 1.15f, c, "overview");
                // four interior looks from the ground-floor centre — at least one faces a room
                var eye = new Vector3(c.x, eyeY, c.z);
                Shoot(cam, eye, eye + Vector3.forward * 3f + Vector3.down * 0.2f, "interior-n");
                Shoot(cam, eye, eye + Vector3.back * 3f + Vector3.down * 0.2f, "interior-s");
                Shoot(cam, eye, eye + Vector3.right * 3f + Vector3.down * 0.2f, "interior-e");
                Shoot(cam, eye, eye + Vector3.left * 3f + Vector3.down * 0.2f, "interior-w");
                Shoot(cam, new Vector3(c.x, eyeY + 0.3f, bounds.min.z - size * 0.8f),
                    new Vector3(c.x, eyeY, c.z), "facade");
                Shoot(cam, c + Vector3.up * size * 1.4f + Vector3.back * 0.1f, c, "top");

                Debug.Log("[CI] shots saved to Build/shots");
            }
            catch (Exception e)
            {
                Debug.LogError($"[CI] RenderShots failed: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static void Shoot(Camera cam, Vector3 from, Vector3 at, string name)
        {
            cam.transform.position = from;
            cam.transform.LookAt(at);
            var rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            System.IO.File.WriteAllBytes($"Build/shots/{name}.png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(rt);
            Debug.Log($"[CI] shot {name}.png");
        }

        /// <summary>Diagnostic: import RP_SHOT_IFC and dump every plumbing fixture's
        /// position/size against the storey levels — for chasing misplaced sanitary ware.</summary>
        public static void DumpMep()
        {
            try
            {
                string ifc = Environment.GetEnvironmentVariable("RP_SHOT_IFC");
                var b = RoomPlanner.Core.Ifc.IfcImporter.Import(
                    RoomPlanner.Core.Ifc.StepFile.Parse(System.IO.File.ReadAllText(ifc)));
                foreach (var s in b.Storeys)
                    Debug.Log($"[CI] storey {s.Name} @ {s.Elevation:0.###}");
                foreach (var m in b.Plumbing)
                {
                    Vector3 min = m.Vertices[0], max = m.Vertices[0];
                    foreach (var p in m.Vertices) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                    var size = max - min;
                    Debug.Log($"[CI] mep '{m.Name}' storey={m.StoreyIndex} origin=({m.Origin.x:0.##},{m.Origin.y:0.##},{m.Origin.z:0.##}) "
                        + $"size=({size.x:0.##},{size.y:0.##},{size.z:0.##}) bottomY={m.Origin.y + min.y:0.##}");
                }
                Debug.Log($"[CI] mep dump done: {b.Plumbing.Count} fixtures, skipped {b.SkippedMep}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[CI] DumpMep failed: {e}");
                EditorApplication.Exit(1);
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
