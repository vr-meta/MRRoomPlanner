using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Editing
{
    /// <summary>
    /// An object whose shape can be edited by dragging point handles — wall nodes now, floor
    /// corners in Phase C (docs/design/13-phase-b-wallgraph.md, step B5).
    ///
    /// Implemented next to the geometry, never in this assembly, so Editing stays ignorant of
    /// what it is dragging. The Select tool spawns the visuals, does the picking and owns the
    /// undo record; the provider only knows how to move its own points.
    /// </summary>
    public interface IHandleProvider
    {
        /// <summary>How many draggable points this object exposes.</summary>
        int HandleCount { get; }

        Vector3 GetHandlePosition(int index);

        /// <summary>
        /// Live drag: move the point and refresh what the user sees. Called every frame, so it
        /// must stay cheap — no physics re-cook here (coding rule 4.2).
        /// </summary>
        void PreviewHandle(int index, Vector3 worldPosition);

        /// <summary>
        /// Finish the drag: settle the geometry (including physics) and return an
        /// already-applied command describing the whole gesture, or null if nothing moved.
        /// One command per drag — not one per frame.
        /// </summary>
        ICommand CommitHandle(int index, Vector3 from, Vector3 to);
    }
}
