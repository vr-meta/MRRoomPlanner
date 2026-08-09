namespace RoomPlanner.Core
{
    /// <summary>
    /// One reversible edit. Do() applies it, Undo() reverts it. Kept device-independent
    /// (no Unity picking/selection deps) so the history is unit-testable. Concrete commands
    /// (move/delete/…) live next to the runtime types they mutate (RoomPlanner.Editing).
    /// </summary>
    public interface ICommand
    {
        /// <summary>Short label for debugging / future history UI.</summary>
        string Name { get; }
        void Do();
        void Undo();
    }
}
