using UnityEngine;

namespace RoomPlanner.Core.Ifc
{
    /// <summary>
    /// Placing an imported building at a user-marked point (#60): pure transforms over
    /// ImportedBuilding data, applied BEFORE BuildScene. The anchor is the bottom-center
    /// of the footprint (XZ bounds of walls/slabs, Y = lowest base), so MoveTo stands
    /// the lowest storey exactly on the marked surface. Openings stay untouched — they
    /// are wall-relative fractions.
    /// </summary>
    public static class ImportPlacement
    {
        /// <summary>Bottom-center of the building's footprint; Vector3.zero for an
        /// empty building.</summary>
        public static Vector3 Anchor(ImportedBuilding b)
        {
            bool any = false;
            float minX = 0f, maxX = 0f, minZ = 0f, maxZ = 0f, minY = 0f;

            void TakeXZ(Vector3 p)
            {
                if (!any)
                {
                    minX = maxX = p.x;
                    minZ = maxZ = p.z;
                    minY = float.MaxValue;
                    any = true;
                    return;
                }
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }
            void TakeY(float y) => minY = Mathf.Min(minY, y);

            foreach (var w in b.Walls)
            {
                foreach (var p in w.Path) TakeXZ(p);
                if (w.Path.Count > 0) TakeY(w.BaseHeight);
            }
            foreach (var s in b.Slabs)
            {
                foreach (var p in s.Outline) TakeXZ(p);
                if (s.Outline.Count > 0) TakeY(s.Level - s.Thickness);
            }
            foreach (var st in b.Stairs) { TakeXZ(st.Base); TakeY(st.Base.y); }
            foreach (var m in b.Plumbing) { TakeXZ(m.Origin); TakeY(m.Origin.y); }

            if (!any) return Vector3.zero;
            return new Vector3((minX + maxX) * 0.5f, minY, (minZ + maxZ) * 0.5f);
        }

        /// <summary>Shift every world-space datum by delta. Local data (MEP vertices,
        /// opening fractions/sills, directions) is deliberately untouched.</summary>
        public static void Translate(ImportedBuilding b, Vector3 delta)
        {
            foreach (var s in b.Storeys) s.Elevation += delta.y;
            foreach (var w in b.Walls)
            {
                for (int i = 0; i < w.Path.Count; i++) w.Path[i] += delta;
                w.BaseHeight += delta.y;
            }
            foreach (var s in b.Slabs)
            {
                for (int i = 0; i < s.Outline.Count; i++) s.Outline[i] += delta;
                foreach (var hole in s.Holes)
                    for (int i = 0; i < hole.Count; i++) hole[i] += delta;
                s.Level += delta.y;
            }
            foreach (var st in b.Stairs) st.Base += delta;
            foreach (var m in b.Plumbing) m.Origin += delta;
            foreach (var o in b.Outlets) o.Position += delta;
        }

        /// <summary>Stand the building's bottom-center on target.</summary>
        public static void MoveTo(ImportedBuilding b, Vector3 target) =>
            Translate(b, target - Anchor(b));
    }
}
