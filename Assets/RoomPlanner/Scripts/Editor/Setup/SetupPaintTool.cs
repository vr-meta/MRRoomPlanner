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
            // Paint room (design/24, issue #52): rooms come from the wall graph, the
            // carved sub-slab from the floor controller — both live on the rig already
            // (SetupWallTool/SetupFloorTool run before this).
            so.FindProperty("walls").objectReferenceValue =
                ctx.Rig.GetComponent<RoomPlanner.Walls.WallGraphRenderer>();
            so.FindProperty("floors").objectReferenceValue =
                ctx.Rig.GetComponent<RoomPlanner.Floors.FloorController>();
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
            var glosses = new List<float>();
            var cats = new List<string>();
            var normals = new List<Texture2D>();   // relief where the set ships one (design/29 §5)

            int missing = 0;
            foreach (var (cat, _, id, tile, gloss, bump) in TextureDownloader.Curated)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDownloader.PathFor(cat, id));
                if (tex == null) { missing++; continue; }
                ids.Add(id);
                textures.Add(tex);
                tiles.Add(tile);
                glosses.Add(gloss);
                cats.Add(cat);
                normals.Add(bump
                    ? AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDownloader.NormalPathFor(cat, id))
                    : null);
            }
            if (missing > 0)
                Debug.LogWarning($"[Setup] FinishLibrary: {missing} texture(s) missing — " +
                    "run RoomPlanner → Download Textures, then SetupRig again");

            // baked laminate (design/22): pattern × color variants into Floors, with the
            // pattern's shared normal map; missing bakes are a warning, not a failure
            int lamMissing = 0;
            foreach (var e in RoomPlanner.Core.LaminateCatalog.Entries)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(LaminateBaker.DiffusePath(e));
                if (tex == null) { lamMissing++; continue; }
                ids.Add(e.Id);
                textures.Add(tex);
                tiles.Add(e.TileMeters);
                glosses.Add(RoomPlanner.Core.LaminateCatalog.Gloss);
                cats.Add("Floors");
                normals.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(LaminateBaker.NormalPath(e.Pattern)));
            }
            if (lamMissing > 0)
                Debug.LogWarning($"[Setup] FinishLibrary: {lamMissing} laminate bake(s) missing — " +
                    "run RoomPlanner → Bake Laminate, then SetupRig again");

            // procedural ceramic tiles (design/23): subway/grid/herringbone glazes into
            // Tiles, with the pattern's shared normal map
            int tileMissing = 0;
            foreach (var e in RoomPlanner.Core.TileCatalog.Entries)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TileBaker.DiffusePath(e));
                if (tex == null) { tileMissing++; continue; }
                ids.Add(e.Id);
                textures.Add(tex);
                tiles.Add(e.TileMeters);
                glosses.Add(RoomPlanner.Core.TileCatalog.Gloss);
                cats.Add("Tiles");
                normals.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(TileBaker.NormalPath(e.Pattern)));
            }
            if (tileMissing > 0)
                Debug.LogWarning($"[Setup] FinishLibrary: {tileMissing} ceramic bake(s) missing — " +
                    "run RoomPlanner → Bake Tiles, then SetupRig again");

            // The IFC dressing table (design/29 §3) names finishes by id — a typo or a
            // dropped catalog entry would show up on the headset as an undressed cabinet,
            // so it is checked here, while the rig is being built.
            var missingIds = new List<string>();
            foreach (string id in RoomPlanner.Core.Ifc.IfcMaterialMap.AllFinishIds)
                if (!ids.Contains(id) && !missingIds.Contains(id)) missingIds.Add(id);
            if (missingIds.Count > 0)
                Debug.LogError("[Setup] IfcMaterialMap points at finishes the catalog does not "
                    + $"have: {string.Join(", ", missingIds)}");

            var so = new SerializedObject(lib);
            FillArray(so.FindProperty("ids"), ids.Count, (p, i) => p.stringValue = ids[i]);
            FillArray(so.FindProperty("textures"), textures.Count, (p, i) => p.objectReferenceValue = textures[i]);
            FillArray(so.FindProperty("tileMeters"), tiles.Count, (p, i) => p.floatValue = tiles[i]);
            FillArray(so.FindProperty("gloss"), glosses.Count, (p, i) => p.floatValue = glosses[i]);
            FillArray(so.FindProperty("categories"), cats.Count, (p, i) => p.stringValue = cats[i]);
            FillArray(so.FindProperty("normalMaps"), normals.Count, (p, i) => p.objectReferenceValue = normals[i]);
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
