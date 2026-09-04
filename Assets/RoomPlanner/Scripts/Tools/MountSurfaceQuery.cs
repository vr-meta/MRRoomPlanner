using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Walls;
using RoomPlanner.Floors;

namespace RoomPlanner.Tools
{
    /// <summary>Scene adapter for native construction and semantic scan planes.
    /// Reuses buffers; a vertical normal alone never makes furniture a wall.</summary>
    public sealed class MountSurfaceQuery
    {
        private GameObject _cachedObject;
        private Wall _wall;
        private Floor _floor;
        private Transform _anchor;
        private MountSurfaceKind _scanKind;
        private PropertyInfo _boundaryProperty;
        private MonoBehaviour _anchorComponent;
        private readonly List<MonoBehaviour> _components = new();

        public bool Resolve(GameObject hitObject, Vector3 point, Vector3 normal, MountSurface surface)
        {
            surface.Clear();
            if (hitObject == null || !hitObject.activeInHierarchy) return false;
            if (_cachedObject != hitObject) Cache(hitObject);
            if (_wall != null && _wall.Segment != null) return WallFace(_wall, normal, surface);
            if (_floor != null) return FloorFace(_floor, normal, surface);
            if (_anchor == null || _scanKind == MountSurfaceKind.Unknown) return false;
            if (_scanKind == MountSurfaceKind.Wall && Mathf.Abs(normal.y) > 0.01f) return false;
            if (_scanKind == MountSurfaceKind.Ceiling && normal.y > -0.99f) return false;
            if (_scanKind == MountSurfaceKind.Floor && normal.y < 0.99f) return false;
            if (_boundaryProperty?.GetValue(_anchorComponent) is not IReadOnlyList<Vector2> boundary
                || boundary.Count < 3) return false;
            surface.Kind = _scanKind;
            surface.Origin = _anchor.position;
            surface.Normal = normal.normalized;
            surface.U = _anchor.right;
            surface.V = _anchor.up;
            foreach (var p in boundary)
                surface.Boundary.Add(surface.Project(_anchor.TransformPoint(new Vector3(p.x, p.y, 0f))));
            surface.BaseLevel = point.y; // tool supplies the active level for scan walls
            return true;
        }

        private void Cache(GameObject target)
        {
            _cachedObject = target;
            _wall = target.GetComponentInParent<Wall>();
            _floor = target.GetComponentInParent<Floor>();
            _anchor = null; _anchorComponent = null; _boundaryProperty = null;
            _scanKind = MountSurfaceKind.Unknown;
            if (_wall != null || _floor != null) return;
            target.GetComponentsInParent(false, _components);
            foreach (var component in _components)
            {
                if (component == null || component.GetType().Name != "MRUKAnchor") continue;
                var type = component.GetType();
                string label = type.GetProperty("Label")?.GetValue(component)?.ToString() ?? "";
                _scanKind = label.Contains("WALL_FACE") ? MountSurfaceKind.Wall
                    : label.Contains("CEILING") ? MountSurfaceKind.Ceiling
                    : label.Contains("FLOOR") ? MountSurfaceKind.Floor : MountSurfaceKind.Unknown;
                _boundaryProperty = type.GetProperty("PlaneBoundary2D");
                _anchor = component.transform;
                _anchorComponent = component;
                break;
            }
        }

        public static bool WallFace(Wall wall, Vector3 normal, MountSurface surface)
        {
            surface.Clear();
            var s = wall != null ? wall.Segment : null;
            if (s == null || s.Suppressed || s.Length < 0.001f) return false;
            var direction = WallMesh.Direction(s);
            var right = wall.transform.TransformDirection(WallMesh.RightNormal(direction));
            bool plus = Vector3.Dot(normal, right) > 0f;
            var outward = plus ? right : -right;
            if (Vector3.Dot(normal, outward) < 0.999f || Mathf.Abs(outward.y) > 0.001f) return false;
            var f = WallMesh.BuildFootprint(s);
            var baseUp = Vector3.up * s.BaseHeight;
            Vector3 localA = (plus ? f.ARight : f.ALeft) + baseUp;
            Vector3 localB = (plus ? f.BRight : f.BLeft) + baseUp;
            var a = wall.transform.TransformPoint(localA);
            var b = wall.transform.TransformPoint(localB);
            var top = wall.transform.TransformPoint(localA + Vector3.up * Mathf.Abs(s.Height));
            float width = Vector3.Distance(a, b), height = Vector3.Distance(a, top);
            if (width < 0.001f || height < 0.001f) return false;
            surface.Kind = MountSurfaceKind.Wall;
            surface.Origin = a;
            surface.U = (b - a) / width;
            surface.V = (top - a) / height;
            surface.Normal = outward.normalized;
            surface.BaseLevel = a.y;
            AddRect(surface.Boundary, 0f, 0f, width, height);
            foreach (var opening in s.Openings)
            {
                // Same fraction mapping as Wall.TriangulateWithOpenings, including mitred ends.
                float left = (opening.AlongFraction - opening.Width / (2f * s.Length)) * width;
                float rightEdge = (opening.AlongFraction + opening.Width / (2f * s.Length)) * width;
                float scale = height / Mathf.Abs(s.Height);
                AddRect(surface.NextHole(), left, opening.SillHeight * scale,
                    rightEdge, (opening.SillHeight + opening.Height) * scale);
            }
            return true;
        }

        private static bool FloorFace(Floor floor, Vector3 normal, MountSurface surface)
        {
            var up = floor.transform.up;
            float dot = Vector3.Dot(normal, up);
            if (Mathf.Abs(dot) < 0.999f) return false;
            bool ceiling = dot < 0f;
            surface.Kind = ceiling ? MountSurfaceKind.Ceiling : MountSurfaceKind.Floor;
            surface.Origin = floor.transform.TransformPoint(new Vector3(0f,
                floor.Level - (ceiling ? floor.Thickness : 0f), 0f));
            surface.U = floor.transform.right;
            surface.V = floor.transform.forward;
            surface.Normal = normal.normalized;
            surface.BaseLevel = surface.Origin.y;
            foreach (var p in floor.Outline) surface.Boundary.Add(surface.Project(floor.transform.TransformPoint(p)));
            foreach (var hole in floor.Holes)
            {
                var target = surface.NextHole();
                foreach (var p in hole) target.Add(surface.Project(floor.transform.TransformPoint(p)));
            }
            return surface.Boundary.Count >= 3;
        }

        private static void AddRect(List<Vector3> points, float x0, float y0, float x1, float y1)
        {
            points.Add(new Vector3(x0, 0f, y0)); points.Add(new Vector3(x1, 0f, y0));
            points.Add(new Vector3(x1, 0f, y1)); points.Add(new Vector3(x0, 0f, y1));
        }
    }
}
