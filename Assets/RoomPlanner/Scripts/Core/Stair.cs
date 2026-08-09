using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Floors;

namespace RoomPlanner.Stairs
{
    /// <summary>
    /// A parametric monolithic stair flight (docs/design/18-ifc-import.md I9): base point +
    /// yaw + riser/tread numbers instead of a dead mesh, in the project's parameters→mesh
    /// spirit. The side profile is the classic zigzag closed into a solid wedge, extruded
    /// across the width. IfcStairFlight maps 1:1 onto these parameters; landings between
    /// flights are ordinary Floor slabs.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class Stair : MonoBehaviour
    {
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

        public void Rebuild() => Build(Base, YawDeg, Width, Risers, RiserHeight, TreadDepth);

        public void MoveBy(Vector3 delta) =>
            Build(Base + delta, YawDeg, Width, Risers, RiserHeight, TreadDepth);

        public void Build(Vector3 basePoint, float yawDeg, float width,
            int risers, float riserHeight, float treadDepth)
        {
            Ensure();
            Base = basePoint;
            YawDeg = yawDeg;
            Width = Mathf.Max(0.2f, width);
            Risers = Mathf.Max(1, risers);
            RiserHeight = Mathf.Clamp(riserHeight, 0.05f, 0.5f);
            TreadDepth = Mathf.Clamp(treadDepth, 0.1f, 1f);
            _mesh.Clear();

            // Side profile in (run, up): up each riser, along each tread, then closed down
            // the back and along the base — a solid wedge, no floating steps.
            var profile = new List<Vector2> { new(0f, 0f) };
            for (int i = 0; i < Risers; i++)
            {
                float x = i * TreadDepth;
                profile.Add(new Vector2(x, (i + 1) * RiserHeight));
                if (i < Risers - 1)
                    profile.Add(new Vector2(x + TreadDepth, (i + 1) * RiserHeight));
            }
            profile.Add(new Vector2(RunLength, 0f));   // back edge, down to the base line

            var rot = Quaternion.Euler(0f, YawDeg, 0f);
            Vector3 run = rot * Vector3.forward;
            Vector3 side = rot * Vector3.right;
            Vector3 L = Base - side * (Width * 0.5f);   // left rail origin
            Vector3 R = Base + side * (Width * 0.5f);

            Vector3 At(Vector3 rail, Vector2 p) => rail + run * p.x + Vector3.up * p.y;

            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i0 = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
                t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
            }

            // ---- perimeter surfaces (risers, treads, back, underside), width-extruded.
            // Walking the profile counter-clockwise (as built) with the LEFT rail first makes
            // every quad face outward (coding rule 1.1).
            float dist = 0f;
            for (int i = 0; i + 1 < profile.Count; i++)
            {
                Vector2 p0 = profile[i], p1 = profile[i + 1];
                float len = Vector2.Distance(p0, p1);
                Quad(At(L, p0), At(L, p1), At(R, p1), At(R, p0),
                    new Vector2(dist, 0f) / Floor.TileMeters,
                    new Vector2(dist + len, 0f) / Floor.TileMeters,
                    new Vector2(dist + len, Width) / Floor.TileMeters,
                    new Vector2(dist, Width) / Floor.TileMeters);
                dist += len;
            }
            // close the base (profile end → start)
            Quad(At(L, profile[profile.Count - 1]), At(L, profile[0]),
                 At(R, profile[0]), At(R, profile[profile.Count - 1]),
                new Vector2(0f, 0f), new Vector2(RunLength, 0f) / Floor.TileMeters,
                new Vector2(RunLength, Width) / Floor.TileMeters, new Vector2(0f, Width) / Floor.TileMeters);

            // ---- side faces: triangulated profile, once per rail, mirrored winding.
            var flat = new List<Vector3>(profile.Count);
            foreach (var p in profile) flat.Add(new Vector3(p.x, 0f, p.y));   // Polygon works in XZ
            var ring = Polygon.ToCounterClockwise(Polygon.Clean(flat));
            var tris = Polygon.Triangulate(ring);
            void Side(Vector3 rail, bool reverse)
            {
                int start = v.Count;
                foreach (var p in ring)
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
            // A CCW ring in XZ triangulates facing DOWN (-Y): mapped onto the side plane that
            // is "toward -side" for the left rail — so left takes it as-is, right reversed.
            Side(L, reverse: false);
            Side(R, reverse: true);

            _mesh.SetVertices(v);
            _mesh.SetUVs(0, uv);
            _mesh.SetTriangles(t, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            if (_collider != null)
            {
                _collider.sharedMesh = null;
                _collider.sharedMesh = _mesh;
            }
        }

        private void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
        }
    }
}
