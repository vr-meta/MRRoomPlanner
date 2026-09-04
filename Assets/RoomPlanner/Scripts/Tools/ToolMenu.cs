using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// The snap strip, v4: a small FLOATING panel (device feedback 2026-08-11 — nothing
    /// may live on the left hand, both hands belong to the tape measure). Spawns near
    /// the gaze once; after that it keeps its OFFSET from the user — walk (smooth
    /// locomotion) and it lazily comes along, but it never jumps back into view: parked
    /// behind you means it STAYS behind you (second round of feedback — chasing the
    /// gaze blocked the interior). Grip-drag parks it anywhere (the offset re-anchors).
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
        [SerializeField] private MenuButton gearBtn;
        [SerializeField] private MenuButton undoBtn;
        [SerializeField] private MenuButton redoBtn;
        [SerializeField] private TMPro.TMP_Text tooltipLabel;      // full-word hint under the strip

        // Tabs (#85): snapping matters while drawing walls and almost never otherwise, so
        // it no longer owns the strip permanently — the first tab holds what is reached
        // constantly (jumping to the tools actually used), snapping sits on its own.
        [SerializeField] private GameObject toolsRow;
        [SerializeField] private GameObject snapRow;
        [SerializeField] private MenuButton tabToolsBtn;
        [SerializeField] private MenuButton tabSnapBtn;
        [SerializeField] private MenuButton[] toolShortcuts;

        /// <summary>0 = tools, 1 = snapping.</summary>
        public int Tab { get; private set; }

        public void SetTab(int tab)
        {
            Tab = Mathf.Clamp(tab, 0, 1);
            if (toolsRow != null) toolsRow.SetActive(Tab == 0);
            if (snapRow != null) snapRow.SetActive(Tab == 1);
            if (tabToolsBtn != null) tabToolsBtn.SetActiveTool(Tab == 0);
            if (tabSnapBtn != null) tabSnapBtn.SetActiveTool(Tab == 1);
        }

        /// <summary>
        /// Entering a tool picks the tab that tool needs: walls and openings live on
        /// snapping, everything else on the shortcuts. Only automatic on a tool CHANGE —
        /// a tab the user chose by hand is not overridden while they stay in the tool.
        /// </summary>
        public void OnToolChanged(string toolId)
        {
            bool wantsSnap = toolId == "wall" || toolId == "openings" || toolId == "floor";
            SetTab(wantsSnap ? 1 : 0);
        }

        private Transform _cam;
        private MenuButton _hi;
        private bool _placed;
        private Vector3 _followOffset;   // world offset from the head — "left of me" stays left
        private Vector3 _lastSelfPos;    // to tell our own motion from a grip-drag
        private MaterialPropertyBlock _stripeMpb;
        private float _tooltipAt = -1f;
        private float _clearHoverAt = -1f;
        private bool _layoutReady;

        // big dead zone: head bobbing and leaning never move the strip, walking does
        private const float FollowDeadZone = 0.35f;
        private const float FollowTau = 0.25f;
        private const float ButtonStep = 0.0505f; // 8.5 mm / 20% gap between 42 mm targets

        public static float SnapButtonX(int index) => (index - 2.5f) * ButtonStep;

        private void Awake() => EnsureRuntimeLayout();

        public void Highlight(MenuButton b)
        {
            EnsureRuntimeLayout();
            if (b == null && _hi != null)
            {
                if (_clearHoverAt < 0f) _clearHoverAt = Time.time + UiTokens.HoverOutSeconds;
                return;
            }
            _clearHoverAt = -1f;
            if (_hi == b) return;
            if (_hi != null) _hi.SetHighlight(false);
            _hi = b;
            if (_hi != null) _hi.SetHighlight(true);
            if (tooltipLabel != null)
            {
                // no hover → the radial discoverability hint
                tooltipLabel.gameObject.SetActive(true);
                tooltipLabel.text = "Tools: press A";
                _tooltipAt = _hi != null ? Time.time + UiTokens.TooltipDelaySeconds : -1f;
            }
        }

        public void Refresh(int activeTool, bool snapCorner, bool snapEdge, bool snapGrid,
            bool snapAngle, bool scanOn, bool renderingOpen = false,
            bool canUndo = false, bool canRedo = false, float gridSize = 0.05f)
        {
            EnsureRuntimeLayout();
            if (toolShortcuts != null)
                foreach (var b in toolShortcuts)
                    if (b != null) b.SetActiveTool(b.ToolIndex == activeTool);
            if (snapCornerBtn != null) snapCornerBtn.SetActiveTool(snapCorner);
            if (snapEdgeBtn != null) snapEdgeBtn.SetActiveTool(snapEdge);
            if (snapGridBtn != null) snapGridBtn.SetActiveTool(snapGrid);
            if (snapAngleBtn != null) snapAngleBtn.SetActiveTool(snapAngle);
            if (scanBtn != null) scanBtn.SetActiveTool(scanOn);
            if (gearBtn != null) gearBtn.SetActiveTool(renderingOpen);
            if (undoBtn != null) undoBtn.SetEnabled(canUndo);
            if (redoBtn != null) redoBtn.SetEnabled(canRedo);
            if (snapGridBtn != null) snapGridBtn.Tooltip = $"Snap to {gridSize * 100f:0} cm grid";
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
            if (_clearHoverAt >= 0f && Time.time >= _clearHoverAt)
            {
                _clearHoverAt = -1f;
                if (_hi != null) _hi.SetHighlight(false);
                _hi = null;
                _tooltipAt = -1f;
                if (tooltipLabel != null) tooltipLabel.text = "Tools: press A";
            }
            if (_tooltipAt >= 0f && Time.time >= _tooltipAt)
            {
                _tooltipAt = -1f;
                string tip = _hi != null ? _hi.Tooltip : null;
                if (!string.IsNullOrEmpty(tip) && tooltipLabel != null) tooltipLabel.text = tip;
            }

            if (_cam == null)
            {
                if (Camera.main == null) return;
                _cam = Camera.main.transform;
            }

            if (!_placed)
            {
                // one-time spawn near the gaze, low-left, out of the pointer's way
                transform.position = _cam.position
                    + _cam.forward * 0.55f - _cam.right * 0.28f - _cam.up * 0.20f;
                _followOffset = transform.position - _cam.position;
                _placed = true;
            }
            else
            {
                // a grip-drag (or anything external) moved us — adopt the new offset
                if (transform.position != _lastSelfPos)
                    _followOffset = transform.position - _cam.position;
                // keep the parked offset as the user walks; never jump back into view
                transform.position = PaletteMath.SmoothFollow(transform.position,
                    _cam.position + _followOffset, Time.deltaTime, FollowTau, FollowDeadZone);
            }
            _lastSelfPos = transform.position;

            // yaw-only billboard (a pitched strip reads at a slant when parked low)
            Vector3 dir = transform.position - _cam.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir);
        }

        /// <summary>
        /// Migrates the checked-in scene as well as freshly generated rigs. Spacing and
        /// state fixes therefore do not depend on rerunning the editor setup command.
        /// </summary>
        private void EnsureRuntimeLayout()
        {
            if (_layoutReady) return;
            snapCornerBtn ??= FindButton("BtnSnapCorner");
            snapEdgeBtn ??= FindButton("BtnSnapEdge");
            snapGridBtn ??= FindButton("BtnSnapGrid");
            snapAngleBtn ??= FindButton("BtnSnapAngle");
            scanBtn ??= FindButton("BtnScan");
            gearBtn ??= FindButton("BtnRender");
            undoBtn ??= FindButton("BtnUndo");
            redoBtn ??= FindButton("BtnRedo");
            undoBtn = EnsureHistoryButton(undoBtn, "BtnUndo", "undo", "Undo (X)", MenuAction.Undo);
            redoBtn = EnsureHistoryButton(redoBtn, "BtnRedo", "redo", "Redo (Y)", MenuAction.Redo);

            var buttons = new[] { snapCornerBtn, snapEdgeBtn, snapGridBtn, snapAngleBtn, scanBtn, gearBtn };
            for (int i = 0; i < buttons.Length; i++)
                if (buttons[i] != null)
                {
                    Vector3 p = buttons[i].transform.localPosition;
                    p.x = SnapButtonX(i);
                    p.y = 0.048f;
                    buttons[i].transform.localPosition = p;
                }
            // The old scene serialized the gear as momentary. It is a page switch now,
            // so an open Rendering page stays visibly selected.
            if (gearBtn != null) gearBtn.ConfigureKind(MenuButtonKind.Radio);
            Position(undoBtn, 0.045f, -0.003f);
            Position(redoBtn, 0.100f, -0.003f);

            Transform chip = transform.Find("ToolChip");
            if (chip != null)
            {
                Vector3 p = chip.localPosition;
                chip.localPosition = new Vector3(-0.078f, -0.003f, p.z);
            }
            if (tooltipLabel != null)
            {
                Vector3 p = tooltipLabel.rectTransform.localPosition;
                tooltipLabel.rectTransform.localPosition = new Vector3(0f, -0.054f, p.z);
                tooltipLabel.rectTransform.sizeDelta = new Vector2(0.27f, 0.016f);
            }
            ResizePanel();
            _layoutReady = true;
        }

        private MenuButton FindButton(string objectName)
        {
            foreach (var button in GetComponentsInChildren<MenuButton>(true))
                if (button != null && button.name == objectName) return button;
            return null;
        }

        private MenuButton EnsureHistoryButton(MenuButton current, string objectName,
            string iconId, string tooltip, MenuAction action)
        {
            if (current == null && gearBtn != null)
            {
                var clone = Instantiate(gearBtn.gameObject, transform, false);
                clone.name = objectName;
                current = clone.GetComponent<MenuButton>();
            }
            if (current == null) return null;
            current.ConfigureGlobal(action, MenuButtonKind.Momentary);
            current.Tooltip = tooltip;
            var icon = current.GetComponentInChildren<IconRenderer>(true);
            if (icon != null) icon.SetIcon(iconId);
            return current;
        }

        private static void Position(MenuButton button, float x, float y)
        {
            if (button == null) return;
            Vector3 p = button.transform.localPosition;
            button.transform.localPosition = new Vector3(x, y, p.z);
        }

        private void ResizePanel()
        {
            const float height = 0.155f;
            Transform panel = transform.Find("Panel");
            if (panel == null) return;
            var plate = panel.GetComponent<RoundedPlate>();
            if (plate != null) plate.Resize(0.31f, height);
            var collider = panel.GetComponent<BoxCollider>();
            if (collider != null) collider.size = new Vector3(0.31f, height, 0.01f);
            Transform rim = panel.Find("Rim");
            var rimPlate = rim != null ? rim.GetComponent<RoundedPlate>() : null;
            if (rimPlate != null) rimPlate.Resize(0.316f, height + 0.006f);
        }
    }
}
