using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>
    /// The 6 mm panel grid (design/20 §3) as pure math: row positions, auto-height,
    /// column split and the slider's value↔position mapping. The inspector renders FROM
    /// this; nothing here touches a scene. All units are real meters — the v2 panel is
    /// built at scale 1, not scaled up after the fact.
    /// </summary>
    public static class PanelLayout
    {
        /// <summary>
        /// One numpad keypress applied to the entry string — pure, so the editing rules
        /// are testable (audit 10 §Б1: the pad had no decimal point and zero tests).
        /// "." or "," starts canonical "0." on an empty/sign-only entry and is refused
        /// once present. Storage stays invariant; the popup localizes it for display.
        /// </summary>
        public static string NumpadPress(string entry, string key, int maxLen = 7)
        {
            entry ??= "";
            switch (key)
            {
                case "⌫": return entry.Length > 0 ? entry.Substring(0, entry.Length - 1) : entry;
                case "±": return entry.StartsWith("-") ? entry.Substring(1) : "-" + entry;
                case ".":
                case ",":
                    if (entry.Contains(".") || entry.Length >= maxLen) return entry;
                    return entry.Length == 0 || entry == "-" ? entry + "0." : entry + ".";
                default: return entry.Length < maxLen ? entry + key : entry;
            }
        }

        /// <summary>Render a canonical numpad entry using the culture's decimal separator.</summary>
        public static string LocalizeNumpadEntry(string entry, string decimalSeparator)
        {
            entry ??= "";
            return string.IsNullOrEmpty(decimalSeparator) || decimalSeparator == "."
                ? entry : entry.Replace(".", decimalSeparator);
        }

        public const float Width = UiTokens.PanelWidth;                 // 0.288
        public const float ContentWidth = Width - 2f * UiTokens.PanelPadding;
        public const float CaptionX = -Width * 0.5f + UiTokens.PanelPadding;   // left edge of captions
        public const float ControlRight = Width * 0.5f - UiTokens.PanelPadding;
        public const float ControlLeft = CaptionX + ContentWidth * UiTokens.CaptionColumn;
        public const float ControlWidth = ControlRight - ControlLeft;

        public const float TabStripHeight = UiTokens.RowStep;

        /// <summary>Y of a row's center, measured down from the panel's top edge.
        /// Layout: padding · title bar · [tab strip] · rows · padding.</summary>
        public static float RowY(int rowIndex, bool hasTabs)
        {
            float y = UiTokens.PanelPadding + UiTokens.TitleBarHeight;
            if (hasTabs) y += TabStripHeight;
            return y + rowIndex * UiTokens.RowStep + UiTokens.RowHeight * 0.5f;
        }

        /// <summary>Full panel height for a row count (auto-height, §3.3). No artificial
        /// floor: padding + title bar already is the natural minimum.</summary>
        public static float PanelHeight(int rows, bool hasTabs)
        {
            float h = 2f * UiTokens.PanelPadding + UiTokens.TitleBarHeight;
            if (hasTabs) h += TabStripHeight;
            if (rows > 0) h += rows * UiTokens.RowStep - (UiTokens.RowStep - UiTokens.RowHeight);
            return h;
        }

        public static float VisiblePanelHeight(float contentHeight) =>
            Mathf.Min(contentHeight, UiTokens.MaxPanelHeight);

        public static float ScrollRange(float contentHeight) =>
            Mathf.Max(0f, contentHeight - UiTokens.MaxPanelHeight);

        public static float ScrollBy(float current, float stickY, float deltaTime,
            float contentHeight, float speed = 0.18f)
        {
            if (Mathf.Abs(stickY) < 0.20f) return Mathf.Clamp(current, 0f, ScrollRange(contentHeight));
            return Mathf.Clamp(current - stickY * deltaTime * speed,
                0f, ScrollRange(contentHeight));
        }

        // ---- slider mapping (§2.2) ----

        /// <summary>Normalized handle position for a value.</summary>
        public static float SliderT(float value, float min, float max)
        {
            if (max <= min) return 0f;
            return Mathf.Clamp01((value - min) / (max - min));
        }

        /// <summary>Value at a normalized position, snapped to the detent step
        /// (step ≤ 0 → continuous) and clamped to the range.</summary>
        public static float SliderValue(float t, float min, float max, float step)
        {
            float v = min + Mathf.Clamp01(t) * (max - min);
            if (step > 0f) v = min + Mathf.Round((v - min) / step) * step;
            return Mathf.Clamp(v, min, max);
        }

        /// <summary>Fine-mode drag (§2.2): grip held scales pointer travel ×0.1 around
        /// the value where fine mode engaged.</summary>
        public const float FineGain = 0.1f;

        // ---- list scrolling (§3.7) ----

        public const int MaxVisibleOptions = 7;

        /// <summary>First visible option index so <paramref name="selected"/> stays in view.</summary>
        public static int ScrollTo(int selected, int count, int visible = MaxVisibleOptions)
        {
            if (count <= visible) return 0;
            int first = selected - visible / 2;
            return Mathf.Clamp(first, 0, count - visible);
        }

        /// <summary>Clamp a scroll offset to the valid window.</summary>
        public static int ClampScroll(int first, int count, int visible = MaxVisibleOptions)
            => Mathf.Clamp(first, 0, Mathf.Max(0, count - visible));
    }
}
