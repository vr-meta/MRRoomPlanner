using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Floors;

namespace RoomPlanner.Stairs
{
    /// <summary>Construction kinds (design/18 I12/I16): a solid monolithic wedge down to
    /// the ground, an open flight (treads on two stringers, nothing underneath), or a
    /// waist slab — the apartment-stairwell flight: steps on top of an inclined slab of
    /// constant thickness with a flat sloped soffit.</summary>
    public enum StairKind { Solid, Open, Waist }

    /// <summary>
    /// A parametric stair flight (docs/design/18-ifc-import.md I9/I12): base point + yaw +
    /// riser/tread numbers instead of a dead mesh, in the project's parameters→mesh
    /// spirit. Profiles are closed 2D rings extruded across the width; IfcStairFlight
    /// maps 1:1 onto the parameters. Landings between flights are ordinary Floor slabs.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class Stair : MonoBehaviour
    {
        private const float TreadThickness = 0.06f;
        private const float StringerDepth = 0.30f;   // measured vertically under the nose line
        private const float StringerWidth = 0.06f;
        private const float WaistDepth = 0.25f;      // slab depth under the nose line, vertical

        private MeshFilter _mf;
        private Mesh _mesh;
        private MeshCollider _collider;

        /// <summary>Bottom of the FIRST riser, on the run centerline (world).</summary>
        public Vector3 Base { get; private set; }
        /// <summary>Run direction (degrees around Y, 0 = +Z), ascending away from Base.</summary>
        public float YawDeg { get; private set; }
        public float Width { get; private set; }
        public int Risers { get; private set; }
        public float RiserHeight { get; private set; }
        public float TreadDepth { get; private set; }
        public StairKind Kind { get; private set; } = StairKind.Solid;

        public float TotalHeight => Risers * RiserHeight;
        /// <summary>Horizontal run length: the last riser tops out onto the landing.</summary>
        public float RunLength => Mathf.Max(0, Risers - 1) * TreadDepth;

        private void Ensure()
        {
            if (_mf == null) _mf = GetComponent<MeshFilter>();
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "StairMesh" };
                _mf.sharedMesh = _mesh;
            }
            if (_collider == null)
            {
                _collider = GetComponent<MeshCollider>();
                if (_collider == null) _collider = gameObject.AddComponent<MeshCollider>();
            }
        }

        public void Rebuild() => Build(Base, YawDeg, Width, Risers, RiserHeight, TreadDepth, Kind);

        public void MoveBy(Vector3 delta) =>
            Build(Base + delta, YawDeg, Width, Risers, RiserHeight, TreadDepth, Kind);

        public void SetKind(StairKind kind) =>
            Build(Base, YawDeg, Width, Risers, RiserHeight, TreadDepth, kind);

        public void Build(Vector3 basePoint, float yawDeg, float width,
            int risers, float riserHeight, float treadDepth, StairKind kind = StairKind.Solid)
        {
            Ensure();
            Base = basePoint;
            YawDeg = yawDeg;
            Width = Mathf.Max(0.2f, width);
            Risers = Mathf.Max(1, risers);
            RiserHeight = Mathf.Clamp(riserHeight, 0.05f, 0.5f);
            TreadDepth = Mathf.Clamp(treadDepth, 0.1f, 1f);
            Kind = kind;
            _mesh.Clear();

            var rot = Quaternion.Euler(0f, YawDeg, 0f);
            Vector3 run = rot * Vector3.forward;
            Vector3 side = rot * Vector3.right;

            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            var vc = new List<Color>();

            void FillColors()
            {
                // vertex AO: contact shadow near the flight's base level (design/04)
                while (vc.Count < v.Count)
                {
                    float ao = MeshShading.HeightAO(v[vc.Count].y - Base.y);
                    vc.Add(new Color(ao, ao, ao, 1f));
                }
            }

            // Extrude a closed 2D ring (run, up) between two side offsets. Rings are wound
            // "up the left edge first" (clockwise in (run,up)); with the LEFT rail leading
            // each perimeter quad this puts every face outward (verified by the riser/
            // tread cross-product walk in the original solid builder).
            void Prism(List<Vector2> ring, float s0, float s1)
            {
                Vector3 L = Base + side * s0;
                Vector3 R = Base + side * s1;
                Vector3 At(Vector3 rail, Vector2 p) => rail + run * p.x + Vector3.up * p.y;

                void Quad(Vector3 qa, Vector3 qb, Vector3 qc, Vector3 qd, float u0, float u1, float w0, float w1)
                {
                    int i0 = v.Count;
                    v.Add(qa); v.Add(qb); v.Add(qc); v.Add(qd);
                    uv.Add(new Vector2(u0, w0)); uv.Add(new Vector2(u1, w0));
                    uv.Add(new Vector2(u1, w1)); uv.Add(new Vector2(u0, w1));
                    t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
                    t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
                }

                float dist = 0f;
                for (int i = 0; i < ring.Count; i++)
                {
                    Vector2 p0 = ring[i], p1 = ring[(i + 1) % ring.Count];
                    float len = Vector2.Distance(p0, p1);
                    if (len < 1e-5f) continue;
                    Quad(At(L, p0), At(L, p1), At(R, p1), At(R, p0),
                        dist / Floor.TileMeters, (dist + len) / Floor.TileMeters,
                        0f, (s1 - s0) / Floor.TileMeters);
                    dist += len;
                }

                var flat = new List<Vector3>(ring.Count);
                foreach (var p in ring) flat.Add(new Vector3(p.x, 0f, p.y));   // Polygon works in XZ
                var clean = Polygon.ToCounterClockwise(Polygon.Clean(flat));
                var tris = Polygon.Triangulate(clean);
                void SideFace(Vector3 rail, bool reverse)
                {
                    int start = v.Count;
                    foreach (var p in clean)
                    {
                        v.Add(rail + run * p.x + Vector3.up * p.z);
                        uv.Add(new Vector2(p.x, p.z) / Floor.TileMeters);
                    }
                    for (int i = 0; i + 2 < tris.Count; i += 3)
                    {
                        if (reverse) { t.Add(start + tris[i + 2]); t.Add(start + tris[i + 1]); t.Add(start + tris[i]); }
                        else { t.Add(start + tris[i]); t.Add(start + tris[i + 1]); t.Add(start + tris[i + 2]); }
                    }
                }
                SideFace(L, reverse: false);
                SideFace(R, reverse: true);
            }

            float H = TotalHeight, Lrun = RunLength;
            float half = Width * 0.5f;

            List<Vector2> StepProfileTop()
            {
                var top = new List<Vector2> { new(0f, 0f) };
                for (int i = 0; i < Risers; i++)
                {
                    float x = i * TreadDepth;
                    top.Add(new Vector2(x, (i + 1) * RiserHeight));
                    if (i < Risers - 1)
                        top.Add(new Vector2(x + TreadDepth, (i + 1) * RiserHeight));
                }
                return top;
            }

            // The waist slab needs room for its soffit; degenerate flights fall back to solid.
            bool waistFits = H > WaistDepth * 1.5f && Lrun > 0.05f;

            if (Kind == StairKind.Solid || (Kind == StairKind.Waist && !waistFits))
            {
                var profile = StepProfileTop();
                profile.Add(new Vector2(Lrun, 0f));
                Prism(profile, -half, half);
            }
            else if (Kind == StairKind.Waist)
            {
                // Steps on an inclined slab of constant depth: the soffit runs parallel to
                // the nose line (y = H/Lrun · x − WaistDepth), meets the ground short of the
                // first riser, and the top end is cut vertically under the landing edge.
                float xGround = WaistDepth * Lrun / H;
                var profile = StepProfileTop();
                profile.Add(new Vector2(Lrun, H - WaistDepth));
                profile.Add(new Vector2(xGround, 0f));
                Prism(profile, -half, half);
            }
            else
            {
                // Open flight: two inclined stringers + floating treads between them.
                // Stringer: a slanted band under the nose line, cropped at the ground.
                float xCut = H > 1e-4f ? Mathf.Min(Lrun, StringerDepth * Lrun / H) : 0f;
                var stringer = new List<Vector2>
                {
                    new(0f, 0f), new(Lrun, H), new(Lrun, H - StringerDepth), new(xCut, 0f),
                };
                Prism(stringer, -half, -half + StringerWidth);
                Prism(stringer, half - StringerWidth, half);

                // A tread under every riser top except the last (that step IS the landing).
                for (int i = 0; i < Risers - 1; i++)
                {
                    float y1 = (i + 1) * RiserHeight;
                    float x0 = i * TreadDepth, x1 = x0 + TreadDepth;
                    var tread = new List<Vector2>
                    {
                        new(x0, y1 - TreadThickness), new(x0, y1), new(x1, y1), new(x1, y1 - TreadThickness),
                    };
                    Prism(tread, -half + StringerWidth, half - StringerWidth);
                }
            }

            FillColors();
            _mesh.SetVertices(v);
            _mesh.SetUVs(0, uv);
            _mesh.SetColors(vc);
            _mesh.SetTriangles(t, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            if (_collider != null)
            {
                _collider.sharedMesh = null;
                _collider.sharedMesh = _mesh;
            }
        }

        /// <summary>
        /// Ensure the slab above this flight is open wherever it violates
        /// StairMath.MinHeadroom (audit 05 §Б1 — imported stairwell holes that leave you
        /// head-butting the ceiling). Any existing holes the required opening overlaps are
        /// absorbed into one rectangle in the flight's frame; the rectangle is clamped to
        /// the slab's extent and cut once. Returns true when the slab was modified.
        /// </summary>
        public bool CutHeadroomIn(Floor slab)
        {
            if (slab == null || slab.Outline.Count < 3) return false;
            float bottomAboveBase = slab.Level - slab.Thickness - Base.y;
            // The slab the flight stands on (or anything below) is not "above the head".
            if (bottomAboveBase <= RiserHeight) return false;
            if (!StairMath.CutRange(Risers, RiserHeight, TreadDepth, bottomAboveBase,
                    out float d0, out float d1)) return false;

            var rot = Quaternion.Euler(0f, YawDeg, 0f);
            Vector3 run = rot * Vector3.forward;
            Vector3 side = rot * Vector3.right;
            float s0 = -(Width * 0.5f + StairMath.SideMargin);
            float s1 = Width * 0.5f + StairMath.SideMargin;
            List<Vector3> RectRing() => new()
            {
                Base + run * d0 + side * s0, Base + run * d1 + side * s0,
                Base + run * d1 + side * s1, Base + run * d0 + side * s1,
            };

            // Already open? Sample a coarse grid over the required opening: a point is a
            // problem only when it is solid slab (inside the outline, outside every hole).
            bool anyMaterial = false;
            const int N = 4;
            for (int i = 0; i <= N && !anyMaterial; i++)
                for (int j = 0; j <= N && !anyMaterial; j++)
                {
                    Vector3 p = Base
                        + run * Mathf.Lerp(d0 + 0.01f, d1 - 0.01f, i / (float)N)
                        + side * Mathf.Lerp(s0 + 0.01f, s1 - 0.01f, j / (float)N);
                    if (!Polygon.Contains(slab.Outline, p)) continue;
                    bool inHole = false;
                    foreach (var h in slab.Holes)
                        if (Polygon.Contains(h, p)) { inHole = true; break; }
                    if (!inHole) anyMaterial = true;
                }
            if (!anyMaterial) return false;      // already open, or the flight is not under this slab

            // Absorb every hole the opening overlaps (their extent joins the rectangle in
            // the flight's frame). Growing the rectangle can reach further holes → loop.
            var absorbed = new List<List<Vector3>>();
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int i = slab.Holes.Count - 1; i >= 0; i--)
                {
                    var h = slab.Holes[i];
                    if (!Polygon.RingsOverlap(RectRing(), h)) continue;
                    foreach (var pt in h)
                    {
                        float d = Vector3.Dot(pt - Base, run);
                        float s = Vector3.Dot(pt - Base, side);
                        d0 = Mathf.Min(d0, d); d1 = Mathf.Max(d1, d);
                        s0 = Mathf.Min(s0, s); s1 = Mathf.Max(s1, s);
                    }
                    absorbed.Add(new List<Vector3>(h));
                    slab.RemoveHole(i);
                    grew = true;
                }
            }

            // Clamp to the slab's own extent in the flight frame — AddHole refuses rings
            // poking past the edge. Non-convex slabs may still refuse; that is surfaced to
            // the caller instead of silently mangling anything.
            float dMin = float.MaxValue, dMax = float.MinValue;
            float sMin = float.MaxValue, sMax = float.MinValue;
            foreach (var pt in slab.Outline)
            {
                float d = Vector3.Dot(pt - Base, run);
                float s = Vector3.Dot(pt - Base, side);
                dMin = Mathf.Min(dMin, d); dMax = Mathf.Max(dMax, d);
                sMin = Mathf.Min(sMin, s); sMax = Mathf.Max(sMax, s);
            }
            const float inset = 0.02f;
            d0 = Mathf.Max(d0, dMin + inset); d1 = Mathf.Min(d1, dMax - inset);
            s0 = Mathf.Max(s0, sMin + inset); s1 = Mathf.Min(s1, sMax - inset);

            bool ok = d1 - d0 > 0.2f && s1 - s0 > 0.2f && slab.AddHole(RectRing());
            if (!ok)
            {
                foreach (var h in absorbed) slab.AddHole(h);   // put back what we took
                return false;
            }
            return true;
        }

        private void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
        }
    }
}
