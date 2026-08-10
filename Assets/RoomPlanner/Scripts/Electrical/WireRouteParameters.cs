using RoomPlanner.Core;
using RoomPlanner.Editing;
using UnityEngine;

namespace RoomPlanner.Electrical
{
    /// <summary>Per-instance rows of ONE wire route: its cable type (undoable).</summary>
    [RequireComponent(typeof(WireRoute))]
    public class WireRouteParameters : MonoBehaviour, ISettingsProvider
    {
        private WireRoute _route;
        private SettingsSchema _schema;

        private WireRoute Route => _route != null ? _route : _route = GetComponent<WireRoute>();

        public SettingsSchema GetSettings()
        {
            if (Route == null) return null;
            _schema ??= new SettingsSchema()
                .Cycle("rcable", "Cable", () => Cable.Label(Route.Cable), CycleCable);
            return _schema;
        }

        private void CycleCable()
        {
            var cmd = new RouteCableCommand(this, Route.Cable, Cable.Next(Route.Cable));
            var model = SceneModel.Instance;
            if (model != null) model.History.Execute(cmd);
            else cmd.Do();
        }

        internal WireRoute Target => Route;
        internal ISelectable Owner => GetComponent<Selectable>();
    }

    /// <summary>Cable-type change of one route — one undo entry.</summary>
    public sealed class RouteCableCommand : ICommand, ISelectableCommand
    {
        private readonly WireRouteParameters _params;
        private readonly CableType _before, _after;

        public RouteCableCommand(WireRouteParameters p, CableType before, CableType after)
        {
            _params = p; _before = before; _after = after;
        }

        public string Name => "Wire cable";
        public ISelectable Target => _params != null ? _params.Owner : null;

        public void Do() => Set(_after);
        public void Undo() => Set(_before);

        private void Set(CableType value)
        {
            if (_params == null || _params.Target == null) return;
            _params.Target.SetCable(value);
        }
    }
}
