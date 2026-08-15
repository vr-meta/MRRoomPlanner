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
        /// <summary>Submesh 0 — the plastic body (plate, rocker, breakers): this is what
        /// paint and the Objects finish land on (design/29 §7a).</summary>
        private readonly List<int> _tris = new();
        /// <summary>Submesh 1 — accents: socket pins, screws, DIN rail, panel handle.
        /// Dark metal, never painted (issue #134).</summary>
        private readonly List<int> _trisAccent = new();
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
            _verts.Clear(); _tris.Clear(); _trisAccent.Clear(); _uvs.Clear();

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
                    // A real consumer unit: enclosure, chamfered door proud of it, a
                    // window with the breaker row behind it, hinges and a handle.
                    float w = ElectricalDefaults.PanelBoxWidth;
                    float h = ElectricalDefaults.PanelBoxHeight;
                    float d = ElectricalDefaults.PanelBoxDepth;
                    AddBox(new Vector3(0f, 0f, d * 0.5f), new Vector3(w, h, d), _tris);

                    float doorW = w - 0.02f, doorH = h - 0.02f, doorZ = d + 0.005f;
                    float winW = doorW - 0.05f, winH = 0.055f;
                    float winY = h * 0.12f;
                    var doorFace = AddChamferedBox(new Vector3(0f, 0f, doorZ),
                        new Vector3(doorW, doorH, 0.01f), Chamfer, emitFront: false);
                    float faceZ = FrontZ;
                    // the door face is cut around the window, so the breakers are really
                    // seen through it rather than buried behind a solid panel
                    AddFaceWithRectHole(doorFace, faceZ,
                        new Vector4(-winW * 0.5f, winY - winH * 0.5f, winW * 0.5f, winY + winH * 0.5f));
                    AddRectRecess(new Vector3(0f, winY, faceZ), new Vector2(winW, winH),
                        0.012f, _trisAccent);
                    float railZ = faceZ - 0.012f;
                    AddBox(new Vector3(0f, h * 0.12f - winH * 0.5f + 0.006f, railZ + 0.002f),
                        new Vector3(winW - 0.004f, 0.008f, 0.004f), _trisAccent);
                    const float breakerW = 0.0175f;
                    int breakers = Mathf.Max(1, Mathf.FloorToInt((winW - 0.006f) / breakerW));
                    for (int i = 0; i < breakers; i++)
                    {
                        float cx = -(breakers - 1) * 0.5f * breakerW + i * breakerW;
                        AddBox(new Vector3(cx, h * 0.12f + 0.004f, railZ + 0.008f),
                            new Vector3(breakerW - 0.002f, winH - 0.02f, 0.012f), _tris);
                    }

                    // handle on the right, two hinge barrels on the left
                    AddBox(new Vector3(doorW * 0.5f - 0.018f, -h * 0.18f, faceZ + 0.006f),
                        new Vector3(0.01f, 0.05f, 0.012f), _trisAccent);
                    for (int s = -1; s <= 1; s += 2)
                        AddBox(new Vector3(-doorW * 0.5f - 0.004f, s * doorH * 0.32f, doorZ),
                            new Vector3(0.008f, 0.03f, 0.012f), _trisAccent);
                    break;
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            if (_trisAccent.Count > 0)
            {
                _mesh.subMeshCount = 2;
                _mesh.SetTriangles(_tris, 0);
                _mesh.SetTriangles(_trisAccent, 1);
            }
            else
            {
                _mesh.subMeshCount = 1;
                _mesh.SetTriangles(_tris, 0);
            }
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            if (_collider != null)
            {
                _collider.sharedMesh = null;
                _collider.sharedMesh = _mesh;
            }
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
            => AddChamferedBox(center, size, chamfer, true);

        /// <summary>Same, but the caller may take over the FRONT face — a recess is only
        /// visible if the face it sinks into actually has a hole (issue #134). Returns the
        /// inset front rect as (min x, min y, max x, max y) at <see cref="FrontZ"/>.</summary>
        private Vector4 AddChamferedBox(Vector3 center, Vector3 size, float chamfer, bool emitFront)
        {
            chamfer = Mathf.Min(chamfer, Mathf.Min(size.x, size.y) * 0.25f);
            Vector3 n = center - size * 0.5f, x = center + size * 0.5f;
            float zBand = Mathf.Min(chamfer, size.z * 0.5f);
            float zRing = x.z - zBand;

            // sides up to the chamfer band
            Quad(new Vector3(x.x, n.y, zRing), new Vector3(x.x, n.y, n.z), new Vector3(x.x, x.y, n.z), new Vector3(x.x, x.y, zRing));
            Quad(new Vector3(n.x, n.y, n.z), new Vector3(n.x, n.y, zRing), new Vector3(n.x, x.y, zRing), new Vector3(n.x, x.y, n.z));
            Quad(new Vector3(n.x, x.y, zRing), new Vector3(x.x, x.y, zRing), new Vector3(x.x, x.y, n.z), new Vector3(n.x, x.y, n.z));
            Quad(new Vector3(n.x, n.y, n.z), new Vector3(x.x, n.y, n.z), new Vector3(x.x, n.y, zRing), new Vector3(n.x, n.y, zRing));
            // back
            Quad(new Vector3(x.x, n.y, n.z), new Vector3(n.x, n.y, n.z), new Vector3(n.x, x.y, n.z), new Vector3(x.x, x.y, n.z));

            // chamfer band: outer ring at zRing → inner ring at the front
            float ix0 = n.x + chamfer, ix1 = x.x - chamfer;
            float iy0 = n.y + chamfer, iy1 = x.y - chamfer;
            Quad(new Vector3(n.x, n.y, zRing), new Vector3(x.x, n.y, zRing), new Vector3(ix1, iy0, x.z), new Vector3(ix0, iy0, x.z));
            Quad(new Vector3(x.x, x.y, zRing), new Vector3(n.x, x.y, zRing), new Vector3(ix0, iy1, x.z), new Vector3(ix1, iy1, x.z));
            Quad(new Vector3(x.x, n.y, zRing), new Vector3(x.x, x.y, zRing), new Vector3(ix1, iy1, x.z), new Vector3(ix1, iy0, x.z));
            Quad(new Vector3(n.x, x.y, zRing), new Vector3(n.x, n.y, zRing), new Vector3(ix0, iy0, x.z), new Vector3(ix0, iy1, x.z));
            // front face
            if (emitFront)
                Quad(new Vector3(ix0, iy0, x.z), new Vector3(ix1, iy0, x.z), new Vector3(ix1, iy1, x.z), new Vector3(ix0, iy1, x.z));
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
        private void AddFaceWithRectHole(Vector4 rect, float z, Vector4 hole)
        {
            float x0 = rect.x, y0 = rect.y, x1 = rect.z, y1 = rect.w;
            float hx0 = Mathf.Clamp(hole.x, x0, x1), hy0 = Mathf.Clamp(hole.y, y0, y1);
            float hx1 = Mathf.Clamp(hole.z, x0, x1), hy1 = Mathf.Clamp(hole.w, y0, y1);
            // four bands around the hole
            Quad(new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, hy0, z), new Vector3(x0, hy0, z));
            Quad(new Vector3(x0, hy1, z), new Vector3(x1, hy1, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z));
            Quad(new Vector3(x0, hy0, z), new Vector3(hx0, hy0, z), new Vector3(hx0, hy1, z), new Vector3(x0, hy1, z));
            Quad(new Vector3(hx1, hy0, z), new Vector3(x1, hy0, z), new Vector3(x1, hy1, z), new Vector3(hx1, hy1, z));
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

        private void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
        }
    }
}
