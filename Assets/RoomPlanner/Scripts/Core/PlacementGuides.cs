using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>One dimension guide: the placed point's offset from a wall axis in plan.</summary>
    public struct WallGuide
    {
        public Vector3 Closest;    // closest point on the wall segment, at the query height
        public Vector3 Normal;     // horizontal unit vector wall -> point
        public float Distance;     // meters, in the XZ plane
        public bool Valid;
    }

    /// <summary>
    /// Placement dimension guides (issue #113, headset feedback 2026-08-15): while an
    /// element is being placed, show its distances to the nearest wall — and near a
    /// corner to the TWO nearest non-parallel walls — and optionally quantize the
    /// position so those distances land on round steps. Pure XZ math over wall axis
    /// segments; no scene dependencies.
    /// </summary>
    public static class PlacementGuides
    {
        /// <summary>Walls further than this are not worth a dimension line.</summary>
        public const float MaxGuideDistance = 3f;

        /// <summary>The second guide must differ in direction by at least this angle —
        /// two parallel walls give one meaningful offset, not two.</summary>
        public const float MinPairAngleDeg = 30f;

        /// <summary>Fills up to two guides for a point over the wall axes; returns the
        /// count. Guide 0 is the nearest wall; guide 1 the nearest one at least
        /// MinPairAngleDeg off guide 0's direction (the corner pair). Degenerate and
        /// far-away segments are skipped; distances are measured in plan (XZ).</summary>
        public static int FindGuides(Vector3 point, IReadOnlyList<Vector3> axisPairs,
            WallGuide[] result)
        {
            if (result == null || result.Length < 2) return 0;
            result[0] = default;
            result[1] = default;
            if (axisPairs == null) return 0;

            var p = new Vector3(point.x, 0f, point.z);
            int count = 0;
            Vector3 firstDir = default;

            for (int pass = 0; pass < 2; pass++)
            {
                float best = MaxGuideDistance;
                WallGuide bestGuide = default;
                Vector3 bestDir = default;
                for (int i = 0; i + 1 < axisPairs.Count; i += 2)
                {
                    Vector3 a = axisPairs[i], b = axisPairs[i + 1];
                    a.y = 0f; b.y = 0f;
                    Vector3 ab = b - a;
                    if (ab.sqrMagnitude < 1e-8f) continue;
                    if (pass == 1)
                    {
                        float ang = Vector3.Angle(ab, firstDir);
                        if (ang > 90f) ang = 180f - ang;
                        if (ang < MinPairAngleDeg) continue;   // parallel to guide 0
                    }
                    float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                    Vector3 c = a + ab * t;
                    Vector3 off = p - c;
                    float d = off.magnitude;
                    if (d >= best || d < 1e-4f) continue;      // on the axis = no direction
                    best = d;
                    bestDir = ab;
                    bestGuide = new WallGuide
                    {
                        Closest = new Vector3(c.x, point.y, c.z),
                        Normal = off / d,
                        Distance = d,
                        Valid = true,
                    };
                }
                if (!bestGuide.Valid) break;
                result[count++] = bestGuide;
                firstDir = bestDir;
            }
            return count;
        }

        /// <summary>Drops guides whose offset direction is roughly parallel to
        /// <paramref name="excludeNormal"/> (in plan) and compacts the array — a
        /// wall-mounted element must not be dimensioned against its OWN wall, or
        /// quantizing would drag it off the face. Returns the new count.</summary>
        public static int FilterByNormal(WallGuide[] guides, int count, Vector3 excludeNormal,
            float maxAbsDot = 0.866f)
        {
            if (guides == null) return 0;
            var ex = new Vector3(excludeNormal.x, 0f, excludeNormal.z);
            if (ex.sqrMagnitude < 1e-8f) return count;
            ex.Normalize();
            int kept = 0;
            for (int i = 0; i < count && i < guides.Length; i++)
            {
                if (!guides[i].Valid) continue;
                if (Mathf.Abs(Vector3.Dot(guides[i].Normal, ex)) > maxAbsDot) continue;
                guides[kept++] = guides[i];
            }
            for (int i = kept; i < guides.Length; i++) guides[i] = default;
            return kept;
        }

        /// <summary>Moves the point along each guide's normal so its wall distances
        /// become multiples of <paramref name="step"/> (nearest multiple; a distance
        /// rounding to zero clamps to one step — placement never lands INSIDE a wall).
        /// With two roughly perpendicular guides both distances land on the grid.</summary>
        public static Vector3 Quantize(Vector3 point, WallGuide[] guides, int count, float step)
        {
            if (guides == null || step <= 0f) return point;
            for (int i = 0; i < count && i < guides.Length; i++)
            {
                if (!guides[i].Valid) continue;
                float rounded = Mathf.Max(step, Mathf.Round(guides[i].Distance / step) * step);
                point += guides[i].Normal * (rounded - guides[i].Distance);
            }
            return point;
        }
    }
}
