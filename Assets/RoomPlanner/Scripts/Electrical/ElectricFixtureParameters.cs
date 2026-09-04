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
        private readonly ElectricPlacement _placement = new();
        private bool _fromEnd;
        private int _tab;
        private string _placementStatus = "";

        private static readonly Color[] FixtureVariants =
        {
            ElectricFixture.WhitePlastic,
            ElectricFixture.BlackPlastic,
        };

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
                    s.NumericStepper("fposts", "Posts", 1f, ElectricalDefaults.MaxPosts,
                        () => Fx.Posts, (_, v) => ApplyPosts(Mathf.RoundToInt(v)), () => $"{Fx.Posts}",
                        () => ApplyPosts(Fx.Posts - 1), () => ApplyPosts(Fx.Posts + 1));
                    AddHeightRow(s, ElectricalDefaults.MinOutletHeight);
                    AddVariantRow(s);
                    break;
                case FixtureKind.Switch:
                    s.NumericStepper("fkeys", "Keys", 1f, ElectricalDefaults.MaxKeys,
                        () => Fx.Keys, (_, v) => ApplyKeys(Mathf.RoundToInt(v)), () => $"{Fx.Keys}",
                        () => ApplyKeys(Fx.Keys - 1), () => ApplyKeys(Fx.Keys + 1));
                    AddHeightRow(s, ElectricalDefaults.MinSwitchHeight);
                    AddVariantRow(s);
                    break;
                case FixtureKind.Junction:
                    // no height preset and nothing to configure — show what branches here
                    s.Readout("fjwires", "Wires", () => $"{AttachedRoutes()}");
                    AddVariantRow(s);
                    break;
                default:
                    s.NumericStepper("fres", "Reserve", 0f, ElectricalDefaults.MaxReservePercent,
                        () => Fx.ReservePercent, (_, v) => ApplyReserve(Mathf.RoundToInt(v)),
                        () => $"{Fx.ReservePercent} %",
                        () => ApplyReserve(Fx.ReservePercent - ElectricalDefaults.ReserveStep),
                        () => ApplyReserve(Fx.ReservePercent + ElectricalDefaults.ReserveStep));
                    AddVariantRow(s);
                    s.Toggle("fopen", "Door open", () => Fx.PanelOpen, ApplyPanelOpen);
                    AddHeightRow(s, 0.1f);
                    break;
            }
            var dimensions = new SettingsSchema()
                .Readout("size", "Size", () => $"{Fx.BlockWidth * 100f:0.#} × {Fx.BlockHeight * 100f:0.#} cm")
                .Toggle("dimensions", "Dimension lines", () => Fx.ShowDimensions, value =>
                {
                    Fx.ShowDimensions = value;
                    if (GetComponent<RoomPlanner.Tools.ServiceDimensionDisplay>() == null)
                        gameObject.AddComponent<RoomPlanner.Tools.ServiceDimensionDisplay>().Material = GetComponent<MeshRenderer>().sharedMaterial;
                });
            if (kind != FixtureKind.Junction)
            {
                dimensions.Segmented("base", "Measure from", new[] { "Start", "End" },
                    () => _fromEnd ? 1 : 0, i => _fromEnd = i == 1)
                    .Numeric("gap", "Edge gap", 0f, 100f, Gap,
                        (_, v) => ApplyGap(v), () => Fx.MountHost != null ? $"{Gap() * 100f:0.#} cm" : "Unhosted",
                        displayScale: 100f);
            }
            dimensions.Readout("placement", "Placement", () => _placementStatus.Length > 0
                ? _placementStatus : GetComponent<RoomPlanner.Tools.MountedServiceFollower>()?.Status
                    ?? (Fx.MountHost != null ? "Hosted" : "Unhosted"));
            return SettingsSchema.Tabbed(new[] { "Properties", "Dimensions" }, () => _tab, i => _tab = i, s, dimensions);
        }

        /// <summary>Height as a v2 numeric field (design/20 §2.6): exact cm entry, ONE
        /// undoable command per commit, clamped in storey-relative space.</summary>
        private void AddHeightRow(SettingsSchema s, float min) =>
            s.Numeric("fh", "Height", min, ElectricalDefaults.MaxMountHeight,
                () => Fx != null ? Fx.HeightAboveLevel : 0f,
                (_, v) => ApplyHeightAbsolute(min, v),
                () => $"{Fx.HeightAboveLevel * 100f:0} cm", displayScale: 100f);

        private void AddVariantRow(SettingsSchema s) =>
            s.Swatch("ffinish", "Finish", FixtureVariants,
                () => Fx.BlackVariant ? 1 : 0, i => ApplyBlackVariant(i == 1));

        private void ApplyPosts(int value)
        {
            int posts = Mathf.Clamp(value, 1, ElectricalDefaults.MaxPosts);
            if (posts != Fx.Posts && Validate(Fx.transform.position, posts))
                Apply(FixtureParamCommand.ForPosts(this, posts));
        }

        public bool Validate(Vector3 position, int posts)
        {
            if (!ServicePlacement.Finite(position)) return false;
            if (Fx.MountHost == null) return true; // legacy objects retain their existing editing behaviour
            var result = _placement.Validate(Fx.MountHost, position, Fx.transform.rotation,
                Fx.Kind, posts, SceneModel.Instance, Fx);
            _placementStatus = ServicePlacement.Describe(result);
            return result == PlacementFailure.None;
        }

        private float Gap()
        {
            if (!_placement.Resolve(Fx.MountHost, Fx.transform.position, Fx.transform.forward)) return 0f;
            return ServicePlacement.EdgeGap(_placement.Surface, Fx.transform.position, Fx.BlockWidth, _fromEnd);
        }

        private void ApplyGap(float gap)
        {
            if (!ServicePlacement.Finite(gap) || gap < 0f
                || !_placement.Resolve(Fx.MountHost, Fx.transform.position, Fx.transform.forward)) return;
            Vector3 target = ServicePlacement.WithEdgeGap(_placement.Surface, Fx.transform.position, Fx.BlockWidth, gap, _fromEnd);
            if (!Validate(target, Fx.Posts)) return;
            Vector3 delta = target - Fx.transform.position;
            if (delta.sqrMagnitude < 1e-10f) return;
            var model = SceneModel.Instance;
            if (model != null) model.History.Execute(new MoveCommand(Owner, delta));
            else Owner.MoveBy(delta);
        }

        private void ApplyKeys(int value) =>
            Apply(FixtureParamCommand.ForKeys(this, Mathf.Clamp(value, 1, ElectricalDefaults.MaxKeys)));

        private void ApplyReserve(int value) =>
            Apply(FixtureParamCommand.ForReserve(this, Mathf.Clamp(value, 0, ElectricalDefaults.MaxReservePercent)));

        private void ApplyBlackVariant(bool black) =>
            Apply(FixtureParamCommand.ForVariant(this, black));

        private void ApplyPanelOpen(bool open) =>
            Apply(FixtureParamCommand.ForPanelOpen(this, open));

        private void ApplyHeightAbsolute(float min, float relValue)
        {
            // clamp in storey-relative space: on an upper floor the world Y is offset by
            // the fixture's BaseLevel, and 30–180 cm still must mean "above THIS floor"
            float rel = Mathf.Clamp(relValue, min, ElectricalDefaults.MaxMountHeight);
            var target = Fx.transform.position; target.y = Fx.BaseLevel + rel;
            if (Mathf.Abs(target.y - Fx.transform.position.y) > 1e-6f && Validate(target, Fx.Posts))
                Apply(FixtureParamCommand.ForHeight(this, target.y));
        }

        /// <summary>Routes attached to this fixture by id (selection-time readout only).</summary>
        private int AttachedRoutes()
        {
            var owner = Owner;
            var model = SceneModel.Instance;
            if (owner == null || model == null || string.IsNullOrEmpty(owner.Id)) return 0;
            int n = 0;
            var items = model.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || !item.IsAlive || item.IsHidden) continue;
                if (item is not Selectable sel || sel.Kind != SelectableKind.Wire) continue;
                var r = sel.Route;
                if (r != null && (r.StartFixtureId == owner.Id || r.EndFixtureId == owner.Id)) n++;
            }
            return n;
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
        private enum Kind { Posts, Keys, Reserve, Height, Variant, PanelOpen }

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

        public static FixtureParamCommand ForVariant(ElectricFixtureParameters p, bool black) =>
            new(p, Kind.Variant, p.Target != null && p.Target.BlackVariant ? 1f : 0f,
                black ? 1f : 0f);

        public static FixtureParamCommand ForPanelOpen(ElectricFixtureParameters p, bool open) =>
            new(p, Kind.PanelOpen, p.Target != null && p.Target.PanelOpen ? 1f : 0f,
                open ? 1f : 0f);

        public string Name => $"Fixture {_kind}";
        public ISelectable Target => _params != null ? _params.Owner : null;

        public void Do() => Set(_after);
        public void Undo() => Set(_before);

        private void Set(float value)
        {
            if (_params == null) return;
            var fx = _params.Target;
            if (fx == null) return;                   // destroyed since the command was recorded
            var owner = _params.Owner;
            switch (_kind)
            {
                case Kind.Posts: fx.Build(fx.Kind, (int)value, fx.Keys); break;
                case Kind.Keys: fx.Build(fx.Kind, fx.Posts, (int)value); break;
                case Kind.Reserve: fx.ReservePercent = (int)value; break;
                case Kind.Variant:
                    fx.SetBlackVariant(value > 0.5f);
                    (owner as Selectable)?.RefreshVisual();
                    break;
                case Kind.PanelOpen:
                    fx.SetPanelOpen(value > 0.5f);
                    (owner as Selectable)?.RefreshVisual();
                    break;
                case Kind.Height:
                    float dy = value - fx.transform.position.y;
                    // through the selection adapter so attached wire ends ride along
                    if (owner != null && owner.IsAlive) owner.MoveBy(new Vector3(0f, dy, 0f));
                    else fx.MoveBy(new Vector3(0f, dy, 0f));
                    break;
            }
        }
    }
}
