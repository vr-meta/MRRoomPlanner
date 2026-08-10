using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Electrical
{
    /// <summary>
    /// A wire run of the Electrical layer (docs/design/19-electrical.md): parametric
    /// polyline on room surfaces rendered as a graphite tube. Ends may be logically
    /// attached to fixtures by SceneModel id — moving the fixture drags the endpoint.
    /// The route owns its Mesh and frees it in OnDestroy (rule 1.5).
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class WireRoute : MonoBehaviour
    {
        public const float Radius = 0.0075f;   // Ø15 mm: reads on passthrough, not a hose
        public const int Sides = 8;

        private MeshFilter _mf;
        private Mesh _mesh;
        private MeshCollider _collider;

        private readonly List<Vector3> _points = new();
        private readonly List<Vector3> _verts = new();
        private readonly List<int> _tris = new();
        private readonly List<Vector2> _uvs = new();

        public CableType Cable { get; private set; } = CableType.C3x25;

        /// <summary>SceneModel ids of attached fixtures; null/empty = free end.</summary>
        public string StartFixtureId { get; set; }
        public string EndFixtureId { get; set; }

        public IReadOnlyList<Vector3> Points => _points;
        public int PointCount => _points.Count;
        public Vector3 GetPoint(int index) => _points[index];
        public float Length => WireMath.PolylineLength(_points);

        /// <summary>Number of attached ends — each one costs a connection allowance in the BOM.</summary>
        public int Connections =>
            (string.IsNullOrEmpty(StartFixtureId) ? 0 : 1) + (string.IsNullOrEmpty(EndFixtureId) ? 0 : 1);

        private void Ensure()
        {
            if (_mf == null) _mf = GetComponent<MeshFilter>();
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "WireMesh" };
                _mf.sharedMesh = _mesh;
            }
            if (_collider == null)
            {
                _collider = GetComponent<MeshCollider>();
                if (_collider == null) _collider = gameObject.AddComponent<MeshCollider>();
            }
        }

        /// <summary>Builds the route. Copies the input (which may alias our own list,
        /// rule 1.4), collapses double clicks, rejects degenerate polylines.</summary>
        public bool Build(List<Vector3> points, CableType cable)
        {
            Ensure();
            Cable = cable;
            if (!ReferenceEquals(points, _points))
            {
                _points.Clear();
                if (points != null) _points.AddRange(points);
            }
            WireMath.CollapsePoints(_points, WireMath.MergeDistance);
            if (_points.Count < 2)
            {
                _mesh.Clear();
                if (_collider != null) _collider.sharedMesh = null;
                return false;
            }
            RebuildMesh();
            return true;
        }

        public void Rebuild() => Build(_points, Cable);

        public void SetCable(CableType cable) => Cable = cable;

        public void MoveBy(Vector3 delta)
        {
            for (int i = 0; i < _points.Count; i++) _points[i] += delta;
            Rebuild();
        }

        /// <summary>Move one control point. Pass refreshCollider=false while a handle drag is
        /// previewing — the visual mesh follows every frame, the physics re-cook happens once
        /// on release (rule 4.2).</summary>
        public void MovePoint(int index, Vector3 world, bool refreshCollider = true)
        {
            if (index < 0 || index >= _points.Count) return;
            _points[index] = world;
            RebuildMesh(refreshCollider);
        }

        public void RefreshCollider()
        {
            if (_collider == null || _mesh == null) return;
            _collider.sharedMesh = null;
            _collider.sharedMesh = _mesh;
        }

        /// <summary>Follows an attached fixture: shifts the matching endpoint(s).
        /// Returns true if anything moved.</summary>
        public bool TryMoveAttachedEnd(string fixtureId, Vector3 delta)
        {
            if (string.IsNullOrEmpty(fixtureId) || _points.Count < 2) return false;
            bool moved = false;
            if (fixtureId == StartFixtureId) { _points[0] += delta; moved = true; }
            if (fixtureId == EndFixtureId) { _points[_points.Count - 1] += delta; moved = true; }
            if (moved) Rebuild();
            return moved;
        }

        private void RebuildMesh(bool refreshCollider = true)
        {
            WireMath.BuildTube(_points, Radius, Sides, _verts, _tris, _uvs);
            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            if (refreshCollider) RefreshCollider();
        }

        private void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
        }
    }
}
