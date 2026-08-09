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

        /// <summary>Short label for the palette button ("Sel", "Wall", …).</summary>
        string PaletteLabel { get; }

        void Tick(bool blocked);
        void OnActivate();
        void OnDeactivate();

        /// <summary>Inspector rows for this tool; null or empty — no settings panel.</summary>
        SettingsSchema GetSettings();
    }
}
