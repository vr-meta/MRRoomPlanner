using System.Collections.Generic;

namespace RoomPlanner.Core
{
    /// <summary>
    /// The icon set (docs/design/20-ui-design-system.md §6.1) — original-drawn SVG path
    /// data, 24×24 grid, filled silhouettes with even-odd counters. Grammar contract:
    /// absolute M/L/H/V/C/Q/Z only (no arcs), holes strictly inside their outer ring,
    /// islands may share an edge but never partially overlap. This class IS the source
    /// of truth for icon art; meshes are built from it by <see cref="SvgTessellator"/>.
    /// </summary>
    public static class IconPaths
    {
        // ---- tool icons ----

        /// <summary>Classic pointer with tail.</summary>
        public const string SelectCursor =
            "M7 2 L7 18 L11 14.5 L13.5 20.5 L16.5 19.2 L14 13.5 L19 13 Z";

        /// <summary>Reel with hub hole + tape tongue.</summary>
        public const string TapeMeasure =
            "M10 4 C14.42 4 18 7.58 18 12 C18 16.42 14.42 20 10 20 C5.58 20 2 16.42 2 12 C2 7.58 5.58 4 10 4 Z " +
            "M10 9 C11.66 9 13 10.34 13 12 C13 13.66 11.66 15 10 15 C8.34 15 7 13.66 7 12 C7 10.34 8.34 9 10 9 Z " +
            "M16.5 17 H22 V20.5 H16.5 Z";

        /// <summary>Running-bond brick courses.</summary>
        public const string Wall =
            "M2 5 H11 V9 H2 Z M13 5 H22 V9 H13 Z " +
            "M2 10 H6.5 V14 H2 Z M8.5 10 H15.5 V14 H8.5 Z M17.5 10 H22 V14 H17.5 Z " +
            "M2 15 H11 V19 H2 Z M13 15 H22 V19 H13 Z";

        /// <summary>Isometric slab: top face + thickness skirt.</summary>
        public const string FloorSlab =
            "M3 11.5 L12 6.5 L21 11.5 L12 16.5 Z " +
            "M3 13.5 L3 15.5 L12 20.5 L21 15.5 L21 13.5 L12 18.5 Z";

        /// <summary>Sheet with a room-outline groove and a title line.</summary>
        public const string Blueprint =
            "M4 3 H20 V21 H4 Z M7 7 H17 V17 H7 Z M9.5 9.5 H14.5 V14.5 H9.5 Z M7 18.5 H13 V19.8 H7 Z";

        /// <summary>Page with a corner fold and a down-arrow cutout.</summary>
        public const string ImportFile =
            "M5 2 H14 V8 H20 V22 H5 Z M15.5 2 L20 6.5 L15.5 6.5 Z " +
            "M11 10 H13 V14 H15.5 L12 18 L8.5 14 H11 Z";

        /// <summary>Two prongs, round-bottom body, cord stub.</summary>
        public const string ElectricPlug =
            "M8 2 H10 V7 H8 Z M14 2 H16 V7 H14 Z " +
            "M6 7 H18 V12 C18 15.31 15.31 18 12 18 C8.69 18 6 15.31 6 12 Z " +
            "M11 18.5 H13 V22 H11 Z";

        /// <summary>Sleeve, frame, handle.</summary>
        public const string PaintRoller =
            "M3 3 H19 V9 H3 Z M19 5 H21 V13 H13 V11 H19 Z M12 13 H14 V21 H12 Z";

        // ---- reserved tool icons (radial slots for future tools) ----

        /// <summary>Door leaf with knob hole + 4-pane window.</summary>
        public const string DoorWindow =
            "M3 4 H11 V20 H3 Z " +
            "M9 11 C9.55 11 10 11.45 10 12 C10 12.55 9.55 13 9 13 C8.45 13 8 12.55 8 12 C8 11.45 8.45 11 9 11 Z " +
            "M13 4 H21 V20 H13 Z " +
            "M14.5 5.5 H16.5 V11 H14.5 Z M17.5 5.5 H19.5 V11 H17.5 Z " +
            "M14.5 13 H16.5 V18.5 H14.5 Z M17.5 13 H19.5 V18.5 H17.5 Z";

        /// <summary>Sofa with cushion slot and legs.</summary>
        public const string Furniture =
            "M2 8 C2 6.9 2.9 6 4 6 H20 C21.1 6 22 6.9 22 8 V18 H18 V16 H6 V18 H2 Z " +
            "M5 12 H19 V13.5 H5 Z";

