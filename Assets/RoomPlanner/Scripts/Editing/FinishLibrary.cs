using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Editing
{
    /// <summary>
    /// The texture catalog on the rig: finish id → Texture2D + tile size + category
    /// (design/04 § «Текстуры v1»). Wired by SetupPaintTool from the CC0 set downloaded
    /// via RoomPlanner → Download Textures; because a scene component references the
    /// textures, they ship in the APK. Consumers resolve ids through this — the finish
    /// MODEL never touches assets.
    /// </summary>
    public class FinishLibrary : MonoBehaviour
    {
        [SerializeField] private string[] ids;
        [SerializeField] private Texture2D[] textures;
        [SerializeField] private float[] tileMeters;
        [SerializeField] private string[] categories;   // "Walls" / "Floors"

        public int Count => ids != null ? ids.Length : 0;

        public bool TryGet(string id, out Texture2D texture, out float tile)
        {
            texture = null;
            tile = 1f;
            if (ids == null || string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < ids.Length; i++)
                if (ids[i] == id)
                {
                    texture = textures != null && i < textures.Length ? textures[i] : null;
                    tile = tileMeters != null && i < tileMeters.Length ? tileMeters[i] : 1f;
                    return texture != null;
                }
            return false;
        }

        /// <summary>Ids of one category, catalog order — the paint tool's swatch rows.</summary>
        public List<string> IdsOf(string category)
        {
            var list = new List<string>();
            if (ids == null) return list;
            for (int i = 0; i < ids.Length; i++)
                if (categories != null && i < categories.Length && categories[i] == category)
                    list.Add(ids[i]);
            return list;
        }
    }
}
