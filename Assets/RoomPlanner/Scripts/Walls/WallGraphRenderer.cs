using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Editing;

namespace RoomPlanner.Walls
{
    /// <summary>
    /// Owns the <see cref="WallGraph"/> and the one <see cref="Wall"/> view per segment that
    /// draws it (docs/design/13-phase-b-wallgraph.md, step B3).
    ///
    /// Single owner of wall lifetime: views are created here, registered with
    /// <see cref="SceneModel"/> here, and removed here — the tool never keeps its own list
    /// (that dual ownership was the Phase A debt, coding rule 2.2).
    ///
    /// Rebuilds are targeted: a segment's shape depends on its neighbours at each node, so
    /// touching a node means rebuilding everything attached to it — and nothing else.
    /// </summary>
    public class WallGraphRenderer : MonoBehaviour
    {
        [SerializeField] private Wall wallPrefab;
        [SerializeField] private SceneModel sceneModel;

        private readonly Dictionary<WallSegment, Wall> _views = new();
        private readonly List<WallSegment> _scratch = new();     // reused; no per-call garbage

        /// <summary>The graph every wall in the scene lives in.</summary>
        public WallGraph Graph { get; } = new WallGraph();

        /// <summary>
        /// Wire the dependencies from code. The rig setup fills the serialized fields instead;
        /// this exists so the component is usable without an Editor-built prefab (tests, and
        /// any future runtime-constructed rig).
        /// </summary>
        public void Configure(Wall prefab, SceneModel model)
        {
            wallPrefab = prefab;
            sceneModel = model;
        }

        public Wall ViewOf(WallSegment s) => s != null && _views.TryGetValue(s, out var w) ? w : null;

        /// <summary>
        /// A wall counts as present only while its view is alive AND not hidden — a deleted
        /// (hidden) wall must not magnet the cursor or shape a neighbour's joint
        /// (coding rule 2.4).
        /// </summary>
        public bool IsVisible(WallSegment s)
        {
            var w = ViewOf(s);
            return w != null && w.gameObject.activeSelf;
        }

        /// <summary>Give every segment a view, drop views whose segment left the graph.</summary>
        public void Sync()
        {
            foreach (var s in Graph.Segments)
                if (!_views.ContainsKey(s)) CreateView(s);

            _scratch.Clear();
            foreach (var kv in _views)
                if (!GraphHas(kv.Key)) _scratch.Add(kv.Key);
            foreach (var gone in _scratch) RemoveView(gone);
        }

        private bool GraphHas(WallSegment s)
        {
            var all = Graph.Segments;
            for (int i = 0; i < all.Count; i++)
                if (all[i] == s) return true;
            return false;
        }

        /// <summary>Rebuild one segment's mesh from the graph.</summary>
        public void RebuildSegment(WallSegment s)
        {
            var view = ViewOf(s);
            if (view == null) return;
            view.BuildSegment(s);
            DressLeaves(view, s);
        }

        /// <summary>Give every door/garage leaf child its selection adapter (issue #50):
        /// a Selectable (SelectableKind.Door) plus the per-door settings page. Idempotent
        /// and allocation-free once dressed — rebuilds run per frame during node drags.</summary>
        private void DressLeaves(Wall view, WallSegment s)
        {
            var wallSel = view.GetComponent<Selectable>();
            foreach (var leaf in view.Leaves)
            {
                if (leaf == null) continue;
                var pars = leaf.GetComponent<OpeningParameters>();
                if (pars == null)
                {
                    // provider BEFORE Selectable — Selectable.Awake caches ISettingsProvider
                    pars = leaf.gameObject.AddComponent<OpeningParameters>();
                    leaf.gameObject.AddComponent<Selectable>();
                }
                pars.Configure(this, s, wallSel);
            }
        }

        /// <summary>Rebuild every wall meeting at this node — they share the joint.</summary>
        public void RebuildAround(WallNode n)
        {
            if (n == null) return;
            foreach (var s in n.Segments) RebuildSegment(s);
        }

        /// <summary>Rebuild a segment and everything attached to either of its ends.</summary>
        public void RebuildNeighbourhood(WallSegment s)
        {
            if (s == null) return;
            RebuildAround(s.A);
            RebuildAround(s.B);
        }

        // ---- lifetime ----

        private Wall CreateView(WallSegment s)
        {
            if (wallPrefab == null) return null;
            var view = Instantiate(wallPrefab, transform);
            view.name = $"Wall#{s.Id}";
            // Instantiate inherits the template's active state, and a parked (inactive) template
            // is a normal way to hold a prefab. A view that exists must be visible and pickable —
            // hiding is DeleteCommand's job, never something inherited by accident.
            if (!view.gameObject.activeSelf) view.gameObject.SetActive(true);
            _views[s] = view;

            // moving a wall moves shared nodes, so its neighbours need rebuilding too
            view.GeometryChanged = moved => RebuildNeighbourhood(moved);

            view.BuildSegment(s);
            DressLeaves(view, s);
            var sel = view.GetComponent<Selectable>();
            if (sceneModel != null) sceneModel.Register(sel);
            // Delete = hide must also pull the segment out of its neighbours' joints and
            // put it back on undo (audit 02 §Б3): mark it suppressed in the graph and
            // rebuild everything sharing either node.
            if (sel != null)
                sel.HiddenChanged += hidden =>
                {
                    s.Suppressed = hidden;
                    RebuildAround(s.A);
                    RebuildAround(s.B);
                };
            return view;
        }

        private void RemoveView(WallSegment s)
        {
            if (!_views.TryGetValue(s, out var view)) return;
            _views.Remove(s);
            if (view == null) return;
            view.GeometryChanged = null;
            // unregister BEFORE destroying so the history cannot keep a dead target (rule 2.2)
            if (sceneModel != null) sceneModel.Unregister(view.GetComponent<Selectable>());
            Destroy(view.gameObject);
        }

        /// <summary>Drop a wall from the graph together with its view.</summary>
        public void RemoveSegment(WallSegment s)
        {
            if (s == null) return;
            WallNode a = s.A, b = s.B;
            Graph.RemoveSegment(s);
            RemoveView(s);
            RebuildAround(a);
            RebuildAround(b);
        }
    }
}