        /// <summary>Top manifold + five fins.</summary>
        public const string Radiator =
            "M2 3 H22 V5 H2 Z " +
            "M3 5 H5 V19 H3 Z M7 5 H9 V19 H7 Z M11 5 H13 V19 H11 Z M15 5 H17 V19 H15 Z M19 5 H21 V19 H19 Z";

        /// <summary>Flanged elbow.</summary>
        public const string Pipe =
            "M4 2 H13 V4 H4 Z M6 4 H11 V13 H20 V18 H6 Z M20 11 H22 V20 H20 Z";

        /// <summary>Staircase profile, four steps.</summary>
        public const string Stairs =
            "M3 21 V17 H8 V13 H13 V9 H18 V5 H21 V21 Z";

        // ---- electrical sub-mode icons (the Electric tool's tab strip) ----

        /// <summary>Round socket face with two pin holes.</summary>
        public const string Outlet =
            "M12 3 C16.97 3 21 7.03 21 12 C21 16.97 16.97 21 12 21 C7.03 21 3 16.97 3 12 C3 7.03 7.03 3 12 3 Z " +
            "M8.6 10.3 C9.54 10.3 10.3 11.06 10.3 12 C10.3 12.94 9.54 13.7 8.6 13.7 C7.66 13.7 6.9 12.94 6.9 12 C6.9 11.06 7.66 10.3 8.6 10.3 Z " +
            "M15.4 10.3 C16.34 10.3 17.1 11.06 17.1 12 C17.1 12.94 16.34 13.7 15.4 13.7 C14.46 13.7 13.7 12.94 13.7 12 C13.7 11.06 14.46 10.3 15.4 10.3 Z";

        /// <summary>Rocker plate with the key split.</summary>
        public const string Switch =
            "M5 3.5 H19 V20.5 H5 Z M7.5 11.3 H16.5 V12.7 H7.5 Z";

        /// <summary>Ortho-routed cable elbow (the way our wires actually run).</summary>
        public const string Wire =
            "M4 20 V10 C4 6.7 6.7 4 10 4 H20 V7 H10 C8.3 7 7 8.3 7 10 V20 Z";

        /// <summary>Square distribution box: lid frame (evenodd hole), four cable entries,
        /// center terminal — the v2 junction box wires branch through.</summary>
        public const string JunctionBox =
            "M4.5 4.5 H19.5 V19.5 H4.5 Z M7 7 V17 H17 V7 Z " +
            "M10.9 1.2 H13.1 V4.5 H10.9 Z M10.9 19.5 H13.1 V22.8 H10.9 Z " +
            "M1.2 10.9 H4.5 V13.1 H1.2 Z M19.5 10.9 H22.8 V13.1 H19.5 Z " +
            "M10.6 10.6 H13.4 V13.4 H10.6 Z";

        /// <summary>Cabinet with a breaker row, main switch and indicator.</summary>
        public const string BreakerPanel =
            "M4 3 H20 V21 H4 Z " +
            "M6.5 6 H9 V10 H6.5 Z M10.75 6 H13.25 V10 H10.75 Z M15 6 H17.5 V10 H15 Z " +
            "M6.5 13 H12 V18 H6.5 Z " +
            "M15.8 14.1 C16.57 14.1 17.2 14.73 17.2 15.5 C17.2 16.27 16.57 16.9 15.8 16.9 C15.03 16.9 14.4 16.27 14.4 15.5 C14.4 14.73 15.03 14.1 15.8 14.1 Z";

        // ---- UI glyphs ----

        public const string Plus =
            "M10.5 4 H13.5 V10.5 H20 V13.5 H13.5 V20 H10.5 V13.5 H4 V10.5 H10.5 Z";

        public const string Minus = "M4 10.5 H20 V13.5 H4 Z";

        public const string ChevronRight =
            "M9 4.5 L16.5 12 L9 19.5 L6.9 17.4 L12.3 12 L6.9 6.6 Z";

        public const string ChevronLeft =
            "M15 4.5 L7.5 12 L15 19.5 L17.1 17.4 L11.7 12 L17.1 6.6 Z";

        public const string ChevronDown =
            "M4.5 9 L12 16.5 L19.5 9 L17.4 6.9 L12 12.3 L6.6 6.9 Z";

        public const string Check =
            "M9.5 19 L3.5 13 L5.6 10.9 L9.5 14.8 L18.4 5.9 L20.5 8 Z";

