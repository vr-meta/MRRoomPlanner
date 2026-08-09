using RoomPlanner.Core;
using RoomPlanner.Editing;

namespace RoomPlanner.Walls
{
    /// <summary>
    /// One undoable change to a single wall's parameters (docs/design/13-phase-b-wallgraph.md, B4).
    ///
    /// Stores the value before and after rather than a delta, so Undo restores exactly what was
    /// there — clamping at the limits would make a delta drift. Implements
    /// <see cref="ISelectableCommand"/> so <see cref="SceneModel.Unregister"/> can purge it when
    /// the wall goes away, and every access is guarded: a command must be a no-op on a dead
    /// target, never an exception (coding rules 2.1 / 3.2).
    /// </summary>
    public sealed class WallParamCommand : ICommand, ISelectableCommand
    {
        private enum Kind { Thickness, Height, Offset, Join, Side }

        private readonly WallParameters _params;
        private readonly Kind _kind;
        private readonly float _beforeF, _afterF;
        private readonly WallOffsetMode _beforeOffset, _afterOffset;
        private readonly WallJoin _beforeJoin, _afterJoin;

        private WallParamCommand(WallParameters p, Kind kind,
            float beforeF = 0f, float afterF = 0f,
            WallOffsetMode beforeOffset = default, WallOffsetMode afterOffset = default,
            WallJoin beforeJoin = default, WallJoin afterJoin = default)
        {
            _params = p;
            _kind = kind;
            _beforeF = beforeF; _afterF = afterF;
            _beforeOffset = beforeOffset; _afterOffset = afterOffset;
            _beforeJoin = beforeJoin; _afterJoin = afterJoin;
        }

        public static WallParamCommand ForThickness(WallParameters p, float value) =>
            new(p, Kind.Thickness, p.TargetSegment?.Thickness ?? 0f, value);

        public static WallParamCommand ForHeight(WallParameters p, float value) =>
            new(p, Kind.Height, p.TargetSegment?.Height ?? 0f, value);

        public static WallParamCommand ForSide(WallParameters p, float value) =>
            new(p, Kind.Side, p.TargetSegment?.SideSign ?? 1f, value);

        public static WallParamCommand ForOffset(WallParameters p, WallOffsetMode value) =>
            new(p, Kind.Offset,
                beforeOffset: p.TargetSegment?.Offset ?? WallOffsetMode.Outer, afterOffset: value);

        public static WallParamCommand ForJoin(WallParameters p, WallJoin value) =>
            new(p, Kind.Join,
                beforeJoin: p.TargetSegment?.Join ?? WallJoin.Miter, afterJoin: value);

        public string Name => $"Wall {_kind}";

        public ISelectable Target => _params != null ? _params.Owner : null;

        public void Do() => Set(forward: true);
        public void Undo() => Set(forward: false);

        private void Set(bool forward)
        {
            // the view may have been destroyed since the command was recorded
            if (_params == null) return;
            var s = _params.TargetSegment;
            if (s == null) return;

            switch (_kind)
            {
                case Kind.Thickness: s.Thickness = forward ? _afterF : _beforeF; break;
                case Kind.Height:    s.Height = forward ? _afterF : _beforeF; break;
                case Kind.Side:      s.SideSign = forward ? _afterF : _beforeF; break;
                case Kind.Offset:    s.Offset = forward ? _afterOffset : _beforeOffset; break;
                case Kind.Join:      s.Join = forward ? _afterJoin : _beforeJoin; break;
            }
            _params.Rebuild();
        }
    }
}
