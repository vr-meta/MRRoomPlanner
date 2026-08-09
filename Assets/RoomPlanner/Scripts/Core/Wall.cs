using System.Collections.Generic;
using UnityEngine;

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
    /// beveled/rounded corners — one clean strip, no overlapping boxes. Two-sided Unlit
    /// material (cull off), so winding is irrelevant.
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
            _pts.Clear();
            _pts.AddRange(centerline);
            _thickness = thickness; _height = height; _mode = mode; _join = join; _interior = interior;
            _mesh.Clear();
            if (_edges != null) _edges.Clear();
            if (_pts.Count < 2) { if (_collider != null) _collider.sharedMesh = null; return; }

            float dOut, dIn;
            switch (mode)
            {
                case WallOffsetMode.Center: dOut = thickness * 0.5f; dIn = thickness * 0.5f; break;
                case WallOffsetMode.Inner:  dOut = 0f;               dIn = thickness;         break;
                default:                    dOut = thickness;        dIn = 0f;                break; // Outer
            }

            List<Cross> sections = BuildFootprint(_pts, dOut, dIn, OutwardSign(_pts, interior), join);
            Triangulate(sections, height);
        }

        /// <summary>Rebuild the mesh from the current centerline and cached parameters.</summary>
        public void Rebuild()
        {
            var pts = new List<Vector3>(_pts);
            Build(pts, _thickness, _height, _mode, _join, _interior);
        }

        /// <summary>Translate the whole wall (centerline + interior reference) and rebuild in place.</summary>
        public void MoveBy(Vector3 delta)
        {
            for (int i = 0; i < _pts.Count; i++) _pts[i] += delta;
            _interior += delta;
            Rebuild();
        }

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
                Vector3 mvec = denom > 1e-3f ? (n0 + n1) / denom : (n0 + n1).normalized;
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

        private void Triangulate(List<Cross> s, float height)
        {
            int m = s.Count;
            var v = new List<Vector3>(m * 4);
            var tris = new List<int>();
            var ev = new List<Vector3>(m * 4);
            var ei = new List<int>();
            Vector3 up = Vector3.up * height;

            for (int j = 0; j < m; j++)
            {
                v.Add(s[j].Inner); v.Add(s[j].Outer); v.Add(s[j].Inner + up); v.Add(s[j].Outer + up);
                ev.Add(s[j].Inner); ev.Add(s[j].Outer); ev.Add(s[j].Inner + up); ev.Add(s[j].Outer + up);
                // verticals
                ei.Add(j * 4 + 0); ei.Add(j * 4 + 2);
                ei.Add(j * 4 + 1); ei.Add(j * 4 + 3);
            }

            for (int j = 0; j < m - 1; j++)
            {
                int a = j * 4, b = (j + 1) * 4;
                Quad(tris, a + 0, a + 1, b + 1, b + 0);   // bottom
                Quad(tris, a + 2, b + 2, b + 3, a + 3);   // top
                Quad(tris, a + 1, a + 3, b + 3, b + 1);   // outer wall
                Quad(tris, a + 0, b + 0, b + 2, a + 2);   // inner wall

                // edge lines along the run
                ei.Add(a + 1); ei.Add(b + 1); // outer base
                ei.Add(a + 3); ei.Add(b + 3); // outer top
                ei.Add(a + 0); ei.Add(b + 0); // inner base
                ei.Add(a + 2); ei.Add(b + 2); // inner top
            }

            // end caps
            Quad(tris, 0, 2, 3, 1);
            int e = (m - 1) * 4;
            Quad(tris, e + 1, e + 3, e + 2, e + 0);
            ei.Add(0); ei.Add(1); ei.Add(2); ei.Add(3);
            ei.Add(e + 0); ei.Add(e + 1); ei.Add(e + 2); ei.Add(e + 3);

            _mesh.SetVertices(v);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            if (_collider != null)
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