        public const string Cross =
            "M12 9.9 L18.4 3.5 L20.5 5.6 L14.1 12 L20.5 18.4 L18.4 20.5 L12 14.1 L5.6 20.5 L3.5 18.4 L9.9 12 L3.5 5.6 L5.6 3.5 Z";

        /// <summary>Almond + pupil hole.</summary>
        public const string Eye =
            "M12 6 C16.9 6 20.9 8.9 22 12 C20.9 15.1 16.9 18 12 18 C7.1 18 3.1 15.1 2 12 C3.1 8.9 7.1 6 12 6 Z " +
            "M12 8.8 C13.77 8.8 15.2 10.23 15.2 12 C15.2 13.77 13.77 15.2 12 15.2 C10.23 15.2 8.8 13.77 8.8 12 C8.8 10.23 10.23 8.8 12 8.8 Z";

        /// <summary>Closed eyelid with lashes (NOT eye+slash — a slash would partially
        /// overlap the eye, which the tessellation contract forbids).</summary>
        public const string EyeOff =
            "M2.5 9.5 C6 14.8 18 14.8 21.5 9.5 L19.4 8.1 C16.6 12.1 7.4 12.1 4.6 8.1 Z " +
            "M10.9 15 H13.1 V19 H10.9 Z " +
            "M5.5 12.4 L7.3 13.1 L5.9 16.9 L4.1 16.2 Z " +
            "M18.5 12.4 L16.7 13.1 L18.1 16.9 L19.9 16.2 Z";

        /// <summary>Shackle band + body + keyhole.</summary>
        public const string Lock =
            "M7 11 V9 C7 6.24 9.24 4 12 4 C14.76 4 17 6.24 17 9 V11 H14.5 V9 C14.5 7.62 13.38 6.5 12 6.5 C10.62 6.5 9.5 7.62 9.5 9 V11 Z " +
            "M5 11 H19 V21 H5 Z " +
            "M12 13.6 C12.88 13.6 13.6 14.32 13.6 15.2 C13.6 16.08 12.88 16.8 12 16.8 C11.12 16.8 10.4 16.08 10.4 15.2 C10.4 14.32 11.12 13.6 12 13.6 Z";

        /// <summary>Lid + handle + tapered body with slot holes.</summary>
        public const string Trash =
            "M9.5 3 H14.5 V5 H9.5 Z M4 5 H20 V7.5 H4 Z " +
            "M5.5 9 L6.8 21 H17.2 L18.5 9 Z " +
            "M9.5 11.5 H11 V18.5 H9.5 Z M13 11.5 H14.5 V18.5 H13 Z";

        /// <summary>Front sheet + back L-frame.</summary>
        public const string Copy =
            "M8 8 H20 V20 H8 Z M4 4 H16 V6.5 H6.5 V16 H4 Z";

        /// <summary>Left arrowhead + clockwise band.</summary>
        public const string Undo =
            "M2.5 8.25 L8 3.5 V13 Z " +
            "M8 6.5 H14 C17.87 6.5 21 9.63 21 13.5 V20 H17.5 V13.5 C17.5 11.57 15.93 10 14 10 H8 Z";

        public const string Redo =
            "M21.5 8.25 L16 3.5 V13 Z " +
            "M16 6.5 H10 C6.13 6.5 3 9.63 3 13.5 V20 H6.5 V13.5 C6.5 11.57 8.07 10 10 10 H16 Z";

        /// <summary>6-tooth gear + hub hole.</summary>
        public const string Gear =
            "M8.91 5.06 L9.58 2.3 L14.42 2.3 L15.09 5.06 L16.47 5.85 L19.19 5.05 L21.61 9.24 L19.56 11.21 L19.56 12.79 L21.61 14.76 L19.19 18.95 L16.47 18.15 L15.09 18.94 L14.42 21.7 L9.58 21.7 L8.91 18.94 L7.53 18.15 L4.81 18.95 L2.39 14.76 L4.44 12.79 L4.44 11.21 L2.39 9.24 L4.81 5.05 L7.53 5.85 Z " +
            "M12 8.6 C13.88 8.6 15.4 10.12 15.4 12 C15.4 13.88 13.88 15.4 12 15.4 C10.12 15.4 8.6 13.88 8.6 12 C8.6 10.12 10.12 8.6 12 8.6 Z";

        // snap family — shared language: the snap POINT is a diamond/square marker,
        // the geometry it snaps to is drawn as bands

