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
            // v2: both cable types visible at once (design/20 §2.3), change stays undoable
            _schema ??= new SettingsSchema()
                .Segmented("rcable", "Cable",
                    new[] { Cable.Label(CableType.C3x15), Cable.Label(CableType.C3x25) },
                    () => (int)Route.Cable, SetCable);
            return _schema;
        }

        private void SetCable(int index)
        {
            var target = (CableType)index;
            if (Route.Cable == target) return;
            var cmd = new RouteCableCommand(this, Route.Cable, target);
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
