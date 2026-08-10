using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// Left-hand palette, v2 = the Snap &amp; Layers STRIP (design/20 §1.9): tool switching
    /// moved to the radial (L3); what stays here is the snap toggles (icon buttons with a
    /// LED), a passive current-tool chip with the layer edge stripe, and the reserved spot
    /// for layer chips. Lazy-follow and palm-gating unchanged (design/16 P1.2).
    /// </summary>
    public class ToolMenu : MonoBehaviour
    {
        [SerializeField] private Transform follow;                 // left controller
        [SerializeField] private Vector3 followOffset = new Vector3(0f, 0.10f, 0f);
        [SerializeField] private bool palmGating = true;           // show only when the palm faces the head
        [SerializeField] private IconRenderer chipIcon;            // current-tool chip (non-interactive)
        [SerializeField] private TMPro.TMP_Text chipLabel;
        [SerializeField] private Renderer chipStripe;              // 4 mm layer-hue edge
        [SerializeField] private MenuButton snapCornerBtn;
        [SerializeField] private MenuButton snapEdgeBtn;
        [SerializeField] private MenuButton snapGridBtn;
        [SerializeField] private MenuButton snapAngleBtn;
        [SerializeField] private MenuButton scanBtn;

        private const float RotStartDeg = 5f;    // billboard re-orients only past this error
        private const float RotSettleDeg = 0.5f;
        private const float FadeTime = 0.15f;

        private Transform _cam;
        private MenuButton _hi;
        private bool _placed;
        private bool _rotating;
        private bool _gateVisible = true;
        private float _visW = 1f;
        private Vector3 _baseScale;

        private void Awake() => _baseScale = transform.localScale;

        [SerializeField] private TMPro.TMP_Text tooltipLabel;   // full-word hint under the palette

        public void Highlight(MenuButton b)
        {
            if (_hi == b) return;
            if (_hi != null) _hi.SetHighlight(false);
            _hi = b;
            if (_hi != null) _hi.SetHighlight(true);
            if (tooltipLabel != null)
            {
                // no hover → the radial discoverability hint (design/20 §1.9 mitigation)
                string tip = _hi != null ? _hi.Tooltip : null;
                if (string.IsNullOrEmpty(tip)) tip = "Tools: click left stick";
                tooltipLabel.gameObject.SetActive(true);
                tooltipLabel.text = tip;
            }
        }

        public void Refresh(int activeTool, bool snapCorner, bool snapEdge, bool snapGrid, bool snapAngle, bool scanOn)
        {
            if (snapCornerBtn != null) snapCornerBtn.SetActiveTool(snapCorner);
            if (snapEdgeBtn != null) snapEdgeBtn.SetActiveTool(snapEdge);
            if (snapGridBtn != null) snapGridBtn.SetActiveTool(snapGrid);
            if (snapAngleBtn != null) snapAngleBtn.SetActiveTool(snapAngle);
            if (scanBtn != null) scanBtn.SetActiveTool(scanOn);
        }

        /// <summary>The passive current-tool chip — the left wrist answers "what tool am I
        /// holding" at a glance (design/20 §1.9).</summary>
        public void SetToolChip(string iconId, string label, Color layerTint)
        {
            if (chipIcon != null && !string.IsNullOrEmpty(iconId)) chipIcon.SetIcon(iconId);
            if (chipLabel != null) chipLabel.text = label ?? "";
            if (chipStripe != null)
            {
                _stripeMpb ??= new MaterialPropertyBlock();
                chipStripe.GetPropertyBlock(_stripeMpb);
                _stripeMpb.SetColor(Shader.PropertyToID("_BaseColor"), layerTint);
                _stripeMpb.SetColor(Shader.PropertyToID("_Color"), layerTint);
                chipStripe.SetPropertyBlock(_stripeMpb);
            }
        }

        private MaterialPropertyBlock _stripeMpb;

        private void LateUpdate()
        {
            if (follow != null)
            {
                Vector3 target = follow.position + followOffset;
                if (!_placed) { transform.position = target; _placed = true; }
                else transform.position = PaletteMath.SmoothFollow(transform.position, target, Time.deltaTime);
            }
            if (_cam == null)
            {
                if (Camera.main == null) return;
                _cam = Camera.main.transform;
            }

            // billboard with a threshold: re-orient only past 5° of error, settle smoothly —
            // full-rate LookRotation jitters with the hand
            Vector3 dir = transform.position - _cam.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion desired = Quaternion.LookRotation(dir);
                float ang = Quaternion.Angle(transform.rotation, desired);
                if (ang > RotStartDeg) _rotating = true;
                if (_rotating)
                {
                    float k = 1f - Mathf.Exp(-Time.deltaTime / 0.2f);
                    transform.rotation = Quaternion.Slerp(transform.rotation, desired, k);
                    if (ang < RotSettleDeg) _rotating = false;
                }
            }

            // palm gating: visible while the controller faces the head (hysteresis, 150 ms fade).
            // Kills both the always-on visual noise and accidental tool-blocking when the left
            // hand crosses the pointer ray.
            if (palmGating && follow != null)
            {
                float palmDot = Vector3.Dot(follow.up, (_cam.position - follow.position).normalized);
                _gateVisible = PaletteMath.GateVisible(_gateVisible, palmDot);
            }
            else _gateVisible = true;

            _visW = Mathf.MoveTowards(_visW, _gateVisible ? 1f : 0f, Time.deltaTime / FadeTime);
            transform.localScale = _baseScale * Mathf.Max(0.0001f, _visW);
        }
    }
}
