using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;

namespace RoomPlanner.Floors
{
    /// <summary>
    /// Per-instance parameters of ONE slab, shown when it is selected (step C4,
    /// docs/design/17-floor-outline.md). Found through <see cref="ISettingsProvider"/>, exactly
    /// like walls — the inspector and the Editing assembly stay ignorant of floors.
    ///
    /// Thickness is the slab's OWN, no longer borrowed from the wall thickness: a 20 cm wall
    /// and a 20 cm slab having to agree was a bug, not a feature.
    /// </summary>
    [RequireComponent(typeof(Floor))]
    public class FloorParameters : MonoBehaviour, ISettingsProvider
    {
        private const float MinThickness = 0.02f, MaxThickness = 1f;

        private Floor _floor;
        private SettingsSchema _schema;

        private Floor Slab => _floor != null ? _floor : _floor = GetComponent<Floor>();

        public SettingsSchema GetSettings()
        {
            if (Slab == null) return null;
            _schema ??= new SettingsSchema()
                .Stepper("fthk", "Thickness",
                    () => $"{Slab.Thickness * 100f:0} cm",
                    () => AdjustThickness(-0.02f), () => AdjustThickness(0.02f))
                .Stepper("flvl", "Level",
                    () => $"{Slab.Level * 100f:0} cm",
                    () => AdjustLevel(-0.1f), () => AdjustLevel(0.1f));
            return _schema;
        }

        private void AdjustThickness(float delta)
        {
            if (Slab == null) return;
            Apply(FloorParamCommand.ForThickness(this,
                Mathf.Clamp(Slab.Thickness + delta, MinThickness, MaxThickness)));
        }

        private void AdjustLevel(float delta)
        {
            if (Slab == null) return;
            Apply(FloorParamCommand.ForLevel(this, Mathf.Round((Slab.Level + delta) * 100f) / 100f));
        }

        private void Apply(FloorParamCommand cmd)
        {
            var model = SceneModel.Instance;
            if (model != null) model.History.Execute(cmd);
            else cmd.Do();                 // no history in a bare test rig — still apply
        }

        internal Floor Target => Slab;
        internal ISelectable Owner => GetComponent<Selectable>();
    }

    /// <summary>
    /// One undoable change to a slab's parameters. Stores before/after values rather than a
    /// delta, so a step into the clamp cannot make Undo drift (same reasoning as walls).
    /// </summary>
    public sealed class FloorParamCommand : ICommand, ISelectableCommand
    {
        private enum Kind { Thickness, Level }

        private readonly FloorParameters _params;
        private readonly Kind _kind;
        private readonly float _before, _after;

        private FloorParamCommand(FloorParameters p, Kind kind, float before, float after)
        {
            _params = p; _kind = kind; _before = before; _after = after;
        }

        public static FloorParamCommand ForThickness(FloorParameters p, float value) =>
            new(p, Kind.Thickness, p.Target != null ? p.Target.Thickness : 0f, value);

        public static FloorParamCommand ForLevel(FloorParameters p, float value) =>
            new(p, Kind.Level, p.Target != null ? p.Target.Level : 0f, value);

        public string Name => $"Floor {_kind}";
        public ISelectable Target => _params != null ? _params.Owner : null;

        public void Do() => Set(_after);
        public void Undo() => Set(_before);

        private void Set(float value)
        {
            if (_params == null) return;
            var slab = _params.Target;
            if (slab == null) return;                 // destroyed since the command was recorded
            if (_kind == Kind.Thickness) slab.SetThickness(value);
            else slab.SetLevel(value);
        }
    }
}
