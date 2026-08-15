#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using RoomPlanner.Core.Furniture;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Bakes catalog thumbnails (#83). Choosing furniture from a text list does not work —
    /// "Sofa (classic)" and "Sofa (fabric)" are indistinguishable until placed, and a pack
    /// has over a hundred rows — so every item gets a picture.
    ///
    /// How: the model is copied into the project so Unity's glTF importer turns it into a
    /// prefab, then rendered through <see cref="PreviewRenderUtility"/> — the same
    /// off-screen renderer inspectors use, which works in batchmode (with a graphics
    /// context, i.e. NOT -nographics) and does not need a scene, camera or lights of ours.
    /// Framing is derived from the model's own bounds, so items look consistent regardless
    /// of how the pack authored its pivots.
    /// </summary>
    public static class FurnitureThumbnailBaker
    {
        public const int Size = 128;
        /// <summary>Where models are staged for import; removed afterwards.</summary>
        private const string StageDir = "Assets/_FurnitureThumbStage";
        public const string PreviewSuffix = ".preview.png";

        [MenuItem("RoomPlanner/Furniture/Bake catalog previews")]
        public static void BakeAllMenu()
        {
            string report = BakeAll();
            Debug.Log(report);
            EditorUtility.DisplayDialog("Furniture previews", report, "OK");
        }

        /// <summary>Headless entry (ci/unity-run.ps1 — must NOT pass -nographics).</summary>
        public static void BakeAllBatch()
        {
            string report = BakeAll();
            Debug.Log(report);
            EditorApplication.Exit(report.Contains("FAILED") ? 1 : 0);
        }

        public static string BakeAll()
        {
            var sb = new System.Text.StringBuilder("[Furniture] preview bake\n");
            string root = FurnitureCatalogBuilder.StreamingRoot;
            if (!Directory.Exists(root)) return sb.Append("  no packs\n").ToString();

            foreach (string folder in Directory.GetDirectories(root))
            {
                string manifest = Path.Combine(folder, FurnitureCatalogParser.ManifestName);
                if (!File.Exists(manifest)) continue;

                var collection = FurnitureCatalogParser.Parse(File.ReadAllText(manifest),
                    FurnitureSource.Bundled, folder, out _);
                if (collection == null) { sb.AppendLine($"  {folder}: FAILED to parse"); continue; }

                int baked = 0, skipped = 0, failed = 0;
                foreach (var item in collection.Items)
                {
                    string outPath = Path.Combine(folder, item.Id + PreviewSuffix);
                    if (File.Exists(outPath)) { skipped++; continue; }
                    if (Bake(Path.Combine(folder, item.File), item, outPath)) baked++;
                    else failed++;
                }
                sb.AppendLine($"  {collection.Id}: {baked} baked, {skipped} kept, " +
                              (failed > 0 ? $"{failed} FAILED" : "0 failed"));
            }

            CleanStage();
            AssetDatabase.Refresh();
            return sb.ToString();
        }

        private static bool Bake(string modelPath, FurnitureItem item, string outPath)
        {
            var prefab = StageAndImport(modelPath);
            if (prefab == null) return false;

            var pru = new PreviewRenderUtility();
            try
            {
                var instance = Object.Instantiate(prefab);
                instance.hideFlags = HideFlags.HideAndDontSave;

                var bounds = BoundsOf(instance);
                if (bounds.size.sqrMagnitude < 1e-8f) { Object.DestroyImmediate(instance); return false; }

                // Centre the model on the origin and look at it from a fixed 3/4 angle, so
                // every thumbnail in every pack reads the same way.
                instance.transform.position = -bounds.center;
                pru.AddSingleGO(instance);

                float radius = bounds.extents.magnitude;
                var dir = new Vector3(1f, 0.75f, 1f).normalized;
                pru.camera.transform.position = dir * radius * 3.2f;
                pru.camera.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
                pru.camera.orthographic = true;
                pru.camera.orthographicSize = radius * 1.12f;   // a little air around the piece
                pru.camera.nearClipPlane = 0.01f;
                pru.camera.farClipPlane = radius * 10f + 10f;
                pru.camera.clearFlags = CameraClearFlags.SolidColor;
                pru.camera.backgroundColor = new Color(0.11f, 0.13f, 0.19f, 1f);   // UiTokens.InsetBg
                pru.lights[0].intensity = 1.1f;
                pru.lights[0].transform.rotation = Quaternion.Euler(35f, 140f, 0f);
                pru.lights[1].intensity = 0.5f;
                pru.ambientColor = new Color(0.35f, 0.35f, 0.4f, 1f);

                pru.BeginStaticPreview(new Rect(0, 0, Size, Size));
                pru.camera.Render();
                var tex = pru.EndStaticPreview();
                Object.DestroyImmediate(instance);
                if (tex == null) return false;

                File.WriteAllBytes(outPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Furniture] preview failed for {item.Id}: {e.Message}");
                return false;
            }
            finally { pru.Cleanup(); }
        }

        /// <summary>
        /// StreamingAssets is invisible to the asset pipeline, so the model (and, for a
        /// .gltf, the folder holding its textures) is copied into Assets just long enough
        /// to be imported.
        /// </summary>
        private static GameObject StageAndImport(string modelPath)
        {
            if (!File.Exists(modelPath)) return null;
            Directory.CreateDirectory(StageDir);

            string src = Path.GetDirectoryName(modelPath);
            string name = Path.GetFileName(modelPath);
            string stagedModel;

            if (Path.GetExtension(modelPath).Equals(".glb", System.StringComparison.OrdinalIgnoreCase))
            {
                stagedModel = Path.Combine(StageDir, name);
                File.Copy(modelPath, stagedModel, true);
            }
            else
            {
                // .gltf: bring the whole asset folder, the file references siblings
                string dstDir = Path.Combine(StageDir, Path.GetFileName(src));
                CopyDirectory(src, dstDir);
                stagedModel = Path.Combine(dstDir, name);
            }

            string unityPath = stagedModel.Replace('\\', '/');
            AssetDatabase.ImportAsset(unityPath, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<GameObject>(unityPath);
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
            {
                if (f.EndsWith(".meta") || f.EndsWith(PreviewSuffix)) continue;
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            }
            foreach (var d in Directory.GetDirectories(src))
                CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        private static void CleanStage()
        {
            if (Directory.Exists(StageDir)) Directory.Delete(StageDir, true);
            if (File.Exists(StageDir + ".meta")) File.Delete(StageDir + ".meta");
        }

        private static Bounds BoundsOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            bool has = false;
            var b = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            return has ? b : new Bounds(Vector3.zero, Vector3.zero);
        }
    }
}
#endif
