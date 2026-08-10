using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Floors;
using RoomPlanner.Stairs;
using RoomPlanner.Walls;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// Shift the WHOLE model by a delta (teleport, design/18 I6): every wall-graph node
    /// once (segments share nodes — moving per-view would move shared corners twice),
    /// then every slab. Undo shifts back — teleporting is a navigation act, but it still
    /// belongs in history so X can always take you home.
    /// Measurements stay put on purpose: they annotate the REAL room, not the model.
    /// </summary>
    public class TeleportCommand : ICommand
    {
        private readonly WallGraphRenderer _walls;
        private readonly List<Floor> _floors;
        private readonly List<Stair> _stairs;
        private readonly List<RoomPlanner.Import.MepView> _mep;
        private readonly List<RoomPlanner.Electrical.ElectricFixture> _fixtures;
        private readonly List<RoomPlanner.Electrical.WireRoute> _routes;
        private readonly List<RoomPlanner.Measure.Measurement> _measurements;
        private readonly Vector3 _delta;

        public TeleportCommand(WallGraphRenderer walls, List<Floor> floors, Vector3 delta,
            List<Stair> stairs = null, List<RoomPlanner.Import.MepView> mep = null,
            List<RoomPlanner.Electrical.ElectricFixture> fixtures = null,
            List<RoomPlanner.Electrical.WireRoute> routes = null,
            List<RoomPlanner.Measure.Measurement> measurements = null)
        {
            _walls = walls;
            _floors = floors;
            _stairs = stairs;
            _mep = mep;
            _fixtures = fixtures;
            _routes = routes;
            _measurements = measurements;
            _delta = delta;
        }

        public string Name => "Teleport";

        public void Do() => Apply(_delta);
        public void Undo() => Apply(-_delta);

        private void Apply(Vector3 d)
        {
            if (_walls != null && _walls.Graph != null)
            {
                var g = _walls.Graph;
                foreach (var n in g.Nodes) g.MoveNode(n, n.Position + d);
                foreach (var s in g.Segments) _walls.RebuildSegment(s);
            }
            if (_floors != null)
                foreach (var f in _floors)
                    if (f != null) f.MoveBy(d);
            if (_stairs != null)
                foreach (var s in _stairs)
                    if (s != null) s.MoveBy(d);
            if (_mep != null)
                foreach (var m in _mep)
                    if (m != null) m.MoveBy(d);
            // The electrical layer is world-anchored model data like everything else —
            // without this shift, outlets and wires visually "follow the user" (headset
            // feedback 2026-08-10). BaseLevel rides along so storey-relative mounting
            // heights stay honest after a vertical teleport.
            if (_fixtures != null)
                foreach (var fx in _fixtures)
                    if (fx != null) { fx.MoveBy(d); fx.BaseLevel += d.y; }
            if (_routes != null)
                foreach (var r in _routes)
                    if (r != null) r.MoveBy(d);
            // Measurements are anchored to the model's geometry (a measured wall keeps
            // its tape) — left behind they visually "travel with the user" (headset
            // feedback 2026-08-10).
            if (_measurements != null)
                foreach (var m in _measurements)
                    if (m != null) m.Set(m.PointA + d, m.PointB + d);
        }

        /// <summary>Every measurement, hidden ones included — same rule as slabs.</summary>
        public static List<RoomPlanner.Measure.Measurement> CollectMeasurements()
        {
            var list = new List<RoomPlanner.Measure.Measurement>();
            foreach (var m in Object.FindObjectsByType<RoomPlanner.Measure.Measurement>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
                list.Add(m);
            return list;
        }

        /// <summary>
        /// Every slab in the scene, hidden ones included (undo must move them too, or a
        /// restored slab reappears in the pre-teleport place). Parked prefab templates
        /// have no outline and are skipped.
        /// </summary>
        public static List<Floor> CollectFloors()
        {
            var list = new List<Floor>();
            foreach (var f in Object.FindObjectsByType<Floor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (f.Outline.Count >= 3) list.Add(f);
            return list;
        }

        /// <summary>Every stair flight, hidden ones included — same rule as slabs.</summary>
        public static List<Stair> CollectStairs()
        {
            var list = new List<Stair>();
            foreach (var s in Object.FindObjectsByType<Stair>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (s.Risers > 0) list.Add(s);
            return list;
        }

        /// <summary>Every imported MEP fixture, hidden ones included.</summary>
        public static List<RoomPlanner.Import.MepView> CollectMep()
        {
            var list = new List<RoomPlanner.Import.MepView>();
            foreach (var m in Object.FindObjectsByType<RoomPlanner.Import.MepView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                list.Add(m);
            return list;
        }

        /// <summary>Every electrical fixture, hidden ones included — same rule as slabs.</summary>
        public static List<RoomPlanner.Electrical.ElectricFixture> CollectFixtures()
        {
            var list = new List<RoomPlanner.Electrical.ElectricFixture>();
            foreach (var f in Object.FindObjectsByType<RoomPlanner.Electrical.ElectricFixture>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                list.Add(f);
            return list;
        }

        /// <summary>Every wire route with real points, hidden ones included.</summary>
        public static List<RoomPlanner.Electrical.WireRoute> CollectRoutes()
        {
            var list = new List<RoomPlanner.Electrical.WireRoute>();
            foreach (var r in Object.FindObjectsByType<RoomPlanner.Electrical.WireRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (r.PointCount >= 2) list.Add(r);
            return list;
        }
    }
}
