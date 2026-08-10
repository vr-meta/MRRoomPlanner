using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;

namespace RoomPlanner.Stairs
{
    /// <summary>
    /// Per-instance parameters of ONE stair flight (audit F2 / 05 §Р1): the first UI the
    /// stair module ever had — imported flights were locked at their IFC numbers. Found
    /// through ISettingsProvider like walls/floors; every commit is one undoable command
    /// with full before/after geometry (absolute, no clamp drift).
    /// </summary>
    [RequireComponent(typeof(Stair))]
    public class StairParameters : MonoBehaviour, ISettingsProvider
    {
        private Stair _stair;
        private SettingsSchema _schema;

        private Stair Flight => _stair != null ? _stair : _stair = GetComponent<Stair>();

        public SettingsSchema GetSettings()
        {
            if (Flight == null) return null;
            _schema ??= new SettingsSchema()
                .Numeric("srs", "Steps", 2f, 30f,
                    () => Flight != null ? Flight.Risers : 0f,
                    (_, v) => Commit(risers: Mathf.RoundToInt(v)),
                    () => Flight != null ? $"{Flight.Risers}" : "—")
                .Numeric("srh", "Riser", 0.05f, 0.5f,
                    () => Flight != null ? Flight.RiserHeight : 0f,
                    (_, v) => Commit(riserHeight: v),
                    () => $"{(Flight != null ? Flight.RiserHeight : 0f) * 100f:0.#} cm", displayScale: 100f)
                .Numeric("std", "Tread", 0.1f, 1f,
                    () => Flight != null ? Flight.TreadDepth : 0f,
                    (_, v) => Commit(tread: v),
                    () => $"{(Flight != null ? Flight.TreadDepth : 0f) * 100f:0.#} cm", displayScale: 100f)
                .Numeric("swd", "Width", 0.5f, 3f,
                    () => Flight != null ? Flight.Width : 0f,
                    (_, v) => Commit(width: v),
                    () => $"{(Flight != null ? Flight.Width : 0f) * 100f:0} cm", displayScale: 100f)
                .Segmented("skind", "Kind", new[] { "Solid", "Open", "Waist" },
                    () => Flight != null ? (int)Flight.Kind : 0,
                    i => Commit(kind: (StairKind)i))
                .Readout("stot", "Total",
                    () => Flight == null ? "—"
                        : $"{Flight.TotalHeight * 100f:0} cm up · run {Flight.RunLength * 100f:0} cm");
            return _schema;
        }

        /// <summary>One undoable command per edit; unspecified fields keep their value.</summary>
        private void Commit(int? risers = null, float? riserHeight = null,
            float? tread = null, float? width = null, StairKind? kind = null)
        {
            var s = Flight;
            if (s == null) return;
            var cmd = new StairParamCommand(this,
                before: (s.Risers, s.RiserHeight, s.TreadDepth, s.Width, s.Kind),
                after: (risers ?? s.Risers, riserHeight ?? s.RiserHeight,
                        tread ?? s.TreadDepth, width ?? s.Width, kind ?? s.Kind));
            var model = SceneModel.Instance;
            if (model != null) model.History.Execute(cmd);
            else cmd.Do();               // bare test rig — still apply
        }

        internal Stair TargetFlight => Flight;
    }

    /// <summary>Full before/after snapshot of the flight's shape — replay cannot drift.</summary>
    public class StairParamCommand : ICommand, ISelectableCommand
    {
        private readonly StairParameters _owner;
        private readonly (int r, float rh, float td, float w, StairKind k) _before, _after;

        public StairParamCommand(StairParameters owner,
            (int, float, float, float, StairKind) before,
            (int, float, float, float, StairKind) after)
        {
            _owner = owner;
            _before = before;
            _after = after;
        }

        public ISelectable Target =>
            _owner != null ? _owner.GetComponent<ISelectable>() : null;

        public string Name => "Stair";
        public void Do() => Apply(_after);
        public void Undo() => Apply(_before);

        private void Apply((int r, float rh, float td, float w, StairKind k) p)
        {
            var s = _owner != null ? _owner.TargetFlight : null;
            if (s == null) return;
            s.Build(s.Base, s.YawDeg, p.w, p.r, p.rh, p.td, p.k);
        }
    }
}
