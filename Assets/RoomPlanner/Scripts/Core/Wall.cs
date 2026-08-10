using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Walls
{
    /// <summary>How thickness is offset relative to the centerline.</summary>
    public enum WallOffsetMode
    {
        Outer,   // line = inner face, thickness grows outward (default: we see the inner edge)
        Center,  // line at the middle
        Inner    // line = outer face, thickness grows inward
    }

    /// <summary>How consecutive segments meet at an interior corner.</summary>
    public enum WallJoin
    {
        Miter,   // sharp: offset edges extended to intersect
        Bevel,   // flat chamfer across the corner
        Round    // arc across the corner
    }

    /// <summary>
    /// A wall = polyline of centerline points + thickness + height → procedural mesh.
    /// Built as an extruded footprint from two offset contours (inner/outer) with mitered/
    /// beveled/rounded corners — one clean strip, no overlapping boxes.
    /// Winding matters even with a two-sided material: MeshCollider raycasts only hit
    /// front faces, so every triangle must face OUTWARD (away from the wall solid) or the
    /// Select/Measure tools pick the far face and report a point one thickness off.
    /// Project principle: parameters → mesh (no CSG / baked unwraps). In RoomPlanner.Core
    /// so the geometry is unit-testable without device deps.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class Wall : MonoBehaviour
    {
        [Tooltip("Optional child MeshFilter that draws edges as lines (for visible seams).")]
        [SerializeField] private MeshFilter edgesFilter;

        private const int RoundSegments = 5;
        private const float MinSize = 0.001f;        // smallest sane thickness/height, m
        /// <summary>How far an imported door leaf stands open (degrees around the hinge) —
        /// wide enough to walk through, closed enough to read as a door.</summary>
        public const float DoorOpenAngleDeg = 75f;
        private const float DupEpsSqr = 1e-6f;       // consecutive points closer than 1 mm collapse
        private const float MiterLimit = 4f;         // max miter length in multiples of the offset

        private MeshFilter _mf;
        private Mesh _mesh;
        private Mesh _edges;
        private MeshCollider _collider;
        private readonly List<Vector3> _pts = new();

        // Cached build parameters so the wall can rebuild itself after edits (move/vertex drag).
        private float _thickness = 0.2f;
        private float _height = 2.7f;
        private WallOffsetMode _mode = WallOffsetMode.Outer;
        private WallJoin _join = WallJoin.Miter;
        private Vector3 _interior;

        public IReadOnlyList<Vector3> Points => _pts;

        private void Ensure()
        {
            if (_mf == null) _mf = GetComponent<MeshFilter>();
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "WallMesh" };
                _mf.sharedMesh = _mesh;
            }
            if (edgesFilter != null && _edges == null)
            {
                _edges = new Mesh { name = "WallEdges" };
                edgesFilter.sharedMesh = _edges;
            }
            if (_collider == null)
            {
                _collider = GetComponent<MeshCollider>();
                if (_collider == null) _collider = gameObject.AddComponent<MeshCollider>();
            }
        }

        public void Build(List<Vector3> centerline, float thickness, float height,
            WallOffsetMode mode, WallJoin join, Vector3 interior)
        {
            Ensure();
            // The input may BE _pts (e.g. via the Points view) — clearing first would wipe it.
            if (!ReferenceEquals(centerline, _pts))
            {
                _pts.Clear();
                if (centerline != null) _pts.AddRange(centerline);
            }
            RemoveDuplicatePoints(_pts);
            _thickness = Mathf.Max(MinSize, Mathf.Abs(thickness));
            _height = Mathf.Max(MinSize, Mathf.Abs(height));
            _mode = mode; _join = join; _interior = interior;
            _mesh.Clear();
            if (_edges != null) _edges.Clear();
            if (_pts.Count < 2) { if (_collider != null) _collider.sharedMesh = null; return; }

            float dOut, dIn;
            switch (mode)
            {
                case WallOffsetMode.Center: dOut = _thickness * 0.5f; dIn = _thickness * 0.5f; break;
                case WallOffsetMode.Inner:  dOut = 0f;                dIn = _thickness;         break;
                default:                    dOut = _thickness;        dIn = 0f;                 break; // Outer
            }

            float oSign = OutwardSign(_pts, interior);
            List<Cross> sections = BuildFootprint(_pts, dOut, dIn, oSign, join);
            // oSign mirrors the footprint across the centerline, which flips triangle
            // orientation; compensate so faces always point outward.
            Triangulate(sections, _height, flip: oSign < 0f);
        }

        /// <summary>Rebuild the mesh from the current centerline and cached parameters.</summary>
        public void Rebuild()
        {
            if (_segment != null) BuildSegment(_segment);
            else Build(_pts, _thickness, _height, _mode, _join, _interior);
        }

        // ---- graph-backed mode (Phase B): one view = one WallSegment ----

        private WallSegment _segment;

        /// <summary>The graph segment this view draws, or null while in legacy polyline mode.</summary>
        public WallSegment Segment => _segment;

        /// <summary>
        /// While true, rebuilds refresh the visible mesh but leave the MeshCollider alone.
        /// Dragging a shared node has to rebuild the walls hanging off it every frame to stay
        /// WYSIWYG, and re-cooking physics meshes at 72–120 Hz is what coding rule 4.2 forbids —
        /// so the collider is re-cooked once, when the drag ends.
        /// </summary>
        public bool DeferCollider { get; set; }

        /// <summary>Re-cook the physics mesh after a deferred drag.</summary>
        public void RefreshCollider()
        {
            if (_collider == null || _mesh == null) return;
            _collider.sharedMesh = null;
            _collider.sharedMesh = _mesh.vertexCount > 0 ? _mesh : null;
        }

        /// <summary>
        /// Draw ONE graph segment, with its ends shaped by whatever meets it at each node
        /// (<see cref="WallMesh"/>). Replaces the polyline path for graph-backed walls; the
        /// polyline <see cref="Build"/> stays for the legacy tool and its tests.
        /// </summary>
        public void BuildSegment(WallSegment s)
        {
            Ensure();
            _segment = s;
            _mesh.Clear();
            if (_edges != null) _edges.Clear();

            if (s == null || s.A == null || s.B == null || s.Length < MinSize)
            {
                if (_collider != null) _collider.sharedMesh = null;
                return;
            }

            var f = WallMesh.BuildFootprint(s);
            float height = Mathf.Max(Mathf.Abs(s.Height), MinSize);
            Vector3 baseUp = Vector3.up * s.BaseHeight;

            // Map the footprint onto the Inner/Outer pair the extruder expects. Which physical
            // side is "outer" depends on SideSign, and mirroring the footprint flips triangle
            // orientation — so the same sign drives `flip`, keeping every face pointing outward
            // (coding rule 1.1).
            bool towardPlus = s.SideSign >= 0f;
            var sections = new List<Cross>(2)
            {
                towardPlus
                    ? new Cross { Inner = f.ALeft + baseUp, Outer = f.ARight + baseUp }
                    : new Cross { Inner = f.ARight + baseUp, Outer = f.ALeft + baseUp },
                towardPlus
                    ? new Cross { Inner = f.BLeft + baseUp, Outer = f.BRight + baseUp }
                    : new Cross { Inner = f.BRight + baseUp, Outer = f.BLeft + baseUp },
            };

            // keep Points meaningful for callers that inspect the centerline
            _pts.Clear();
            _pts.Add(s.A.Position);
            _pts.Add(s.B.Position);
            _thickness = Mathf.Abs(s.Thickness);
            _height = height;
            _mode = s.Offset;
            _join = s.Join;

            if (s.Openings.Count > 0)
                TriangulateWithOpenings(sections[0], sections[1], height, !towardPlus, s.Openings, s.Length);
            else
                Triangulate(sections, height, flip: !towardPlus);
        }

        /// <summary>
        /// Panelisation v0 (docs/design/18 I8, precursor of docs/design/03): a straight
        /// graph segment with door/window openings. The wall surface is tiled with quads
        /// around each opening (piers, under-sill, header), plus jambs/sill/header faces
        /// into the cut and a transparent glass pane (submesh 1) for windows.
        /// Openings are placed by fraction of the CENTERLINE; on strongly mitred ends the
        /// footprint edge is slightly longer, so a jamb hugging a sharp corner can drift
        /// by up to half a thickness — acceptable for v0.
        /// </summary>
        private void TriangulateWithOpenings(Cross a, Cross b, float height, bool flip,
            List<WallOpening> openings, float centerlineLength)
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            var glass = new List<int>();
            var joinery = new List<int>();   // frames + door leaves (submesh 2)
            var ev = new List<Vector3>();
            var ei = new List<int>();
            float baseY = a.Inner.y;
            float thickness = Mathf.Max((a.Outer - a.Inner).magnitude, MinSize);

            // d = fraction across the thickness: 0 = inner surface, 1 = outer
            Vector3 P(float t, float y, float d) => Vector3.Lerp(
                Vector3.Lerp(a.Inner, b.Inner, t), Vector3.Lerp(a.Outer, b.Outer, t), d) + Vector3.up * y;
            Vector3 I(float t, float y) => P(t, y, 0f);
            Vector3 O(float t, float y) => P(t, y, 1f);
            Vector3 M(float t, float y) => P(t, y, 0.5f);

            var colors = new List<Color>();

            int Vert(Vector3 p, float u, float vv)
            {
                v.Add(p);
                uv.Add(new Vector2(u, vv));
                // vertex AO: skirting shadow by height above the wall base (design/04)
                float ao = MeshShading.HeightAO(p.y - baseY);
                colors.Add(new Color(ao, ao, ao, 1f));
                return v.Count - 1;
            }

            // NOTE the inverted default order: the helpers below enumerate corners
            // "up first, then along", which is the REVERSE of the outward orientation the
            // legacy extruder uses — so the un-flipped case reverses (verified against
            // Triangulate()'s quads; WallOpeningPlayTests pin the result with raycasts).
            void Face(List<int> target, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                float u0, float v0, float u1, float v1)
            {
                int i0 = Vert(p0, u0, v0), i1 = Vert(p1, u0, v1), i2 = Vert(p2, u1, v1), i3 = Vert(p3, u1, v0);
                if (flip) Quad(target, i0, i1, i2, i3);
                else Quad(target, i0, i3, i2, i1);
            }

            float U(float t) => t * centerlineLength / TileMeters;
            float VV(float y) => (baseY + y) / TileMeters;
            // metric UV span ACROSS the thickness — caps/tops need a real second axis or
            // the texture smears into stripes (headset feedback 2026-08-11)
            float wu = thickness / TileMeters;

            // faces oriented as in Triangulate(): inner toward the room, outer away, etc.
            void FaceInner(float t0, float t1, float y0, float y1) =>
                Face(tris, I(t0, y0), I(t0, y1), I(t1, y1), I(t1, y0), U(t0), VV(y0), U(t1), VV(y1));
            void FaceOuter(float t0, float t1, float y0, float y1) =>
                Face(tris, O(t0, y1), O(t0, y0), O(t1, y0), O(t1, y1), U(t0), VV(y1), U(t1), VV(y0));
            void FaceUp(float t0, float t1, float y) =>
                Face(tris, I(t0, y), O(t0, y), O(t1, y), I(t1, y), U(t0), 0f, U(t1), wu);
            void FaceDown(float t0, float t1, float y) =>
                Face(tris, O(t0, y), I(t0, y), I(t1, y), O(t1, y), U(t0), 0f, U(t1), wu);
            void FaceCapBack(float t, float y0, float y1) =>   // faces -t (wall start / right jamb)
                Face(tris, O(t, y0), O(t, y1), I(t, y1), I(t, y0), U(t), VV(y0), U(t) + wu, VV(y1));
            void FaceCapForward(float t, float y0, float y1) => // faces +t (wall end / left jamb)
                Face(tris, I(t, y0), I(t, y1), O(t, y1), O(t, y0), U(t), VV(y0), U(t) + wu, VV(y1));

            // A free-standing joinery block (frame bar / door leaf): all six faces, each
            // reusing the orientation of the corresponding verified wall-face pattern.
            void Box(float t0, float t1, float y0, float y1, float d0, float d1)
            {
                float dw = Mathf.Abs(d1 - d0) * wu;   // metric depth of the block
                Face(joinery, P(t0, y0, d0), P(t0, y1, d0), P(t1, y1, d0), P(t1, y0, d0), U(t0), VV(y0), U(t1), VV(y1));
                Face(joinery, P(t0, y1, d1), P(t0, y0, d1), P(t1, y0, d1), P(t1, y1, d1), U(t0), VV(y1), U(t1), VV(y0));
                Face(joinery, P(t0, y1, d0), P(t0, y1, d1), P(t1, y1, d1), P(t1, y1, d0), U(t0), 0f, U(t1), dw);
                Face(joinery, P(t0, y0, d1), P(t0, y0, d0), P(t1, y0, d0), P(t1, y0, d1), U(t0), 0f, U(t1), dw);
                Face(joinery, P(t0, y0, d1), P(t0, y1, d1), P(t0, y1, d0), P(t0, y0, d0), U(t0), VV(y0), U(t0) + dw, VV(y1));
                Face(joinery, P(t1, y0, d0), P(t1, y1, d0), P(t1, y1, d1), P(t1, y0, d1), U(t1), VV(y0), U(t1) + dw, VV(y1));
            }

            // Box() with WORLD axes: x along `ax`, y along `ay`, z along `az` (full-length
            // vectors, corner at `o`). Same face pattern, so the winding stays verified as
            // long as (ax, ay, az) matches the handedness of the wall's (t, y, d) frame.
            void BoxWorld(Vector3 o, Vector3 ax, Vector3 ay, Vector3 az)
            {
                Vector3 Q(float x, float y, float z) => o + ax * x + ay * y + az * z;
                Face(joinery, Q(0, 0, 0), Q(0, 1, 0), Q(1, 1, 0), Q(1, 0, 0), 0f, 0f, 1f, 1f);
                Face(joinery, Q(0, 1, 1), Q(0, 0, 1), Q(1, 0, 1), Q(1, 1, 1), 0f, 1f, 1f, 0f);
                Face(joinery, Q(0, 1, 0), Q(0, 1, 1), Q(1, 1, 1), Q(1, 1, 0), 0f, 1f, 1f, 1f);
                Face(joinery, Q(0, 0, 1), Q(0, 0, 0), Q(1, 0, 0), Q(1, 0, 1), 0f, 0f, 1f, 0f);
                Face(joinery, Q(0, 0, 1), Q(0, 1, 1), Q(0, 1, 0), Q(0, 0, 0), 0f, 0f, 0f, 1f);
                Face(joinery, Q(1, 0, 0), Q(1, 1, 0), Q(1, 1, 1), Q(1, 0, 1), 1f, 0f, 1f, 1f);
            }

            // A door leaf swung open around its hinge jamb (imported swing data). Returns
            // false when the swing is unknown or degenerate — caller emits the closed leaf.
            bool OpenLeaf(float t0, float t1, float ys, float yh, WallOpening src)
            {
                if (src == null) return false;
                if (src.SwingDir.sqrMagnitude < 1e-6f || src.HingeDir.sqrMagnitude < 1e-6f) return false;

                var alongW = P(1f, 0f, 0.5f) - P(0f, 0f, 0.5f); alongW.y = 0f;
                var depthW = P(0.5f, 0f, 1f) - P(0.5f, 0f, 0f); depthW.y = 0f;
                if (alongW.sqrMagnitude < 1e-8f || depthW.sqrMagnitude < 1e-8f) return false;
                alongW.Normalize(); depthW.Normalize();

                bool hingeAtEnd = Vector3.Dot(src.HingeDir, alongW) < 0f;
                var pHinge = P(hingeAtEnd ? t1 : t0, ys, 0.5f);
                var pFree = P(hingeAtEnd ? t0 : t1, ys, 0.5f);
                var run = pFree - pHinge; run.y = 0f;
                float w = run.magnitude;
                float leafH = yh - ys;
                if (w < 1e-3f || leafH < 1e-3f) return false;
                run /= w;

                var side = Vector3.Dot(src.SwingDir, depthW) >= 0f ? depthW : -depthW;
                float a = DoorOpenAngleDeg * Mathf.Deg2Rad;
                var leafDir = run * Mathf.Cos(a) + side * Mathf.Sin(a);
                // keep the box frame's handedness equal to the wall frame's, or every
                // face of the leaf would point inward
                float hand = Vector3.Dot(Vector3.Cross(alongW, Vector3.up), depthW) >= 0f ? 1f : -1f;
                var leafN = Vector3.Cross(leafDir, Vector3.up).normalized * hand;
                float leafT = Mathf.Min(0.04f, thickness * 0.4f);

                BoxWorld(pHinge - leafN * (leafT * 0.5f),
                    leafDir * w, Vector3.up * leafH, leafN * leafT);
                return true;
            }

            void EdgeRect(System.Func<float, float, Vector3> at, float t0, float t1, float y0, float y1)
            {
                int e0 = ev.Count;
                ev.Add(at(t0, y0)); ev.Add(at(t1, y0)); ev.Add(at(t1, y1)); ev.Add(at(t0, y1));
                ei.Add(e0); ei.Add(e0 + 1); ei.Add(e0 + 1); ei.Add(e0 + 2);
                ei.Add(e0 + 2); ei.Add(e0 + 3); ei.Add(e0 + 3); ei.Add(e0);
            }

            // ---- openings, sanitised: sorted, clamped, non-overlapping ----
            var ops = new List<(float t0, float t1, float ys, float yh, bool hasGlass, WallOpening src)>();
            foreach (var op in openings)
            {
                float halfT = Mathf.Max(0.01f, op.Width) / Mathf.Max(centerlineLength, MinSize) * 0.5f;
                float t0 = Mathf.Clamp01(op.AlongFraction - halfT);
                float t1 = Mathf.Clamp01(op.AlongFraction + halfT);
                float ys = Mathf.Clamp(op.SillHeight, 0f, height - 0.02f);
                float yh = Mathf.Clamp(op.SillHeight + op.Height, ys + 0.02f, height);
                if (t1 - t0 < 1e-3f) continue;
                ops.Add((t0, t1, ys, yh, !op.IsDoor, op));
            }
            ops.Sort((x, y) => x.t0.CompareTo(y.t0));
            for (int i = ops.Count - 1; i > 0; i--)
                if (ops[i].t0 < ops[i - 1].t1) ops.RemoveAt(i);   // overlap — keep the first

            // ---- walk the strips ----
            float cursor = 0f;
            foreach (var op in ops)
            {
                if (op.t0 > cursor)   // pier before the opening
                {
                    FaceInner(cursor, op.t0, 0f, height);
                    FaceOuter(cursor, op.t0, 0f, height);
                    FaceUp(cursor, op.t0, height);
                    FaceDown(cursor, op.t0, 0f);
                }

                if (op.ys > 0.01f)    // under-sill band + its exposed top
                {
                    FaceInner(op.t0, op.t1, 0f, op.ys);
                    FaceOuter(op.t0, op.t1, 0f, op.ys);
                    FaceDown(op.t0, op.t1, 0f);
                    FaceUp(op.t0, op.t1, op.ys);
                }
                if (op.yh < height - 0.01f)   // header band + its exposed bottom
                {
                    FaceInner(op.t0, op.t1, op.yh, height);
                    FaceOuter(op.t0, op.t1, op.yh, height);
                    FaceUp(op.t0, op.t1, height);
                    FaceDown(op.t0, op.t1, op.yh);
                }
                else
                {
                    FaceUp(op.t0, op.t1, height);   // opening reaches the top: keep the wall crown
                }

                FaceCapForward(op.t0, op.ys, op.yh);   // left jamb faces into the opening
                FaceCapBack(op.t1, op.ys, op.yh);      // right jamb

                EdgeRect(I, op.t0, op.t1, op.ys, op.yh);
                EdgeRect(O, op.t0, op.t1, op.ys, op.yh);

                if (op.hasGlass)
                {
                    // window: a mid-plane pane, emitted both ways so it reads from both sides
                    Face(glass, M(op.t0, op.ys), M(op.t0, op.yh), M(op.t1, op.yh), M(op.t1, op.ys),
                        U(op.t0), VV(op.ys), U(op.t1), VV(op.yh));
                    Face(glass, M(op.t1, op.ys), M(op.t1, op.yh), M(op.t0, op.yh), M(op.t0, op.ys),
                        U(op.t1), VV(op.ys), U(op.t0), VV(op.yh));
                }

                // ---- joinery (design/18 I12): a frame hugging the reveal; doors get a leaf
                float frameT = Mathf.Min(0.06f / Mathf.Max(centerlineLength, MinSize),
                                         (op.t1 - op.t0) * 0.25f);        // 60 mm legs
                float frameY = Mathf.Min(0.06f, (op.yh - op.ys) * 0.25f); // 60 mm head/sill bars
                float frameHalf = Mathf.Min(0.05f, thickness * 0.45f) / thickness;
                float fd0 = 0.5f - frameHalf, fd1 = 0.5f + frameHalf;

                Box(op.t0, op.t0 + frameT, op.ys, op.yh, fd0, fd1);           // left leg
                Box(op.t1 - frameT, op.t1, op.ys, op.yh, fd0, fd1);           // right leg
                Box(op.t0 + frameT, op.t1 - frameT, op.yh - frameY, op.yh, fd0, fd1); // head
                if (op.hasGlass)
                {
                    Box(op.t0 + frameT, op.t1 - frameT, op.ys, op.ys + frameY, fd0, fd1); // sill board
                }
                else if (op.src != null && op.src.Kind == OpeningKind.Garage)
                {
                    // Sectional garage leaf (audit F1): four stacked panels of alternating
                    // thickness — the steps read as the section seams without through-gaps.
                    // Always closed, no swing; opening animation is v2 (design/03).
                    float thick = Mathf.Min(0.02f, thickness * 0.2f) / thickness;
                    float thin = thick * 0.55f;
                    float ys0 = op.ys, yh0 = op.yh - frameY;
                    const int Panels = 4;
                    float panelH = (yh0 - ys0) / Panels;
                    if (panelH > 0.02f)
                        for (int p = 0; p < Panels; p++)
                        {
                            float half = (p & 1) == 0 ? thick : thin;
                            Box(op.t0 + frameT, op.t1 - frameT,
                                ys0 + p * panelH, ys0 + (p + 1) * panelH,
                                0.5f - half, 0.5f + half);
                        }
                    else
                        Box(op.t0 + frameT, op.t1 - frameT, ys0, yh0, 0.5f - thick, 0.5f + thick);
                }
                else if (!OpenLeaf(op.t0 + frameT, op.t1 - frameT, op.ys, op.yh - frameY, op.src))
                {
                    // door leaf: fills the frame, thinner than the wall; closed when the
                    // swing side is unknown (hand-drawn walls) — opening it is Phase D
                    float leafHalf = Mathf.Min(0.02f, thickness * 0.2f) / thickness;
                    Box(op.t0 + frameT, op.t1 - frameT, op.ys, op.yh - frameY,
                        0.5f - leafHalf, 0.5f + leafHalf);
                }

                cursor = op.t1;
            }
            if (cursor < 1f)          // final pier (or the whole wall if all openings clipped)
            {
                FaceInner(cursor, 1f, 0f, height);
                FaceOuter(cursor, 1f, 0f, height);
                FaceUp(cursor, 1f, height);
                FaceDown(cursor, 1f, 0f);
            }

            FaceCapBack(0f, 0f, height);      // wall end caps
            FaceCapForward(1f, 0f, height);

            // outline edges of the whole wall (verticals + top/bottom runs)
            EdgeRect(I, 0f, 1f, 0f, height);
            EdgeRect(O, 0f, 1f, 0f, height);

            _mesh.subMeshCount = 3;
            _mesh.SetVertices(v);
            _mesh.SetUVs(0, uv);
            _mesh.SetColors(colors);
            _mesh.SetTriangles(tris, 0);
            _mesh.SetTriangles(glass, 1);
            _mesh.SetTriangles(joinery, 2);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            if (_collider != null && !DeferCollider)
            {
                _collider.sharedMesh = null;
                _collider.sharedMesh = _mesh;
            }

            if (_edges != null)
            {
                _edges.SetVertices(ev);
                _edges.SetIndices(ei, MeshTopology.Lines, 0);
                _edges.RecalculateBounds();
            }
        }

        /// <summary>Collapse consecutive (near-)identical points — double-clicks in MR are common
        /// and would otherwise inject a degenerate normal into the footprint.</summary>
        private static void RemoveDuplicatePoints(List<Vector3> pts)
        {
            for (int i = pts.Count - 1; i > 0; i--)
                if ((pts[i] - pts[i - 1]).sqrMagnitude < DupEpsSqr) pts.RemoveAt(i);
        }

        /// <summary>Translate the whole wall (centerline + interior reference) and rebuild in place.</summary>
        public void MoveBy(Vector3 delta)
        {
            if (_segment != null)
            {
                // Graph-backed: move the NODES. Anything else attached to them shares those
                // nodes and therefore follows — which is the point of the graph. Rebuilding
                // those neighbours is the renderer's job (it knows all the views).
                _segment.A.Position += delta;
                if (_segment.B != _segment.A) _segment.B.Position += delta;
                GeometryChanged?.Invoke(_segment);
                Rebuild();
                return;
            }
            for (int i = 0; i < _pts.Count; i++) _pts[i] += delta;
            _interior += delta;
            Rebuild();
        }

        /// <summary>
        /// Raised after this view mutates shared graph state, so the owner can rebuild the
        /// other walls that hang off the same nodes.
        /// </summary>
        public System.Action<WallSegment> GeometryChanged;

        // ---- footprint (cross-sections along the centerline) ----

        private struct Cross { public Vector3 Inner; public Vector3 Outer; }

        // +1 if the outer side (away from us) is +rightNormal of the first segment, else -1.
        private static float OutwardSign(List<Vector3> pts, Vector3 interior)
        {
            Vector3 rn = RightNormal(pts[0], pts[1]);
            Vector3 mid = (pts[0] + pts[1]) * 0.5f;
            return Vector3.Dot(rn, mid - interior) >= 0f ? 1f : -1f;
        }

        private static Vector3 RightNormal(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a; d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) d = Vector3.forward;
            d.Normalize();
            return new Vector3(d.z, 0f, -d.x); // Cross(up, dir)
        }

        private static List<Cross> BuildFootprint(List<Vector3> pts, float dOut, float dIn, float oSign, WallJoin join)
        {
            int n = pts.Count;
            var rn = new Vector3[n - 1];
            for (int i = 0; i < n - 1; i++) rn[i] = RightNormal(pts[i], pts[i + 1]);

            var list = new List<Cross>();

            // start cap
            list.Add(new Cross
            {
                Inner = pts[0] + rn[0] * (-oSign * dIn),
                Outer = pts[0] + rn[0] * (oSign * dOut)
            });

            // interior vertices
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 n0 = rn[i - 1], n1 = rn[i];
                Vector3 dir0 = (pts[i] - pts[i - 1]); dir0.y = 0f; dir0.Normalize();
                Vector3 dir1 = (pts[i + 1] - pts[i]); dir1.y = 0f; dir1.Normalize();
                float turn = dir0.x * dir1.z - dir0.z * dir1.x;
                float denom = 1f + Vector3.Dot(n0, n1);

                bool straight = Mathf.Abs(turn) < 1e-4f;
                Vector3 sum = n0 + n1;
                Vector3 mvec;
                if (denom > 1e-3f) mvec = sum / denom;
                // ~180° reversal: no miter direction exists; fall back to the incoming normal.
                else mvec = sum.sqrMagnitude > 1e-6f ? sum.normalized : n0;
                // Miter limit: |mvec| = 1/cos(θ/2) is unbounded near a reversal — cap the spike.
                if (mvec.sqrMagnitude > MiterLimit * MiterLimit) mvec = mvec.normalized * MiterLimit;
                Vector3 miterOuter = pts[i] + mvec * (oSign * dOut);
                Vector3 miterInner = pts[i] + mvec * (-oSign * dIn);

                if (straight || join == WallJoin.Miter || denom <= 1e-3f)
                {
                    list.Add(new Cross { Inner = miterInner, Outer = miterOuter });
                    continue;
                }

                // Bevel / Round: convex contour gets the join, concave uses the miter point.
                bool outerConvex = oSign * turn > 0f;
                if (outerConvex)
                {
                    Vector3 a = pts[i] + n0 * (oSign * dOut);
                    Vector3 b = pts[i] + n1 * (oSign * dOut);
                    var outerPts = JoinPoints(pts[i], a, b, join);
                    foreach (var op in outerPts) list.Add(new Cross { Inner = miterInner, Outer = op });
                }
                else
                {
                    Vector3 a = pts[i] + n0 * (-oSign * dIn);
                    Vector3 b = pts[i] + n1 * (-oSign * dIn);
                    var innerPts = JoinPoints(pts[i], a, b, join);
                    foreach (var ip in innerPts) list.Add(new Cross { Inner = ip, Outer = miterOuter });
                }
            }

            // end cap
            list.Add(new Cross
            {
                Inner = pts[n - 1] + rn[n - 2] * (-oSign * dIn),
                Outer = pts[n - 1] + rn[n - 2] * (oSign * dOut)
            });

            return list;
        }

        // Points across a convex corner from a to b around center p.
        private static List<Vector3> JoinPoints(Vector3 p, Vector3 a, Vector3 b, WallJoin join)
        {
            var res = new List<Vector3>();
            if (join == WallJoin.Bevel)
            {
                res.Add(a);
                res.Add(b);
                return res;
            }
            // Round: arc from a to b around p
            Vector3 va = a - p, vb = b - p;
            float r = va.magnitude;
            if (r < 1e-5f) { res.Add(a); return res; }
            for (int j = 0; j <= RoundSegments; j++)
            {
                float t = (float)j / RoundSegments;
                Vector3 dir = Vector3.Slerp(va.normalized, vb.normalized, t);
                res.Add(p + dir * r);
            }
            return res;
        }

        // ---- extrude cross-sections into a mesh (+edges) ----

        /// <summary>
        /// Metres per texture tile. Metric UVs (docs/design/04-surfaces-materials.md): U runs
        /// ALONG the wall, V goes up, both in metres — so the same tile size looks identical on
        /// a 1 m and a 10 m wall, with no stretching and no seams to hand-tune.
        /// </summary>
        public const float TileMeters = 1f;

        private void Triangulate(List<Cross> s, float height, bool flip)
        {
            int m = s.Count;
            var v = new List<Vector3>(m * 4);
            var uv = new List<Vector2>(m * 4);
            var colors = new List<Color>(m * 4);
            var tris = new List<int>();
            var ev = new List<Vector3>(m * 4);
            var ei = new List<int>();
            Vector3 up = Vector3.up * height;
            // vertex AO: skirting shadow — darker at the floor contact line (design/04)
            float aoBottom = MeshShading.HeightAO(0f), aoTop = MeshShading.HeightAO(height);

            // Reverses winding when the footprint is mirrored (oSign < 0) so faces stay outward.
            void AddQuad(int a, int b, int c, int d)
            {
                if (flip) Quad(tris, a, d, c, b);
                else Quad(tris, a, b, c, d);
            }

            float run = 0f;                 // distance travelled along the wall, in metres
            Vector3 prevMid = Vector3.zero;
            var us = new float[m];          // per-section U, reused by the dedicated faces below

            for (int j = 0; j < m; j++)
            {
                Vector3 mid = (s[j].Inner + s[j].Outer) * 0.5f;
                if (j > 0) run += Vector3.Distance(new Vector3(mid.x, 0f, mid.z),
                                                   new Vector3(prevMid.x, 0f, prevMid.z));
                prevMid = mid;

                float u = run / TileMeters;
                us[j] = u;
                float vBottom = s[j].Inner.y / TileMeters;
                float vTop = (s[j].Inner.y + height) / TileMeters;

                v.Add(s[j].Inner); v.Add(s[j].Outer); v.Add(s[j].Inner + up); v.Add(s[j].Outer + up);
                uv.Add(new Vector2(u, vBottom)); uv.Add(new Vector2(u, vBottom));
                uv.Add(new Vector2(u, vTop)); uv.Add(new Vector2(u, vTop));
                colors.Add(new Color(aoBottom, aoBottom, aoBottom, 1f));
                colors.Add(new Color(aoBottom, aoBottom, aoBottom, 1f));
                colors.Add(new Color(aoTop, aoTop, aoTop, 1f));
                colors.Add(new Color(aoTop, aoTop, aoTop, 1f));
                ev.Add(s[j].Inner); ev.Add(s[j].Outer); ev.Add(s[j].Inner + up); ev.Add(s[j].Outer + up);
                // verticals
                ei.Add(j * 4 + 0); ei.Add(j * 4 + 2);
                ei.Add(j * 4 + 1); ei.Add(j * 4 + 3);
            }

            for (int j = 0; j < m - 1; j++)
            {
                int a = j * 4, b = (j + 1) * 4;
                AddQuad(a + 1, a + 3, b + 3, b + 1);   // outer wall
                AddQuad(a + 0, b + 0, b + 2, a + 2);   // inner wall

                // edge lines along the run
                ei.Add(a + 1); ei.Add(b + 1); // outer base
                ei.Add(a + 3); ei.Add(b + 3); // outer top
                ei.Add(a + 0); ei.Add(b + 0); // inner base
                ei.Add(a + 2); ei.Add(b + 2); // inner top
            }

            // Top, bottom and caps get DEDICATED vertices: the ring vertices carry the
            // along-the-wall UVs, and reusing them smeared the texture into stripes across
            // the thickness (headset feedback 2026-08-11). U runs along, V spans the
            // thickness in metres; positions are identical, so winding and collider hold.
            int flatBase = v.Count;
            for (int j = 0; j < m; j++)
            {
                float w = Vector3.Distance(s[j].Inner, s[j].Outer) / TileMeters;
                v.Add(s[j].Inner + up); v.Add(s[j].Outer + up);   // +0 topInner, +1 topOuter
                uv.Add(new Vector2(us[j], 0f)); uv.Add(new Vector2(us[j], w));
                colors.Add(new Color(aoTop, aoTop, aoTop, 1f));
                colors.Add(new Color(aoTop, aoTop, aoTop, 1f));
                v.Add(s[j].Inner); v.Add(s[j].Outer);             // +2 botInner, +3 botOuter
                uv.Add(new Vector2(us[j], 0f)); uv.Add(new Vector2(us[j], w));
                colors.Add(new Color(aoBottom, aoBottom, aoBottom, 1f));
                colors.Add(new Color(aoBottom, aoBottom, aoBottom, 1f));
            }
            for (int j = 0; j < m - 1; j++)
            {
                int a = flatBase + j * 4, b = flatBase + (j + 1) * 4;
                AddQuad(a + 2, a + 3, b + 3, b + 2);   // bottom (same corner order as before)
                AddQuad(a + 0, b + 0, b + 1, a + 1);   // top
            }

            // end caps: U spans the thickness, V climbs the height
            int capBase = v.Count;
            void CapVert(Vector3 p, float capU, float ao)
            {
                v.Add(p);
                uv.Add(new Vector2(capU, p.y / TileMeters));
                colors.Add(new Color(ao, ao, ao, 1f));
            }
            float w0 = Vector3.Distance(s[0].Inner, s[0].Outer) / TileMeters;
            CapVert(s[0].Inner, 0f, aoBottom); CapVert(s[0].Inner + up, 0f, aoTop);
            CapVert(s[0].Outer + up, w0, aoTop); CapVert(s[0].Outer, w0, aoBottom);
            AddQuad(capBase + 0, capBase + 1, capBase + 2, capBase + 3);
            var sl = s[m - 1];
            float wL = Vector3.Distance(sl.Inner, sl.Outer) / TileMeters;
            CapVert(sl.Outer, 0f, aoBottom); CapVert(sl.Outer + up, 0f, aoTop);
            CapVert(sl.Inner + up, wL, aoTop); CapVert(sl.Inner, wL, aoBottom);
            AddQuad(capBase + 4, capBase + 5, capBase + 6, capBase + 7);

            int e = (m - 1) * 4;
            ei.Add(0); ei.Add(1); ei.Add(2); ei.Add(3);
            ei.Add(e + 0); ei.Add(e + 1); ei.Add(e + 2); ei.Add(e + 3);

            // Submeshes 1/2 are window glass and joinery — empty here, but always present
            // so a renderer configured with [wall, glass, joinery] is valid for every wall.
            _mesh.subMeshCount = 3;
            _mesh.SetVertices(v);
            _mesh.SetUVs(0, uv);
            _mesh.SetColors(colors);
            _mesh.SetTriangles(tris, 0);
            _mesh.SetTriangles(new List<int>(), 1);
            _mesh.SetTriangles(new List<int>(), 2);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            if (_collider != null && !DeferCollider)
            {
                // reassign to force the physics mesh to refresh
                _collider.sharedMesh = null;
                _collider.sharedMesh = _mesh;
            }

            if (_edges != null)
            {
                _edges.SetVertices(ev);
                _edges.SetIndices(ei, MeshTopology.Lines, 0);
                _edges.RecalculateBounds();
            }
        }

        private static void Quad(List<int> t, int a, int b, int c, int d)
        {
            t.Add(a); t.Add(b); t.Add(c);
            t.Add(a); t.Add(c); t.Add(d);
        }

        private void OnDestroy()
        {
            // Meshes created via `new Mesh` are not freed by Destroy(gameObject).
            if (Application.isPlaying)
            {
                if (_mesh != null) Destroy(_mesh);
                if (_edges != null) Destroy(_edges);
            }
            else
            {
                if (_mesh != null) DestroyImmediate(_mesh);
                if (_edges != null) DestroyImmediate(_edges);
            }
        }

        /// <summary>
        /// Pure geometry for one straight segment box (8 verts: 0-3 bottom, 4-7 top).
        /// Kept for unit tests / simple cases.
        /// </summary>
        public static Vector3[] SegmentVertices(Vector3 a, Vector3 b, float thick, float h, WallOffsetMode mode, Vector3 interior)
        {
            Vector3 dir = b - a; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            dir.Normalize();

            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 mid = (a + b) * 0.5f;
            if (Vector3.Dot(side, mid - interior) < 0f) side = -side;

            Vector3 inner, outer;
            switch (mode)
            {
                case WallOffsetMode.Center: inner = -side * (thick * 0.5f); outer = side * (thick * 0.5f); break;
                case WallOffsetMode.Inner:  inner = -side * thick;          outer = Vector3.zero;          break;
                default:                    inner = Vector3.zero;           outer = side * thick;          break;
            }

            Vector3 up = Vector3.up * h;
            Vector3 a0 = a + inner, a1 = a + outer, b0 = b + inner, b1 = b + outer;
            return new[] { a0, a1, b1, b0, a0 + up, a1 + up, b1 + up, b0 + up };
        }
    }
}
