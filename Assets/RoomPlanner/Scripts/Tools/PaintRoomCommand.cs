using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Floors;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// Paint ONE room of a storey-wide slab (design/24, issue #52): the room's inset
    /// ring becomes a hole in the donor slab, a nested sub-slab with the same outline
    /// takes the chosen finish — all as a single undo entry. The sub-slab is created
    /// once and hidden/shown across undo/redo (the CreateCommand mirror pattern), so
    /// its identity — and any further paint on it — survives the round-trip.
    /// </summary>
    public class PaintRoomCommand : ICommand, ISelectableCommand
    {
        private readonly FloorController _floors;
        private readonly Floor _donor;
        private readonly Selectable _donorSel;
        private readonly List<Vector3> _ring;
        private readonly SurfaceFinish _finish;
        private readonly Texture2D _tex, _normal;
        private Floor _sub;
        private Selectable _subSel;

        public PaintRoomCommand(FloorController floors, Floor donor, Selectable donorSel,
            List<Vector3> ring, SurfaceFinish finish, Texture2D tex, Texture2D normal)
        {
            _floors = floors;
            _donor = donor;
            _donorSel = donorSel;
            _ring = ring;
            _finish = finish;
            _tex = tex;
            _normal = normal;
        }

        /// <summary>Purged together with the donor slab.</summary>
        public ISelectable Target => _donorSel;

        public string Name => "Paint room";

        private bool Alive => _donorSel != null && _donorSel.IsAlive && _donor != null;

        public void Do()
        {
            if (!Alive) return;
            if (!_donor.AddHole(_ring)) return;   // validated by the caller; races just no-op
            if (_sub == null)
            {
                _sub = _floors != null
                    ? _floors.CreateImported(_ring, _donor.Level, _donor.Thickness) : null;
                if (_sub == null) { RemoveRingHole(); return; }
                _subSel = _sub.GetComponent<Selectable>();
            }
            else if (_subSel != null && _subSel.IsAlive)
            {
                _subSel.SetHidden(false);
            }
            if (_subSel != null && _subSel.IsAlive) _subSel.SetFinish(_finish, _tex, _normal);
        }

        public void Undo()
        {
            if (!Alive) return;
            RemoveRingHole();
            if (_subSel != null && _subSel.IsAlive) _subSel.SetHidden(true);
        }

        /// <summary>Find our ring among the donor's holes (AddHole cleans the list, so
        /// match by centroid + area, not by instance).</summary>
        private void RemoveRingHole()
        {
            float area = Mathf.Abs(SignedAreaXZ(_ring));
            Vector3 c = CentroidXZ(_ring);
            var holes = _donor.Holes;
            for (int i = holes.Count - 1; i >= 0; i--)
            {
                var h = holes[i];
                if (Mathf.Abs(Mathf.Abs(SignedAreaXZ(h)) - area) > area * 0.01f + 1e-4f) continue;
                Vector3 hc = CentroidXZ(h);
                if ((hc - c).sqrMagnitude > 0.01f * 0.01f) continue;
                _donor.RemoveHole(i);
                return;
            }
        }

        private static float SignedAreaXZ(IReadOnlyList<Vector3> poly)
        {
            float sum = 0f;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                sum += poly[j].x * poly[i].z - poly[i].x * poly[j].z;
            return sum * 0.5f;
        }

        private static Vector3 CentroidXZ(IReadOnlyList<Vector3> poly)
        {
            Vector3 c = Vector3.zero;
            for (int i = 0; i < poly.Count; i++) c += poly[i];
            c /= Mathf.Max(1, poly.Count);
            c.y = 0f;
            return c;
        }
    }
}
