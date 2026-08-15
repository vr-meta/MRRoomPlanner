using RoomPlanner.Core;
using RoomPlanner.Editing;
using UnityEngine;

namespace RoomPlanner.Plumbing
{
    /// <summary>Per-instance rows of ONE plumb fixture: the stub-out angle (undoable
    /// rebuild); the floor drain has nothing to tune and shows a readout.</summary>
    [RequireComponent(typeof(PlumbFixture))]
    public class PlumbFixtureParameters : MonoBehaviour, ISettingsProvider
    {
        private PlumbFixture _fixture;
        private SettingsSchema _outletSchema, _drainSchema;

        private PlumbFixture Fixture =>
            _fixture != null ? _fixture : _fixture = GetComponent<PlumbFixture>();

        public SettingsSchema GetSettings()
        {
            if (Fixture == null) return null;
            if (Fixture.Kind == PlumbFixtureKind.FloorDrain)
            {
                _drainSchema ??= new SettingsSchema()
                    .Readout("fdsize", "Drain",
                        () => $"{PlumbingDefaults.DrainSize * 100f:0}×{PlumbingDefaults.DrainSize * 100f:0} cm · D50 port");
                return _drainSchema;
            }
            _outletSchema ??= new SettingsSchema()
                .Segmented("fangle", "Angle", new[] { "90°", "45°" },
                    () => Fixture.Angle == OutletAngle.Deg90 ? 0 : 1, SetAngle);
            return _outletSchema;
        }

        private void SetAngle(int index)
        {
            var target = index == 0 ? OutletAngle.Deg90 : OutletAngle.Deg45;
            if (Fixture.Angle == target) return;
            var cmd = new OutletAngleCommand(this, Fixture.Angle, target);
            var model = SceneModel.Instance;
            if (model != null) model.History.Execute(cmd);
            else cmd.Do();
        }

        internal PlumbFixture Target => Fixture;
        internal ISelectable Owner => GetComponent<Selectable>();
    }

    /// <summary>Angle change of one stub-out — one undo entry, rebuilds the mesh.</summary>
    public sealed class OutletAngleCommand : ICommand, ISelectableCommand
    {
        private readonly PlumbFixtureParameters _params;
        private readonly OutletAngle _before, _after;

        public OutletAngleCommand(PlumbFixtureParameters p, OutletAngle before, OutletAngle after)
        {
            _params = p; _before = before; _after = after;
        }

        public string Name => "Outlet angle";
        public ISelectable Target => _params != null ? _params.Owner : null;

        public void Do() => Set(_after);
        public void Undo() => Set(_before);

        private void Set(OutletAngle value)
        {
            if (_params == null || _params.Target == null) return;
            _params.Target.Build(_params.Target.Kind, value);
        }
    }
}
