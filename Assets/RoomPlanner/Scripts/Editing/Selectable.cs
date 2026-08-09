using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Walls;
using RoomPlanner.Floors;
using RoomPlanner.Measure;

namespace RoomPlanner.Editing
{
    /// <summary>
    /// Selection adapter placed on wall/floor/measurement roots. Auto-detects which kind it
    /// wraps, exposes bounds/highlight/move/hide, and delegates move to the underlying
    /// procedural object so geometry stays parametric (no transform drift).
    /// Highlight is a non-destructive tint via MaterialPropertyBlock; hide = SetActive(false)
    /// so a deleted object is invisible AND unpickable, and Undo simply re-activates it.
    /// </summary>
    public class Selectable : MonoBehaviour, ISelectable
    {
        private static readonly Color HoverColor = new Color(0.35f, 0.9f, 1f);   // cyan
        private static readonly Color SelectColor = new Color(0.3f, 1f, 0.5f);   // green
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int FaceColorId = Shader.PropertyToID("_FaceColor"); // TMP labels

        private SelectableKind _kind;
        private bool _resolved;
        private Wall _wall;
        private Floor _floor;
        private Measurement _measurement;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _mpb;
        private HighlightState _state = HighlightState.None;

        public string Id { get; set; }
        public Transform Transform => transform;
        public bool IsHidden => !gameObject.activeSelf;

        // `this` is compile-time typed as a UnityEngine.Object here, so the overloaded
        // null-check correctly reports a destroyed component (unlike interface-typed refs).
        public bool IsAlive => this != null;

        public SelectableKind Kind { get { Resolve(); return _kind; } }

        private void Awake() => Resolve();

        private void OnDestroy()
        {
            // Safety net: drop this object (and its history commands) from the model even if
            // the destroying code forgot the explicit Unregister.
            var model = SceneModel.Instance;
            if (model != null) model.Unregister(this);
        }

        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            _wall = GetComponent<Wall>();
            _floor = GetComponent<Floor>();
            _measurement = GetComponent<Measurement>();
            if (_wall != null) _kind = SelectableKind.Wall;
            else if (_floor != null) _kind = SelectableKind.Floor;
            else _kind = SelectableKind.Measurement;
            _renderers = GetComponentsInChildren<Renderer>(true);
            _mpb = new MaterialPropertyBlock();
        }

        public Bounds WorldBounds
        {
            get
            {
                Resolve();
                bool has = false;
                Bounds b = new Bounds(transform.position, Vector3.zero);
                if (_renderers != null)
                {
                    foreach (var r in _renderers)
                    {
                        if (r == null || !r.enabled) continue;
                        if (!has) { b = r.bounds; has = true; }
                        else b.Encapsulate(r.bounds);
                    }
                }
                if (!has) b = new Bounds(transform.position, Vector3.one * 0.1f);
                return b;
            }
        }

        public void SetHighlight(HighlightState state)
        {
            Resolve();
            if (_state == state) return;
            _state = state;
            if (_renderers == null) return;

            bool tint = state != HighlightState.None;
            Color c = state == HighlightState.Selected ? SelectColor : HoverColor;
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                if (tint)
                {
                    r.GetPropertyBlock(_mpb);
                    _mpb.SetColor(BaseColorId, c);
                    _mpb.SetColor(ColorId, c);
                    _mpb.SetColor(FaceColorId, c);   // TMP text (measurement badge) uses _FaceColor
                    r.SetPropertyBlock(_mpb);
                }
                else
                {
                    r.SetPropertyBlock(null);   // restore the material's own color
                }
            }
        }

        public void SetHidden(bool hidden)
        {
            if (hidden == !gameObject.activeSelf) return;
            gameObject.SetActive(!hidden);
        }

        public void MoveBy(Vector3 delta)
        {
            Resolve();
            if (_wall != null) _wall.MoveBy(delta);
            else if (_floor != null) _floor.MoveBy(delta);
            else if (_measurement != null) _measurement.MoveBy(delta);
        }

        public string Describe()
        {
            Resolve();
            switch (_kind)
            {
                case SelectableKind.Wall:
                    return $"Length {WallLength() * 100f:0} cm";
                case SelectableKind.Floor:
                    if (_floor != null)
                    {
                        Vector3 a = _floor.CornerA, b = _floor.CornerB;
                        return $"{Mathf.Abs(b.x - a.x) * 100f:0} x {Mathf.Abs(b.z - a.z) * 100f:0} cm";
                    }
                    return "Floor";
                default:
                    return _measurement != null ? $"{_measurement.Distance * 100f:0} cm" : "Measurement";
            }
        }

        private float WallLength()
        {
            if (_wall == null) return 0f;
            var p = _wall.Points;
            float len = 0f;
            for (int i = 1; i < p.Count; i++) len += Vector3.Distance(p[i - 1], p[i]);
            return len;
        }
    }
}
