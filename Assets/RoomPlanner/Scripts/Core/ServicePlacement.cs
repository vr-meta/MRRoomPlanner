using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core
{
    public enum MountSurfaceKind { Unknown, Wall, Floor, Ceiling }
    public enum PlacementFailure { None, WrongSurface, InvalidSize, OutsideSurface, Opening, OffSurface, Overlap }

    /// <summary>Reusable surface snapshot. Rings use (x, 0, z) in the U/V frame;
    /// neither mesh triangle indices nor world-axis bounding boxes define the mount.</summary>
    public sealed class MountSurface
    {
        public MountSurfaceKind Kind;
        public Vector3 Origin, U, V, Normal;
        public readonly List<Vector3> Boundary = new();
        public readonly List<List<Vector3>> Holes = new();
        public int HoleCount;
        public float BaseLevel;

        public void Clear()
        {
            Kind = MountSurfaceKind.Unknown;
            Boundary.Clear();
            HoleCount = 0;
        }

        public List<Vector3> NextHole()
        {
            if (HoleCount == Holes.Count) Holes.Add(new List<Vector3>(4));
            var hole = Holes[HoleCount++];
            hole.Clear();
            return hole;
        }

        public Vector3 Project(Vector3 world)
        {
            var d = world - Origin;
            return new Vector3(Vector3.Dot(d, U), 0f, Vector3.Dot(d, V));
        }

        public Vector3 Point(float u, float v) => Origin + U * u + V * v;
    }

    /// <summary>Allocation-free placement and dimension calculations, shared by tools
    /// and exact edits. Distances are metres; tolerances do not act as snap radii.</summary>
    public static class ServicePlacement
    {
        public const float Tolerance = 0.0001f;
        public const float VisualOffset = 0.002f;

        public static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        public static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);

        public static string Describe(PlacementFailure failure) => failure switch
        {
            PlacementFailure.None => "Ready",
            PlacementFailure.WrongSurface => "Wrong surface",
            PlacementFailure.InvalidSize => "Invalid size",
            PlacementFailure.OutsideSurface => "Outside surface",
            PlacementFailure.Opening => "Opening",
            PlacementFailure.OffSurface => "Off surface",
            _ => "Overlap",
        };

        /// <summary>Writes a mounting rectangle into the supplied four-point buffer.</summary>
        public static void Rectangle(MountSurface surface, Vector3 center, Quaternion rotation,
            Vector2 size, Vector3[] corners)
        {
            Vector3 x = rotation * Vector3.right * (size.x * 0.5f);
            Vector3 y = rotation * Vector3.up * (size.y * 0.5f);
            corners[0] = surface.Project(center - x - y);
            corners[1] = surface.Project(center + x - y);
            corners[2] = surface.Project(center + x + y);
            corners[3] = surface.Project(center - x + y);
        }

        public static PlacementFailure Validate(MountSurface surface, Vector3 center,
            Quaternion rotation, Vector2 size, Vector3[] scratch)
        {
            if (surface == null || surface.Kind == MountSurfaceKind.Unknown)
                return PlacementFailure.WrongSurface;
            if (!Finite(center) || !Finite(size.x) || !Finite(size.y) || size.x <= 0f || size.y <= 0f)
                return PlacementFailure.InvalidSize;
            if (Mathf.Abs(Vector3.Dot(center - surface.Origin, surface.Normal)) > VisualOffset + Tolerance
                || Vector3.Dot(rotation * Vector3.forward, surface.Normal) < 0.999f)
                return PlacementFailure.OffSurface;
            Rectangle(surface, center, rotation, size, scratch);
            for (int i = 0; i < 4; i++)
                if (!ContainsInclusive(surface.Boundary, scratch[i])) return PlacementFailure.OutsideSurface;
            // Corners alone do not detect a concavity crossing the rectangle's edge.
            if (EdgesCross(surface.Boundary, scratch)) return PlacementFailure.OutsideSurface;
            for (int i = 0; i < surface.HoleCount; i++)
            {
                var hole = surface.Holes[i];
                if (Polygon.RingsOverlap(hole, scratch) || Touches(hole, scratch))
                    return PlacementFailure.Opening;
            }
            return PlacementFailure.None;
        }

        public static bool ContainsInclusive(IReadOnlyList<Vector3> ring, Vector3 point)
        {
            if (ring == null || ring.Count < 3) return false;
            if (Polygon.Contains(ring, point)) return true;
            for (int i = 0; i < ring.Count; i++)
                if (OnSegment(point, ring[i], ring[(i + 1) % ring.Count])) return true;
            return false;
        }

        private static bool OnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-12f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + t * ab)).sqrMagnitude <= Tolerance * Tolerance;
        }

        private static float Cross(Vector3 a, Vector3 b, Vector3 c) =>
            (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);

        private static bool EdgesCross(IReadOnlyList<Vector3> a, IReadOnlyList<Vector3> b)
        {
            for (int i = 0; i < a.Count; i++)
                for (int j = 0; j < b.Count; j++)
                {
                    var a0 = a[i]; var a1 = a[(i + 1) % a.Count];
                    var b0 = b[j]; var b1 = b[(j + 1) % b.Count];
                    if (Cross(a0, a1, b0) * Cross(a0, a1, b1) < -1e-12f
                        && Cross(b0, b1, a0) * Cross(b0, b1, a1) < -1e-12f) return true;
                }
            return false;
        }

        private static bool Touches(IReadOnlyList<Vector3> a, IReadOnlyList<Vector3> b)
        {
            for (int i = 0; i < a.Count; i++)
                for (int j = 0; j < b.Count; j++)
                    if (OnSegment(a[i], b[j], b[(j + 1) % b.Count])
                        || OnSegment(b[j], a[i], a[(i + 1) % a.Count])) return true;
            return false;
        }

        /// <summary>Clear gap along the surface U axis to its start/end boundary.
        /// A fixed side keeps the dimension from changing its reference during drag.</summary>
        public static float EdgeGap(MountSurface surface, Vector3 center, float width, bool fromEnd)
        {
            BoundsAlongU(surface, out float min, out float max);
            float u = surface.Project(center).x;
            return (fromEnd ? max - u : u - min) - width * 0.5f;
        }

        public static Vector3 WithEdgeGap(MountSurface surface, Vector3 center,
            float width, float gap, bool fromEnd)
        {
            BoundsAlongU(surface, out float min, out float max);
            float u = fromEnd ? max - gap - width * 0.5f : min + gap + width * 0.5f;
            return center + surface.U * (u - surface.Project(center).x);
        }

        private static void BoundsAlongU(MountSurface surface, out float min, out float max)
        {
            min = float.PositiveInfinity; max = float.NegativeInfinity;
            foreach (var p in surface.Boundary) { min = Mathf.Min(min, p.x); max = Mathf.Max(max, p.x); }
        }

        /// <summary>SAT for coplanar rectangles; opposite wall faces do not collide.
        /// Non-coplanar physical obstacles must be checked by the scene adapter.</summary>
        public static bool PlatesOverlap(Vector3 a, Quaternion ar, Vector2 sizeA,
            Vector3 b, Quaternion br, Vector2 sizeB, float clearance)
        {
            var n = ar * Vector3.forward;
            if (Vector3.Dot(n, br * Vector3.forward) < 0.999f
                || Mathf.Abs(Vector3.Dot(b - a, n)) > 0.01f) return false;
            var ax = ar * Vector3.right; var ay = ar * Vector3.up;
            var bx = br * Vector3.right; var by = br * Vector3.up;
            var d = b - a;
            return !Separated(ax, d, ax, ay, sizeA, bx, by, sizeB, clearance)
                && !Separated(ay, d, ax, ay, sizeA, bx, by, sizeB, clearance)
                && !Separated(bx, d, ax, ay, sizeA, bx, by, sizeB, clearance)
                && !Separated(by, d, ax, ay, sizeA, bx, by, sizeB, clearance);
        }

        private static bool Separated(Vector3 axis, Vector3 delta, Vector3 ax, Vector3 ay,
            Vector2 a, Vector3 bx, Vector3 by, Vector2 b, float gap) =>
            Mathf.Abs(Vector3.Dot(delta, axis)) >=
            (Mathf.Abs(Vector3.Dot(ax, axis)) * a.x + Mathf.Abs(Vector3.Dot(ay, axis)) * a.y
            + Mathf.Abs(Vector3.Dot(bx, axis)) * b.x + Mathf.Abs(Vector3.Dot(by, axis)) * b.y) * 0.5f + gap;
    }
}
