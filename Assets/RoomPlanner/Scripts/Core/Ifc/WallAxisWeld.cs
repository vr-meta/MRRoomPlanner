using UnityEngine;

namespace RoomPlanner.Core.Ifc
{
    /// <summary>
    /// Issues #111/#119 shared root: Revit joins walls by their FACES, so an imported
    /// wall's axis often stops half a thickness short of the neighbour's axis. The
    /// wall graph then has dangling endpoints (Project1: 140 nodes over 120 segments),
    /// room rings never close (4 rooms found in a ~15-room house → whole-storey
    /// repaint), and corner joins leave 45° voids. This weld snaps every dangling
    /// axis endpoint onto the nearest other-wall axis within the joint reach —
    /// deterministic, before any graph is built.
    /// </summary>
    public static class WallAxisWeld
    {
        /// <summary>Extra reach beyond the two half-thicknesses.</summary>
        public const float Slack = 0.03f;

        /// <summary>Cap on the ALONG-AXIS slide. Sliding never rotates the wall, so it
        /// may run further than the lateral reach — a 45° kitchen join needs
        /// gap/sin(45°), which overflows the plain reach.</summary>
        public const float MaxSlide = 0.35f;
        /// <summary>Endpoints already this close to the target axis stay put.</summary>
        public const float AlreadyOn = 0.01f;
        /// <summary>Same-storey filter for the vertical gap between axes.</summary>
        public const float LevelTolerance = 0.5f;

        public static int Apply(ImportedBuilding b)
        {
            if (b == null || b.Walls == null) return 0;
            int welds = 0;
            var stubs = new System.Collections.Generic.List<ImportedWall>();
            foreach (var w in b.Walls)
            {
                if (w.Path.Count < 2) continue;
                welds += WeldEnd(b, w, 0, stubs);
                welds += WeldEnd(b, w, w.Path.Count - 1, stubs);
            }
            b.Walls.AddRange(stubs);   // never mutate the list mid-iteration

            // Phase 2: whatever still DANGLES after the rotation-free pass gets the
            // blunt corner weld (lateral pull to the nearest axis point / endpoint).
            // These are the hidden layered-wall butts — the visible straight joins
            // already closed in phase 1, so no bevels where the eye lives.
            int lateral = 0;
            foreach (var w in b.Walls)
            {
                if (w.Path.Count < 2) continue;
                if (!TouchesAnyAxis(b, w, 0)) lateral += WeldLateral(b, w, 0);
                if (!TouchesAnyAxis(b, w, w.Path.Count - 1))
                    lateral += WeldLateral(b, w, w.Path.Count - 1);
            }
            Debug.Log($"[Weld] slid={welds} stubs={stubs.Count} lateral={lateral}");
            return welds + stubs.Count + lateral;
        }

        /// <summary>The end already rests on another wall's axis (or endpoint) —
        /// phase 1 connected it, or the file drew it connected.</summary>
        private static bool TouchesAnyAxis(ImportedBuilding b, ImportedWall wall, int endIndex)
        {
            Vector3 end = wall.Path[endIndex];
            foreach (var other in b.Walls)
            {
                if (ReferenceEquals(other, wall) || other.Path.Count < 2) continue;
                Vector3 a = other.Path[0], c = other.Path[other.Path.Count - 1];
                if (Mathf.Abs(a.y - end.y) > LevelTolerance) continue;
                if (DistXZ(ClosestOnSegmentXZ(a, c, end), end) <= AlreadyOn) return true;
            }
            return false;
        }

        /// <summary>Round-2 corner weld, phase-2 only: pull the endpoint onto the
        /// nearest axis within the joint reach, snapping to the neighbour's endpoint
        /// when the contact is corner-like (the T-split refuses near-end projections).</summary>
        private static int WeldLateral(ImportedBuilding b, ImportedWall wall, int endIndex)
        {
            Vector3 end = wall.Path[endIndex];
            float bestDist = float.MaxValue;
            Vector3 bestPoint = end;
            foreach (var other in b.Walls)
            {
                if (ReferenceEquals(other, wall) || other.Path.Count < 2) continue;
                Vector3 a = other.Path[0], c = other.Path[other.Path.Count - 1];
                if (Mathf.Abs(a.y - end.y) > LevelTolerance) continue;
                float reach = wall.Thickness * 0.5f + other.Thickness * 0.5f + Slack;
                Vector3 p = ClosestOnSegmentXZ(a, c, end);
                if (DistXZ(p, a) < reach) p = a;
                else if (DistXZ(p, c) < reach) p = c;
                float d = DistXZ(p, end);
                if (d >= reach + 0.1f || d >= bestDist) continue;   // layered gaps run to ~22 cm
                bestDist = d;
                bestPoint = new Vector3(p.x, end.y, p.z);
            }
            if (bestDist == float.MaxValue || bestDist <= AlreadyOn) return 0;
            Vector3 far = wall.Path[endIndex == 0 ? wall.Path.Count - 1 : 0];
            if (DistXZ(bestPoint, far) < 0.08f) return 0;
            wall.Path[endIndex] = bestPoint;
            return 1;
        }

