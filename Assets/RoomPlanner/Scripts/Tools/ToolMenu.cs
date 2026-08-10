using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// The snap strip, v3: a small FLOATING panel (device feedback 2026-08-11 — nothing
    /// may live on the left hand, both hands belong to the tape measure). Behaves like
    /// the inspector: spawns near the gaze low-left, world-locked, follows only when
    /// parked out of view, and grip-drag parks it anywhere. Content: 5 snap toggles,
    /// the current-tool chip and the rendering-settings gear.
    /// </summary>
    public class ToolMenu : MonoBehaviour
    {
        [SerializeField] private IconRenderer chipIcon;            // current-tool chip (non-interactive)
        [SerializeField] private TMPro.TMP_Text chipLabel;
        [SerializeField] private Renderer chipStripe;              // 4 mm layer-hue edge
        [SerializeField] private MenuButton snapCornerBtn;
        [SerializeField] private MenuButton snapEdgeBtn;
        [SerializeField] private MenuButton snapGridBtn;
        [SerializeField] private MenuButton snapAngleBtn;
        [SerializeField] private MenuButton scanBtn;
        [SerializeField] private TMPro.TMP_Text tooltipLabel;      // full-word hint under the strip

        private Transform _cam;
        private MenuButton _hi;
        private bool _placed;
        private MaterialPropertyBlock _stripeMpb;

        public void Highlight(MenuButton b)
        {
            if (_hi == b) return;
            if (_hi != null) _hi.SetHighlight(false);
            _hi = b;
            if (_hi != null) _hi.SetHighlight(true);
            if (tooltipLabel != null)
            {
                // no hover → the radial discoverability hint
                string tip = _hi != null ? _hi.Tooltip : null;
                if (string.IsNullOrEmpty(tip)) tip = "Tools: hold A";
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

        /// <summary>The passive current-tool chip — answers "what tool am I holding".</summary>
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

        private void LateUpdate()
        {
            if (_cam == null)
            {
                if (Camera.main == null) return;
                _cam = Camera.main.transform;
            }

            // inspector-style parking: place once near the gaze (low-left, out of the
            // pointer's way), then world-lock; re-place only when parked out of reach
            if (!_placed || OutOfView())
            {
                transform.position = _cam.position
                    + _cam.forward * 0.55f - _cam.right * 0.28f - _cam.up * 0.20f;
                _placed = true;
            }

            // yaw-only billboard (a pitched strip reads at a slant when parked low)
            Vector3 dir = transform.position - _cam.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir);
        }

        private bool OutOfView()
        {
            Vector3 to = transform.position - _cam.position;
            if (to.magnitude > 2.2f) return true;
            return Vector3.Angle(_cam.forward, to) > 70f;   // parked behind the back
        }
    }
}
