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
        public const int PlasticSubmesh = 0;
        public const int AccentSubmesh = 1;
        public const int MetalSubmesh = 2;
        public const int SubmeshCount = 3;

        public static readonly Color WhitePlastic = new(0.92f, 0.92f, 0.91f, 1f);
        public static readonly Color BlackPlastic = new(0.055f, 0.06f, 0.065f, 1f);
        public static readonly Color DarkAccent = new(0.20f, 0.21f, 0.22f, 1f);
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
        /// <summary>Submesh 0 — the plastic body (plate, rocker, breakers): this is what
        /// paint and the Objects finish land on (design/29 §7a).</summary>
        private readonly List<int> _tris = new();
        /// <summary>Submesh 1 — accents: socket pins, screws, DIN rail, panel handle.
        /// Dark metal, never painted (issue #134).</summary>
        private readonly List<int> _trisAccent = new();
        /// <summary>Submesh 2 — panel enclosure and door, brushed metal.</summary>
        private readonly List<int> _trisMetal = new();
        private readonly List<Vector2> _uvs = new();

        public FixtureKind Kind { get; private set; }
        public int Posts { get; private set; } = 1;
        public int Keys { get; private set; } = 1;
        public bool BlackVariant { get; private set; }
        public bool PanelOpen { get; private set; }
        public Color PlasticColor => BlackVariant ? BlackPlastic : WhitePlastic;
        public Color PanelMetalColor => BlackVariant ? BlackMetal : BrushedMetal;

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
            FixtureKind.Junction => ElectricalDefaults.JunctionBoxSize,
            _ => ElectricalDefaults.PanelBoxWidth,
        };

        public float BlockHeight => Kind switch
        {
            FixtureKind.Panel => ElectricalDefaults.PanelBoxHeight,
            FixtureKind.Junction => ElectricalDefaults.JunctionBoxSize,
            _ => ElectricalDefaults.PostModule,
        };

        /// <summary>Cable entry point in local space: top center of the block for
        /// outlets/switches, bottom center for the panel (wires dive into it), lid
        /// center for the junction box (wires branch through its face).</summary>
        public Vector3 TerminalLocal => Kind switch
        {
            FixtureKind.Panel => new Vector3(0f, -ElectricalDefaults.PanelBoxHeight * 0.5f,
                ElectricalDefaults.PanelBoxDepth * 0.5f),
            FixtureKind.Junction => new Vector3(0f, 0f, ElectricalDefaults.JunctionBoxDepth),
            _ => new Vector3(0f, ElectricalDefaults.PostModule * 0.5f, ElectricalDefaults.PlateDepth * 0.5f),
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
            if (_renderer != null && _renderer.sharedMaterials.Length < SubmeshCount)
            {
                var mats = _renderer.sharedMaterials;
                var plastic = mats.Length > 0 ? mats[0] : null;
                var accent = mats.Length > 1 ? mats[1] : plastic;
                _renderer.sharedMaterials = new[] { plastic, accent, plastic };
            }
        }

        public void Rebuild() => Build(Kind, Posts, Keys, BlackVariant, PanelOpen);

        /// <summary>Placement is the transform, so a move never re-cooks the mesh.</summary>
        public void MoveBy(Vector3 delta) => transform.position += delta;

        public void SetBlackVariant(bool black)
        {
            Ensure();
            if (BlackVariant == black) return;
            BlackVariant = black;
            ApplyVariant();
        }

        public void SetPanelOpen(bool open)
        {
            bool next = Kind == FixtureKind.Panel && open;
            if (PanelOpen == next) return;
            PanelOpen = next;
            if (Kind == FixtureKind.Panel)
                Build(Kind, Posts, Keys, BlackVariant, PanelOpen);
        }

        public void Build(FixtureKind kind, int posts, int keys) =>
            Build(kind, posts, keys, BlackVariant, PanelOpen);

        public void Build(FixtureKind kind, int posts, int keys, bool blackVariant,
            bool panelOpen)
        {
            Ensure();
            Kind = kind;
            Posts = Mathf.Clamp(posts, 1, ElectricalDefaults.MaxPosts);
            Keys = Mathf.Clamp(keys, 1, ElectricalDefaults.MaxKeys);
            BlackVariant = blackVariant;
            PanelOpen = kind == FixtureKind.Panel && panelOpen;
            _verts.Clear(); _tris.Clear(); _trisAccent.Clear(); _trisMetal.Clear(); _uvs.Clear();

            switch (Kind)
            {
                case FixtureKind.Outlet:
                {
                    // Plate with chamfered edges (issue #134) and a RECESSED socket cup
                    // per post with its two pin holes — the shape people recognise from
                    // across the room, not a boss glued onto a slab.
                    float w = Posts * ElectricalDefaults.PostModule;
                    float h = ElectricalDefaults.PostModule;
                    float d = ElectricalDefaults.PlateDepth;
                    const float cupR = 0.0265f;
                    var face = AddChamferedBox(new Vector3(0f, 0f, d * 0.5f),
                        new Vector3(w, h, d), Chamfer, emitFront: false);
                    // the plate face is a ring around each cup — otherwise the recess
                    // hides behind a solid quad and the outlet reads as a slab
                    AddFaceWithRoundHoles(face, FrontZ, Posts, cupR, 16);
                    for (int i = 0; i < Posts; i++)
                    {
                        float cx = (i + 0.5f) * ElectricalDefaults.PostModule - w * 0.5f;
                        AddRoundRecess(new Vector2(cx, 0f), d, cupR, 0.005f, 16, _tris);
                        // two pins, 19 mm apart, sunk into the cup floor
                        for (int s = -1; s <= 1; s += 2)
                            AddRoundRecess(new Vector2(cx + s * 0.0095f, 0f), d - 0.005f,
                                0.0022f, 0.006f, 6, _trisAccent);
                        // earth contacts: the two side strips of a Schuko
                        for (int s = -1; s <= 1; s += 2)
                            AddBox(new Vector3(cx, s * 0.0245f, d - 0.0035f),
                                new Vector3(0.016f, 0.003f, 0.004f), _trisAccent);
                    }
                    break;
                }
                case FixtureKind.Switch:
                {
                    // Rocker with a REAL gap and a tilt: the shadow line around the key
                    // is what makes a switch read as a switch.
                    float w = ElectricalDefaults.PostModule;
                    float h = ElectricalDefaults.PostModule;
                    float d = ElectricalDefaults.PlateDepth;
                    AddChamferedBox(new Vector3(0f, 0f, d * 0.5f), new Vector3(w, h, d), Chamfer);
                    const float area = 0.062f, gap = 0.0035f, sink = 0.0015f;
                    float keyW = (area - (Keys - 1) * gap) / Keys;
                    // the well the keys sit in — its floor is the shadow behind the gaps
                    AddRectRecess(new Vector3(0f, 0f, d), new Vector2(area + gap, area + gap),
                        sink, _trisAccent);
                    for (int i = 0; i < Keys; i++)
                    {
                        float cx = -area * 0.5f + keyW * 0.5f + i * (keyW + gap);
                        // pressed-in at the bottom, proud at the top — a rocker at rest
                        AddTiltedKey(cx, keyW, area, d - sink, 0.007f, 0.0022f);
                    }
                    break;
                }
                case FixtureKind.Junction:
                {
                    // distribution box: a small cube with a proud lid — mounts on walls
                    // and ceilings, wires branch through its face (v2)
                    float s = ElectricalDefaults.JunctionBoxSize;
                    float d = ElectricalDefaults.JunctionBoxDepth;
                    AddBox(new Vector3(0f, 0f, d * 0.5f), new Vector3(s, s, d), _tris);
                    AddChamferedBox(new Vector3(0f, 0f, d + 0.003f),
                        new Vector3(s - 0.01f, s - 0.01f, 0.006f), Chamfer);
                    // four lid screws
                    float sc = s * 0.5f - 0.012f;
                    for (int sx = -1; sx <= 1; sx += 2)
                        for (int sy = -1; sy <= 1; sy += 2)
                            AddRoundRecess(new Vector2(sx * sc, sy * sc), d + 0.006f,
                                0.0035f, 0.0015f, 6, _trisAccent);
                    break;
                }
                default: // Panel
                {
                    // Open-front metal consumer unit. A closed door still has a narrow
                    // inspection window; opening it reveals both DIN rows and breakers.
                    float w = ElectricalDefaults.PanelBoxWidth;
                    float h = ElectricalDefaults.PanelBoxHeight;
                    float d = ElectricalDefaults.PanelBoxDepth;
                    const float frame = 0.018f;
                    AddBox(new Vector3(0f, 0f, d * 0.12f),
                        new Vector3(w, h, d * 0.24f), _trisMetal);
                    AddBox(new Vector3(-w * 0.5f + frame * 0.5f, 0f, d * 0.5f),
                        new Vector3(frame, h, d), _trisMetal);
                    AddBox(new Vector3(w * 0.5f - frame * 0.5f, 0f, d * 0.5f),
                        new Vector3(frame, h, d), _trisMetal);
                    AddBox(new Vector3(0f, h * 0.5f - frame * 0.5f, d * 0.5f),
                        new Vector3(w - frame * 2f, frame, d), _trisMetal);
                    AddBox(new Vector3(0f, -h * 0.5f + frame * 0.5f, d * 0.5f),
                        new Vector3(w - frame * 2f, frame, d), _trisMetal);
                    AddBox(new Vector3(0f, 0f, d * 0.26f),
                        new Vector3(w - frame * 2f, h - frame * 2f, 0.003f), _trisAccent);

                    const int breakers = 7;
                    float breakerW = (w - 0.075f) / breakers;
                    for (int row = -1; row <= 1; row += 2)
                    {
                        float y = row * h * 0.20f;
                        AddBox(new Vector3(0f, y, d * 0.48f),
                            new Vector3(w - 0.055f, 0.016f, 0.012f), _trisAccent);
                        for (int i = 0; i < breakers; i++)
                        {
                            float x = -0.5f * (breakers - 1) * breakerW + i * breakerW;
                            AddChamferedBox(new Vector3(x, y, d * 0.70f),
                                new Vector3(breakerW - 0.002f, 0.050f, 0.030f),
                                Chamfer * 0.7f, _tris);
                            AddBox(new Vector3(x, y + 0.004f, d * 0.88f),
                                new Vector3(breakerW * 0.42f, 0.014f, 0.005f), _trisAccent);
                        }
                    }

                    float doorW = w - 0.02f, doorH = h - 0.02f, doorZ = d + 0.005f;
                    const float doorDepth = 0.010f;
                    if (!PanelOpen)
                    {
                        float winW = doorW - 0.05f, winH = 0.055f;
                        float winY = h * 0.12f;
                        var doorFace = AddChamferedBox(new Vector3(0f, 0f, doorZ),
                            new Vector3(doorW, doorH, doorDepth), Chamfer,
                            emitFront: false, tris: _trisMetal);
                        float faceZ = FrontZ;
                        AddFaceWithRectHole(doorFace, faceZ,
                            new Vector4(-winW * 0.5f, winY - winH * 0.5f,
                                winW * 0.5f, winY + winH * 0.5f), _trisMetal);
                        AddRectRecess(new Vector3(0f, winY, faceZ),
                            new Vector2(winW, winH), 0.012f, _trisAccent);
                        AddBox(new Vector3(doorW * 0.5f - 0.018f, -h * 0.18f,
                            faceZ + 0.006f), new Vector3(0.01f, 0.05f, 0.012f), _trisAccent);
                        AddDoorScrews(Vector3.zero, Quaternion.identity, doorW, doorH,
                            faceZ + 0.0002f);
                    }
                    else
                    {
                        var rotation = Quaternion.Euler(0f, -100f, 0f);
                        var hinge = new Vector3(-doorW * 0.5f, 0f, doorZ);
                        var center = hinge + rotation * new Vector3(doorW * 0.5f, 0f, 0f);
                        AddRotatedBox(center, new Vector3(doorW, doorH, doorDepth),
                            rotation, _trisMetal);
                        AddRotatedBox(center + rotation * new Vector3(
                                doorW * 0.5f - 0.018f, -h * 0.18f, doorDepth * 0.8f),
                            new Vector3(0.01f, 0.05f, 0.012f), rotation, _trisAccent);
                        AddDoorScrews(center, rotation, doorW, doorH,
                            doorDepth * 0.5f + 0.0002f);
                    }

                    // two hinge barrels remain fixed to the enclosure.
                    for (int s = -1; s <= 1; s += 2)
                        AddBox(new Vector3(-doorW * 0.5f - 0.004f, s * doorH * 0.32f, doorZ),
                            new Vector3(0.008f, 0.03f, 0.012f), _trisAccent);
                    break;
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.subMeshCount = SubmeshCount;
            _mesh.SetTriangles(_tris, PlasticSubmesh, false);
            _mesh.SetTriangles(_trisAccent, AccentSubmesh, false);
            _mesh.SetTriangles(_trisMetal, MetalSubmesh, false);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            if (_collider != null)
            {
                _collider.sharedMesh = null;
                _collider.sharedMesh = _mesh;
            }
            ApplyVariant();
        }

        /// <summary>Edge break on every visible plastic part (issue #134): 1.2 mm reads
        /// as a moulded bevel at arm's length without eating triangles.</summary>
        private const float Chamfer = 0.0012f;

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => Quad(a, b, c, d, _tris);

        /// <summary>One quad into the given submesh, with METRIC UVs (design/29 §4):
        /// the plastic grain then tiles at real-world scale like every other surface.</summary>
        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, List<int> tris)
        {
            int i0 = _verts.Count;
            _verts.Add(a); _verts.Add(b); _verts.Add(c); _verts.Add(d);
            var n = Vector3.Cross(b - a, c - a);
            _uvs.Add(RoomPlanner.Core.BoxUv.Project(a, n));
            _uvs.Add(RoomPlanner.Core.BoxUv.Project(b, n));
            _uvs.Add(RoomPlanner.Core.BoxUv.Project(c, n));
            _uvs.Add(RoomPlanner.Core.BoxUv.Project(d, n));
            tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
            tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
        }

        /// <summary>One triangle with metric UVs — the fan of a round recess floor.</summary>
        private void Tri(Vector3 a, Vector3 b, Vector3 c, List<int> tris)
        {
            int i0 = _verts.Count;
            _verts.Add(a); _verts.Add(b); _verts.Add(c);
            var n = Vector3.Cross(b - a, c - a);
            _uvs.Add(RoomPlanner.Core.BoxUv.Project(a, n));
            _uvs.Add(RoomPlanner.Core.BoxUv.Project(b, n));
            _uvs.Add(RoomPlanner.Core.BoxUv.Project(c, n));
            tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
        }

        /// <summary>Axis-aligned box, every face wound outward (rule 1.1).</summary>
        private void AddBox(Vector3 center, Vector3 size) => AddBox(center, size, _tris);

        private void AddBox(Vector3 center, Vector3 size, List<int> tris)
        {
            Vector3 n = center - size * 0.5f, x = center + size * 0.5f;
            Quad(new Vector3(n.x, n.y, x.z), new Vector3(x.x, n.y, x.z), new Vector3(x.x, x.y, x.z), new Vector3(n.x, x.y, x.z), tris); // +Z
            Quad(new Vector3(x.x, n.y, n.z), new Vector3(n.x, n.y, n.z), new Vector3(n.x, x.y, n.z), new Vector3(x.x, x.y, n.z), tris); // -Z
            Quad(new Vector3(x.x, n.y, x.z), new Vector3(x.x, n.y, n.z), new Vector3(x.x, x.y, n.z), new Vector3(x.x, x.y, x.z), tris); // +X
            Quad(new Vector3(n.x, n.y, n.z), new Vector3(n.x, n.y, x.z), new Vector3(n.x, x.y, x.z), new Vector3(n.x, x.y, n.z), tris); // -X
            Quad(new Vector3(n.x, x.y, x.z), new Vector3(x.x, x.y, x.z), new Vector3(x.x, x.y, n.z), new Vector3(n.x, x.y, n.z), tris); // +Y
            Quad(new Vector3(n.x, n.y, n.z), new Vector3(x.x, n.y, n.z), new Vector3(x.x, n.y, x.z), new Vector3(n.x, n.y, x.z), tris); // -Y
        }

        /// <summary>
        /// Box whose FRONT face (+Z) is inset by <paramref name="chamfer"/>, so the edge
        /// catches the light instead of ending in a razor line. Back and sides stay
        /// square — they sit against the wall and nobody sees them.
        /// </summary>
        private void AddChamferedBox(Vector3 center, Vector3 size, float chamfer)
            => AddChamferedBox(center, size, chamfer, true, _tris);

        private void AddChamferedBox(Vector3 center, Vector3 size, float chamfer,
            List<int> tris) => AddChamferedBox(center, size, chamfer, true, tris);

        /// <summary>Same, but the caller may take over the FRONT face — a recess is only
        /// visible if the face it sinks into actually has a hole (issue #134). Returns the
        /// inset front rect as (min x, min y, max x, max y) at <see cref="FrontZ"/>.</summary>
        private Vector4 AddChamferedBox(Vector3 center, Vector3 size, float chamfer, bool emitFront)
            => AddChamferedBox(center, size, chamfer, emitFront, _tris);

        private Vector4 AddChamferedBox(Vector3 center, Vector3 size, float chamfer,
            bool emitFront, List<int> tris)
        {
            chamfer = Mathf.Min(chamfer, Mathf.Min(size.x, size.y) * 0.25f);
            Vector3 n = center - size * 0.5f, x = center + size * 0.5f;
            float zBand = Mathf.Min(chamfer, size.z * 0.5f);
            float zRing = x.z - zBand;

            // sides up to the chamfer band
            Quad(new Vector3(x.x, n.y, zRing), new Vector3(x.x, n.y, n.z), new Vector3(x.x, x.y, n.z), new Vector3(x.x, x.y, zRing), tris);
            Quad(new Vector3(n.x, n.y, n.z), new Vector3(n.x, n.y, zRing), new Vector3(n.x, x.y, zRing), new Vector3(n.x, x.y, n.z), tris);
            Quad(new Vector3(n.x, x.y, zRing), new Vector3(x.x, x.y, zRing), new Vector3(x.x, x.y, n.z), new Vector3(n.x, x.y, n.z), tris);
            Quad(new Vector3(n.x, n.y, n.z), new Vector3(x.x, n.y, n.z), new Vector3(x.x, n.y, zRing), new Vector3(n.x, n.y, zRing), tris);
            // back
            Quad(new Vector3(x.x, n.y, n.z), new Vector3(n.x, n.y, n.z), new Vector3(n.x, x.y, n.z), new Vector3(x.x, x.y, n.z), tris);

            // chamfer band: outer ring at zRing → inner ring at the front
            float ix0 = n.x + chamfer, ix1 = x.x - chamfer;
            float iy0 = n.y + chamfer, iy1 = x.y - chamfer;
            Quad(new Vector3(n.x, n.y, zRing), new Vector3(x.x, n.y, zRing), new Vector3(ix1, iy0, x.z), new Vector3(ix0, iy0, x.z), tris);
            Quad(new Vector3(x.x, x.y, zRing), new Vector3(n.x, x.y, zRing), new Vector3(ix0, iy1, x.z), new Vector3(ix1, iy1, x.z), tris);
            Quad(new Vector3(x.x, n.y, zRing), new Vector3(x.x, x.y, zRing), new Vector3(ix1, iy1, x.z), new Vector3(ix1, iy0, x.z), tris);
            Quad(new Vector3(n.x, x.y, zRing), new Vector3(n.x, n.y, zRing), new Vector3(ix0, iy0, x.z), new Vector3(ix0, iy1, x.z), tris);
            // front face
            if (emitFront)
                Quad(new Vector3(ix0, iy0, x.z), new Vector3(ix1, iy0, x.z), new Vector3(ix1, iy1, x.z), new Vector3(ix0, iy1, x.z), tris);
            FrontZ = x.z;
            return new Vector4(ix0, iy0, ix1, iy1);
        }

        /// <summary>Z of the last chamfered box's front plane.</summary>
        private float FrontZ;

        /// <summary>
        /// Front face with round holes in it: each hole gets its own cell of the rect
        /// (posts are evenly spaced), and the cell is a square-with-a-circular-hole built
        /// as a radial fan. Without this the socket cups sit BEHIND a solid face and the
        /// plate reads as a slab (found on the headless shot, issue #134).
        /// </summary>
        private void AddFaceWithRoundHoles(Vector4 rect, float z, int cells, float radius,
            int segments)
        {
            float x0 = rect.x, y0 = rect.y, x1 = rect.z, y1 = rect.w;
            float cellW = (x1 - x0) / Mathf.Max(1, cells);
            segments = Mathf.Max(8, segments);
            for (int c = 0; c < cells; c++)
            {
                float cx0 = x0 + c * cellW, cx1 = cx0 + cellW;
                float cx = (cx0 + cx1) * 0.5f, cy = (y0 + y1) * 0.5f;
                float hw = (cx1 - cx0) * 0.5f, hh = (y1 - y0) * 0.5f;
                for (int i = 0; i < segments; i++)
                {
                    float a0 = i / (float)segments * Mathf.PI * 2f;
                    float a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                    var p0 = new Vector3(cx + Mathf.Cos(a0) * radius, cy + Mathf.Sin(a0) * radius, z);
                    var p1 = new Vector3(cx + Mathf.Cos(a1) * radius, cy + Mathf.Sin(a1) * radius, z);
                    var s0 = RectPoint(cx, cy, hw, hh, a0, z);
                    var s1 = RectPoint(cx, cy, hw, hh, a1, z);
                    Quad(p0, s0, s1, p1);   // wound to face the room (rule 1.1)
                }
            }
        }

        /// <summary>Where the ray at <paramref name="angle"/> leaves the cell rect.</summary>
        private static Vector3 RectPoint(float cx, float cy, float hw, float hh, float angle, float z)
        {
            float dx = Mathf.Cos(angle), dy = Mathf.Sin(angle);
            float tx = Mathf.Abs(dx) < 1e-6f ? float.MaxValue : hw / Mathf.Abs(dx);
            float ty = Mathf.Abs(dy) < 1e-6f ? float.MaxValue : hh / Mathf.Abs(dy);
            float t = Mathf.Min(tx, ty);
            return new Vector3(cx + dx * t, cy + dy * t, z);
        }

        /// <summary>Front face with ONE rectangular hole — the window of the panel door.</summary>
        private void AddFaceWithRectHole(Vector4 rect, float z, Vector4 hole) =>
            AddFaceWithRectHole(rect, z, hole, _tris);

        private void AddFaceWithRectHole(Vector4 rect, float z, Vector4 hole, List<int> tris)
        {
            float x0 = rect.x, y0 = rect.y, x1 = rect.z, y1 = rect.w;
            float hx0 = Mathf.Clamp(hole.x, x0, x1), hy0 = Mathf.Clamp(hole.y, y0, y1);
            float hx1 = Mathf.Clamp(hole.z, x0, x1), hy1 = Mathf.Clamp(hole.w, y0, y1);
            // four bands around the hole
            Quad(new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, hy0, z), new Vector3(x0, hy0, z), tris);
            Quad(new Vector3(x0, hy1, z), new Vector3(x1, hy1, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z), tris);
            Quad(new Vector3(x0, hy0, z), new Vector3(hx0, hy0, z), new Vector3(hx0, hy1, z), new Vector3(x0, hy1, z), tris);
            Quad(new Vector3(hx1, hy0, z), new Vector3(x1, hy0, z), new Vector3(x1, hy1, z), new Vector3(hx1, hy1, z), tris);
        }

        /// <summary>Round hole sunk into a +Z face at <paramref name="faceZ"/>: a ring of
        /// wall quads plus the floor disc, wound to be seen from the front.</summary>
        private void AddRoundRecess(Vector2 center, float faceZ, float radius, float depth,
            int segments, List<int> tris)
        {
            segments = Mathf.Max(4, segments);
            float floorZ = faceZ - depth;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                var p0 = new Vector3(center.x + Mathf.Cos(a0) * radius, center.y + Mathf.Sin(a0) * radius, faceZ);
                var p1 = new Vector3(center.x + Mathf.Cos(a1) * radius, center.y + Mathf.Sin(a1) * radius, faceZ);
                var q0 = new Vector3(p0.x, p0.y, floorZ);
                var q1 = new Vector3(p1.x, p1.y, floorZ);
                // Wall: the material is OUTSIDE the cylinder, so the surface normal
                // points toward the axis — the cup subtracts volume (rule 1.1).
                Quad(p0, p1, q1, q0, tris);
                // floor fan, facing the room
                Tri(q0, q1, new Vector3(center.x, center.y, floorZ), tris);
            }
        }

        /// <summary>Rectangular well sunk into a +Z face — the shadow behind switch keys
        /// and the window of the panel door.</summary>
        private void AddRectRecess(Vector3 faceCenter, Vector2 size, float depth, List<int> tris)
        {
            float x0 = faceCenter.x - size.x * 0.5f, x1 = faceCenter.x + size.x * 0.5f;
            float y0 = faceCenter.y - size.y * 0.5f, y1 = faceCenter.y + size.y * 0.5f;
            float zf = faceCenter.z, zb = faceCenter.z - depth;
            Quad(new Vector3(x0, y0, zf), new Vector3(x1, y0, zf), new Vector3(x1, y0, zb), new Vector3(x0, y0, zb), tris);
            Quad(new Vector3(x1, y1, zf), new Vector3(x0, y1, zf), new Vector3(x0, y1, zb), new Vector3(x1, y1, zb), tris);
            Quad(new Vector3(x1, y0, zf), new Vector3(x1, y1, zf), new Vector3(x1, y1, zb), new Vector3(x1, y0, zb), tris);
            Quad(new Vector3(x0, y1, zf), new Vector3(x0, y0, zf), new Vector3(x0, y0, zb), new Vector3(x0, y1, zb), tris);
            Quad(new Vector3(x0, y0, zb), new Vector3(x1, y0, zb), new Vector3(x1, y1, zb), new Vector3(x0, y1, zb), tris);
        }

        /// <summary>A rocker at rest: pressed in at the bottom, proud at the top, with a
        /// chamfered face. Six faces, built directly so the tilt is real geometry.</summary>
        private void AddTiltedKey(float cx, float width, float height, float baseZ,
            float thickTop, float thickBottom)
        {
            float x0 = cx - width * 0.5f, x1 = cx + width * 0.5f;
            float y0 = -height * 0.5f, y1 = height * 0.5f;
            float zTop = baseZ + thickTop, zBottom = baseZ + thickBottom;

            // front face (tilted): top edge proud, bottom edge sunk
            Quad(new Vector3(x0, y0, zBottom), new Vector3(x1, y0, zBottom),
                new Vector3(x1, y1, zTop), new Vector3(x0, y1, zTop));
            // sides
            Quad(new Vector3(x1, y0, zBottom), new Vector3(x1, y0, baseZ),
                new Vector3(x1, y1, baseZ), new Vector3(x1, y1, zTop));
            Quad(new Vector3(x0, y0, baseZ), new Vector3(x0, y0, zBottom),
                new Vector3(x0, y1, zTop), new Vector3(x0, y1, baseZ));
            // top and bottom edges
            Quad(new Vector3(x0, y1, zTop), new Vector3(x1, y1, zTop),
                new Vector3(x1, y1, baseZ), new Vector3(x0, y1, baseZ));
            Quad(new Vector3(x0, y0, baseZ), new Vector3(x1, y0, baseZ),
                new Vector3(x1, y0, zBottom), new Vector3(x0, y0, zBottom));
        }

        private void AddRotatedBox(Vector3 center, Vector3 size, Quaternion rotation,
            List<int> tris)
        {
            Vector3 n = -size * 0.5f, x = size * 0.5f;
            Vector3 P(float px, float py, float pz) =>
                center + rotation * new Vector3(px, py, pz);
            Quad(P(n.x, n.y, x.z), P(x.x, n.y, x.z), P(x.x, x.y, x.z), P(n.x, x.y, x.z), tris);
            Quad(P(x.x, n.y, n.z), P(n.x, n.y, n.z), P(n.x, x.y, n.z), P(x.x, x.y, n.z), tris);
            Quad(P(x.x, n.y, x.z), P(x.x, n.y, n.z), P(x.x, x.y, n.z), P(x.x, x.y, x.z), tris);
            Quad(P(n.x, n.y, n.z), P(n.x, n.y, x.z), P(n.x, x.y, x.z), P(n.x, x.y, n.z), tris);
            Quad(P(n.x, x.y, x.z), P(x.x, x.y, x.z), P(x.x, x.y, n.z), P(n.x, x.y, n.z), tris);
            Quad(P(n.x, n.y, n.z), P(x.x, n.y, n.z), P(x.x, n.y, x.z), P(n.x, n.y, x.z), tris);
        }

        private void AddDoorScrews(Vector3 center, Quaternion rotation, float width,
            float height, float localZ)
        {
            float x = width * 0.5f - 0.012f;
            float y = height * 0.5f - 0.012f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    AddDisc(center + rotation * new Vector3(sx * x, sy * y, localZ),
                        0.003f, rotation, _trisAccent, 10);
        }

        private void AddDisc(Vector3 center, float radius, Quaternion rotation,
            List<int> tris, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 p0 = center + rotation * new Vector3(
                    Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius, 0f);
                Vector3 p1 = center + rotation * new Vector3(
                    Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius, 0f);
                Tri(center, p0, p1, tris);
            }
        }

        private void ApplyVariant()
        {
            if (_renderer == null) return;
            SetMaterialSurface(PlasticSubmesh, PlasticColor, 0.55f, 0f);
            SetMaterialSurface(AccentSubmesh, DarkAccent, 0.75f, 0.45f);
            SetMaterialSurface(MetalSubmesh, PanelMetalColor, 0.38f, 0.65f);
        }

        private void SetMaterialSurface(int index, Color color, float smoothness,
            float metallic)
        {
            _renderer.GetPropertyBlock(_variantBlock, index);
            _variantBlock.SetColor(BaseColorId, color);
            _variantBlock.SetColor(ColorId, color);
            _variantBlock.SetFloat(SmoothnessId, smoothness);
            _variantBlock.SetFloat(MetallicId, metallic);
            _renderer.SetPropertyBlock(_variantBlock, index);
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
