using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Floors;
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
        private readonly Vector3 _delta;

        public TeleportCommand(WallGraphRenderer walls, List<Floor> floors, Vector3 delta)
        {
            _walls = walls;
            _floors = floors;
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
    }
}
