using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;

namespace RoomPlanner.Walls
{
    /// <summary>
    /// Creates an independent graph-backed copy of a wall. The first Do materialises the
    /// segment and its view; Undo hides it and Redo shows that same object again, preserving
    /// ids and keeping later commands anchored to a stable selectable.
    /// </summary>
    public sealed class WallDuplicateCommand : ICommand, ISelectableCommand
    {
        private readonly WallGraphRenderer _renderer;
        private readonly Wall _sourceView;
        private readonly WallSegment _source;
        private readonly Vector3 _delta;
        private WallSegment _copy;
        private Selectable _result;

        public WallDuplicateCommand(WallGraphRenderer renderer, Wall sourceView, Vector3 delta)
        {
            _renderer = renderer;
            _sourceView = sourceView;
            _source = sourceView != null ? sourceView.Segment : null;
            _delta = delta;
        }

        public string Name => "Duplicate Wall";
        public ISelectable Target => _result;
        public Selectable Result => _result;
        public WallSegment ResultSegment => _copy;

        /// <summary>Signed perpendicular offset from the source's A→B centreline.</summary>
        public static Vector3 OffsetDelta(WallSegment source, float meters)
        {
            if (source == null) return Vector3.zero;
            Vector3 direction = source.B.Position - source.A.Position;
            direction.y = 0f;
            if (direction.sqrMagnitude < WallGraph.MinSegmentLength * WallGraph.MinSegmentLength)
                return Vector3.zero;
            direction.Normalize();
            return new Vector3(direction.z, 0f, -direction.x) * meters;
        }

        public void Do()
        {
            if (_result != null && _result.IsAlive)
            {
                _result.SetHidden(false);
                return;
            }
            if (_renderer == null || _source == null || _source.A == null || _source.B == null
                || _delta.sqrMagnitude < 1e-10f)
                return;

            var graph = _renderer.Graph;
            var a = graph.CreateNode(_source.A.Position + _delta);
            var b = graph.CreateNode(_source.B.Position + _delta);
            _copy = graph.AddSegment(a, b);
            if (_copy == null) return;

            _copy.CopyParamsFrom(_source);
            for (int i = 0; i < _source.Openings.Count; i++)
                _copy.Openings.Add(Clone(_source.Openings[i]));

            _renderer.Sync();
            _renderer.RebuildNeighbourhood(_copy);
            var view = _renderer.ViewOf(_copy);
            _result = view != null ? view.GetComponent<Selectable>() : null;
            CopyFinishes(_sourceView != null ? _sourceView.GetComponent<Selectable>() : null, _result);
        }

        public void Undo()
        {
            if (_result != null && _result.IsAlive) _result.SetHidden(true);
        }

        private static WallOpening Clone(WallOpening source) => new()
        {
            Id = source.Id,
            AlongFraction = source.AlongFraction,
            Width = source.Width,
            Height = source.Height,
            SillHeight = source.SillHeight,
            Kind = source.Kind,
            SwingDir = source.SwingDir,
            HingeDir = source.HingeDir,
            OpenFraction = source.OpenFraction,
        };

        private static void CopyFinishes(Selectable source, Selectable target)
        {
            if (source == null || target == null) return;
            target.SetFinishSide(WallSide.Inner, source.FinishOf(WallSide.Inner),
                source.FinishTextureOf(WallSide.Inner), source.FinishNormalOf(WallSide.Inner));
            target.SetFinishSide(WallSide.Outer, source.FinishOf(WallSide.Outer),
                source.FinishTextureOf(WallSide.Outer), source.FinishNormalOf(WallSide.Outer));
        }
    }
}
