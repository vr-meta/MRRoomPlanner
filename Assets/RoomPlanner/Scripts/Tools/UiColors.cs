using UnityEngine;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// The UI color system (design/16-ux-v2.md P1.6). Two rules keep it scalable:
    /// STATES are expressed through brightness/shape using the reserved system colors below;
    /// HUE belongs to LAYERS (data). Never use a layer color for a state or vice versa.
    /// </summary>
    public static class UiColors
    {
        // ---- system state colors — reserved forever ----
        public static readonly Color Hover = FromHex(0x7FD9FF);      // targeting/hover (cyan)
        public static readonly Color Selected = FromHex(0x55E6A0);   // selected/active (mint)

        // ---- chrome ----
        public static readonly Color PanelBg = FromHex(0x0F1219);
        public static readonly Color PanelRim = FromHex(0x454C5F);
        public static readonly Color ButtonBg = FromHex(0x292E3D);
        public static readonly Color ButtonHoverBg = FromHex(0x3A4256);
        public static readonly Color ButtonDisabledBg = FromHex(0x1A1E28);
        public static readonly Color LabelLight = FromHex(0xFFFFFF);   // full white — brightness feedback 2026-08-10
        public static readonly Color LabelDark = FromHex(0x10131A);
        public const float DisabledLabelAlpha = 0.25f;

        // ---- layer palette reserve (07-mep-layers) — data hues, never states ----
        public static readonly Color LayerStructure = FromHex(0x9AA7BE);
        public static readonly Color LayerElectrical = FromHex(0xFFC94D);
        public static readonly Color LayerHeating = FromHex(0xFF7A5C);
        public static readonly Color LayerPlumbing = FromHex(0x4DA6FF);
        public static readonly Color LayerInterior = FromHex(0xE0A96B);
        public static readonly Color LayerBlueprint = FromHex(0x8AD0C8);
        public static readonly Color LayerMeasurements = FromHex(0x9B7BFF);

        private static Color FromHex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f);
    }
}
