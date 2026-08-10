#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using RoomPlanner.Tools;

namespace RoomPlanner.EditorTools
{
    /// <summary>Paint tool ("Pnt", design/04): controller wiring + the FinishLibrary —
    /// the texture catalog built from the CC0 set (RoomPlanner → Download Textures).
    /// A missing set is a warning, not a failure: the tool falls back to colors only
    /// and the panel says "run Download Textures".</summary>
    internal static class SetupPaintTool
    {
        public static void Build(RigContext ctx)
        {
            ctx.Paint = ctx.Rig.AddComponent<PaintController>();
            ctx.Finishes = BuildFinishLibrary(ctx.Rig);

            var so = new SerializedObject(ctx.Paint);
            so.FindProperty("pointer").objectReferenceValue = ctx.Pointer;
            so.FindProperty("input").objectReferenceValue = ctx.Input;
            so.FindProperty("manager").objectReferenceValue = ctx.Manager;
            so.FindProperty("sceneModel").objectReferenceValue = ctx.SceneModel;
            so.FindProperty("reticle").objectReferenceValue = ctx.Reticle.transform;
            so.FindProperty("library").objectReferenceValue = ctx.Finishes;
            so.ApplyModifiedProperties();
        }

        /// <summary>id → Texture2D catalog on the rig; scene references pull the textures
        /// into the APK. Entries whose file is missing are skipped with a warning.
        /// Internal: the UiShots screenshot pipeline builds one for its panels too.</summary>
        internal static RoomPlanner.Editing.FinishLibrary BuildFinishLibrary(GameObject host)
        {
            var lib = host.AddComponent<RoomPlanner.Editing.FinishLibrary>();
            var ids = new List<string>();
            var textures = new List<Texture2D>();
            var tiles = new List<float>();
            var cats = new List<string>();

            int missing = 0;
            foreach (var (cat, _, id, tile) in TextureDownloader.Curated)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDownloader.PathFor(cat, id));
                if (tex == null) { missing++; continue; }
                ids.Add(id);
                textures.Add(tex);
                tiles.Add(tile);
                cats.Add(cat);
            }
            if (missing > 0)
                Debug.LogWarning($"[Setup] FinishLibrary: {missing} texture(s) missing — " +
                    "run RoomPlanner → Download Textures, then SetupRig again");

            var so = new SerializedObject(lib);
            FillArray(so.FindProperty("ids"), ids.Count, (p, i) => p.stringValue = ids[i]);
            FillArray(so.FindProperty("textures"), textures.Count, (p, i) => p.objectReferenceValue = textures[i]);
            FillArray(so.FindProperty("tileMeters"), tiles.Count, (p, i) => p.floatValue = tiles[i]);
            FillArray(so.FindProperty("categories"), cats.Count, (p, i) => p.stringValue = cats[i]);
            so.ApplyModifiedProperties();
            return lib;
        }

        private static void FillArray(SerializedProperty array, int count,
            System.Action<SerializedProperty, int> set)
        {
            array.arraySize = count;
            for (int i = 0; i < count; i++) set(array.GetArrayElementAtIndex(i), i);
        }
    }
}
#endif
