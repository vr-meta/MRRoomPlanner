using RoomPlanner.Core;
using RoomPlanner.Editing;
using UnityEngine;

namespace RoomPlanner.Plumbing
{
    /// <summary>Per-instance rows of ONE pipe run: its diameter (undoable), plus the
    /// BOM reserve on a riser (the electrical-panel precedent).</summary>
    [RequireComponent(typeof(PipeRoute))]
    public class PipeRouteParameters : MonoBehaviour, ISettingsProvider
    {
        private PipeRoute _route;
        private SettingsSchema _pipeSchema, _riserSchema;

        private PipeRoute Route => _route != null ? _route : _route = GetComponent<PipeRoute>();

        public SettingsSchema GetSettings()
        {
            if (Route == null) return null;
            if (Route.IsRiser)
            {
                _riserSchema ??= new SettingsSchema()
                    .Readout("rsize", "Stack", () => "D110 · riser")
                    .Slider("rres", "Reserve", 0f, PlumbingDefaults.MaxReservePercent,
                        PlumbingDefaults.ReserveStep,
                        () => Route.ReservePercent,
                        v => Route.ReservePercent = Mathf.RoundToInt(v),
                        (_, v) => Route.ReservePercent = Mathf.RoundToInt(v),
                        () => $"{Route.ReservePercent} %");
                return _riserSchema;
            }
            _pipeSchema ??= new SettingsSchema()
                .Segmented("pdia", "Diameter",
                    new[] { PipeSpec.Label(PipeDiameter.D110), PipeSpec.Label(PipeDiameter.D50), PipeSpec.Label(PipeDiameter.D40) },
                    () => (int)Route.Diameter, SetDiameter);
            return _pipeSchema;
        }

        private void SetDiameter(int index)
        {
            var target = (PipeDiameter)index;
            if (Route.Diameter == target) return;
            var cmd = new PipeDiameterCommand(this, Route.Diameter, target);
            var model = SceneModel.Instance;
            if (model != null) model.History.Execute(cmd);
            else cmd.Do();
        }

        internal PipeRoute Target => Route;
        internal ISelectable Owner => GetComponent<Selectable>();
    }

    /// <summary>Diameter change of one pipe — one undo entry.</summary>
    public sealed class PipeDiameterCommand : ICommand, ISelectableCommand
    {
        private readonly PipeRouteParameters _params;
        private readonly PipeDiameter _before, _after;

        public PipeDiameterCommand(PipeRouteParameters p, PipeDiameter before, PipeDiameter after)
        {
            _params = p; _before = before; _after = after;
        }

        public string Name => "Pipe diameter";
        public ISelectable Target => _params != null ? _params.Owner : null;

        public void Do() => Set(_after);
        public void Undo() => Set(_before);

        private void Set(PipeDiameter value)
        {
            if (_params == null || _params.Target == null) return;
            _params.Target.SetDiameter(value);
        }
    }
}
