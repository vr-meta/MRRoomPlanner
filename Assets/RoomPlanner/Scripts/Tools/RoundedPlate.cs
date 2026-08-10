using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// The standard rounded-rect plate (design/20 §6.3) as a component: panels, button
    /// backgrounds and wells that the editor Setup wires up. The MeshRenderer exists at
    /// setup time (so MenuButton can bind to it), but the mesh itself is generated in
    /// Awake — nothing mesh-like is serialized into the scene. Replaces the square Quad
    /// primitives of v1 so the built app matches the design previews.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class RoundedPlate : MonoBehaviour
    {
        [SerializeField] private float width = 0.045f;
        [SerializeField] private float height = 0.030f;
        [SerializeField] private float radius = UiTokens.RadiusM;

        private Mesh _mesh;

        public float Width => width;
        public float Height => height;

        private void Awake() => Rebuild();

        /// <summary>Editor-time wiring (Setup) and runtime resizing (inspector auto-height).</summary>
        public void Configure(float w, float h, float r, Material material)
        {
            width = w;
            height = h;
            radius = r;
            var mr = GetComponent<MeshRenderer>();
            if (material != null) mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (Application.isPlaying) Rebuild();
        }

        /// <summary>Resize in place (auto-height panels) — one mesh rebuild, no scaling,
        /// so the corner radius stays true.</summary>
        public void Resize(float w, float h)
        {
            width = w;
            height = h;
            Rebuild();
        }

        /// <summary>Build/refresh the mesh now — Awake path in play mode, called explicitly
        /// by the editor screenshot pipeline.</summary>
        public void Rebuild()
        {
            var data = UiMeshes.RoundedRect(width, height, radius);
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "RoundedPlate", hideFlags = HideFlags.DontSave };
            }
            else
            {
                _mesh.Clear();
            }
            _mesh.SetVertices(data.Vertices);
            _mesh.SetTriangles(data.Triangles, 0);
            var normals = new Vector3[data.Vertices.Length];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.back;
            _mesh.SetNormals(normals);
            _mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
            }
        }
    }
}
