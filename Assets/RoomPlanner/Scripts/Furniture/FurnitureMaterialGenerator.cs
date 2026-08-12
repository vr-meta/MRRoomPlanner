using System.Collections.Generic;
using UnityEngine;
using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using GLTFast.Schema;
using Material = UnityEngine.Material;
using Texture = UnityEngine.Texture;   // GLTFast.Schema has a Texture of its own

namespace RoomPlanner.Furniture
{
    /// <summary>
    /// Builds catalog materials on the PROJECT's shader instead of glTFast's own
    /// (headset feedback 2026-08-12: every placed piece came out magenta).
    ///
    /// glTFast's shaders live only in its package: nothing in a built scene references
    /// them, so Unity's shader stripping drops them from the APK and every imported
    /// material resolves to the error shader. Shipping them via "Always Included Shaders"
    /// would bloat the build with variants we do not use — and the pieces would still be
    /// lit differently from the rest of the room. Generating our own material instead
    /// fixes both: nothing to strip, and furniture takes the same light, shadows and
    /// vertex AO as walls and slabs.
    ///
    /// Only base colour and base texture are carried over; the CC0 catalog packs are flat
    /// shaded, and metallic/roughness maps would cost Quest fill rate for nothing.
    /// </summary>
    public class FurnitureMaterialGenerator : IMaterialGenerator
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private readonly Material _template;
        private readonly List<Material> _created = new();

        /// <summary>Materials this generator owns; the loader disposes them (rules 12 §1.5).</summary>
        public IReadOnlyList<Material> Created => _created;

        public FurnitureMaterialGenerator(Material template) => _template = template;

        public Material GetDefaultMaterial(bool pointsSupport = false) =>
            Make("Furniture (default)", Color.white, null);

        public Material GenerateMaterial(MaterialBase gltfMaterial, IGltfReadable gltf,
            bool pointsSupport = false)
        {
            var color = Color.white;
            Texture2D texture = null;

            var pbr = gltfMaterial?.PbrMetallicRoughness;
            if (pbr != null)
            {
                // glTF stores base colour LINEAR; Unity's colour properties expect gamma
                // (same conversion glTFast's own generators do).
                color = pbr.BaseColor.gamma;
                var info = pbr.BaseColorTexture;
                if (info != null && info.index >= 0 && gltf != null)
                    texture = gltf.GetTexture(info.index);
            }

            return Make(gltfMaterial?.name ?? "Furniture", color, texture);
        }

        public void SetLogger(ICodeLogger logger) { }

        private Material Make(string name, Color color, Texture texture)
        {
            var material = _template != null ? new Material(_template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = name;

            if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, color);
            else if (material.HasProperty(ColorId)) material.SetColor(ColorId, color);

            if (texture != null)
            {
                if (material.HasProperty(BaseMapId)) material.SetTexture(BaseMapId, texture);
                else if (material.HasProperty(MainTexId)) material.SetTexture(MainTexId, texture);
            }

            _created.Add(material);
            return material;
        }

        /// <summary>Release every material this generator made.</summary>
        public void Dispose()
        {
            foreach (var m in _created)
            {
                if (m == null) continue;
                if (Application.isPlaying) Object.Destroy(m);
                else Object.DestroyImmediate(m);
            }
            _created.Clear();
        }
    }
}
