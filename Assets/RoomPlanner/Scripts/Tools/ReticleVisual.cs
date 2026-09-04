using TMPro;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tools
{
    public enum ReticleSnapKind { None, Corner, Edge, Grid, Angle }

    /// <summary>
    /// Presentation layer for the shared aim point. Tools still own its position and
    /// visibility; this component adds tool identity, snap shape and short-lived guidance.
    /// It upgrades the legacy sphere in place, including the checked-in scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ReticleVisual : MonoBehaviour
    {
        private LineRenderer _outline;
        private IconRenderer _glyph;
        private TMP_Text _dimension;
        private GameObject _dimensionBadge;
        private TMP_Text _gesture;
        private Material _material;
        private MaterialPropertyBlock _mpb;
        private ReticleSnapKind _snap = (ReticleSnapKind)(-1);
        private string _toolId;
        private string _gestureText;
        private float _gestureUntil;
        private float _rootScale = 1f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public ReticleSnapKind SnapKind => _snap;

        public static ReticleVisual Ensure(Transform root)
        {
            if (root == null) return null;
            var visual = root.GetComponent<ReticleVisual>();
            return visual != null ? visual : root.gameObject.AddComponent<ReticleVisual>();
        }

        public static ReticleVisual For(Transform root) =>
            root != null ? root.GetComponent<ReticleVisual>() : null;

        public void ConfigureTool(string toolId, string iconId, Color accent,
            string gestureHint, bool showGesture = false)
        {
            EnsureBuilt();
            bool changed = _toolId != toolId;
            _toolId = toolId;
            _gestureText = gestureHint ?? "";
            if (_glyph != null)
            {
                _glyph.SetIcon(iconId);
                _glyph.SetTint(accent);
            }
            TintOutline(accent);
            SetSnap(ReticleSnapKind.None);
            SetDimension(null);
            if (changed || showGesture)
                _gestureUntil = Time.unscaledTime + 2.5f;
            RefreshGesture();
        }

        public void SetSnap(ReticleSnapKind kind)
        {
            EnsureBuilt();
            if (_snap == kind) return;
            _snap = kind;
            Vector2[] shape = Shape(kind);
            _outline.loop = kind != ReticleSnapKind.Edge && kind != ReticleSnapKind.Angle;
            _outline.positionCount = shape.Length;
            float radius = 0.020f / _rootScale;
            for (int i = 0; i < shape.Length; i++)
                _outline.SetPosition(i, new Vector3(shape[i].x * radius, shape[i].y * radius, 0f));
        }

        public void SetDimension(string text)
        {
            EnsureBuilt();
            bool show = !string.IsNullOrWhiteSpace(text);
            if (_dimensionBadge != null) _dimensionBadge.SetActive(show);
            _dimension.gameObject.SetActive(show);
            if (show && _dimension.text != text) _dimension.text = text;
        }

        public static Vector2[] Shape(ReticleSnapKind kind)
        {
            switch (kind)
            {
                case ReticleSnapKind.Corner:
                    return new[] { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
                case ReticleSnapKind.Edge:
                    return new[] { new Vector2(-1f, 0f), new Vector2(1f, 0f) };
                case ReticleSnapKind.Grid:
                    return new[]
                    {
                        new Vector2(-0.8f, 0.8f), new Vector2(0.8f, 0.8f),
                        new Vector2(0.8f, -0.8f), new Vector2(-0.8f, -0.8f),
                    };
                case ReticleSnapKind.Angle:
                    return new[] { new Vector2(-0.9f, 0.7f), Vector2.zero, new Vector2(0.9f, 0.7f) };
                default:
                    var circle = new Vector2[16];
                    for (int i = 0; i < circle.Length; i++)
                    {
                        float a = i * Mathf.PI * 2f / circle.Length;
                        circle[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    }
                    return circle;
            }
        }

        private void EnsureBuilt()
        {
            if (_outline != null) return;
            var legacy = GetComponent<Renderer>();
            _material = legacy != null ? legacy.sharedMaterial : null;
            if (legacy != null) legacy.enabled = false;
            _rootScale = Mathf.Max(0.0001f, transform.lossyScale.x);

            var outlineGo = new GameObject("SnapShape");
            outlineGo.transform.SetParent(transform, false);
            outlineGo.transform.localPosition = new Vector3(0f, 0f, -0.02f / _rootScale);
            _outline = outlineGo.AddComponent<LineRenderer>();
            _outline.useWorldSpace = false;
            _outline.widthMultiplier = 0.0025f / _rootScale;
            _outline.numCapVertices = 3;
            _outline.numCornerVertices = 2;
            _outline.sharedMaterial = _material;

            var glyphGo = new GameObject("ToolGlyph");
            glyphGo.transform.SetParent(transform, false);
            glyphGo.transform.localPosition = new Vector3(0f, 0f, -0.025f / _rootScale);
            _glyph = glyphGo.AddComponent<IconRenderer>();
            _glyph.Init("select-cursor", _material, 0.016f / _rootScale);

            _dimension = MakeText("Dimension", 0.036f, 0.016f);
            _dimensionBadge = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _dimensionBadge.name = "DimensionBadge";
            _dimensionBadge.layer = gameObject.layer;
            _dimensionBadge.transform.SetParent(transform, false);
            _dimensionBadge.transform.localPosition = new Vector3(
                0f, 0.036f / _rootScale, -0.028f / _rootScale);
            _dimensionBadge.transform.localScale = new Vector3(
                0.14f / _rootScale, 0.026f / _rootScale, 1f);
            var badgeCollider = _dimensionBadge.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(badgeCollider);
            else DestroyImmediate(badgeCollider);
            var badgeRenderer = _dimensionBadge.GetComponent<MeshRenderer>();
            badgeRenderer.sharedMaterial = _material;
            badgeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Tint(badgeRenderer, UiTokens.PanelBg);
            _dimensionBadge.SetActive(false);
            _gesture = MakeText("GestureHint", -0.038f, 0.012f);
            SetSnap(ReticleSnapKind.None);
        }

        private TMP_Text MakeText(string objectName, float worldY, float worldHeight)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, worldY / _rootScale, -0.03f / _rootScale);
            var text = go.AddComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableAutoSizing = false;
            text.fontSize = worldHeight * 6.5f / _rootScale;
            text.rectTransform.sizeDelta = new Vector2(0.22f / _rootScale, worldHeight * 1.5f / _rootScale);
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.color = UiTokens.LabelLight;
            go.SetActive(false);
            return text;
        }

        private void TintOutline(Color color)
        {
            if (_outline == null) return;
            _outline.startColor = color;
            _outline.endColor = color;
            _mpb ??= new MaterialPropertyBlock();
            _outline.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, color);
            _mpb.SetColor(ColorId, color);
            _outline.SetPropertyBlock(_mpb);
        }

        private void Tint(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            _mpb ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, color);
            _mpb.SetColor(ColorId, color);
            renderer.SetPropertyBlock(_mpb);
        }

        private void LateUpdate()
        {
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam != null)
            {
                Vector3 toward = transform.position - cam.position;
                if (toward.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(toward);
            }
            RefreshGesture();
        }

        private void RefreshGesture()
        {
            if (_gesture == null) return;
            bool show = Time.unscaledTime < _gestureUntil && !string.IsNullOrEmpty(_gestureText);
            _gesture.gameObject.SetActive(show);
            if (show) _gesture.text = _gestureText;
        }
    }
}
