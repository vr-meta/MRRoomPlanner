using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Electrical;

namespace RoomPlanner.Plumbing
{
    /// <summary>
    /// A point element of the Plumbing layer (docs/design/28-plumbing.md): a wall
    /// stub-out for the toilet (D110) or the sink family (D50) — straight or with the
    /// classic 45-degree down elbow — or the floor drain. The mesh is built in LOCAL
    /// space (wall fixtures: back at z=0, +Z into the room; the drain: grate in the
    /// XZ plane, body sunk below) — placement is the transform, so dragging is a pure
    /// transform move with no re-cook (rule 4.2). Owns its Mesh (rule 1.5).
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class PlumbFixture : MonoBehaviour
    {
        private MeshFilter _mf;
        private Mesh _mesh;
        private MeshCollider _collider;

        private readonly List<Vector3> _verts = new();
        private readonly List<int> _tris = new();
        private readonly List<Vector2> _uvs = new();
        private readonly List<Vector3> _tubePts = new();
        private readonly List<Vector3> _tubeVerts = new();
        private readonly List<int> _tubeTris = new();
        private readonly List<Vector2> _tubeUvs = new();

        public PlumbFixtureKind Kind { get; private set; }
        public OutletAngle Angle { get; private set; } = OutletAngle.Deg90;

        /// <summary>Storey level (Y) this fixture was mounted against — heights stay
        /// storey-relative on upper floors (the electrical precedent).</summary>
        public float BaseLevel { get; set; }

        public float HeightAboveLevel => transform.position.y - BaseLevel;

        /// <summary>Pipe size the fixture connects with: toilet D110, everything else D50.</summary>
        public PipeDiameter Diameter =>
            Kind == PlumbFixtureKind.ToiletOutlet ? PipeDiameter.D110 : PipeDiameter.D50;

        /// <summary>Footprint for clearance checks, meters.</summary>
        public float BlockWidth => Kind switch
        {
            PlumbFixtureKind.ToiletOutlet => PipeSpec.Radius(PipeDiameter.D110) * 2f * PlumbingDefaults.SocketFlare,
            PlumbFixtureKind.SinkOutlet => PipeSpec.Radius(PipeDiameter.D50) * 2f * PlumbingDefaults.SocketFlare,
            _ => PlumbingDefaults.DrainSize,
        };

        /// <summary>Pipe entry in local space: the open socket of a stub-out, the side
        /// port of the drain.</summary>
        public Vector3 TerminalLocal
        {
            get
            {
                if (Kind == PlumbFixtureKind.FloorDrain)
                    return new Vector3(0f, -PlumbingDefaults.DrainDepth * 0.6f,
                        PlumbingDefaults.DrainSize * 0.5f + PlumbingDefaults.DrainPortLength);
                if (Angle == OutletAngle.Deg90)
                    return new Vector3(0f, 0f, PlumbingDefaults.StubLength);
                float leg = PlumbingDefaults.Stub45Drop * 0.70710678f;
                return new Vector3(0f, -leg, PlumbingDefaults.Stub45Run + leg);
            }
        }

        public Vector3 TerminalWorld => transform.TransformPoint(TerminalLocal);

        private void Ensure()
        {
            if (_mf == null) _mf = GetComponent<MeshFilter>();
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "PlumbFixtureMesh" };
                _mf.sharedMesh = _mesh;
            }
            if (_collider == null)
            {
                _collider = GetComponent<MeshCollider>();
                if (_collider == null) _collider = gameObject.AddComponent<MeshCollider>();
            }
        }

        public void Rebuild() => Build(Kind, Angle);

        /// <summary>Placement is the transform, so a move never re-cooks the mesh.</summary>
        public void MoveBy(Vector3 delta) => transform.position += delta;

        public void Build(PlumbFixtureKind kind, OutletAngle angle)
        {
            Ensure();
            Kind = kind;
            Angle = angle;
            _verts.Clear(); _tris.Clear(); _uvs.Clear();

            if (Kind == PlumbFixtureKind.FloorDrain) BuildDrain();
            else BuildStubOut(PipeSpec.Radius(Diameter));

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

        private void BuildStubOut(float radius)
        {
            _tubePts.Clear();
            _tubePts.Add(new Vector3(0f, 0f, -0.005f));   // seat slightly into the wall
            Vector3 mouth;
            if (Angle == OutletAngle.Deg90)
            {
                mouth = new Vector3(0f, 0f, PlumbingDefaults.StubLength);
                _tubePts.Add(mouth);
            }
            else
            {
                _tubePts.Add(new Vector3(0f, 0f, PlumbingDefaults.Stub45Run));
                float leg = PlumbingDefaults.Stub45Drop * 0.70710678f;
                mouth = new Vector3(0f, -leg, PlumbingDefaults.Stub45Run + leg);
                _tubePts.Add(mouth);
            }
            AppendTube(_tubePts, radius);

            // socket bell: a short flared ring at the mouth, along the last leg
            Vector3 dir = (mouth - _tubePts[_tubePts.Count - 2]).normalized;
            _tubePts.Clear();
            _tubePts.Add(mouth - dir * 0.03f);
            _tubePts.Add(mouth);
            AppendTube(_tubePts, radius * PlumbingDefaults.SocketFlare);
        }

        private void BuildDrain()
        {
            float s = PlumbingDefaults.DrainSize;
            float d = PlumbingDefaults.DrainDepth;
            // body sunk below the floor plane, grate rim flush on top
            AddBox(new Vector3(0f, -d * 0.5f, 0f), new Vector3(s, d, s));
            AddBox(new Vector3(0f, 0.003f, 0f), new Vector3(s + 0.01f, 0.006f, s + 0.01f));

            // D50 side port the shower/washer run plugs into
            _tubePts.Clear();
            _tubePts.Add(new Vector3(0f, -d * 0.6f, s * 0.5f - 0.01f));
            _tubePts.Add(TerminalLocal);
            AppendTube(_tubePts, PipeSpec.Radius(PipeDiameter.D50));
        }

        /// <summary>BuildTube into scratch lists, then append with an index offset —
        /// BuildTube clears its outputs, so multi-part meshes merge here.</summary>
        private void AppendTube(List<Vector3> points, float radius)
        {
            WireMath.BuildTube(points, radius, PipeRoute.Sides, _tubeVerts, _tubeTris, _tubeUvs);
            int baseIndex = _verts.Count;
            _verts.AddRange(_tubeVerts);
            _uvs.AddRange(_tubeUvs);
            for (int i = 0; i < _tubeTris.Count; i++) _tris.Add(baseIndex + _tubeTris[i]);
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
