namespace RoomPlanner.Electrical
{
    /// <summary>Point elements of the Electrical layer (docs/design/19-electrical.md).
    /// Junction (v2) is the ceiling/wall distribution box wires branch through.</summary>
    public enum FixtureKind { Outlet, Switch, Panel, Junction }

    /// <summary>Cable assortment v1: lighting 3x1.5, power outlets 3x2.5.</summary>
    public enum CableType { C3x15, C3x25 }

    public static class Cable
    {
        public const int TypeCount = 2;

        public static string Label(CableType t) => t == CableType.C3x15 ? "3x1.5" : "3x2.5";

        public static CableType Next(CableType t) =>
            t == CableType.C3x15 ? CableType.C3x25 : CableType.C3x15;

        /// <summary>Default circuit cable by the fixture a route starts from (trade practice,
        /// not a smart feature: switches feed lighting 1.5 mm², outlets need 2.5 mm²).</summary>
        public static CableType DefaultFor(FixtureKind kind) =>
            kind == FixtureKind.Switch ? CableType.C3x15 : CableType.C3x25;
    }

    /// <summary>Shared presets and limits of the Electrical layer. Heights are measured
    /// from the storey level (ToolManager.Level) to the fixture center.</summary>
    public static class ElectricalDefaults
    {
        // mounting height presets (meters above storey level)
        public const float OutletHeight = 0.30f;
        public const float SwitchHeight = 0.90f;
        public const float PanelHeight = 1.50f;
        public const float MinOutletHeight = 0.10f;
        public const float MinSwitchHeight = 0.60f;
        public const float MaxMountHeight = 1.80f;
        public const float HeightStep = 0.05f;

        // wire routing
        public const float CeilingOffset = 0.20f;      // horizontal run distance below the ceiling
        // 1 cm = the Ø15 tube presses right against the ceiling (headset request 2026-08-10)
        public const float MinCeilingOffset = 0.01f;
        public const float MaxCeilingOffset = 0.50f;
        public const float CeilingOffsetStep = 0.05f;

        // dirty-input protection (coding rule 1.3)
        public const float MinPointStep = 0.03f;       // clicks closer than this are ignored
        public const float PlaceDebounceSeconds = 0.25f;
        public const float TerminalSnapRadius = 0.10f;
        public const float FixtureClearance = 0.05f;   // min gap between fixture blocks

        // fixture block dimensions (meters)
        public const float PostModule = 0.08f;         // one outlet post / switch frame module
        public const float PlateDepth = 0.012f;
        public const int MaxPosts = 5;
        public const int MaxKeys = 3;
        public const float PanelBoxWidth = 0.30f;
        public const float PanelBoxHeight = 0.40f;
        public const float PanelBoxDepth = 0.08f;
        // v2 junction box: mounts on walls AND ceilings, free height, no preset
        public const float JunctionBoxSize = 0.08f;
        public const float JunctionBoxDepth = 0.045f;

        // BOM
        public const int DefaultReservePercent = 10;
        public const int MaxReservePercent = 30;
        public const int ReserveStep = 5;

        public static float PresetHeight(FixtureKind kind) => kind switch
        {
            FixtureKind.Outlet => OutletHeight,
            FixtureKind.Switch => SwitchHeight,
            _ => PanelHeight,
        };
    }
}
