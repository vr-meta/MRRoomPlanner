using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Editing
{
    /// <summary>
    /// Central registry of every editable object plus the shared Undo/Redo history. Replaces
    /// the per-controller lists as the single owner (roadmap docs/design/11, Phase A). Creation
    /// tools register their objects here; the Select tool queries it for picking.
    /// </summary>
    public class SceneModel : MonoBehaviour
    {
        private const int MenuLayer = 2;   // IgnoreRaycast — never pick menu colliders
        private const float PickDistance = 15f;

        private readonly List<ISelectable> _items = new();
        private int _nextId = 1;

        public EditHistory History { get; } = new();
        public IReadOnlyList<ISelectable> Items => _items;

        public void Register(ISelectable s)
        {
            if (s == null || _items.Contains(s)) return;
            if (string.IsNullOrEmpty(s.Id)) s.Id = (_nextId++).ToString();
            _items.Add(s);
        }

        public void Unregister(ISelectable s)
        {
            if (s != null) _items.Remove(s);
        }

        /// <summary>
        /// Ray-pick the nearest active Selectable. Uses RaycastAll and skips colliders without a
        /// Selectable (scan mesh, reticle) so geometry occluded by the scan can still be picked
        /// when working from a plan.
        /// </summary>
        public bool TryPick(Ray ray, out ISelectable hit, out Vector3 point)
        {
            hit = null;
            point = default;
            var hits = Physics.RaycastAll(ray, PickDistance, ~(1 << MenuLayer), QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) return false;

            float best = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                // interface lookup so this assembly needn't reference the concrete Selectable
                // (which lives in Assembly-CSharp with Meta/TMP deps).
                var sel = h.collider.GetComponentInParent<ISelectable>();
                if (sel == null || sel.IsHidden) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    hit = sel;
                    point = h.point;
                }
            }
            return hit != null;
        }
    }
}
