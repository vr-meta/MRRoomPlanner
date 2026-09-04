using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Electrical
{
    /// <summary>
    /// Cheap parametric electrical fixtures. Geometry is baked once when a parameter changes;
    /// placement and dragging only move the transform, so neither meshes nor colliders are
    /// rebuilt per frame. Local +Z points into the room and z=0 lies on the mounting surface.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class ElectricFixture : MonoBehaviour
    {
        public const int PlasticSubmesh = 0;
        public const int DetailSubmesh = 1;
        public const int MetalSubmesh = 2;
        public const int SubmeshCount = 3;

        public const float PlateChamfer = 0.004f;
        public const float SocketCupDepth = 0.003f;
        public const float SwitchKeyGap = 0.004f;
        public const float RockerTiltDegrees = 3.5f;

        public static readonly Color WhitePlastic = new(0.92f, 0.92f, 0.91f, 1f);
        public static readonly Color BlackPlastic = new(0.055f, 0.06f, 0.065f, 1f);
        public static readonly Color DarkDetail = new(0.018f, 0.020f, 0.022f, 1f);
        public static readonly Color BrushedMetal = new(0.48f, 0.50f, 0.52f, 1f);
        public static readonly Color BlackMetal = new(0.075f, 0.08f, 0.085f, 1f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");

        private MeshFilter _mf;
        private MeshRenderer _renderer;
        private Mesh _mesh;
        private MeshCollider _collider;
        private MaterialPropertyBlock _variantBlock;

        private readonly List<Vector3> _verts = new();
        private readonly List<Vector2> _uvs = new();
        private readonly List<int>[] _subTriangles = { new(), new(), new() };

        public FixtureKind Kind { get; private set; }
        public int Posts { get; private set; } = 1;
        public int Keys { get; private set; } = 1;
        public bool BlackVariant { get; private set; }
        public bool PanelOpen { get; private set; }
        public Color PlasticColor => BlackVariant ? BlackPlastic : WhitePlastic;
        public Color PanelMetalColor => BlackVariant ? BlackMetal : BrushedMetal;

        /// <summary>Storey level (Y) this fixture was mounted against.</summary>
        public float BaseLevel { get; set; }

        public float HeightAboveLevel => transform.position.y - BaseLevel;

        private int _reservePercent = ElectricalDefaults.DefaultReservePercent;
        public int ReservePercent
        {
            get => _reservePercent;
            set => _reservePercent = Mathf.Clamp(value, 0, ElectricalDefaults.MaxReservePercent);
        }

        public float BlockWidth => Kind switch
        {
            FixtureKind.Outlet => Posts * ElectricalDefaults.PostModule,
            FixtureKind.Switch => ElectricalDefaults.PostModule,
            FixtureKind.Junction => ElectricalDefaults.JunctionBoxSize,
            _ => ElectricalDefaults.PanelBoxWidth,
        };

        public float BlockHeight => Kind switch
        {
            FixtureKind.Panel => ElectricalDefaults.PanelBoxHeight,
            FixtureKind.Junction => ElectricalDefaults.JunctionBoxSize,
            _ => ElectricalDefaults.PostModule,
        };

        public Vector3 TerminalLocal => Kind switch
        {
            FixtureKind.Panel => new Vector3(0f, -ElectricalDefaults.PanelBoxHeight * 0.5f,
                ElectricalDefaults.PanelBoxDepth * 0.5f),
            FixtureKind.Junction => new Vector3(0f, 0f, ElectricalDefaults.JunctionBoxDepth),
            _ => new Vector3(0f, ElectricalDefaults.PostModule * 0.5f,
                ElectricalDefaults.PlateDepth * 0.5f),
        };

        public Vector3 TerminalWorld => transform.TransformPoint(TerminalLocal);

        private void Ensure()
        {
            if (_mf == null) _mf = GetComponent<MeshFilter>();
            if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
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
            _variantBlock ??= new MaterialPropertyBlock();

            // Bare test/restore objects start with one material slot. Three slots keep
            // property-block indices valid even when no real materials have been wired.
            if (_renderer != null && _renderer.sharedMaterials.Length < SubmeshCount)
            {
                var first = _renderer.sharedMaterial;
                _renderer.sharedMaterials = new[] { first, first, first };
            }
        }

        public void Rebuild() => Build(Kind, Posts, Keys, BlackVariant, PanelOpen);

        public void MoveBy(Vector3 delta) => transform.position += delta;

        public void Build(FixtureKind kind, int posts, int keys) =>
            Build(kind, posts, keys, BlackVariant, PanelOpen);

        /// <summary>One entry point used by placement, restore and the preview.</summary>
        public void Build(FixtureKind kind, int posts, int keys, bool blackVariant, bool panelOpen)
        {
            Ensure();
            Kind = kind;
            Posts = Mathf.Clamp(posts, 1, ElectricalDefaults.MaxPosts);
            Keys = Mathf.Clamp(keys, 1, ElectricalDefaults.MaxKeys);
            BlackVariant = blackVariant;
            PanelOpen = kind == FixtureKind.Panel && panelOpen;
            BuildGeometry();
        }

        public void SetBlackVariant(bool black)
        {
            Ensure();
            BlackVariant = black;
            ApplyVariant();
        }

        public void SetPanelOpen(bool open)
        {
            bool next = Kind == FixtureKind.Panel && open;
            if (PanelOpen == next) return;
            PanelOpen = next;
            if (Kind == FixtureKind.Panel) BuildGeometry();
        }

        private void BuildGeometry()
        {
            Ensure();
            _verts.Clear();
            _uvs.Clear();
            for (int i = 0; i < _subTriangles.Length; i++) _subTriangles[i].Clear();

            switch (Kind)
            {
                case FixtureKind.Outlet: BuildOutlet(); break;
                case FixtureKind.Switch: BuildSwitch(); break;
                case FixtureKind.Junction: BuildJunction(); break;
                default: BuildPanel(); break;
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.subMeshCount = SubmeshCount;
            for (int i = 0; i < SubmeshCount; i++)
                _mesh.SetTriangles(_subTriangles[i], i, false);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            _collider.sharedMesh = null;
            _collider.sharedMesh = _mesh;
            ApplyVariant();
        }

        private void BuildOutlet()
        {
            float w = Posts * ElectricalDefaults.PostModule;
            float h = ElectricalDefaults.PostModule;
            float d = ElectricalDefaults.PlateDepth;
            AddFrontChamferedBox(new Vector3(0f, 0f, d * 0.5f), new Vector3(w, h, d),
                PlateChamfer, PlasticSubmesh, Quaternion.identity);

            for (int i = 0; i < Posts; i++)
            {
                float cx = (i + 0.5f) * ElectricalDefaults.PostModule - w * 0.5f;
                AddSocketCup(new Vector3(cx, 0f, d));
            }
        }

        private void BuildSwitch()
        {
            float w = ElectricalDefaults.PostModule;
            float h = ElectricalDefaults.PostModule;
            float d = ElectricalDefaults.PlateDepth;
            AddFrontChamferedBox(new Vector3(0f, 0f, d * 0.5f), new Vector3(w, h, d),
                PlateChamfer, PlasticSubmesh, Quaternion.identity);

            const float area = 0.060f;
            float keyW = (area - (Keys - 1) * SwitchKeyGap) / Keys;
            // The dark well remains visible in the real gap around and between rockers.
            AddBox(new Vector3(0f, 0f, d + 0.0015f),
                new Vector3(area + 0.004f, area + 0.004f, 0.003f), DetailSubmesh,
                Quaternion.identity);
            for (int i = 0; i < Keys; i++)
            {
                float cx = -area * 0.5f + keyW * 0.5f + i * (keyW + SwitchKeyGap);
                AddFrontChamferedBox(new Vector3(cx, 0f, d + 0.006f),
                    new Vector3(keyW, area, 0.008f), 0.0015f, PlasticSubmesh,
                    Quaternion.Euler(RockerTiltDegrees, 0f, 0f));
            }
        }

        private void BuildJunction()
        {
            float s = ElectricalDefaults.JunctionBoxSize;
            float d = ElectricalDefaults.JunctionBoxDepth;
            AddFrontChamferedBox(new Vector3(0f, 0f, d * 0.5f), new Vector3(s, s, d),
                PlateChamfer, PlasticSubmesh, Quaternion.identity);
            AddFrontChamferedBox(new Vector3(0f, 0f, d + 0.003f),
                new Vector3(s - 0.010f, s - 0.010f, 0.006f), 0.0015f,
                PlasticSubmesh, Quaternion.identity);
            AddDisc(new Vector3(-s * 0.31f, -s * 0.31f, d + 0.0062f), 0.003f,
                Quaternion.identity, DetailSubmesh, 10);
            AddDisc(new Vector3(s * 0.31f, s * 0.31f, d + 0.0062f), 0.003f,
                Quaternion.identity, DetailSubmesh, 10);
        }

        private void BuildPanel()
        {
            float w = ElectricalDefaults.PanelBoxWidth;
            float h = ElectricalDefaults.PanelBoxHeight;
            float d = ElectricalDefaults.PanelBoxDepth;
            const float frame = 0.018f;

            // Open-front metal enclosure: back, four returns and an inset dark cavity.
            AddBox(new Vector3(0f, 0f, d * 0.12f), new Vector3(w, h, d * 0.24f),
                MetalSubmesh, Quaternion.identity);
            AddBox(new Vector3(-w * 0.5f + frame * 0.5f, 0f, d * 0.5f),
                new Vector3(frame, h, d), MetalSubmesh, Quaternion.identity);
            AddBox(new Vector3(w * 0.5f - frame * 0.5f, 0f, d * 0.5f),
                new Vector3(frame, h, d), MetalSubmesh, Quaternion.identity);
            AddBox(new Vector3(0f, h * 0.5f - frame * 0.5f, d * 0.5f),
                new Vector3(w - frame * 2f, frame, d), MetalSubmesh, Quaternion.identity);
            AddBox(new Vector3(0f, -h * 0.5f + frame * 0.5f, d * 0.5f),
                new Vector3(w - frame * 2f, frame, d), MetalSubmesh, Quaternion.identity);
            AddBox(new Vector3(0f, 0f, d * 0.26f),
                new Vector3(w - frame * 2f, h - frame * 2f, 0.003f),
                DetailSubmesh, Quaternion.identity);

            if (PanelOpen)
            {
                // DIN rails and two rows of breakers, visible only with the door swung out.
                for (int row = -1; row <= 1; row += 2)
                {
                    float y = row * h * 0.20f;
                    AddBox(new Vector3(0f, y, d * 0.48f),
                        new Vector3(w - 0.055f, 0.016f, 0.012f), MetalSubmesh,
                        Quaternion.identity);
                    const int breakers = 7;
                    float bw = (w - 0.075f) / breakers;
                    for (int i = 0; i < breakers; i++)
                    {
                        float x = -0.5f * (breakers - 1) * bw + i * bw;
                        AddFrontChamferedBox(new Vector3(x, y, d * 0.70f),
                            new Vector3(bw - 0.002f, 0.050f, 0.030f), 0.001f,
                            PlasticSubmesh, Quaternion.identity);
                        AddBox(new Vector3(x, y + 0.004f, d * 0.717f),
                            new Vector3(bw * 0.42f, 0.014f, 0.005f), DetailSubmesh,
                            Quaternion.Euler(-10f, 0f, 0f));
                    }
                }
            }

            float doorW = w - 0.040f;
            float doorH = h - 0.060f;
            const float doorDepth = 0.008f;
            var doorRotation = PanelOpen ? Quaternion.Euler(0f, -100f, 0f) : Quaternion.identity;
            var hinge = new Vector3(-doorW * 0.5f, 0f, d + doorDepth * 0.5f);
            var doorCenter = hinge + doorRotation * new Vector3(doorW * 0.5f, 0f, 0f);
            AddFrontChamferedBox(doorCenter, new Vector3(doorW, doorH, doorDepth),
                0.0025f, MetalSubmesh, doorRotation);

            float sx = doorW * 0.5f - 0.012f;
            float sy = doorH * 0.5f - 0.012f;
            AddDoorScrew(doorCenter, doorRotation, new Vector3(-sx, -sy, doorDepth * 0.5f + 0.0002f));
            AddDoorScrew(doorCenter, doorRotation, new Vector3(sx, -sy, doorDepth * 0.5f + 0.0002f));
            AddDoorScrew(doorCenter, doorRotation, new Vector3(-sx, sy, doorDepth * 0.5f + 0.0002f));
            AddDoorScrew(doorCenter, doorRotation, new Vector3(sx, sy, doorDepth * 0.5f + 0.0002f));
        }

        private void AddDoorScrew(Vector3 center, Quaternion rotation, Vector3 local) =>
            AddDisc(center + rotation * local, 0.003f, rotation, DetailSubmesh, 10);

        private void AddSocketCup(Vector3 center)
        {
            const int segments = 16;
            const float outer = 0.030f;
            const float inner = 0.024f;
            float rimZ = center.z + 0.004f;
            float baseZ = rimZ - SocketCupDepth;

            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 o0 = new(center.x + Mathf.Cos(a0) * outer,
                    center.y + Mathf.Sin(a0) * outer, rimZ);
                Vector3 o1 = new(center.x + Mathf.Cos(a1) * outer,
                    center.y + Mathf.Sin(a1) * outer, rimZ);
                Vector3 i0 = new(center.x + Mathf.Cos(a0) * inner,
                    center.y + Mathf.Sin(a0) * inner, rimZ);
                Vector3 i1 = new(center.x + Mathf.Cos(a1) * inner,
                    center.y + Mathf.Sin(a1) * inner, rimZ);
                Vector3 b0 = new(i0.x, i0.y, baseZ);
                Vector3 b1 = new(i1.x, i1.y, baseZ);
                Quad(o0, o1, i1, i0, PlasticSubmesh);
                Quad(i0, i1, b1, b0, PlasticSubmesh);
            }
            AddDisc(new Vector3(center.x, center.y, baseZ), inner,
                Quaternion.identity, PlasticSubmesh, segments);
            AddDisc(new Vector3(center.x - 0.0095f, center.y, baseZ + 0.0002f), 0.0032f,
                Quaternion.identity, DetailSubmesh, 10);
            AddDisc(new Vector3(center.x + 0.0095f, center.y, baseZ + 0.0002f), 0.0032f,
                Quaternion.identity, DetailSubmesh, 10);
        }

        /// <summary>A box whose four front edges are truly chamfered, not shaded round.</summary>
        private void AddFrontChamferedBox(Vector3 center, Vector3 size, float chamfer,
            int submesh, Quaternion rotation)
        {
            float hx = size.x * 0.5f, hy = size.y * 0.5f, hz = size.z * 0.5f;
            float c = Mathf.Min(chamfer, Mathf.Min(hx, Mathf.Min(hy, size.z)) * 0.45f);
            float back = -hz, bevel = hz - c, front = hz;

            Vector3 P(float x, float y, float z) => center + rotation * new Vector3(x, y, z);

            Vector3 b0 = P(-hx, -hy, back), b1 = P(hx, -hy, back);
            Vector3 b2 = P(hx, hy, back), b3 = P(-hx, hy, back);
            Vector3 o0 = P(-hx, -hy, bevel), o1 = P(hx, -hy, bevel);
            Vector3 o2 = P(hx, hy, bevel), o3 = P(-hx, hy, bevel);
            Vector3 f0 = P(-hx + c, -hy + c, front), f1 = P(hx - c, -hy + c, front);
            Vector3 f2 = P(hx - c, hy - c, front), f3 = P(-hx + c, hy - c, front);

            Quad(b1, b0, b3, b2, submesh);
            Quad(o1, b1, b2, o2, submesh);
            Quad(b0, o0, o3, b3, submesh);
            Quad(b3, o3, o2, b2, submesh);
            Quad(b0, b1, o1, o0, submesh);
            Quad(o0, o1, f1, f0, submesh);
            Quad(o1, o2, f2, f1, submesh);
            Quad(o2, o3, f3, f2, submesh);
            Quad(o3, o0, f0, f3, submesh);
            Quad(f0, f1, f2, f3, submesh);
        }

        private void AddBox(Vector3 center, Vector3 size, int submesh, Quaternion rotation)
        {
            Vector3 n = -size * 0.5f, x = size * 0.5f;
            Vector3 P(float px, float py, float pz) => center + rotation * new Vector3(px, py, pz);
            Quad(P(n.x, n.y, x.z), P(x.x, n.y, x.z), P(x.x, x.y, x.z), P(n.x, x.y, x.z), submesh);
            Quad(P(x.x, n.y, n.z), P(n.x, n.y, n.z), P(n.x, x.y, n.z), P(x.x, x.y, n.z), submesh);
            Quad(P(x.x, n.y, x.z), P(x.x, n.y, n.z), P(x.x, x.y, n.z), P(x.x, x.y, x.z), submesh);
            Quad(P(n.x, n.y, n.z), P(n.x, n.y, x.z), P(n.x, x.y, x.z), P(n.x, x.y, n.z), submesh);
            Quad(P(n.x, x.y, x.z), P(x.x, x.y, x.z), P(x.x, x.y, n.z), P(n.x, x.y, n.z), submesh);
            Quad(P(n.x, n.y, n.z), P(x.x, n.y, n.z), P(x.x, n.y, x.z), P(n.x, n.y, x.z), submesh);
        }

        private void AddDisc(Vector3 center, float radius, Quaternion rotation, int submesh,
            int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 p0 = center + rotation * new Vector3(Mathf.Cos(a0) * radius,
                    Mathf.Sin(a0) * radius, 0f);
                Vector3 p1 = center + rotation * new Vector3(Mathf.Cos(a1) * radius,
                    Mathf.Sin(a1) * radius, 0f);
                Triangle(center, p0, p1, submesh);
            }
        }

        /// <summary>Every UV unit is one metre. Faces restart at zero, but never stretch a
        /// 6 cm feature over the same 0..1 range as a 60 cm panel.</summary>
        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int submesh)
        {
            int i0 = _verts.Count;
            _verts.Add(a); _verts.Add(b); _verts.Add(c); _verts.Add(d);
            float u = Vector3.Distance(a, b);
            float v = (Vector3.Distance(b, c) + Vector3.Distance(a, d)) * 0.5f;
            _uvs.Add(Vector2.zero); _uvs.Add(new Vector2(u, 0f));
            _uvs.Add(new Vector2(u, v)); _uvs.Add(new Vector2(0f, v));
            var tris = _subTriangles[submesh];
            tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
            tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
        }

        private void Triangle(Vector3 a, Vector3 b, Vector3 c, int submesh)
        {
            int i0 = _verts.Count;
            _verts.Add(a); _verts.Add(b); _verts.Add(c);
            float u = Vector3.Distance(a, b);
            float v = Vector3.Distance(a, c);
            _uvs.Add(Vector2.zero); _uvs.Add(new Vector2(u, 0f)); _uvs.Add(new Vector2(0f, v));
            var tris = _subTriangles[submesh];
            tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
        }

        private void ApplyVariant()
        {
            if (_renderer == null) return;
            SetMaterialSurface(PlasticSubmesh, PlasticColor, 0.55f, 0f);
            SetMaterialSurface(DetailSubmesh, DarkDetail, 0.12f, 0f);
            SetMaterialSurface(MetalSubmesh, PanelMetalColor, 0.38f, 0.65f);
        }

        private void SetMaterialSurface(int materialIndex, Color color, float smoothness,
            float metallic)
        {
            _renderer.GetPropertyBlock(_variantBlock, materialIndex);
            _variantBlock.SetColor(BaseColorId, color);
            _variantBlock.SetColor(ColorId, color);
            _variantBlock.SetFloat(SmoothnessId, smoothness);
            _variantBlock.SetFloat(MetallicId, metallic);
            _renderer.SetPropertyBlock(_variantBlock, materialIndex);
            _variantBlock.Clear();
        }

        private void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
        }
    }
}
