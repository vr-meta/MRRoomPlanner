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
        /// <summary>Endpoints already this close to the target axis stay put.</summary>
        public const float AlreadyOn = 0.01f;
        /// <summary>Same-storey filter for the vertical gap between axes.</summary>
        public const float LevelTolerance = 0.5f;

        public static int Apply(ImportedBuilding b)
        {
            if (b == null || b.Walls == null) return 0;
            int welds = 0;
            foreach (var w in b.Walls)
            {
                if (w.Path.Count < 2) continue;
                welds += WeldEnd(b, w, 0);
                welds += WeldEnd(b, w, w.Path.Count - 1);
            }
            return welds;
        }

        private static int WeldEnd(ImportedBuilding b, ImportedWall wall, int endIndex)
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
                // a contact near the neighbour's END is a CORNER, not a T: snap to the
                // endpoint itself so the two graph nodes merge into one — the T-split
                // deliberately refuses projections that close to a segment end
                if (DistXZ(p, a) < reach) p = a;
                else if (DistXZ(p, c) < reach) p = c;
                float d = DistXZ(p, end);
                if (d >= reach || d >= bestDist) continue;
                bestDist = d;
                bestPoint = new Vector3(p.x, end.y, p.z);
            }

            if (bestDist == float.MaxValue || bestDist <= AlreadyOn) return 0;
            // never weld a wall into a point: a short pier (shorter than the joint
            // reach) must keep its body, or its ring opens where it vanished
            Vector3 far = wall.Path[endIndex == 0 ? wall.Path.Count - 1 : 0];
            if (DistXZ(bestPoint, far) < 0.08f) return 0;
            wall.Path[endIndex] = bestPoint;
            return 1;
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
