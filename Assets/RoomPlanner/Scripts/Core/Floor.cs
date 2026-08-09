using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Floors
{
    /// <summary>
    /// A rectangular floor slab at a given level with thickness. The top face is UV-mapped by
    /// WORLD position (plan scale + offset), so a floorplan image aligns across several slabs
    /// and can be scaled/positioned independently of the slab size.
    /// In RoomPlanner.Core so geometry is unit-testable without device deps.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class Floor : MonoBehaviour
    {
        private MeshFilter _mf;
        private Mesh _mesh;
        private MeshCollider _collider;

        public Vector3 CornerA { get; private set; }
        public Vector3 CornerB { get; private set; }
        public float Level { get; private set; }

        // Cached build parameters so the slab can rebuild itself after edits (move/resize).
        private float _thickness = 0.2f;
        private float _planScale = 5f;
        private float _planOriginX;
        private float _planOriginZ;

        private void Ensure()
        {
            if (_mf == null) _mf = GetComponent<MeshFilter>();
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "FloorMesh" };
                _mf.sharedMesh = _mesh;
            }
            if (_collider == null)
            {
                _collider = GetComponent<MeshCollider>();
                if (_collider == null) _collider = gameObject.AddComponent<MeshCollider>();
            }
        }

        /// <summary>Rebuild from the current corners/level and cached parameters.</summary>
        public void Rebuild() => Build(CornerA, CornerB, Level, _thickness, _planScale, _planOriginX, _planOriginZ);

        /// <summary>Translate the whole slab and rebuild in place.</summary>
        public void MoveBy(Vector3 delta)
        {
            Build(CornerA + delta, CornerB + delta, Level + delta.y,
                _thickness, _planScale, _planOriginX, _planOriginZ);
        }

        public void Build(Vector3 a, Vector3 b, float level, float thickness,
            float planScale, float planOriginX, float planOriginZ)
        {
            Ensure();
            // Keep the stored corners on the slab's top plane so CornerA.y never drifts from Level.
            a.y = level; b.y = level;
            CornerA = a; CornerB = b; Level = level;
            _thickness = Mathf.Max(0.001f, Mathf.Abs(thickness));
            _planScale = planScale; _planOriginX = planOriginX; _planOriginZ = planOriginZ;
            _mesh.Clear();

            float minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x);
            float minZ = Mathf.Min(a.z, b.z), maxZ = Mathf.Max(a.z, b.z);
            if (maxX - minX < 1e-4f || maxZ - minZ < 1e-4f) { if (_collider != null) _collider.sharedMesh = null; return; }

            float top = level, bot = level - _thickness;
            // Negative scale = mirrored plan (legit); only guard against near-zero.
            float s = Mathf.Abs(planScale) > 1e-4f ? planScale : 1f;

            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();

            // top (0-3) — UV by world position so the plan aligns across slabs
            AddTop(v, uv, minX, top, minZ, planOriginX, planOriginZ, s);
            AddTop(v, uv, maxX, top, minZ, planOriginX, planOriginZ, s);
            AddTop(v, uv, maxX, top, maxZ, planOriginX, planOriginZ, s);
            AddTop(v, uv, minX, top, maxZ, planOriginX, planOriginZ, s);
            // bottom (4-7)
            v.Add(new Vector3(minX, bot, minZ)); uv.Add(Vector2.zero);
            v.Add(new Vector3(maxX, bot, minZ)); uv.Add(Vector2.zero);
            v.Add(new Vector3(maxX, bot, maxZ)); uv.Add(Vector2.zero);
            v.Add(new Vector3(minX, bot, maxZ)); uv.Add(Vector2.zero);

            // All faces wound OUTWARD — MeshCollider raycasts only hit front faces, so a
            // downward pick must land on the top (y = level), not tunnel to the bottom.
            Quad(t, 0, 3, 2, 1);   // top (+Y)
            Quad(t, 4, 5, 6, 7);   // bottom (−Y)
            Quad(t, 0, 1, 5, 4);   // side z = minZ (−Z)
            Quad(t, 1, 2, 6, 5);   // side x = maxX (+X)
            Quad(t, 2, 3, 7, 6);   // side z = maxZ (+Z)
            Quad(t, 3, 0, 4, 7);   // side x = minX (−X)

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

        private static void AddTop(List<Vector3> v, List<Vector2> uv, float x, float y, float z,
            float ox, float oz, float s)
        {
            v.Add(new Vector3(x, y, z));
            uv.Add(new Vector2((x - ox) / s, (z - oz) / s));
        }

        private static void Quad(List<int> t, int a, int b, int c, int d)
        {
            t.Add(a); t.Add(b); t.Add(c);
            t.Add(a); t.Add(c); t.Add(d);
        }

        private void OnDestroy()
        {
            // The mesh created via `new Mesh` is not freed by Destroy(gameObject).
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
        }
    }
}
