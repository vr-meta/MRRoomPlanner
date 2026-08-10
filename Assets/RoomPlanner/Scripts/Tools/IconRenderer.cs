using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// Renders one icon from <see cref="IconPaths"/> as a flat mesh (design/20 §5).
    /// Meshes are tessellated once per icon id and SHARED through a static cache — the
    /// set is bounded (~45 icons), so the cache is deliberately never freed and OnDestroy
    /// must not destroy the shared mesh (exception to coding rule 6, documented here).
    /// Tint comes from a MaterialPropertyBlock; the material is never mutated.
    /// </summary>
    public class IconRenderer : MonoBehaviour
    {
        [SerializeField] private string iconId;
        [SerializeField] private Material material;
        [SerializeField] private float size = 0.014f;   // metric glyph size (≥1.2° at 0.65 m)

        private static readonly Dictionary<string, Mesh> Cache = new();
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;

        public string IconId => iconId;

        private void Awake()
        {
            if (!string.IsNullOrEmpty(iconId)) Build();
        }

        /// <summary>Wire up an icon created at runtime (inspector rows, radial sectors).</summary>
        public void Init(string id, Material mat, float metricSize)
        {
            iconId = id;
            material = mat;
            size = metricSize;
            Build();
        }

        /// <summary>Swap the glyph only — keeps material and size (radial hub).</summary>
        public void SetIcon(string id)
        {
            if (iconId == id) return;
            iconId = id;
            Build();
        }

        public void SetTint(Color c)
        {
            if (_renderer == null) return;
            _mpb ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(ColorId, c);
            _renderer.SetPropertyBlock(_mpb);
        }

        private void Build()
        {
            var mesh = SharedMesh(iconId);
            if (mesh == null) return;

            var filter = GetComponent<MeshFilter>();
            if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            transform.localScale = new Vector3(size, size, 1f);
        }

        /// <summary>Icon mesh from the cache, tessellated on first use. Null for unknown ids.</summary>
        public static Mesh SharedMesh(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (Cache.TryGetValue(id, out var cached) && cached != null) return cached;
            if (!IconPaths.All.TryGetValue(id, out var path))
            {
                Debug.LogWarning($"[Icons] unknown icon id '{id}'");
                return null;
            }
            var data = SvgTessellator.Build(path);
            if (data.IsEmpty)
            {
                Debug.LogError($"[Icons] icon '{id}' failed to tessellate");
                return null;
            }
            var mesh = ToMesh(data, $"Icon_{id}");
            Cache[id] = mesh;
            return mesh;
        }

        /// <summary>Wrap Core mesh data into a UnityEngine.Mesh (front toward −Z).</summary>
        public static Mesh ToMesh(SvgMesh data, string name)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(data.Vertices);
            mesh.SetTriangles(data.Triangles, 0);
            var normals = new Vector3[data.Vertices.Length];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.back;
            mesh.SetNormals(normals);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
