using TMPro;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// The slider row's interactive part (design/20 §2.2). Carries the field and its
    /// track geometry; ToolManager owns the drag (trigger capture, grip = fine ×0.1,
    /// release = ONE commit → one undo command). The collider spans the whole track at
    /// 2.1° height; the visual track is thinner.
    /// </summary>
    public class SliderWidget : MonoBehaviour
    {
        private SettingField _field;
        private Transform _fill;         // scaled 0..trackWidth
        private Transform _knob;
        private TMP_Text _valueLabel;
        private Renderer _fillRenderer;
        private float _trackWidth;

        private float _dragStartValue;
        private bool _dragging;
        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public SettingField Field => _field;
        public bool IsDragging => _dragging;

        // The fill gets its OWN mesh, rebuilt at the true width. The plate meshes from
        // the panel cache are SHARED between rows — mutating one would resize every
        // plate of that size, and x-scaling it stretched the rounded ends into ovals
        // (headset feedback 2026-08-11).
        private Mesh _ownFillMesh;
        private float _fillH, _fillR, _fillWidthNow = -1f;

        public void Init(SettingField field, Transform fill, Renderer fillRenderer,
            Transform knob, TMP_Text valueLabel, float trackWidth,
            float fillHeight = 0.006f, float fillRadius = 0.003f)
        {
            _field = field;
            _fill = fill;
            _fillRenderer = fillRenderer;
            _knob = knob;
            _valueLabel = valueLabel;
            _trackWidth = trackWidth;
            _fillH = fillHeight;
            _fillR = fillRadius;
            var mf = fill != null ? fill.GetComponent<MeshFilter>() : null;
            if (mf != null)
            {
                _ownFillMesh = new Mesh { name = "SliderFill", hideFlags = HideFlags.DontSave };
                mf.sharedMesh = _ownFillMesh;
                _fill.localScale = Vector3.one;
            }
            Refresh();
        }

        private void OnDestroy()
        {
            if (_ownFillMesh != null) Destroy(_ownFillMesh);
        }

        /// <summary>Local x (relative to the track's LEFT edge) → previewed value.</summary>
        public void PreviewAt(float localX)
        {
            float t = _trackWidth > 0f ? localX / _trackWidth : 0f;
            float v = PanelLayout.SliderValue(t, _field.Min, _field.Max, _field.Step);
            _field.SetNumber?.Invoke(v);
            Refresh();
        }

        public void BeginDrag()
        {
            _dragging = true;
            _dragStartValue = _field.GetNumber();
            Refresh();
        }

        /// <summary>Release: one undo command from where the drag started.</summary>
        public void EndDrag()
        {
            if (!_dragging) return;
            _dragging = false;
            float after = _field.GetNumber();
            if (!Mathf.Approximately(_dragStartValue, after))
                _field.CommitNumber?.Invoke(_dragStartValue, after);
            Refresh();
        }

        /// <summary>Rebuild a pill/rounded-rect mesh at the TRUE width — never scale it:
        /// scaling stretched the rounded caps into ovals (headset feedback 2026-08-11).
        /// Shared with the inspector's progress bars.</summary>
        public static void RebuildPill(Mesh mesh, float w, float h, float r)
        {
            var data = UiMeshes.RoundedRect(w, h, Mathf.Min(r, Mathf.Min(w, h) * 0.5f));
            mesh.Clear();
            mesh.SetVertices(data.Vertices);
            mesh.SetTriangles(data.Triangles, 0);
            var normals = new Vector3[data.Vertices.Length];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.back;
            mesh.SetNormals(normals);
            mesh.RecalculateBounds();
        }

        public void Refresh()
        {
            if (_field == null) return;
            float t = PanelLayout.SliderT(_field.GetNumber(), _field.Min, _field.Max);
            if (_fill != null && _ownFillMesh != null)
            {
                float w = Mathf.Max(0.002f, t * _trackWidth);
                if (Mathf.Abs(w - _fillWidthNow) > 0.0003f)
                {
                    _fillWidthNow = w;
                    RebuildPill(_ownFillMesh, w, _fillH, _fillR);
                }
                var p = _fill.localPosition;
                _fill.localPosition = new Vector3(t * _trackWidth * 0.5f, p.y, p.z);
            }
            if (_knob != null)
            {
                var p = _knob.localPosition;
                _knob.localPosition = new Vector3(t * _trackWidth, p.y, p.z);
                _knob.localScale = Vector3.one * (_dragging ? 1.12f : 1f);
            }
            if (_fillRenderer != null)
            {
                _mpb ??= new MaterialPropertyBlock();
                _fillRenderer.GetPropertyBlock(_mpb);
                Color c = _dragging ? UiTokens.TrackFillActive : UiTokens.TrackFill;
                _mpb.SetColor(BaseColorId, c);
                _mpb.SetColor(ColorId, c);
                _fillRenderer.SetPropertyBlock(_mpb);
            }
            if (_valueLabel != null && _field.Value != null) _valueLabel.text = _field.Value();
        }
    }
}
