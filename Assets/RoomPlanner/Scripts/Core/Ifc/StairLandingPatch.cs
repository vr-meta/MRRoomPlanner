using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core.Ifc
{
    /// <summary>
    /// Issue #116: in the drawings the stairwell hole can outrun the flight that
    /// serves it — the top edge of the terrace flight in Project1 ends INSIDE the
    /// hole, 14 cm short of the slab edge, leaving a gap you cannot walk over. For
    /// every flight whose top edge lands inside a hole of its arrival slab, this adds
    /// a LANDING PATCH: a flight-wide strip of floor at the slab level, from the top
    /// edge of the flight to the hole boundary ahead (stepping up onto the patch is
    /// the flight's natural last riser). Additive only — hole rings stay untouched.
    /// </summary>
    public static class StairLandingPatch
    {
        public const float LevelTolerance = 0.5f;    // arrival-slab search window
        public const float Overlap = 0.05f;          // tuck the patch under the slab edge
        public const float MaxTravel = 3f;           // sanity cap for the strip length

        public static void Apply(ImportedBuilding b)
        {
            if (b == null) return;
            var patches = new List<ImportedSlab>();
            foreach (var s in b.Stairs)
            {
                if (s.Risers <= 0 || s.RiserHeight <= 0f || s.TreadDepth <= 0f) continue;
                float topY = s.Base.y + s.Risers * s.RiserHeight;

                Vector3 dir = Quaternion.Euler(0f, s.YawDeg, 0f) * Vector3.forward;
                Vector3 topEdge = s.Base + dir * (s.Risers * s.TreadDepth);

                // several slabs can share the arrival level (the terrace does) — the
                // one whose HOLE contains the top edge is the stairwell slab, not
                // whichever happened to come first in the file
                ImportedSlab arrival = null;
                List<Vector3> throughHole = null;
                float bestGap = LevelTolerance;
                foreach (var sl in b.Slabs)
                {
                    float gap = Mathf.Abs(sl.Level - topY);
                    if (gap >= bestGap) continue;
                    foreach (var hole in sl.Holes)
                    {
                        if (hole == null || hole.Count < 3) continue;
                        if (!PointInRingXZ(hole, topEdge)) continue;
                        bestGap = gap; arrival = sl; throughHole = hole;
                        break;
                    }
                }
                if (arrival == null) continue;

                float travel = RayToRingXZ(throughHole, topEdge, dir);
                if (travel <= 0f || travel > MaxTravel) continue;
                patches.Add(MakePatch(topEdge, dir, s.Width, travel + Overlap, arrival));
            }
            b.Slabs.AddRange(patches);
        }

        private static ImportedSlab MakePatch(Vector3 topEdge, Vector3 dir, float width,
            float length, ImportedSlab arrival)
        {
            Vector3 right = new Vector3(dir.z, 0f, -dir.x);
            Vector3 a = topEdge - right * (width * 0.5f);
            Vector3 bq = topEdge + right * (width * 0.5f);
            float y = arrival.Level;
            var patch = new ImportedSlab
            {
                Level = arrival.Level,
                Thickness = arrival.Thickness,
                Outline = new List<Vector3>
                {
                    new(a.x, y, a.z),
                    new(bq.x, y, bq.z),
                    new(bq.x + dir.x * length, y, bq.z + dir.z * length),
                    new(a.x + dir.x * length, y, a.z + dir.z * length),
                },
            };
            return patch;
        }

        /// <summary>Even-odd point-in-ring test in plan.</summary>
        public static bool PointInRingXZ(List<Vector3> ring, Vector3 p)
        {
            bool inside = false;
            int n = ring.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector3 a = ring[i], c = ring[j];
                if (a.z > p.z != c.z > p.z
                    && p.x < (c.x - a.x) * (p.z - a.z) / (c.z - a.z) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>Nearest positive intersection of the ray (origin, dir) with the
        /// ring's edges in plan; -1 when the ray never leaves through the ring.</summary>
        public static float RayToRingXZ(List<Vector3> ring, Vector3 origin, Vector3 dir)
        {
            float best = -1f;
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = ring[i], c = ring[(i + 1) % n];
                Vector3 e = c - a;
                float denom = dir.x * e.z - dir.z * e.x;
                if (Mathf.Abs(denom) < 1e-8f) continue;
                float t = ((a.x - origin.x) * e.z - (a.z - origin.z) * e.x) / denom;
                float u = ((a.x - origin.x) * dir.z - (a.z - origin.z) * dir.x) / denom;
                if (t <= 1e-4f || u < 0f || u > 1f) continue;
                if (best < 0f || t < best) best = t;
            }
            return best;
        }
    }
}
