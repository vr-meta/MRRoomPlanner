using NUnit.Framework;
using RoomPlanner.Core;

namespace RoomPlanner.Tests
{
    public class EditHistoryTests
    {
        // Minimal fake command: tracks an int value, records how often Do/Undo ran.
        private class SetValueCommand : ICommand
        {
            private readonly int[] _cell;
            private readonly int _before;
            private readonly int _after;
            public int Dos, Undos;

            public SetValueCommand(int[] cell, int after)
            {
                _cell = cell;
                _before = cell[0];
                _after = after;
            }

            public string Name => "SetValue";
            public void Do() { _cell[0] = _after; Dos++; }
            public void Undo() { _cell[0] = _before; Undos++; }
        }

        [Test]
        public void Execute_RunsCommand_AndEnablesUndo()
        {
            var cell = new[] { 0 };
            var h = new EditHistory();
            h.Execute(new SetValueCommand(cell, 5));

            Assert.AreEqual(5, cell[0]);
            Assert.IsTrue(h.CanUndo);
            Assert.IsFalse(h.CanRedo);
        }

        [Test]
        public void Record_DoesNotRunCommand_ButIsUndoable()
        {
            // Simulate a drag: the value moves 0 → 7 live, outside the command. The command
            // captures 'before' = 0 at construction; Record() must NOT re-apply Do().
            var live = new[] { 0 };
            var moved = new SetValueCommand(live, 7);
            live[0] = 7;                        // live effect applied outside the command
            var h = new EditHistory();
            h.Record(moved);

            Assert.AreEqual(0, moved.Dos, "Record must not call Do()");
            Assert.IsTrue(h.CanUndo);

            h.Undo();
            Assert.AreEqual(0, live[0], "Undo reverts to captured 'before'");
        }

        [Test]
        public void UndoThenRedo_RestoresValue()
        {
            var cell = new[] { 0 };
            var h = new EditHistory();
            var cmd = new SetValueCommand(cell, 9);
            h.Execute(cmd);

            h.Undo();
            Assert.AreEqual(0, cell[0]);
            Assert.IsTrue(h.CanRedo);

            h.Redo();
            Assert.AreEqual(9, cell[0]);
            Assert.AreEqual(2, cmd.Dos, "Do runs on Execute and again on Redo");
            Assert.AreEqual(1, cmd.Undos);
        }

        [Test]
        public void NewEdit_ClearsRedo()
        {
            var cell = new[] { 0 };
            var h = new EditHistory();
            h.Execute(new SetValueCommand(cell, 1));
            h.Undo();
            Assert.IsTrue(h.CanRedo);

            h.Execute(new SetValueCommand(cell, 2));
            Assert.IsFalse(h.CanRedo, "A fresh edit discards the redo stack");
        }

        [Test]
        public void Undo_OnEmpty_IsNoOp()
        {
            var h = new EditHistory();
            Assert.DoesNotThrow(() => h.Undo());
            Assert.DoesNotThrow(() => h.Redo());
            Assert.IsFalse(h.CanUndo);
        }

        [Test]
        public void Record_ClearsRedo()
        {
            var cell = new[] { 0 };
            var h = new EditHistory();
            h.Execute(new SetValueCommand(cell, 1));
            h.Undo();
            Assert.IsTrue(h.CanRedo);

            h.Record(new SetValueCommand(cell, 2));
            Assert.IsFalse(h.CanRedo, "Record is a fresh edit and must discard the redo stack");
        }

        [Test]
        public void Undo_IsLifo()
        {
            var cell = new[] { 0 };
            var h = new EditHistory();
            h.Execute(new SetValueCommand(cell, 1));   // before=0
            h.Execute(new SetValueCommand(cell, 2));   // before=1

            h.Undo();
            Assert.AreEqual(1, cell[0], "last command undoes first");
            h.Undo();
            Assert.AreEqual(0, cell[0]);
        }

        [Test]
        public void Clear_EmptiesBothStacks()
        {
            var cell = new[] { 0 };
            var h = new EditHistory();
            h.Execute(new SetValueCommand(cell, 1));
            h.Execute(new SetValueCommand(cell, 2));
            h.Undo();

            h.Clear();

            Assert.IsFalse(h.CanUndo);
            Assert.IsFalse(h.CanRedo);
        }

        [Test]
        public void PurgeWhere_RemovesFromBothStacks_KeepsOthers()
        {
            var cell = new[] { 0 };
            var h = new EditHistory();
            // Construct each command right before Execute — 'before' is captured at construction.
            var doomedUndo = new SetValueCommand(cell, 1);
            h.Execute(doomedUndo);
            var kept = new SetValueCommand(cell, 2);
            h.Execute(kept);
            var doomedRedo = new SetValueCommand(cell, 3);
            h.Execute(doomedRedo);
            h.Undo();   // doomedRedo → redo stack
            Assert.AreEqual(2, h.UndoCount);
            Assert.AreEqual(1, h.RedoCount);

            h.PurgeWhere(c => c == doomedUndo || c == doomedRedo);

            Assert.AreEqual(1, h.UndoCount);
            Assert.AreEqual(0, h.RedoCount);
            h.Undo();
            Assert.AreEqual(1, cell[0], "the surviving command still undoes correctly");
        }

        // ---- exception safety: a throwing command must not silently vanish ----

        private class ThrowingCommand : ICommand
        {
            public bool Throw = true;
            public string Name => "Throwing";
            public void Do() { if (Throw) throw new System.InvalidOperationException("do"); }
            public void Undo() { if (Throw) throw new System.InvalidOperationException("undo"); }
        }

        [Test]
        public void Undo_WhenCommandThrows_KeepsCommandOnUndoStack()
        {
            var h = new EditHistory();
            var cmd = new ThrowingCommand { Throw = false };
            h.Execute(cmd);
            cmd.Throw = true;

            Assert.Throws<System.InvalidOperationException>(() => h.Undo());
            Assert.IsTrue(h.CanUndo, "the command must stay on the undo stack");
            Assert.IsFalse(h.CanRedo, "a failed undo must not populate redo");

            cmd.Throw = false;
            Assert.DoesNotThrow(() => h.Undo(), "recovered command undoes normally");
            Assert.IsTrue(h.CanRedo);
        }

        [Test]
        public void Execute_WhenDoThrows_RecordsNothing()
        {
            var h = new EditHistory();
            Assert.Throws<System.InvalidOperationException>(() => h.Execute(new ThrowingCommand()));
            Assert.IsFalse(h.CanUndo, "a command that failed to apply must not enter the history");
        }
    }
}
