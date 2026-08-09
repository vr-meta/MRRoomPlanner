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

        public Vector3 CornerA { get; private set; }
        public Vector3 CornerB { get; private set; }
        public float Level { get; private set; }

        private void Ensure()
        {
            if (_mf == null) _mf = GetComponent<MeshFilter>();
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "FloorMesh" };
                _mf.sharedMesh = _mesh;
            }
        }

        public void Build(Vector3 a, Vector3 b, float level, float thickness,
            float planScale, float planOriginX, float planOriginZ)
        {
            Ensure();
            CornerA = a; CornerB = b; Level = level;
            _mesh.Clear();

            float minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x);
            float minZ = Mathf.Min(a.z, b.z), maxZ = Mathf.Max(a.z, b.z);
            if (maxX - minX < 1e-4f || maxZ - minZ < 1e-4f) return;

            float top = level, bot = level - thickness;
            float s = planScale > 1e-4f ? planScale : 1f;

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

            Quad(t, 0, 1, 2, 3);   // top
            Quad(t, 4, 7, 6, 5);   // bottom
            Quad(t, 0, 4, 5, 1);   // sides
            Quad(t, 1, 5, 6, 2);
            Quad(t, 2, 6, 7, 3);
            Quad(t, 3, 7, 4, 0);

            _mesh.SetVertices(v);
            _mesh.SetUVs(0, uv);
            _mesh.SetTriangles(t, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
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
    }
}
