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
        private readonly List<List<Vector3>> _holes = new();

        /// <summary>
        /// The closed outline of the slab, in order, on the top plane. A rectangle is just the
        /// 4-point case (docs/design/17-floor-outline.md).
        /// </summary>
        public IReadOnlyList<Vector3> Outline => _outline;

        /// <summary>Holes cut in the slab — stairwells, shafts (step C6).</summary>
        public IReadOnlyList<IReadOnlyList<Vector3>> Holes => _holes;

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

        /// <summary>
        /// Snapshot of the rings. Every rebuild clears the live lists before refilling them, so
        /// handing them straight back in would wipe the input mid-flight (coding rule 1.4).
        /// </summary>
        private List<Vector3> OutlineCopy() => new(_outline);

        private List<IReadOnlyList<Vector3>> HolesCopy()
        {
            var copy = new List<IReadOnlyList<Vector3>>(_holes.Count);
            foreach (var h in _holes) copy.Add(new List<Vector3>(h));
            return copy;
        }

        /// <summary>Rebuild from the current outline/level and cached parameters.</summary>
        public void Rebuild() =>
            BuildOutline(OutlineCopy(), HolesCopy(), Level, _thickness,
                _planScale, _planRotationDeg, _planOriginX, _planOriginZ);

        /// <summary>Translate the whole slab and rebuild in place.</summary>
        public void MoveBy(Vector3 delta)
        {
            var moved = new List<Vector3>(_outline.Count);
            foreach (var p in _outline) moved.Add(p + delta);
            var holes = HolesCopy();
            foreach (var h in holes)
            {
                var ring = (List<Vector3>)h;
                for (int i = 0; i < ring.Count; i++) ring[i] += delta;   // holes travel with the slab
            }
            BuildOutline(moved, holes, Level + delta.y, _thickness,
                _planScale, _planRotationDeg, _planOriginX, _planOriginZ);
        }

        /// <summary>
        /// Re-apply the blueprint placement WITHOUT touching the shape. Rebuilding through the
        /// two-corner API would flatten a polygonal slab into its bounding box.
        /// </summary>
        public void SetPlanPlacement(float planScale, float planRotationDeg, float planOriginX, float planOriginZ)
        {
            BuildOutline(OutlineCopy(), HolesCopy(), Level, _thickness,
                planScale, planRotationDeg, planOriginX, planOriginZ);
        }

        /// <summary>
        /// Move ONE outline corner (vertex editing, step C4). The caller owns undo.
        /// </summary>
        public void MoveCorner(int index, Vector3 position)
        {
            if (index < 0 || index >= _outline.Count) return;
            var pts = OutlineCopy();
            position.y = Level;
            pts[index] = position;
            BuildOutline(pts, HolesCopy(), Level, _thickness,
                _planScale, _planRotationDeg, _planOriginX, _planOriginZ);
        }

        /// <summary>
        /// Cut a hole in the slab (stairwell, shaft). Returns false when the ring is not usable
        /// or does not sit fully inside the outline — a hole poking through the edge is not a
        /// hole, and refusing beats producing a shape nobody can reason about (rule 1.3).
        /// </summary>
        public bool AddHole(IReadOnlyList<Vector3> hole)
        {
            var ring = Polygon.Clean(hole);
            if (ring.Count < 3 || !Polygon.IsSimple(ring)) return false;
            foreach (var p in ring)
                if (!Polygon.Contains(_outline, p)) return false;

            var holes = HolesCopy();
            holes.Add(ring);
            BuildOutline(OutlineCopy(), holes, Level, _thickness,
                _planScale, _planRotationDeg, _planOriginX, _planOriginZ);
            return _holes.Count == holes.Count;      // false if the bridge could not be cut
        }

        /// <summary>Remove a hole by index (undo of AddHole).</summary>
        public void RemoveHole(int index)
        {
            if (index < 0 || index >= _holes.Count) return;
            var holes = HolesCopy();
            holes.RemoveAt(index);
            BuildOutline(OutlineCopy(), holes, Level, _thickness,
                _planScale, _planRotationDeg, _planOriginX, _planOriginZ);
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
            => BuildOutline(outline, null, level, thickness, planScale, planRotationDeg, planOriginX, planOriginZ);

        /// <summary>
        /// Build the slab with holes cut in it — stairwells, shafts (step C6). The holes are
        /// parametric openings in the outline, never a boolean subtraction of meshes.
        /// </summary>
        public void BuildOutline(IReadOnlyList<Vector3> outline,
            IReadOnlyList<IReadOnlyList<Vector3>> holes, float level, float thickness,
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

            _holes.Clear();
            if (holes != null)
            {
                foreach (var h in holes)
                {
                    var ring = Polygon.Clean(h);
                    if (ring.Count < 3) continue;
                    for (int i = 0; i < ring.Count; i++) ring[i] = new Vector3(ring[i].x, level, ring[i].z);
                    _holes.Add(ring);
                }
            }
            UpdateCornersFromOutline();

            // The top/bottom faces come from the outline with its holes bridged in; the rings
            // themselves stay intact for the side walls and for editing.
            var tris = Polygon.TriangulateWithHoles(pts, _holes, out var faceVerts);
            if (tris.Count == 0)
            {
                if (_collider != null) _collider.sharedMesh = null;
                return;
            }

            int n = faceVerts.Count;
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

            // --- top face 0..n-1: UV from the blueprint placement, so the plan aligns across slabs
            for (int i = 0; i < n; i++)
            {
                var p = new Vector3(faceVerts[i].x, top, faceVerts[i].z);
                v.Add(p);
                uv.Add(BlueprintMath.WorldToPlanUV(p, placement));
            }
            // --- bottom face n..2n-1: metric UV in the ground plane
            for (int i = 0; i < n; i++)
            {
                v.Add(new Vector3(faceVerts[i].x, bot, faceVerts[i].z));
                uv.Add(new Vector2(faceVerts[i].x / TileMeters, faceVerts[i].z / TileMeters));
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
            AddSideWalls(pts, v, uv, t, top, bot);
            // Hole walls come from rings wound the OTHER way, so the same quad order makes them
            // face INTO the hole — which is outward from the solid, exactly as required.
            foreach (var hole in _holes) AddSideWalls(Polygon.ToClockwise(hole), v, uv, t, top, bot);

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

        private static void AddSideWalls(List<Vector3> ring, List<Vector3> v, List<Vector2> uv,
            List<int> t, float top, float bot)
        {
            int n = ring.Count;
            float run = 0f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 a = ring[i], b = ring[j];
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
        public float Area => Polygon.AreaWithHoles(_outline, _holes);

        /// <summary>Slab thickness, per instance (step C4).</summary>
        public float Thickness => _thickness;

        public void SetThickness(float thickness) =>
            BuildOutline(OutlineCopy(), HolesCopy(), Level, thickness,
                _planScale, _planRotationDeg, _planOriginX, _planOriginZ);

        /// <summary>Move the whole slab to another storey level, keeping its shape.</summary>
        public void SetLevel(float level) =>
            BuildOutline(OutlineCopy(), HolesCopy(), level, _thickness,
                _planScale, _planRotationDeg, _planOriginX, _planOriginZ);

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
