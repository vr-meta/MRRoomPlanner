using UnityEngine;

namespace RoomPlanner.Walls
{
    /// <summary>
    /// The moving leaf of a door / garage opening (issue #50): a child GameObject of
    /// the wall view, box meshes + BoxColliders, driven purely by transforms — the
    /// wall mesh itself no longer carries the leaf, so opening/closing never rebuilds
    /// a mesh and allocates nothing per frame. Local frame: origin at the leaf's
    /// bottom hinge corner (door) / bottom centre (garage), X along the wall,
    /// Y up, Z toward the swing/fold side.
    /// </summary>
    public class OpeningLeafView : MonoBehaviour
    {
        /// <summary>Full toggle travel time, seconds.</summary>
        public const float AnimSeconds = 0.45f;

        public Wall Owner { get; private set; }
        public WallOpening Opening { get; private set; }

        private float _yawSign = -1f;      // -1: +X free edge swings toward +Z
        private Transform _door;           // swing leaf (doors)
        private Transform[] _panels;       // sectional panels (garage)
        private Mesh _doorMesh, _thickMesh, _thinMesh;

        // built dims — Rebuild() early-outs while they match (node drags rebuild the
        // wall every frame; the leaf must not be recreated then, coding rule 4.2)
        private OpeningKind _builtKind = (OpeningKind)(-1);
        private float _builtW = -1f, _builtH = -1f, _builtT = -1f;

        private float _applied = -1f;      // last fraction pushed into transforms
        private float _animFrom, _animTo;
        private float _animT = 1f;         // 1 = idle
        private float _lastOpen = 1f;      // where the trigger toggle re-opens to

        public void Bind(Wall owner, WallOpening opening, float yawSign)
        {
            Owner = owner;
            Opening = opening;
            _yawSign = yawSign;
            if (opening != null && opening.OpenFraction > 0.05f) _lastOpen = opening.OpenFraction;
        }

        /// <summary>Snap or animate toward a fraction; the slider drives it directly,
        /// the trigger toggle animates.</summary>
        public void SetFraction(float fraction, bool animate)
        {
            fraction = Mathf.Clamp01(fraction);
            if (fraction > 0.05f) _lastOpen = fraction;
            if (animate)
            {
                _animFrom = _applied < 0f ? 0f : _applied;
                _animTo = fraction;
                _animT = 0f;
            }
            else
            {
                _animT = 1f;
                Apply(fraction);
            }
        }

        /// <summary>Trigger on a selected door: open ↔ closed (to the last-used %).</summary>
        public void Toggle()
        {
            float current = Opening != null ? Opening.OpenFraction : 0f;
            SetFraction(current > 0.05f ? 0f : _lastOpen, animate: true);
        }

        private void Update()
        {
            if (_animT >= 1f) return;
            _animT = Mathf.Min(1f, _animT + Time.deltaTime / AnimSeconds);
            Apply(Mathf.Lerp(_animFrom, _animTo, Mathf.SmoothStep(0f, 1f, _animT)));
        }

        private void Apply(float fraction)
        {
            _applied = fraction;
            if (Opening != null) Opening.OpenFraction = fraction;
            if (_door != null)
                _door.localRotation = Quaternion.Euler(0f, _yawSign * OpeningPose.DoorYawDeg(fraction), 0f);
            if (_panels != null)
                for (int i = 0; i < _panels.Length; i++)
                {
                    var p = _panels[i];
                    if (p == null) continue;
                    OpeningPose.GaragePanel(_builtH, _panels.Length, i, fraction,
                        out float y, out float z, out float tilt);
                    p.localPosition = new Vector3(0f, y, z);
                    p.localRotation = Quaternion.Euler(tilt, 0f, 0f);
                }
        }

