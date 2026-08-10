using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Electrical
{
    /// <summary>
    /// A wall-mounted electrical fixture (docs/design/19-electrical.md): outlet block
    /// (1–5 posts), switch (1–3 keys) or the breaker panel. The mesh is built in LOCAL
    /// space (back plate at z=0 against the wall, +Z pointing into the room, block
    /// centered at the transform) — placement is the transform, so dragging is a pure
    /// transform move with no re-cook (rule 4.2). Owns its Mesh (rule 1.5).
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class ElectricFixture : MonoBehaviour
    {
        private MeshFilter _mf;
        private Mesh _mesh;
        private MeshCollider _collider;

        private readonly List<Vector3> _verts = new();
        private readonly List<int> _tris = new();
        private readonly List<Vector2> _uvs = new();

        public FixtureKind Kind { get; private set; }
        public int Posts { get; private set; } = 1;
        public int Keys { get; private set; } = 1;

        /// <summary>Storey level (Y) this fixture was mounted against. Mounting heights are
        /// relative to it — display and clamps must survive non-zero storeys.</summary>
        public float BaseLevel { get; set; }

        /// <summary>Mounting height above the storey level, meters.</summary>
        public float HeightAboveLevel => transform.position.y - BaseLevel;

        private int _reservePercent = ElectricalDefaults.DefaultReservePercent;
        /// <summary>BOM reserve, meaningful for the Panel.</summary>
        public int ReservePercent
        {
            get => _reservePercent;
            set => _reservePercent = Mathf.Clamp(value, 0, ElectricalDefaults.MaxReservePercent);
        }

        /// <summary>Full block width in meters (for clearance checks).</summary>
        public float BlockWidth => Kind switch
        {
            FixtureKind.Outlet => Posts * ElectricalDefaults.PostModule,
            FixtureKind.Switch => ElectricalDefaults.PostModule,
            _ => ElectricalDefaults.PanelBoxWidth,
        };

        public float BlockHeight => Kind == FixtureKind.Panel
            ? ElectricalDefaults.PanelBoxHeight : ElectricalDefaults.PostModule;

        /// <summary>Cable entry point in local space: top center of the block for
        /// outlets/switches, bottom center for the panel (wires dive into it).</summary>
        public Vector3 TerminalLocal => Kind == FixtureKind.Panel
            ? new Vector3(0f, -ElectricalDefaults.PanelBoxHeight * 0.5f, ElectricalDefaults.PanelBoxDepth * 0.5f)
            : new Vector3(0f, ElectricalDefaults.PostModule * 0.5f, ElectricalDefaults.PlateDepth * 0.5f);

        public Vector3 TerminalWorld => transform.TransformPoint(TerminalLocal);

        private void Ensure()
        {
            if (_mf == null) _mf = GetComponent<MeshFilter>();
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "FixtureMesh" };
                _mf.sharedMesh = _mesh;
            }
            if (_collider == null)
            {
                _collider = GetComponent<MeshCollider>();
                if (_collider == null) _collider = gameObject.AddComponent<MeshCollider>();
            }
        }

        public void Rebuild() => Build(Kind, Posts, Keys);

        /// <summary>Placement is the transform, so a move never re-cooks the mesh.</summary>
        public void MoveBy(Vector3 delta) => transform.position += delta;

        public void Build(FixtureKind kind, int posts, int keys)
        {
            Ensure();
            Kind = kind;
            Posts = Mathf.Clamp(posts, 1, ElectricalDefaults.MaxPosts);
            Keys = Mathf.Clamp(keys, 1, ElectricalDefaults.MaxKeys);
            _verts.Clear(); _tris.Clear(); _uvs.Clear();

            switch (Kind)
            {
                case FixtureKind.Outlet:
                {
                    float w = Posts * ElectricalDefaults.PostModule;
                    float h = ElectricalDefaults.PostModule;
                    float d = ElectricalDefaults.PlateDepth;
                    AddBox(new Vector3(0f, 0f, d * 0.5f), new Vector3(w, h, d));
                    for (int i = 0; i < Posts; i++)
                    {
                        float cx = (i + 0.5f) * ElectricalDefaults.PostModule - w * 0.5f;
                        // socket well: a raised round-ish boss reads as an outlet from afar
                        AddBox(new Vector3(cx, 0f, d + 0.003f), new Vector3(0.06f, 0.06f, 0.006f));
                    }
                    break;
                }
                case FixtureKind.Switch:
                {
                    float w = ElectricalDefaults.PostModule;
                    float h = ElectricalDefaults.PostModule;
                    float d = ElectricalDefaults.PlateDepth;
                    AddBox(new Vector3(0f, 0f, d * 0.5f), new Vector3(w, h, d));
                    const float area = 0.06f, gap = 0.004f;
                    float keyW = (area - (Keys - 1) * gap) / Keys;
                    for (int i = 0; i < Keys; i++)
                    {
                        float cx = -area * 0.5f + keyW * 0.5f + i * (keyW + gap);
                        AddBox(new Vector3(cx, 0f, d + 0.004f), new Vector3(keyW, area, 0.008f));
                    }
                    break;
                }
                default: // Panel
                {
                    float w = ElectricalDefaults.PanelBoxWidth;
                    float h = ElectricalDefaults.PanelBoxHeight;
                    float d = ElectricalDefaults.PanelBoxDepth;
                    AddBox(new Vector3(0f, 0f, d * 0.5f), new Vector3(w, h, d));
                    // door: a slimmer face proud of the box — schematic, not a cabinet model
                    AddBox(new Vector3(0f, 0f, d + 0.004f), new Vector3(w - 0.04f, h - 0.06f, 0.008f));
                    break;
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            if (_collider != null)
            {
                _collider.sharedMesh = null;
                _collider.sharedMesh = _mesh;
            }
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i0 = _verts.Count;
            _verts.Add(a); _verts.Add(b); _verts.Add(c); _verts.Add(d);
            _uvs.Add(new Vector2(0f, 0f)); _uvs.Add(new Vector2(1f, 0f));
            _uvs.Add(new Vector2(1f, 1f)); _uvs.Add(new Vector2(0f, 1f));
            _tris.Add(i0); _tris.Add(i0 + 1); _tris.Add(i0 + 2);
            _tris.Add(i0); _tris.Add(i0 + 2); _tris.Add(i0 + 3);
        }

        /// <summary>Axis-aligned box, every face wound outward (rule 1.1).</summary>
        private void AddBox(Vector3 center, Vector3 size)
        {
            Vector3 n = center - size * 0.5f, x = center + size * 0.5f;
            Quad(new Vector3(n.x, n.y, x.z), new Vector3(x.x, n.y, x.z), new Vector3(x.x, x.y, x.z), new Vector3(n.x, x.y, x.z)); // +Z
            Quad(new Vector3(x.x, n.y, n.z), new Vector3(n.x, n.y, n.z), new Vector3(n.x, x.y, n.z), new Vector3(x.x, x.y, n.z)); // -Z
            Quad(new Vector3(x.x, n.y, x.z), new Vector3(x.x, n.y, n.z), new Vector3(x.x, x.y, n.z), new Vector3(x.x, x.y, x.z)); // +X
            Quad(new Vector3(n.x, n.y, n.z), new Vector3(n.x, n.y, x.z), new Vector3(n.x, x.y, x.z), new Vector3(n.x, x.y, n.z)); // -X
            Quad(new Vector3(n.x, x.y, x.z), new Vector3(x.x, x.y, x.z), new Vector3(x.x, x.y, n.z), new Vector3(n.x, x.y, n.z)); // +Y
            Quad(new Vector3(n.x, n.y, n.z), new Vector3(x.x, n.y, n.z), new Vector3(x.x, n.y, x.z), new Vector3(n.x, n.y, x.z)); // -Y
        }

        private void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
        }
    }
}
