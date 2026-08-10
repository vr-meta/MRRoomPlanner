using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>
    /// The design tokens — single source of truth for every UI color, size and timing
    /// (docs/design/20-ui-design-system.md §3, §4, §6; the doc mirrors this class).
    /// Rules that keep it scalable: STATES are brightness/achromatic + the three reserved
    /// system colors; HUE belongs to data layers; alpha is allowed only for disabled
    /// content, the radial scrim and ≤150 ms fades.
    /// </summary>
    public static class UiTokens
    {
        // ---- system state colors — reserved forever, never layer hues ----
        public static readonly Color Hover = FromHex(0x7FD9FF);        // targeting/hover (cyan)
        public static readonly Color Selected = FromHex(0x55E6A0);     // selected/active (mint)
        public static readonly Color FocusRing = FromHex(0xA8E4FF);    // focus outline (lighter than Hover)
        public static readonly Color Danger = FromHex(0xE0566B);       // destructive (crimson ~352° — far from Heating ~10°)
        public static readonly Color DangerHover = FromHex(0xFF7B8E);
        public static readonly Color DangerTint = FromHex(0x35222B);   // armed destructive button bg

        // ---- surfaces ----
        public static readonly Color PanelBg = FromHex(0x0F1219);
        public static readonly Color PanelRim = FromHex(0x454C5F);
        public static readonly Color ButtonBg = FromHex(0x292E3D);
        public static readonly Color ButtonHoverBg = FromHex(0x3A4256);
        public static readonly Color ButtonDisabledBg = FromHex(0x1A1E28);
        public static readonly Color InsetBg = FromHex(0x1C2130);      // "carved-in": slider tracks, fields, wells
        public static readonly Color Divider = FromHex(0x2A3040);      // solid hairlines, no alpha

        // ---- text ----
        public static readonly Color LabelLight = FromHex(0xFFFFFF);
        public static readonly Color LabelDark = FromHex(0x10131A);    // on Selected/Danger fills
        public static readonly Color LabelDim = FromHex(0x8A93A8);     // group headers, hints, units
        public const float DisabledAlpha = 0.40f;

        // ---- controls ----
        public static readonly Color TrackFill = FromHex(0x6E7A94);        // slider fill, idle
        public static readonly Color TrackFillActive = FromHex(0x7FD9FF);  // slider fill while grabbed (= Hover)
        public static readonly Color Knob = FromHex(0xFFFFFF);
        public static readonly Color ToggleOnBg = FromHex(0x2E5B48);       // Selected premixed into PanelBg, solid

        // ---- layer palette (07-mep-layers) — data hues, never states ----
        public static readonly Color LayerStructure = FromHex(0x9AA7BE);
        public static readonly Color LayerElectrical = FromHex(0xFFC94D);
        public static readonly Color LayerHeating = FromHex(0xFF7A5C);
        public static readonly Color LayerPlumbing = FromHex(0x4DA6FF);
        public static readonly Color LayerInterior = FromHex(0xE0A96B);
        public static readonly Color LayerBlueprint = FromHex(0x8AD0C8);
        public static readonly Color LayerMeasurements = FromHex(0x9B7BFF);

        // ---- panel grid (§3): canonical distance 0.65 m, 1° ≈ 0.0113 m ----
        public const float Grid = 0.006f;            // everything is a multiple of this
        public const float PanelWidth = 0.288f;      // ≈25°
        public const float RowHeight = 0.030f;       // 2.6°
        public const float RowStep = 0.036f;         // row + 6 mm gap
        public const float PanelPadding = 0.012f;
        public const float TitleBarHeight = 0.036f;
        public const float CaptionColumn = 0.42f;    // caption 42% / control 58%
        public const float MinTarget = 0.023f;       // 2.0° — collider floor for ray input
        public const float TargetGap = 0.006f;       // collider-to-collider minimum

        // ---- radii / rims (m) ----
        public const float RadiusS = 0.003f;         // chips, swatches, checkboxes
        public const float RadiusM = 0.006f;         // buttons, fields — the default plate
        public const float RadiusL = 0.010f;         // panels, popovers
        public const float RimHairline = 0.001f;
        public const float RimFocus = 0.002f;
        public const float RimActive = 0.003f;

        // ---- elevation (z toward viewer = negative; 6 mm steps, §6.3) ----
        public const float ZPanelBg = +0.006f;
        public const float ZPlate = 0f;
        public const float ZContent = -0.006f;
        public const float ZPopoverPlate = -0.012f;
        public const float ZPopoverContent = -0.018f;
        public const float ZModalPlate = -0.024f;
        public const float ZModalContent = -0.030f;

        // ---- radial menu (§1, §6.5) ----
        // 0.165 (was 0.11): device feedback 2026-08-10 — the wheel read too small; ×1.5
        // keeps every derived proportion (hub/icons/scrim) via the shared base constant
        public const float RadialOuterRadius = 0.165f;   // at 0.6 m spawn distance
        public const float RadialInnerRadius = RadialOuterRadius * 0.45f;
        public const float RadialHubRadius = RadialOuterRadius * 0.36f;
        public const float RadialIconRadius = RadialOuterRadius * 0.72f;
        public const float RadialSectorGapDeg = 2.5f;
        public const float RadialScrimRadius = RadialOuterRadius * 1.25f;
        public const float RadialScrimAlpha = 0.55f;
        public const float RadialSpawnDistance = 0.60f;

        // ---- motion budget (§4): nothing above 150 ms except the destructive hold ----
        public const float HoverInSeconds = 0.06f;
        public const float HoverOutSeconds = 0.09f;
        public const float PressFlashSeconds = 0.08f;
        public const float ToggleSlideSeconds = 0.06f;
        public const float PopupOpenSeconds = 0.10f;
        public const float PopupCloseSeconds = 0.08f;
        public const float RadialOpenSeconds = 0.12f;
        public const float RadialCloseSeconds = 0.08f;
        public const float PanelFadeSeconds = 0.15f;
        public const float DestructiveHoldSeconds = 0.5f;
        public const float DestructiveDisarmSeconds = 3f;
        public const float PostCloseDebounceSeconds = 0.15f;
        public const float TooltipDelaySeconds = 0.35f;

        private static Color FromHex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f);
    }
}