        /// <summary>(Re)build the leaf children; a no-op while the dimensions match, so
        /// per-frame wall rebuilds (node drags) only re-place the root transform.</summary>
        public void Rebuild(OpeningKind kind, float width, float height, float wallThickness, Material mat)
        {
            if (kind == _builtKind && Mathf.Approximately(width, _builtW)
                && Mathf.Approximately(height, _builtH) && Mathf.Approximately(wallThickness, _builtT))
            {
                if (_applied < 0f) Apply(Opening != null ? Opening.OpenFraction : 0f);
                return;
            }
            _builtKind = kind;
            _builtW = width;
            _builtH = height;
            _builtT = wallThickness;
            Clear();

            float thick = 2f * Mathf.Min(0.02f, wallThickness * 0.2f);   // closed-leaf look
            if (kind == OpeningKind.Garage)
            {
                int n = OpeningPose.GaragePanels;
                float ph = height / n;
                if (ph <= 0.02f) { n = 1; ph = height; }
                // alternating panel thickness — the section seams stay real geometry
                _thickMesh = BuildBox(new Vector3(-width * 0.5f, 0f, -thick * 0.5f),
                                      new Vector3(width * 0.5f, ph, thick * 0.5f));
                float thin = thick * 0.55f;
                _thinMesh = BuildBox(new Vector3(-width * 0.5f, 0f, -thin * 0.5f),
                                     new Vector3(width * 0.5f, ph, thin * 0.5f));
                _panels = new Transform[n];
                for (int i = 0; i < n; i++)
                    _panels[i] = MakePart($"Panel{i}", (i & 1) == 0 ? _thickMesh : _thinMesh, mat);
            }
            else
            {
                // door leaf: origin at the hinge edge, extends toward +X
                _doorMesh = BuildBox(new Vector3(0f, 0f, -thick * 0.5f),
                                     new Vector3(width, height, thick * 0.5f));
                _door = MakePart("Leaf", _doorMesh, mat);
            }
            _applied = -1f;
            Apply(Opening != null ? Opening.OpenFraction : 0f);
        }

        private Transform MakePart(string partName, Mesh mesh, Material mat)
        {
            var go = new GameObject(partName);
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            var box = go.AddComponent<BoxCollider>();
            box.center = mesh.bounds.center;
            box.size = mesh.bounds.size;
            return go.transform;
        }

        private void Clear()
        {
            _door = null;
            _panels = null;
            for (int i = transform.childCount - 1; i >= 0; i--)
                Free(transform.GetChild(i).gameObject);
            Free(_doorMesh); _doorMesh = null;
            Free(_thickMesh); _thickMesh = null;
            Free(_thinMesh); _thinMesh = null;
        }

        private static void Free(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        private void OnDestroy()
        {
            Free(_doorMesh);
            Free(_thickMesh);
            Free(_thinMesh);
        }

        /// <summary>Axis-aligned box, per-face vertices/UVs, outward winding (Unity
        /// front face normal = Cross(v, u) for the quad (c, c+v, c+u+v, c+u)).</summary>
        private static Mesh BuildBox(Vector3 min, Vector3 max)
        {
            var mesh = new Mesh { name = "LeafBox" };
            var size = max - min;
            var verts = new System.Collections.Generic.List<Vector3>(24);
            var uvs = new System.Collections.Generic.List<Vector2>(24);
            var tris = new System.Collections.Generic.List<int>(36);

            void Face(Vector3 corner, Vector3 u, Vector3 v)
            {
                int b = verts.Count;
                verts.Add(corner); verts.Add(corner + v); verts.Add(corner + u + v); verts.Add(corner + u);
                uvs.Add(Vector2.zero); uvs.Add(new Vector2(0f, v.magnitude));
                uvs.Add(new Vector2(u.magnitude, v.magnitude)); uvs.Add(new Vector2(u.magnitude, 0f));
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }

            Vector3 X = Vector3.right * size.x, Y = Vector3.up * size.y, Z = Vector3.forward * size.z;
            Face(new Vector3(min.x, min.y, max.z), Y, X);   // +Z  (Cross(X,Y) = +Z)
            Face(new Vector3(min.x, min.y, min.z), X, Y);   // -Z
            Face(new Vector3(max.x, min.y, min.z), Z, Y);   // +X  (Cross(Y,Z) = +X)
            Face(new Vector3(min.x, min.y, min.z), Y, Z);   // -X
            Face(new Vector3(min.x, max.y, min.z), X, Z);   // +Y  (Cross(Z,X) = +Y)
            Face(new Vector3(min.x, min.y, min.z), Z, X);   // -Y

            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
