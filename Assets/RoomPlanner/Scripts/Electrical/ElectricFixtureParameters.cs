using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;

namespace RoomPlanner.Electrical
{
    /// <summary>
    /// Per-instance rows of ONE fixture, shown when it is selected — posts/keys/height for
    /// wall fixtures, the BOM reserve for the panel. Found through <see cref="ISettingsProvider"/>
    /// like walls and floors; every change is an undoable before/after command.
    /// </summary>
    [RequireComponent(typeof(ElectricFixture))]
    public class ElectricFixtureParameters : MonoBehaviour, ISettingsProvider
    {
        private ElectricFixture _fixture;
        private SettingsSchema _schema;
        private FixtureKind _schemaKind;

        private ElectricFixture Fx => _fixture != null ? _fixture : _fixture = GetComponent<ElectricFixture>();

        public SettingsSchema GetSettings()
        {
            if (Fx == null) return null;
            if (_schema == null || _schemaKind != Fx.Kind)
            {
                _schemaKind = Fx.Kind;
                _schema = BuildSchema(_schemaKind);
            }
            return _schema;
        }

        private SettingsSchema BuildSchema(FixtureKind kind)
        {
            var s = new SettingsSchema();
            switch (kind)
            {
                case FixtureKind.Outlet:
                    s.Stepper("fposts", "Posts", () => $"{Fx.Posts}",
                        () => ApplyPosts(Fx.Posts - 1), () => ApplyPosts(Fx.Posts + 1));
                    AddHeightRow(s, ElectricalDefaults.MinOutletHeight);
                    break;
                case FixtureKind.Switch:
                    s.Stepper("fkeys", "Keys", () => $"{Fx.Keys}",
                        () => ApplyKeys(Fx.Keys - 1), () => ApplyKeys(Fx.Keys + 1));
                    AddHeightRow(s, ElectricalDefaults.MinSwitchHeight);
                    break;
                default:
                    s.Stepper("fres", "Reserve", () => $"{Fx.ReservePercent} %",
                        () => ApplyReserve(Fx.ReservePercent - ElectricalDefaults.ReserveStep),
                        () => ApplyReserve(Fx.ReservePercent + ElectricalDefaults.ReserveStep));
                    break;
            }
            return s;
        }

        /// <summary>Height as a v2 numeric field (design/20 §2.6): exact cm entry, ONE
        /// undoable command per commit, clamped in storey-relative space.</summary>
        private void AddHeightRow(SettingsSchema s, float min) =>
            s.Numeric("fh", "Height", min, ElectricalDefaults.MaxMountHeight,
                () => Fx != null ? Fx.HeightAboveLevel : 0f,
                (_, v) => ApplyHeightAbsolute(min, v),
                () => $"{Fx.HeightAboveLevel * 100f:0} cm", displayScale: 100f);

        private void ApplyPosts(int value) =>
            Apply(FixtureParamCommand.ForPosts(this, Mathf.Clamp(value, 1, ElectricalDefaults.MaxPosts)));

        private void ApplyKeys(int value) =>
            Apply(FixtureParamCommand.ForKeys(this, Mathf.Clamp(value, 1, ElectricalDefaults.MaxKeys)));

        private void ApplyReserve(int value) =>
            Apply(FixtureParamCommand.ForReserve(this, Mathf.Clamp(value, 0, ElectricalDefaults.MaxReservePercent)));

        private void ApplyHeightAbsolute(float min, float relValue)
        {
            // clamp in storey-relative space: on an upper floor the world Y is offset by
            // the fixture's BaseLevel, and 30–180 cm still must mean "above THIS floor"
            float rel = Mathf.Clamp(relValue, min, ElectricalDefaults.MaxMountHeight);
            Apply(FixtureParamCommand.ForHeight(this, Fx.BaseLevel + rel));
        }

        private void Apply(FixtureParamCommand cmd)
        {
            var model = SceneModel.Instance;
            if (model != null) model.History.Execute(cmd);
            else cmd.Do();                 // bare test rig without history — still apply
        }

        internal ElectricFixture Target => Fx;
        internal ISelectable Owner => GetComponent<Selectable>();
    }

    /// <summary>
    /// One undoable change to a fixture's parameters. Before/after values, not deltas —
    /// a step into the clamp must not make Undo drift (same reasoning as walls/floors).
    /// Height moves go through the owner's MoveBy so attached wire ends follow both ways.
    /// </summary>
    public sealed class FixtureParamCommand : ICommand, ISelectableCommand
    {
        private enum Kind { Posts, Keys, Reserve, Height }

        private readonly ElectricFixtureParameters _params;
        private readonly Kind _kind;
        private readonly float _before, _after;

        private FixtureParamCommand(ElectricFixtureParameters p, Kind kind, float before, float after)
        {
            _params = p; _kind = kind; _before = before; _after = after;
        }

        public static FixtureParamCommand ForPosts(ElectricFixtureParameters p, int value) =>
            new(p, Kind.Posts, p.Target != null ? p.Target.Posts : 1, value);

        public static FixtureParamCommand ForKeys(ElectricFixtureParameters p, int value) =>
            new(p, Kind.Keys, p.Target != null ? p.Target.Keys : 1, value);

        public static FixtureParamCommand ForReserve(ElectricFixtureParameters p, int value) =>
            new(p, Kind.Reserve, p.Target != null ? p.Target.ReservePercent : 0, value);

        public static FixtureParamCommand ForHeight(ElectricFixtureParameters p, float y) =>
            new(p, Kind.Height, p.Target != null ? p.Target.transform.position.y : 0f, y);

        public string Name => $"Fixture {_kind}";
        public ISelectable Target => _params != null ? _params.Owner : null;

        public void Do() => Set(_after);
        public void Undo() => Set(_before);

        private void Set(float value)
        {
            if (_params == null) return;
            var fx = _params.Target;
            if (fx == null) return;                   // destroyed since the command was recorded
            switch (_kind)
            {
                case Kind.Posts: fx.Build(fx.Kind, (int)value, fx.Keys); break;
                case Kind.Keys: fx.Build(fx.Kind, fx.Posts, (int)value); break;
                case Kind.Reserve: fx.ReservePercent = (int)value; break;
                case Kind.Height:
                    float dy = value - fx.transform.position.y;
                    var owner = _params.Owner;
                    // through the selection adapter so attached wire ends ride along
                    if (owner != null && owner.IsAlive) owner.MoveBy(new Vector3(0f, dy, 0f));
                    else fx.MoveBy(new Vector3(0f, dy, 0f));
                    break;
            }
        }
    }
}
