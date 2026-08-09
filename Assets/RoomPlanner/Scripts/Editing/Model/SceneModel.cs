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
        private readonly RaycastHit[] _hits = new RaycastHit[32];
        private int _nextId = 1;

        /// <summary>Scene-wide instance so Selectable.OnDestroy can self-unregister even when
        /// a destroy site forgets to (safety net; the primary path is explicit Unregister).</summary>
        public static SceneModel Instance { get; private set; }

        public EditHistory History { get; } = new();
        public IReadOnlyList<ISelectable> Items => _items;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Register(ISelectable s)
        {
            if (s == null || !s.IsAlive || _items.Contains(s)) return;
            if (string.IsNullOrEmpty(s.Id)) s.Id = (_nextId++).ToString();
            _items.Add(s);
        }

        /// <summary>
        /// Remove the object from the model AND purge its commands from the history. Call
        /// before hard-destroying an object — a MoveCommand/DeleteCommand replayed against a
        /// destroyed component would throw or silently no-op out of sync.
        /// </summary>
        public void Unregister(ISelectable s)
        {
            if (s == null) return;
            _items.Remove(s);
            History.PurgeWhere(c => c is ISelectableCommand sc && ReferenceEquals(sc.Target, s));
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
            // Non-alloc: this runs every frame in the default (Select) tool.
            int count = Physics.RaycastNonAlloc(ray, _hits, PickDistance, ~(1 << MenuLayer), QueryTriggerInteraction.Ignore);

            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var h = _hits[i];
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
