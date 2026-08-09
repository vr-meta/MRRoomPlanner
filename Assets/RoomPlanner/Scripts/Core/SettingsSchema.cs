using System;
using System.Collections.Generic;

namespace RoomPlanner.Core
{
    /// <summary>Row types the inspector can render.</summary>
    public enum SettingKind
    {
        Stepper,   // caption  [−]  value  [+]
        Cycle      // caption  value  [>]
    }

    /// <summary>
    /// One inspector row, fully described by the tool that owns it: how to display the
    /// current value and what to do on button presses. Pure delegates — no Unity types —
    /// so schemas are unit-testable and the inspector stays generic.
    /// </summary>
    public sealed class SettingField
    {
        public string Id;              // stable id (debug / future persistence)
        public string Caption;         // row caption, English (UI language rule)
        public SettingKind Kind;
        public Func<string> Value;     // current display value
        public Action Decrease;        // Stepper only
        public Action Increase;        // Stepper: +; Cycle: next
    }

    /// <summary>
    /// A tool's settings, built fluently by the tool itself (design/14-modularity.md).
    /// The inspector renders rows from this — adding a parameter touches ONLY the tool.
    /// </summary>
    public sealed class SettingsSchema
    {
        private readonly List<SettingField> _fields = new();

        public IReadOnlyList<SettingField> Fields => _fields;

        public SettingsSchema Stepper(string id, string caption, Func<string> value, Action decrease, Action increase)
        {
            _fields.Add(new SettingField
            {
                Id = id, Caption = caption, Kind = SettingKind.Stepper,
                Value = value, Decrease = decrease, Increase = increase,
            });
            return this;
        }

        public SettingsSchema Cycle(string id, string caption, Func<string> value, Action next)
        {
            _fields.Add(new SettingField
            {
                Id = id, Caption = caption, Kind = SettingKind.Cycle,
                Value = value, Increase = next,
            });
            return this;
        }
    }
}
