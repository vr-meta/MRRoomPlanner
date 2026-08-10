using TMPro;
using UnityEngine;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// GLOBAL palette actions only. Per-tool parameter buttons are runtime-bound via
    /// <see cref="MenuButton.OnClick"/> and never appear here (design/14-modularity.md).
    /// </summary>
    public enum MenuAction
    {
        None,
        SelectTool,        // activates the tool at MenuButton.ToolIndex
        ToggleSnapCorner,
        ToggleSnapEdge,
        ToggleSnapGrid,
        ToggleSnapAngle,
        ToggleScan
    }

    /// <summary>Button semantics (design/16 P1.3): one-of selection (tool), on/off toggle
    /// (snap), or a plain action (steppers, cycles). Radio and Toggle must LOOK different —
    /// an active tool inverts, a toggle shows strip + LED.</summary>
    public enum MenuButtonKind { Momentary, Radio, Toggle }

    /// <summary>
    /// Кнопка меню (слой IgnoreRaycast). Действие — либо рантайм-делегат <see cref="OnClick"/>,
    /// либо глобальный <see cref="MenuAction"/>. Визуальные состояния (hover / pressed /
    /// disabled / active) — через MPB-тинт фона и цвет лейбла, материалы не мутируются.
    /// </summary>
    public class MenuButton : MonoBehaviour
    {
        [SerializeField] private MenuAction action;
        [SerializeField] private int toolIndex = -1;   // for MenuAction.SelectTool
        [SerializeField] private MenuButtonKind kind = MenuButtonKind.Momentary;
        [SerializeField] private GameObject activeMark;
        [SerializeField] private GameObject ledDot;    // Toggle ON indicator
        [SerializeField] private Renderer bgRenderer;  // tinted per state (MPB)
        [SerializeField] private TMP_Text label;

        public MenuAction Action => action;
        public int ToolIndex => toolIndex;
        public MenuButtonKind Kind => kind;

        /// <summary>Runtime-bound handler (schema rows); takes precedence over Action.</summary>
        public System.Action OnClick;

        /// <summary>Holding the trigger auto-repeats OnClick (stepper −/+ rows; UX v2 P0.2).</summary>
        public bool Repeatable;

        /// <summary>Full-word hint shown by the palette while hovered — the button labels
        /// are abbreviations ("Sel") and need explaining (headset feedback 2026-08-10).</summary>
        [SerializeField] private string tooltip;
        public string Tooltip
        {
            get => tooltip;
            set => tooltip = value;
        }

        /// <summary>Disabled buttons keep their collider but ignore hover/press.</summary>
        public bool Interactable => _enabled;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private const float PressFlash = 0.08f;

        private Vector3 _baseScale;
        private bool _hovered;
        private bool _on;              // Radio: active tool; Toggle: switched on
        private bool _enabled = true;
        private float _pressedAt = -10f;
        private MaterialPropertyBlock _mpb;

        private void Awake()
        {
            _baseScale = transform.localScale;
            _mpb = new MaterialPropertyBlock();
            Apply();
        }

        private void Update()
        {
            // let the press flash decay back to the resting state
            if (_pressedAt > 0f && Time.time - _pressedAt <= PressFlash + 0.02f) Apply();
        }

        /// <summary>Wire the visual refs for buttons created at runtime (inspector rows).</summary>
        public void InitRuntime(MenuButtonKind buttonKind, Renderer background, TMP_Text buttonLabel)
        {
            kind = buttonKind;
            bgRenderer = background;
            label = buttonLabel;
            Apply();
        }

        public void SetHighlight(bool on)
        {
            if (!_enabled) on = false;
            if (_hovered == on) return;
            _hovered = on;
            Apply();
        }

        /// <summary>Radio: active tool; Toggle: switched on. (Name kept for call-site compat.)</summary>
        public void SetActiveTool(bool on)
        {
            _on = on;
            if (activeMark != null) activeMark.SetActive(on);
            if (ledDot != null) ledDot.SetActive(on && kind == MenuButtonKind.Toggle);
            Apply();
        }

        /// <summary>Visual click confirmation — a short dip + brightness flash (UX v2 P1.3).</summary>
        public void Press()
        {
            _pressedAt = Time.time;
            Apply();
        }

        public void SetEnabled(bool on)
        {
            _enabled = on;
            if (!on) _hovered = false;
            Apply();
        }

        private void Apply()
        {
            bool flashing = _pressedAt > 0f && Time.time - _pressedAt < PressFlash;
            // pressed dips; hover lifts slightly (1.06 — the old 1.12 overlapped neighbours)
            float s = flashing ? 0.97f : _hovered ? 1.06f : 1f;
            transform.localScale = _baseScale * s;

            // Radio active = inverted button (light bg, dark text) — reads at a glance and
            // stays distinct from a snap toggle's strip+LED.
            bool inverted = _on && kind == MenuButtonKind.Radio;
            Color bg = !_enabled ? UiColors.ButtonDisabledBg
                : inverted ? UiColors.LabelLight
                : _hovered ? UiColors.ButtonHoverBg
                : UiColors.ButtonBg;
            if (flashing) bg = Color.Lerp(bg, Color.white, 0.35f);

            if (bgRenderer != null)
            {
                bgRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, bg);
                _mpb.SetColor(ColorId, bg);
                bgRenderer.SetPropertyBlock(_mpb);
            }
            if (label != null)
            {
                Color lc = inverted ? UiColors.LabelDark : UiColors.LabelLight;
                if (!_enabled) lc.a = UiColors.DisabledLabelAlpha;
                label.color = lc;
            }
        }
    }
}
