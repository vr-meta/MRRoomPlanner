using System.Collections.Generic;

namespace RoomPlanner.Core
{
    /// <summary>
    /// Undo/Redo stack of <see cref="ICommand"/>. Two entry points:
    /// • <see cref="Execute"/> — run the command now, then remember it (for discrete edits
    ///   like delete / duplicate).
    /// • <see cref="Record"/> — remember a command that was ALREADY applied live (e.g. a drag
    ///   that moved the object frame-by-frame); Undo/Redo then replay it normally.
    /// Any new edit clears the redo stack. Pure C# → unit-testable in RoomPlanner.Tests.
    /// </summary>
    public class EditHistory
    {
        private readonly Stack<ICommand> _undo = new();
        private readonly Stack<ICommand> _redo = new();

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public int UndoCount => _undo.Count;
        public int RedoCount => _redo.Count;

        /// <summary>Apply the command and push it onto the undo stack.</summary>
        public void Execute(ICommand cmd)
        {
            if (cmd == null) return;
            cmd.Do();
            _undo.Push(cmd);
            _redo.Clear();
        }

        /// <summary>Push an already-applied command (its effect is live) without re-running Do().</summary>
        public void Record(ICommand cmd)
        {
            if (cmd == null) return;
            _undo.Push(cmd);
            _redo.Clear();
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            var cmd = _undo.Pop();
            cmd.Undo();
            _redo.Push(cmd);
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            var cmd = _redo.Pop();
            cmd.Do();
            _undo.Push(cmd);
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }
    }
}
