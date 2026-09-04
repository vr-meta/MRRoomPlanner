namespace RoomPlanner.Plumbing
{
    /// <summary>Drain pipe assortment v1 (docs/design/30-plumbing.md): riser/toilet 110,
    /// sink/shower/washer runs 50, appliance tails 40.</summary>
    public enum PipeDiameter { D110, D50, D40 }

    /// <summary>Point elements of the Plumbing layer: stub-outs for the toilet and the
    /// sink family, and the floor drain under showers/washing machines.</summary>
    public enum PlumbFixtureKind { ToiletOutlet, SinkOutlet, FloorDrain }

    /// <summary>Stub-out geometry: straight out of the wall, the classic angled-down
    /// elbow toward a riser, or its mirrored rise (washer standpipes, vents — #114).</summary>
    public enum OutletAngle { Deg90, Deg45, Deg45Up }

    public static class PipeSpec
    {
        public const int TypeCount = 3;

        public static float Radius(PipeDiameter d) => d switch
        {
            PipeDiameter.D110 => 0.055f,
            PipeDiameter.D50 => 0.025f,
            _ => 0.020f,
        };

        public static string Label(PipeDiameter d) => d switch
        {
            PipeDiameter.D110 => "110",
            PipeDiameter.D50 => "50",
            _ => "40",
        };
    }

    /// <summary>Shared presets and limits of the Plumbing layer, mirroring
    /// ElectricalDefaults. Heights are measured from the storey level to the stub axis.</summary>
    public static class PlumbingDefaults
    {
        // stub-out mounting height presets (meters above storey level, to the pipe axis)
        public const float ToiletOutletHeight = 0.18f;
        public const float SinkOutletHeight = 0.45f;
        public const float MinOutletHeight = 0.05f;
        public const float MaxOutletHeight = 1.20f;
        public const float HeightStep = 0.05f;

        // stub-out geometry
        public const float StubLength = 0.15f;         // straight 90-degree stub
        public const float Stub45Run = 0.08f;          // wall exit before the 45-degree elbow
        public const float Stub45Drop = 0.10f;         // elbow leg length
        public const float SocketFlare = 1.25f;        // bell mouth radius multiplier

        // floor drain
        public const float DrainSize = 0.15f;          // square grate side
        public const float DrainDepth = 0.08f;         // body sunk below the floor plane
        public const float DrainPortLength = 0.10f;    // D50 side port

        // dirty-input protection (coding rule 1.3)
        public const float MinPointStep = 0.03f;
        public const float PlaceDebounceSeconds = 0.25f;
        public const float TerminalSnapRadius = 0.10f;
        // the drain port sits low in a grate corner — headset feedback 2026-08-15 (#115):
        // the standard radius was too fiddly to hit with a ray
        public const float DrainSnapRadius = 0.15f;
        public const float FixtureClearance = 0.05f;

        // BOM
        public const float ConnectionAllowance = 0.15f;
        public const int DefaultReservePercent = 10;
        public const int MaxReservePercent = 30;
        public const int ReserveStep = 5;

        public static float PresetHeight(PlumbFixtureKind kind) =>
            kind == PlumbFixtureKind.ToiletOutlet ? ToiletOutletHeight : SinkOutletHeight;
    }
}
