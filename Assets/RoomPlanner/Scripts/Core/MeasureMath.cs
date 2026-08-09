using System.Globalization;
using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>
    /// Pure, device-independent helpers shared by tools. Unit-testable (no OVR/TMP).
    /// </summary>
    public static class MeasureMath
    {
        /// <summary>Snap point b onto the nearest world axis relative to a (vertical / horizontal X / Z).</summary>
        public static Vector3 SnapToAxis(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
            if (ay >= ax && ay >= az) return a + new Vector3(0f, d.y, 0f); // vertical
            if (ax >= az) return a + new Vector3(d.x, 0f, 0f);             // horizontal X
            return a + new Vector3(0f, 0f, d.z);                           // horizontal Z
        }

        /// <summary>
        /// Snap the horizontal (XZ) direction from a to b to the nearest angular step (degrees),
        /// keeping b's height and the horizontal distance. Used for walls (e.g. 15° increments).
        /// </summary>
        public static Vector3 SnapToAngleXZ(Vector3 a, Vector3 b, float stepDegrees)
        {
            Vector3 d = b - a;
            var flat = new Vector2(d.x, d.z);
            float len = flat.magnitude;
            if (len < 1e-5f) return b;

            float step = Mathf.Max(1f, stepDegrees);
            float ang = Mathf.Atan2(flat.y, flat.x) * Mathf.Rad2Deg;
            float snapped = Mathf.Round(ang / step) * step * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Cos(snapped), 0f, Mathf.Sin(snapped));
            return new Vector3(a.x, b.y, a.z) + dir * len;
        }

        /// <summary>Round the XZ position to a grid step (meters), keeping Y. size &lt;= 0 → no change.</summary>
        public static Vector3 SnapToGridXZ(Vector3 p, float size)
        {
            if (size <= 1e-5f) return p;
            return new Vector3(Mathf.Round(p.x / size) * size, p.y, Mathf.Round(p.z / size) * size);
        }

        /// <summary>Intersect a ray with the horizontal plane y = planeY. False if parallel / behind.</summary>
        public static bool RayPlaneY(Ray ray, float planeY, out Vector3 hit)
        {
            hit = default;
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return false;
            float t = (planeY - ray.origin.y) / ray.direction.y;
            if (t <= 0f) return false;
            hit = ray.origin + ray.direction * t;
            return true;
        }

        /// <summary>Closest point on segment [a,b] to p (clamped to the segment).</summary>
        public static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-10f) return a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            return a + ab * t;
        }

        /// <summary>Format a distance (meters) as centimeters, e.g. "123 cm". Always cm.</summary>
        public static string FormatDistanceCm(float meters)
        {
            float cm = meters * 100f;
            return cm.ToString("0", CultureInfo.InvariantCulture) + " cm";
        }
    }
}
