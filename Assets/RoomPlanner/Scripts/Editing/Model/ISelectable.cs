using UnityEngine;

namespace RoomPlanner.Editing
{
    /// <summary>Object families that can be selected/edited in the scene.</summary>
    public enum SelectableKind { Measurement, Wall, Floor }

    /// <summary>Visual state driven by the Select tool.</summary>
    public enum HighlightState { None, Hover, Selected }

    /// <summary>
    /// Anything the Select tool can pick, highlight, move and delete. Implemented by the
    /// lightweight <see cref="Selectable"/> component that sits on wall/floor/measurement roots.
    /// This is the seam the roadmap (docs/design/11) calls the "missing edit layer".
    /// </summary>
    public interface ISelectable
    {
        string Id { get; set; }
        SelectableKind Kind { get; }
        Transform Transform { get; }
        Bounds WorldBounds { get; }
        bool IsHidden { get; }

        void SetHighlight(HighlightState state);
        void SetHidden(bool hidden);
        void MoveBy(Vector3 delta);

        /// <summary>Short human-readable summary for the inspector (e.g. "Length 3.20 m").</summary>
        string Describe();
    }
}
