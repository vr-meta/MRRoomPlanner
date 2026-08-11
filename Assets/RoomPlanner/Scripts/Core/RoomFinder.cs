using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Walls
{
    /// <summary>One detected room: the closed centreline ring of its walls
    /// (design/24, issue #52). Pure data — painting/BOM consume it.</summary>
    public sealed class RoomRing
    {
        public readonly List<Vector3> Polygon = new();
        public float Area;    // m², in the XZ plane
        public float Level;   // node height of the ring's storey

        /// <summary>Even-odd containment in XZ (spur spikes cancel themselves).</summary>
        public bool ContainsXZ(Vector3 p)
        {
            bool inside = false;
            for (int i = 0, j = Polygon.Count - 1; i < Polygon.Count; j = i++)
            {
                Vector3 a = Polygon[i], b = Polygon[j];
                if (a.z > p.z != b.z > p.z
                    && p.x < (b.x - a.x) * (p.z - a.z) / (b.z - a.z) + a.x)
                    inside = !inside;
            }
            return inside;
        }
    }

    /// <summary>
    /// Rooms from the wall graph (design/24, issue #52): every BOUNDED face of the
    /// planar graph is a room. Suppressed (deleted) walls merge their rooms, dead-end
    /// spurs contribute nothing, storeys are separated by node height. The T-heal
    /// (SplitTJunctions) must run first on imported graphs — a partition ending on a
    /// node that merely touches a long wall is topologically disconnected until split.
    /// </summary>
    public static class RoomFinder
    {
        /// <summary>Rings smaller than this are numeric junk, not closets.</summary>
        public const float MinArea = 0.3f;

        /// <summary>Nodes within this height band belong to one storey.</summary>
        public const float LevelBand = 0.5f;

        /// <summary>Extra reach beyond the wall's half thickness when deciding a touch.</summary>
        public const float TouchMargin = 0.02f;

        /// <summary>Directions closer than ~30° count as parallel — a nearby double
        /// wall must never be welded into its neighbour.</summary>
        private const float ParallelDot = 0.866f;   // cos 30°

        /// <summary>
        /// T-heal: a partition typically ends on the FACE of the wall it runs into —
        /// its endpoint sits half a thickness off the centreline (headset scene:
        /// 88 touches at 5–10 cm, zero within 3 cm). A node whose walls CROSS the
        /// segment's direction and whose position lies inside the segment's body
        /// (half thickness + margin) gets snapped onto the centreline and the segment
        /// splits there; opening fractions on every wall at the node are rescaled so
        /// doors keep their world positions. Idempotent; returns the split count.
        /// </summary>
        public static int SplitTJunctions(WallGraph g, float extraMargin = TouchMargin)
        {
            if (g == null) return 0;
            int total = 0;
            bool again = true;
            int guard = 0;
            while (again && guard++ < 64)
            {
                again = false;
                var nodes = g.Nodes;
                for (int ni = 0; ni < nodes.Count && !again; ni++)
                {
                    var n = nodes[ni];
                    if (n.Degree == 0) continue;   // an orphan must not cut walls
                    var segs = g.Segments;
                    for (int si = 0; si < segs.Count; si++)
                    {
                        var s = segs[si];
                        if (s == null || s.A == null || s.B == null || s.Has(n)) continue;
                        if (Mathf.Abs(s.A.Position.y - n.Position.y) > LevelBand) continue;

                        Vector3 a = s.A.Position, b = s.B.Position;
                        Vector3 dir = b - a; dir.y = 0f;
                        float len2 = dir.sqrMagnitude;
                        if (len2 < 1e-8f) continue;
                        Vector3 toN = n.Position - a; toN.y = 0f;
                        float t = Vector3.Dot(toN, dir) / len2;
                        Vector3 onSeg = a + dir * Mathf.Clamp01(t);
                        Vector3 off = n.Position - onSeg; off.y = 0f;

                        float reach = s.Thickness * 0.5f + extraMargin;
                        if (off.sqrMagnitude > reach * reach) continue;
                        // the projection must land mid-span, clear of both ends
                        float len = Mathf.Sqrt(len2);
                        if (t * len < reach || (1f - t) * len < reach) continue;
                        if (!CrossesDirection(n, dir)) continue;   // parallel double wall

                        SnapNodeOntoLine(g, n, new Vector3(onSeg.x, n.Position.y, onSeg.z));
                        if (!g.SplitSegmentWith(s, n, extraMargin + 0.001f)) continue;
                        total++;
                        again = true;   // the segment list changed — restart the scan
                        break;
                    }
                }
            }
            return total;
        }

        /// <summary>At least one wall at the node crosses `dir` by ≥ 30° — the
        /// signature of a real T, not of a parallel neighbour.</summary>
        private static bool CrossesDirection(WallNode n, Vector3 dir)
        {
            Vector3 d = dir.normalized;
            foreach (var seg in n.Segments)
            {
                Vector3 own = seg.B.Position - seg.A.Position; own.y = 0f;
                if (own.sqrMagnitude < 1e-8f) continue;
                if (Mathf.Abs(Vector3.Dot(own.normalized, d)) < ParallelDot) return true;
            }
            return false;
        }

        /// <summary>Move the junction onto the centreline, rescaling opening fractions
        /// of every attached wall so doors/windows keep their WORLD positions.</summary>
        private static void SnapNodeOntoLine(WallGraph g, WallNode n, Vector3 target)
        {
            Vector3 flat = target - n.Position; flat.y = 0f;
            if (flat.sqrMagnitude < 1e-10f) return;

            foreach (var seg in n.Segments)
            {
                if (seg.Openings.Count == 0) continue;
                float oldLen = seg.Length;
                if (oldLen < 1e-4f) continue;
                Vector3 otherPos = seg.Other(n).Position;
                float newLen = Vector3.Distance(
                    new Vector3(otherPos.x, 0f, otherPos.z), new Vector3(target.x, 0f, target.z));
                if (newLen < 1e-4f) continue;
                bool movedIsB = seg.B == n;
                foreach (var o in seg.Openings)
                {
                    // anchor at the UNMOVED end: distance from it stays put
                    float fromA = o.AlongFraction * oldLen;
                    o.AlongFraction = movedIsB
                        ? Mathf.Clamp01(fromA / newLen)
                        : Mathf.Clamp01(1f - (oldLen - fromA) / newLen);
                }
            }
            g.MoveNode(n, target);
        }

        /// <summary>All rooms in the graph, every storey. Call on demand (a paint
        /// click, an import) — this walks the whole graph and allocates.</summary>
        public static List<RoomRing> FindRooms(WallGraph g)
        {
            var rooms = new List<RoomRing>();
            if (g == null) return rooms;

            // active segments, then their storey levels (greedy clustering)
            var active = new List<WallSegment>();
            foreach (var s in g.Segments)
                if (s != null && !s.Suppressed && s.A != null && s.B != null)
                    active.Add(s);
            var levels = new List<float>();
            foreach (var s in active)
            {
                AddLevel(levels, s.A.Position.y);
                AddLevel(levels, s.B.Position.y);
            }

            var lvlSegs = new List<WallSegment>();
            foreach (float level in levels)
            {
                lvlSegs.Clear();
                foreach (var s in active)
                    if (Mathf.Abs(s.A.Position.y - level) <= LevelBand
                        && Mathf.Abs(s.B.Position.y - level) <= LevelBand)
                        lvlSegs.Add(s);
                if (lvlSegs.Count >= 3) WalkFaces(lvlSegs, level, rooms);
            }
            return rooms;
        }

        private static void AddLevel(List<float> levels, float y)
        {
            for (int i = 0; i < levels.Count; i++)
                if (Mathf.Abs(levels[i] - y) <= LevelBand) return;
            levels.Add(y);
        }

        // ---- planar face walk ----

        private struct DirEdge
        {
            public int SegIndex;
            public bool Forward;
        }

        private static void WalkFaces(List<WallSegment> segs, float level, List<RoomRing> rooms)
        {
            // per-node outgoing edges sorted by heading angle
            var outgoing = new Dictionary<WallNode, List<(float angle, DirEdge e)>>();
            for (int i = 0; i < segs.Count; i++)
            {
                Register(outgoing, segs[i].A, Heading(segs[i].A, segs[i].B),
                    new DirEdge { SegIndex = i, Forward = true });
                Register(outgoing, segs[i].B, Heading(segs[i].B, segs[i].A),
                    new DirEdge { SegIndex = i, Forward = false });
            }
            foreach (var list in outgoing.Values)
                list.Sort((x, y) => x.angle.CompareTo(y.angle));

            // The traversal rule (smallest CCW turn from the reversed incoming heading)
            // walks every BOUNDED face with a positive shoelace sign and the unbounded
            // face negative — |area| cannot discriminate (a lone room and the outside
            // have the same magnitude), the SIGN can.
            var visited = new bool[segs.Count, 2];
            for (int i = 0; i < segs.Count; i++)
                for (int dir = 0; dir < 2; dir++)
                {
                    if (visited[i, dir]) continue;
                    var start = new DirEdge { SegIndex = i, Forward = dir == 0 };
                    var ring = new RoomRing { Level = level };
                    var e = start;
                    int guard = segs.Count * 2 + 4;
                    while (guard-- > 0)
                    {
                        visited[e.SegIndex, e.Forward ? 0 : 1] = true;
                        ring.Polygon.Add(Tail(segs, e).Position);
                        e = Next(segs, outgoing, e);
                        if (e.SegIndex == start.SegIndex && e.Forward == start.Forward) break;
                    }
                    PruneSpikes(ring.Polygon);           // spur walk-backs leave A,B,A spikes
                    float signed = ShoelaceXZ(ring.Polygon);
                    if (signed <= 0f) continue;          // the unbounded face
                    if (signed < MinArea) continue;      // numeric junk / spur loops
                    if (ring.Polygon.Count < 3) continue;
                    ring.Area = signed;
                    rooms.Add(ring);
                }
        }

        private static void Register(Dictionary<WallNode, List<(float, DirEdge)>> outgoing,
            WallNode n, float angle, DirEdge e)
        {
            if (!outgoing.TryGetValue(n, out var list))
            {
                list = new List<(float, DirEdge)>();
                outgoing[n] = list;
            }
            list.Add((angle, e));
        }

        private static WallNode Tail(List<WallSegment> segs, DirEdge e) =>
            e.Forward ? segs[e.SegIndex].A : segs[e.SegIndex].B;

        private static WallNode Head(List<WallSegment> segs, DirEdge e) =>
            e.Forward ? segs[e.SegIndex].B : segs[e.SegIndex].A;

        private static float Heading(WallNode from, WallNode to)
        {
            Vector3 d = to.Position - from.Position;
            return Mathf.Atan2(d.z, d.x);
        }

        /// <summary>The next edge of the face: the LARGEST CCW turn from the reversed
        /// incoming heading (= first edge clockwise from the back-edge) keeps the face
        /// interior on the left, so every bounded face traces CCW (positive shoelace).
        /// The exact reverse edge is the last resort — that is the spur walk-back.</summary>
        private static DirEdge Next(List<WallSegment> segs,
            Dictionary<WallNode, List<(float angle, DirEdge e)>> outgoing, DirEdge e)
        {
            var h = Head(segs, e);
            float rev = Heading(h, Tail(segs, e));
            var list = outgoing[h];
            int best = 0;
            float bestDelta = -1f;
            for (int i = 0; i < list.Count; i++)
            {
                float d = Mathf.Repeat(list[i].angle - rev, 2f * Mathf.PI);
                bool isReverse = list[i].e.SegIndex == e.SegIndex && list[i].e.Forward != e.Forward;
                if (isReverse || d < 1e-5f) d = 0f;   // walk back only at dead ends
                if (d > bestDelta) { bestDelta = d; best = i; }
            }
            return list[best].e;
        }

        /// <summary>Remove zero-area spikes (dead-end spur walk-backs: …A,B,A…) and
        /// consecutive duplicates. Spikes contribute nothing to the area but break
        /// IsSimple/inset — a ring with a spur inside it would refuse to carve.</summary>
        private static void PruneSpikes(List<Vector3> poly)
        {
            bool changed = true;
            while (changed && poly.Count >= 3)
            {
                changed = false;
                for (int i = poly.Count - 1; i >= 0 && poly.Count >= 3; i--)
                {
                    int prev = (i - 1 + poly.Count) % poly.Count;
                    int next = (i + 1) % poly.Count;
                    Vector3 a = poly[prev], b = poly[i], c = poly[next];
                    if ((a - c).sqrMagnitude < 1e-6f)        // spike tip: A,B,A
                    {
                        poly.RemoveAt(i);
                        changed = true;
                    }
                    else if ((a - b).sqrMagnitude < 1e-6f)   // consecutive duplicate
                    {
                        poly.RemoveAt(i);
                        changed = true;
                    }
                }
            }
        }

        private static float ShoelaceXZ(List<Vector3> poly)
        {
            float sum = 0f;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                sum += poly[j].x * poly[i].z - poly[i].x * poly[j].z;
            return sum * 0.5f;
        }

        /// <summary>Miter-inset the polygon inward by d metres (design/24: room rings
        /// shrink ~2 cm so adjacent holes and slab borders never touch). Works for both
        /// windings — "inward" is decided by the polygon's own orientation.</summary>
        public static List<Vector3> Inset(List<Vector3> poly, float d)
        {
            var result = new List<Vector3>(poly.Count);
            if (poly.Count < 3) { result.AddRange(poly); return result; }
            float orientation = Mathf.Sign(ShoelaceXZ(poly));
            if (orientation == 0f) orientation = 1f;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector3 prev = poly[(i - 1 + poly.Count) % poly.Count];
                Vector3 cur = poly[i];
                Vector3 next = poly[(i + 1) % poly.Count];
                Vector3 d0 = cur - prev; d0.y = 0f;
                Vector3 d1 = next - cur; d1.y = 0f;
                if (d0.sqrMagnitude < 1e-10f || d1.sqrMagnitude < 1e-10f) { result.Add(cur); continue; }
                d0.Normalize(); d1.Normalize();
                // inward normals for a CCW-in-XZ polygon point... decided by orientation
                Vector3 n0 = new Vector3(-d0.z, 0f, d0.x) * orientation;
                Vector3 n1 = new Vector3(-d1.z, 0f, d1.x) * orientation;
                Vector3 m = n0 + n1;
                float denom = 1f + Vector3.Dot(n0, n1);
                Vector3 shift = denom > 1e-3f ? m / denom : (m.sqrMagnitude > 1e-8f ? m.normalized : n0);
                if (shift.sqrMagnitude > 16f) shift = shift.normalized * 4f;   // miter cap
                result.Add(cur + shift * d);
            }
            return result;
        }
    }
}