        /// <summary>2×2 grid, diamond at the crossing.</summary>
        public const string GridSnap =
            "M3 3 H10 V10 H3 Z M14 3 H21 V10 H14 Z M3 14 H10 V21 H3 Z M14 14 H21 V21 H14 Z " +
            "M12 8.5 L15.5 12 L12 15.5 L8.5 12 Z";

        /// <summary>Square marker at the vertex of two wall bands.</summary>
        public const string CornerSnap =
            "M3 14 H10 V21 H3 Z M4.75 3 H8.25 V12.5 H4.75 Z M11.5 15.75 H21 V19.25 H11.5 Z";

        /// <summary>Diamond descending onto an edge band.</summary>
        public const string EdgeSnap =
            "M3 16.5 H21 V19.5 H3 Z M12 5.5 L16.5 10 L12 14.5 L7.5 10 Z";

        /// <summary>Protractor half-disc with two ray slots.</summary>
        public const string AngleSnap =
            "M22 19 C22 13.48 17.52 9 12 9 C6.48 9 2 13.48 2 19 Z " +
            "M13.2 15.6 L17.4 11.4 L18.7 12.7 L14.5 16.9 Z " +
            "M14.5 17.3 H19.5 V18.6 H14.5 Z";

        /// <summary>Emitter dot + two quarter-arc bands.</summary>
        public const string Scan =
            "M5 16.8 C6.21 16.8 7.2 17.79 7.2 19 C7.2 20.21 6.21 21.2 5 21.2 C3.79 21.2 2.8 20.21 2.8 19 C2.8 17.79 3.79 16.8 5 16.8 Z " +
            "M5 9.2 C10.41 9.2 14.8 13.59 14.8 19 H12 C12 15.13 8.87 12 5 12 Z " +
            "M5 4.2 C13.17 4.2 19.8 10.83 19.8 19 H17 C17 12.37 11.63 7 5 7 Z";

        /// <summary>Disc, thumb hole, three paint-well holes.</summary>
        public const string Palette =
            "M12 2 C17.52 2 22 6.48 22 12 C22 17.52 17.52 22 12 22 C6.48 22 2 17.52 2 12 C2 6.48 6.48 2 12 2 Z " +
            "M16 13 C17.66 13 19 14.34 19 16 C19 17.66 17.66 19 16 19 C14.34 19 13 17.66 13 16 C13 14.34 14.34 13 16 13 Z " +
            "M12 4.4 C12.88 4.4 13.6 5.12 13.6 6 C13.6 6.88 12.88 7.6 12 7.6 C11.12 7.6 10.4 6.88 10.4 6 C10.4 5.12 11.12 4.4 12 4.4 Z " +
            "M7.5 6.9 C8.38 6.9 9.1 7.62 9.1 8.5 C9.1 9.38 8.38 10.1 7.5 10.1 C6.62 10.1 5.9 9.38 5.9 8.5 C5.9 7.62 6.62 6.9 7.5 6.9 Z " +
            "M6 11.9 C6.88 11.9 7.6 12.62 7.6 13.5 C7.6 14.38 6.88 15.1 6 15.1 C5.12 15.1 4.4 14.38 4.4 13.5 C4.4 12.62 5.12 11.9 6 11.9 Z";

        /// <summary>Bulb + diagonal shaft + tip.</summary>
        public const string Dropper =
            "M17.5 3 C19.43 3 21 4.57 21 6.5 C21 8.43 19.43 10 17.5 10 C15.57 10 14 8.43 14 6.5 C14 4.57 15.57 3 17.5 3 Z " +
            "M13.88 8.28 L15.72 10.12 L5.42 20.42 L3.58 18.58 Z " +
            "M5.42 20.42 L3.58 18.58 L2.6 21.4 Z";

        /// <summary>Fist: palm + three curled fingers.</summary>
        public const string HandGrab =
            "M6.5 9 H17.5 C18.6 9 19.5 9.9 19.5 11 V16 C19.5 18.21 17.71 20 15.5 20 H8.5 C6.29 20 4.5 18.21 4.5 16 V11 C4.5 9.9 5.4 9 6.5 9 Z " +
            "M6.6 9 V7.2 C6.6 6.43 7.22 5.8 8 5.8 C8.78 5.8 9.4 6.43 9.4 7.2 V9 Z " +
            "M10 9 V6.4 C10 5.63 10.62 5 11.4 5 C12.18 5 12.8 5.63 12.8 6.4 V9 Z " +
            "M13.4 9 V7.2 C13.4 6.43 14.02 5.8 14.8 5.8 C15.58 5.8 16.2 6.43 16.2 7.2 V9 Z";