        private static int WeldEnd(ImportedBuilding b, ImportedWall wall, int endIndex,
            System.Collections.Generic.List<ImportedWall> stubs)
        {
            Vector3 end = wall.Path[endIndex];
            Vector3 far = wall.Path[endIndex == 0 ? wall.Path.Count - 1 : 0];
            Vector3 ownDir = new Vector3(end.x - far.x, 0f, end.z - far.z);
            if (ownDir.sqrMagnitude < 1e-8f) return 0;
            ownDir.Normalize();

            float bestMove = float.MaxValue;
            Vector3 bestPoint = end;

            foreach (var other in b.Walls)
            {
                if (ReferenceEquals(other, wall) || other.Path.Count < 2) continue;
                Vector3 a = other.Path[0], c = other.Path[other.Path.Count - 1];
                if (Mathf.Abs(a.y - end.y) > LevelTolerance) continue;

                float reach = wall.Thickness * 0.5f + other.Thickness * 0.5f + Slack;
                // The FIRST weld pulled the endpoint sideways to the closest axis point,
                // slightly ROTATING the wall — straight kitchen joins came back bevelled
                // (feedback round 3). The endpoint may only slide ALONG its own axis:
                // intersect the two axis lines in plan and take that point when the
                // slide is within the joint reach and it lands on (or reasonably near
                // the end of) the neighbour's segment.
                Vector3 od = new Vector3(c.x - a.x, 0f, c.z - a.z);
                float olen = od.magnitude;
                if (olen < 1e-4f) continue;
                od /= olen;
                float denom = ownDir.x * od.z - ownDir.z * od.x;
                if (Mathf.Abs(denom) < 0.17f) continue;   // near-parallel — not a joint (~10°)

                // solve end + t*ownDir = a + u*od (XZ)
                float ax = a.x - end.x, az = a.z - end.z;
                float t = (ax * od.z - az * od.x) / denom;
                float u = (ax * ownDir.z - az * ownDir.x) / denom;
                if (Mathf.Abs(t) >= MaxSlide) continue;           // slide beyond any joint
                if (u < -reach || u > olen + reach) continue;     // off the neighbour's body
                float move = Mathf.Abs(t);
                if (move >= bestMove) continue;
                bestMove = move;
                bestPoint = new Vector3(end.x + ownDir.x * t, end.y, end.z + ownDir.z * t);
            }

            if (bestMove != float.MaxValue && bestMove > AlreadyOn)
            {
                // never weld a wall into a point: a short pier must keep its body,
                // or its ring opens where it vanished
                if (DistXZ(bestPoint, far) < 0.08f) return 0;
                wall.Path[endIndex] = bestPoint;
                return 1;
            }
            if (bestMove != float.MaxValue) return 0;   // already touching

            // No slide target: parallel LAYERED walls (bearing + lining, 7-22 cm apart
            // in Project1) still break the rings. Moving the endpoint sideways ROTATES
            // the wall (the bevelled kitchen joins of round 2) — so instead a tiny
            // CONNECTOR STUB bridges the gap: the original axes stay exact, the ring
            // closes through the stub, and the open slot between the layers gets a
            // real end cap.
            float bestGap = float.MaxValue;
            Vector3 stubTo = end;
            ImportedWall target = null;
            foreach (var other in b.Walls)
            {
                if (ReferenceEquals(other, wall) || other.Path.Count < 2) continue;
                Vector3 a = other.Path[0], c = other.Path[other.Path.Count - 1];
                if (Mathf.Abs(a.y - end.y) > LevelTolerance) continue;
                float reach = wall.Thickness * 0.5f + other.Thickness * 0.5f + Slack;
                Vector3 p = ClosestOnSegmentXZ(a, c, end);
                float d = DistXZ(p, end);
                if (d >= reach + 0.15f || d >= bestGap) continue;   // layered-wall scale
                bestGap = d;
                stubTo = new Vector3(p.x, end.y, p.z);
                target = other;
            }
            if (target == null || bestGap <= AlreadyOn || bestGap == float.MaxValue) return 0;
            stubs.Add(new ImportedWall
            {
                Path = new System.Collections.Generic.List<Vector3> { end, stubTo },
                Thickness = Mathf.Min(wall.Thickness, target.Thickness),
                Height = wall.Height,
                BaseHeight = wall.BaseHeight,
            });
            return 0;   // the stub itself is counted by the caller
        }

        private static Vector3 ClosestOnSegmentXZ(Vector3 a, Vector3 b, Vector3 p)
        {
            a.y = 0f; b.y = 0f;
            var p2 = new Vector3(p.x, 0f, p.z);
            Vector3 ab = b - a;
            if (ab.sqrMagnitude < 1e-10f) return a;
            float t = Mathf.Clamp01(Vector3.Dot(p2 - a, ab) / ab.sqrMagnitude);
            return a + ab * t;
        }

        private static float DistXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
