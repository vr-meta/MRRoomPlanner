using System;
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
        public int Version { get; private set; }

        /// <summary>Apply the command and push it onto the undo stack.</summary>
        public void Execute(ICommand cmd)
        {
            if (cmd == null) return;
            cmd.Do();
            _undo.Push(cmd);
            _redo.Clear();
            Version++;
        }

        /// <summary>Push an already-applied command (its effect is live) without re-running Do().</summary>
        public void Record(ICommand cmd)
        {
            if (cmd == null) return;
            _undo.Push(cmd);
            _redo.Clear();
            Version++;
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            // Peek before invoking: if Undo() throws, the command stays on the stack instead
            // of silently vanishing from both stacks.
            var cmd = _undo.Peek();
            cmd.Undo();
            _undo.Pop();
            _redo.Push(cmd);
            Version++;
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            var cmd = _redo.Peek();
            cmd.Do();
            _redo.Pop();
            _undo.Push(cmd);
            Version++;
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            Version++;
        }

        /// <summary>
        /// Remove matching commands from both stacks (order preserved). Used when a target
        /// object is destroyed for good, so its commands can never be replayed.
        /// </summary>
        public void PurgeWhere(Func<ICommand, bool> shouldRemove)
        {
            if (shouldRemove == null) return;
            PurgeStack(_undo, shouldRemove);
            PurgeStack(_redo, shouldRemove);
            Version++;
        }

        private static void PurgeStack(Stack<ICommand> stack, Func<ICommand, bool> shouldRemove)
        {
            if (stack.Count == 0) return;
            var kept = new List<ICommand>(stack.Count);
            foreach (var cmd in stack)                    // enumerates top → bottom
                if (!shouldRemove(cmd)) kept.Add(cmd);
            if (kept.Count == stack.Count) return;
            stack.Clear();
            for (int i = kept.Count - 1; i >= 0; i--) stack.Push(kept[i]);
        }
    }
}