        /// <summary>Folder with a tab — file pickers (plan, IFC).</summary>
        public const string Folder =
            "M3 5 H10 L12 7.5 H21 V19 H3 Z";

        /// <summary>Crosshair ring with ticks and a center dot — blueprint calibration.</summary>
        public const string Calibrate =
            "M12 5 C15.87 5 19 8.13 19 12 C19 15.87 15.87 19 12 19 C8.13 19 5 15.87 5 12 C5 8.13 8.13 5 12 5 Z " +
            "M12 7 C14.76 7 17 9.24 17 12 C17 14.76 14.76 17 12 17 C9.24 17 7 14.76 7 12 C7 9.24 9.24 7 12 7 Z " +
            "M12 10.2 C13 10.2 13.8 11 13.8 12 C13.8 13 13 13.8 12 13.8 C11 13.8 10.2 13 10.2 12 C10.2 11 11 10.2 12 10.2 Z " +
            "M11.2 1.6 H12.8 V4.6 H11.2 Z M19.4 11.2 H22.4 V12.8 H19.4 Z " +
            "M11.2 19.4 H12.8 V22.4 H11.2 Z M1.6 11.2 H4.6 V12.8 H1.6 Z";

        /// <summary>Stack of storeys — the Import storey filter.</summary>
        public const string Storey =
            "M12 2.5 L21 7.5 L12 12.5 L3 7.5 Z " +
            "M3 10.5 L3 12.5 L12 17.5 L21 12.5 L21 10.5 L12 15.5 Z " +
            "M3 15.5 L3 17.5 L12 22.5 L21 17.5 L21 15.5 L12 20.5 Z";

        /// <summary>Angled eraser with a tip split — "Original look" in Paint.</summary>
        public const string Eraser =
            "M14.5 3.5 L20.5 9.5 L12 18 H6.5 L3.5 15 Z " +
            "M15.9 6.3 L17.3 7.7 L10.4 14.6 L9 13.2 Z";

        /// <summary>Plate with key holes + spacebar.</summary>
        public const string Numpad =
            "M2 6 H22 V18 H2 Z " +
            "M4.5 8.5 H6.5 V10.5 H4.5 Z M9 8.5 H11 V10.5 H9 Z M13 8.5 H15 V10.5 H13 Z M17.5 8.5 H19.5 V10.5 H17.5 Z " +
            "M4.5 13.5 H6.5 V15.5 H4.5 Z M17.5 13.5 H19.5 V15.5 H17.5 Z " +
            "M8.5 13.5 H15.5 V15.5 H8.5 Z";

        /// <summary>
        /// Registry: kebab-case id → path data. Ids are what tools reference
        /// (<c>ITool.IconId</c>) and what the icon tests enumerate.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
        {
            // tools
            { "select-cursor", SelectCursor },
            { "tape-measure", TapeMeasure },
            { "wall", Wall },
            { "floor-slab", FloorSlab },
            { "blueprint", Blueprint },
            { "import-file", ImportFile },
            { "electric-plug", ElectricPlug },
            { "paint-roller", PaintRoller },
            // reserved tools / scene objects
            { "door-window", DoorWindow },
            { "furniture", Furniture },
            { "radiator", Radiator },
            { "pipe", Pipe },
            { "stairs", Stairs },
            // electrical sub-modes (Electric tab strip)
            { "outlet", Outlet },
            { "switch", Switch },
            { "wire", Wire },
            { "junction-box", JunctionBox },
            { "breaker-panel", BreakerPanel },
            // glyphs
            { "plus", Plus },
            { "minus", Minus },
            { "chevron-right", ChevronRight },
            { "chevron-left", ChevronLeft },
            { "chevron-down", ChevronDown },
            { "check", Check },
            { "cross", Cross },
            { "eye", Eye },
            { "eye-off", EyeOff },
            { "lock", Lock },
            { "trash", Trash },
            { "copy", Copy },
            { "undo", Undo },
            { "redo", Redo },
            { "gear", Gear },
            { "grid-snap", GridSnap },
            { "corner-snap", CornerSnap },
            { "edge-snap", EdgeSnap },
            { "angle-snap", AngleSnap },
            { "scan", Scan },
            { "palette", Palette },
            { "dropper", Dropper },
            { "hand-grab", HandGrab },
            { "numpad", Numpad },
            { "folder", Folder },
            { "calibrate", Calibrate },
            { "storey", Storey },
            { "eraser", Eraser },
        };
    }
}
