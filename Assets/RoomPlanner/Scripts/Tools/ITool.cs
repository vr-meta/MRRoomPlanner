using RoomPlanner.Core;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// A tool driven by ToolManager. The active tool gets Tick() every frame;
    /// blocked=true — the pointer is over the menu, tool input must be ignored.
    /// Tools are registered in ToolManager's list (no central enum) and self-describe
    /// their palette button and settings — see design/14-modularity.md.
    /// </summary>
    public interface ITool
    {
        /// <summary>Stable machine id ("select", "wall", …) — debug / future persistence.</summary>
        string Id { get; }

        /// <summary>Short label for tooltips and the palette tool chip ("Sel", "Wall", …).</summary>
        string PaletteLabel { get; }

        /// <summary>IconPaths id for the radial sector and tool chip (design/20 §5).
        /// Default null → the abbreviated text label is shown instead.</summary>
        string IconId => null;

        void Tick(bool blocked);
        void OnActivate();
        void OnDeactivate();

        /// <summary>Inspector rows for this tool; null or empty — no settings panel.</summary>
        SettingsSchema GetSettings();
    }
}
