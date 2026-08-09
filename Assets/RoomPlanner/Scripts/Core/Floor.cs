using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Floors
{
    /// <summary>
    /// A floor slab: a closed OUTLINE at a given level, extruded down by its thickness
    /// (docs/design/17-floor-outline.md). Real flats are rarely rectangular, and the outline is
    /// also what walls will snap to — a rectangle is simply the 4-point case, and the old
    /// two-corner API still builds one.
    ///
    /// The top face is UV-mapped by WORLD position (plan scale + rotation + offset), so a
    /// floorplan image aligns across several slabs independently of their shape.
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

        private readonly List<Vector3> _outline = new();

        /// <summary>
        /// The closed outline of the slab, in order, on the top plane. A rectangle is just the
        /// 4-point case (docs/design/17-floor-outline.md).
        /// </summary>
        public IReadOnlyList<Vector3> Outline => _outline;

        // Cached build parameters so the slab can rebuild itself after edits (move/resize).
        private float _thickness = 0.2f;
        private float _planScale = 5f;
        private float _planRotationDeg;
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

        /// <summary>Rebuild from the current outline/level and cached parameters.</summary>
        public void Rebuild() =>
            BuildOutline(new List<Vector3>(_outline), Level, _thickness,
                _planScale, _planRotationDeg, _planOriginX, _planOriginZ);

        /// <summary>Translate the whole slab and rebuild in place.</summary>
        public void MoveBy(Vector3 delta)
        {
            var moved = new List<Vector3>(_outline.Count);
            foreach (var p in _outline) moved.Add(p + delta);
            BuildOutline(moved, Level + delta.y, _thickness,
                _planScale, _planRotationDeg, _planOriginX, _planOriginZ);
        }

        /// <summary>
        /// Move ONE outline corner (vertex editing, step C4). The caller owns undo.
        /// </summary>
        public void MoveCorner(int index, Vector3 position)
        {
            if (index < 0 || index >= _outline.Count) return;
            var pts = new List<Vector3>(_outline);
            position.y = Level;
            pts[index] = position;
            BuildOutline(pts, Level, _thickness, _planScale, _planRotationDeg, _planOriginX, _planOriginZ);
        }

        /// <summary>Legacy signature (no plan rotation) — kept so existing callers/tests stand.</summary>
        public void Build(Vector3 a, Vector3 b, float level, float thickness,
            float planScale, float planOriginX, float planOriginZ)
            => Build(a, b, level, thickness, planScale, 0f, planOriginX, planOriginZ);

        public void Build(Vector3 a, Vector3 b, float level, float thickness,
            float planScale, float planRotationDeg, float planOriginX, float planOriginZ)
        {
            // A rectangle is the 4-point outline; go through the same builder so there is one
            // mesh path to keep correct.
            a.y = level; b.y = level;
            float minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x);
            float minZ = Mathf.Min(a.z, b.z), maxZ = Mathf.Max(a.z, b.z);

            BuildOutline(new List<Vector3>
            {
                new(minX, level, minZ), new(maxX, level, minZ),
                new(maxX, level, maxZ), new(minX, level, maxZ),
            }, level, thickness, planScale, planRotationDeg, planOriginX, planOriginZ);

            // callers of the rectangle API expect the corners they passed, not the bounding box
            CornerA = a; CornerB = b;
        }

        /// <summary>
        /// Build the slab from an arbitrary closed outline (docs/design/17-floor-outline.md).
        /// The outline is cleaned and its winding normalised, so it does not matter which way
        /// round the user drew it. An outline that crosses itself is REFUSED (empty mesh and no
        /// collider) rather than turned into silent garbage — coding rule 1.3.
        /// </summary>
        public void BuildOutline(IReadOnlyList<Vector3> outline, float level, float thickness,
            float planScale, float planRotationDeg, float planOriginX, float planOriginZ)
        {
            Ensure();
            Level = level;
            _thickness = Mathf.Max(0.001f, Mathf.Abs(thickness));
            _planScale = planScale; _planRotationDeg = planRotationDeg;
            _planOriginX = planOriginX; _planOriginZ = planOriginZ;
            _mesh.Clear();

            var pts = Polygon.Clean(outline);
            for (int i = 0; i < pts.Count; i++) pts[i] = new Vector3(pts[i].x, level, pts[i].z);
            pts = Polygon.ToCounterClockwise(pts);

            _outline.Clear();
            _outline.AddRange(pts);
            UpdateCornersFromOutline();

            var tris = pts.Count >= 3 && Polygon.IsSimple(pts) ? Polygon.Triangulate(pts) : new List<int>();
            if (tris.Count == 0)
            {
                if (_collider != null) _collider.sharedMesh = null;
                return;
            }

            int n = pts.Count;
            float top = level, bot = level - _thickness;
            // Negative scale = mirrored plan (legit); near-zero is guarded inside BlueprintMath.
            var placement = new BlueprintPlacement
            {
                Scale = planScale, RotationDeg = planRotationDeg,
                OriginX = planOriginX, OriginZ = planOriginZ,
            };

            // Top, bottom and sides get their OWN vertices instead of sharing a ring. Sharing
            // would force one UV per vertex onto three surfaces that need different ones (the
            // plan on top, metric tiling on the sides), and RecalculateNormals would average
            // the rim into a soft round edge instead of a crisp one.
            var v = new List<Vector3>(n * 6);
            var uv = new List<Vector2>(n * 6);
            var t = new List<int>();

            // --- top ring 0..n-1: UV from the blueprint placement, so the plan aligns across slabs
            for (int i = 0; i < n; i++)
            {
                var p = new Vector3(pts[i].x, top, pts[i].z);
                v.Add(p);
                uv.Add(BlueprintMath.WorldToPlanUV(p, placement));
            }
            // --- bottom ring n..2n-1: metric UV in the ground plane
            for (int i = 0; i < n; i++)
            {
                v.Add(new Vector3(pts[i].x, bot, pts[i].z));
                uv.Add(new Vector2(pts[i].x / TileMeters, pts[i].z / TileMeters));
            }

            // Winding: a counter-clockwise ring (as Polygon defines it) triangulates with its
            // normal pointing DOWN, so the TOP face takes the reversed triple and the bottom
            // takes it as-is. Faces must point outward or a downward pick tunnels to the
            // underside (coding rule 1.1 / audit WP2).
            for (int i = 0; i < tris.Count; i += 3)
            {
                t.Add(tris[i + 2]); t.Add(tris[i + 1]); t.Add(tris[i]);          // top (+Y)
            }
            for (int i = 0; i < tris.Count; i += 3)
            {
                t.Add(n + tris[i]); t.Add(n + tris[i + 1]); t.Add(n + tris[i + 2]); // bottom (−Y)
            }

            // --- sides: four fresh vertices per edge, U running around the perimeter in metres
            // (per-edge rather than per-corner, so the closing edge has no UV seam)
            float run = 0f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 a = pts[i], b = pts[j];
                float len = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                float uA = run / TileMeters, uB = (run + len) / TileMeters;
                run += len;

                float vTop = top / TileMeters, vBot = bot / TileMeters;
                int baseIndex = v.Count;

                v.Add(new Vector3(a.x, top, a.z)); uv.Add(new Vector2(uA, vTop));
                v.Add(new Vector3(b.x, top, b.z)); uv.Add(new Vector2(uB, vTop));
                v.Add(new Vector3(b.x, bot, b.z)); uv.Add(new Vector2(uB, vBot));
                v.Add(new Vector3(a.x, bot, a.z)); uv.Add(new Vector2(uA, vBot));

                Quad(t, baseIndex, baseIndex + 1, baseIndex + 2, baseIndex + 3);   // outward
            }

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

        /// <summary>Bounding corners, kept for callers that still think in rectangles.</summary>
        private void UpdateCornersFromOutline()
        {
            if (_outline.Count == 0) { CornerA = CornerB = new Vector3(0f, Level, 0f); return; }
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in _outline)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }
            CornerA = new Vector3(minX, Level, minZ);
            CornerB = new Vector3(maxX, Level, maxZ);
        }

        /// <summary>Area of the slab in square metres — shown in the inspector.</summary>
        public float Area => Polygon.Area(_outline);

        /// <summary>
        /// Metres per texture tile for the sides and the underside (metric UVs,
        /// docs/design/04-surfaces-materials.md). The TOP is mapped by the blueprint placement
        /// instead, because that is the surface the floorplan is traced on.
        /// </summary>
        public const float TileMeters = 1f;

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
