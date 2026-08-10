using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// Thin alias over <see cref="UiTokens"/> kept for existing call sites — the single
    /// source of truth for every UI color lives in Core (design/20 §6.2; the doc mirrors
    /// UiTokens). New code should reference UiTokens directly.
    /// </summary>
    public static class UiColors
    {
        // ---- system state colors — reserved forever ----
        public static readonly Color Hover = UiTokens.Hover;
        public static readonly Color Selected = UiTokens.Selected;

        // ---- chrome ----
        public static readonly Color PanelBg = UiTokens.PanelBg;
        public static readonly Color PanelRim = UiTokens.PanelRim;
        public static readonly Color ButtonBg = UiTokens.ButtonBg;
        public static readonly Color ButtonHoverBg = UiTokens.ButtonHoverBg;
        public static readonly Color ButtonDisabledBg = UiTokens.ButtonDisabledBg;
        public static readonly Color LabelLight = UiTokens.LabelLight;
        public static readonly Color LabelDark = UiTokens.LabelDark;
        public const float DisabledLabelAlpha = 0.25f;

        // ---- layer palette reserve (07-mep-layers) — data hues, never states ----
        public static readonly Color LayerStructure = UiTokens.LayerStructure;
        public static readonly Color LayerElectrical = UiTokens.LayerElectrical;
        public static readonly Color LayerHeating = UiTokens.LayerHeating;
        public static readonly Color LayerPlumbing = UiTokens.LayerPlumbing;
        public static readonly Color LayerInterior = UiTokens.LayerInterior;
        public static readonly Color LayerBlueprint = UiTokens.LayerBlueprint;
        public static readonly Color LayerMeasurements = UiTokens.LayerMeasurements;
    }
}
