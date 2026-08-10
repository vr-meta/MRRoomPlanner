using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>
    /// Row types the inspector can render — the full widget vocabulary of
    /// docs/design/20-ui-design-system.md §2. Twelve kinds, hard cap: every kind is a
    /// state machine to maintain and a pattern the user must learn.
    /// </summary>
    public enum SettingKind
    {
        Stepper,    // caption  [−]  value  [+]        — discrete steps, ≤ ~20 values or unbounded
        Cycle,      // caption  value  [>]             — LEGACY (design/20 §2): migrating to
                    //                                   Segmented / Select / Action / Tabs
        Slider,     // caption  track+handle  value    — bounded continuous range
        Segmented,  // caption  [opt|opt|opt]          — ≤4 mutually exclusive options, all visible
        Select,     // caption  value  ⌄  → popup list — 5+ options or dynamic lists
        Toggle,     // caption           pill          — booleans; immediate effect
        Numeric,    // caption  [ value unit ]         — tap → numpad popup, exact entry
        Swatch,     // caption  chip → swatch grid     — color presets (hue here IS data)
        Action,     // [ icon  caption ]               — command button; optional destructive
        Readout,    // caption          value          — inert status text, no chrome
        Progress,   // caption  ▮▮▮▯▯  value           — determinate bar or indeterminate pulse
        Header      // ── CAPTION ──                   — group divider, mandatory past 6 rows
    }

    /// <summary>
    /// One inspector row, fully described by the tool that owns it. Pure delegates —
    /// no scene types — so schemas stay unit-testable and the inspector generic.
    /// Only the fields relevant to the row's Kind are set; the rest stay null/default.
    /// </summary>
    public sealed class SettingField
    {
        public string Id;              // stable id (debug / future persistence)
        public string Caption;         // row caption, English (UI language rule)
        public SettingKind Kind;
        public string IconId;          // optional IconPaths id (Action rows, headers)

        public Func<string> Value;     // display string (Stepper/Cycle/Numeric/Readout/Select)
        public Action Decrease;        // Stepper −
        public Action Increase;        // Stepper +; Cycle next; Action invoke

        // Slider / Numeric range
        public float Min;
        public float Max;
        public float Step;             // detent interval; <= 0 → continuous
        public Func<float> GetNumber;
        public Action<float> SetNumber;            // live preview while dragging / typing
        public Action<float, float> CommitNumber;  // (before, after) → ONE undo command on release/OK

        // Segmented / Select / Swatch selection
        public string[] Options;                   // static option labels
        public Func<string[]> OptionsProvider;     // dynamic lists (files); wins over Options
        public Func<int> GetIndex;
        public Action<int> SetIndex;

        // Toggle
        public Func<bool> GetOn;
        public Action<bool> SetOn;

        // Swatch palette (selection via GetIndex/SetIndex)
        public Color[] Palette;

        // Action
        public bool Destructive;       // outline + hold-to-fill Danger (design/20 §2.8)

        // Progress: 0..1; negative → indeterminate
        public Func<float> GetProgress;

        /// <summary>Options resolved at render time — provider wins for dynamic lists.</summary>
        public string[] ResolveOptions() => OptionsProvider != null ? OptionsProvider() : Options;
    }

    /// <summary>
    /// A tool's settings, built fluently by the tool itself (design/14-modularity.md).
    /// The inspector renders rows from this — adding a parameter touches ONLY the tool.
    /// Sub-modes are TABS (design/20 §2.12): a schema either has Fields, or has tab pages
    /// (each page is itself a plain schema) — never both.
    /// </summary>
    public sealed class SettingsSchema
    {
        private readonly List<SettingField> _fields = new();

        public IReadOnlyList<SettingField> Fields => _fields;

        // ---- tabs (replaces the swap-whole-schema hack for sub-modes) ----
        public string[] Tabs { get; private set; }
        public Func<int> ActiveTab { get; private set; }
        public Action<int> SelectTab { get; private set; }
        public SettingsSchema[] TabPages { get; private set; }
        public bool HasTabs => Tabs != null && Tabs.Length > 0;

        /// <summary>The schema whose rows are currently visible (self when untabbed).</summary>
        public SettingsSchema ActivePage()
        {
            if (!HasTabs) return this;
            int i = Mathf.Clamp(ActiveTab?.Invoke() ?? 0, 0, TabPages.Length - 1);
            return TabPages[i];
        }

        public static SettingsSchema Tabbed(string[] tabs, Func<int> activeTab,
            Action<int> selectTab, params SettingsSchema[] pages)
        {
            if (tabs == null || pages == null || tabs.Length != pages.Length)
                throw new ArgumentException("tabs and pages must match 1:1");
            return new SettingsSchema
            {
                Tabs = tabs, ActiveTab = activeTab, SelectTab = selectTab, TabPages = pages,
            };
        }

        // ---- fluent builders, one per kind ----

        public SettingsSchema Stepper(string id, string caption, Func<string> value,
            Action decrease, Action increase)
        {
            _fields.Add(new SettingField
            {
                Id = id, Caption = caption, Kind = SettingKind.Stepper,
                Value = value, Decrease = decrease, Increase = increase,
            });
            return this;
        }

        /// <summary>LEGACY — kept alive until every tool migrates to Segmented / Select /
        /// Action / Tabs (checklist 2m U7); do not add new Cycle rows.</summary>
        public SettingsSchema Cycle(string id, string caption, Func<string> value, Action next)
        {
            _fields.Add(new SettingField
            {
                Id = id, Caption = caption, Kind = SettingKind.Cycle,
                Value = value, Increase = next,
            });
            return this;
        }

        public SettingsSchema Slider(string id, string caption, float min, float max, float step,
            Func<float> get, Action<float> preview, Action<float, float> commit,
            Func<string> value)
        {
            _fields.Add(new SettingField
            {
                Id = id, Caption = caption, Kind = SettingKind.Slider,
                Min = min, Max = max, Step = step,
                GetNumber = get, SetNumber = preview, CommitNumber = commit, Value = value,
            });
            return this;
        }

        public SettingsSchema Segmented(string id, string caption, string[] options,
            Func<int> getIndex, Action<int> setIndex)
        {
            _fields.Add(new SettingField
            {
                Id = id, Caption = caption, Kind = SettingKind.Segmented,
                Options = options, GetIndex = getIndex, SetIndex = setIndex,
            });
            return this;
        }

        public SettingsSchema Select(string id, string caption, Func<string[]> options,
            Func<int> getIndex, Action<int> setIndex)
        {
            _fields.Add(new SettingField
            {
                Id = id, Caption = caption, Kind = SettingKind.Select,
                OptionsProvider = options, GetIndex = getIndex, SetIndex = setIndex,
            });
            return this;
        }

        public SettingsSchema Toggle(string id, string caption, Func<bool> getOn, Action<bool> setOn)
        {
            _fields.Add(new SettingField
            {
                Id = id, Caption = caption, Kind = SettingKind.Toggle,
                GetOn = getOn, SetOn = setOn,
            });
            return this;
        }

        public SettingsSchema Numeric(string id, string caption, float min, float max,
            Func<float> get, Action<float, float> commit, Func<string> value)
        {
            _fields.Add(new SettingField
            {
                Id = id, Caption = caption, Kind = SettingKind.Numeric,
                Min = min, Max = max, GetNumber = get, CommitNumber = commit, Value = value,
            });
            return this;
        }

        public SettingsSchema Swatch(string id, string caption, Color[] palette,
            Func<int> getIndex, Action<int> setIndex)
        {
            _fields.Add(new SettingField
            {
                Id = id, Caption = caption, Kind = SettingKind.Swatch,
                Palette = palette, GetIndex = getIndex, SetIndex = setIndex,
            });
            return this;
        }

        public SettingsSchema Action(string id, string caption, string iconId, Action invoke,
            bool destructive = false)
        {
            _fields.Add(new SettingField
            {
                Id = id, Caption = caption, Kind = SettingKind.Action,
                IconId = iconId, Increase = invoke, Destructive = destructive,
            });
            return this;
        }

        public SettingsSchema Readout(string id, string caption, Func<string> value)
        {
            _fields.Add(new SettingField
            {
                Id = id, Caption = caption, Kind = SettingKind.Readout, Value = value,
            });
            return this;
        }

        public SettingsSchema Progress(string id, string caption, Func<float> progress)
        {
            _fields.Add(new SettingField
            {
                Id = id, Caption = caption, Kind = SettingKind.Progress, GetProgress = progress,
            });
            return this;
        }

        public SettingsSchema Header(string id, string caption)
        {
            _fields.Add(new SettingField
            {
                Id = id, Caption = caption, Kind = SettingKind.Header,
            });
            return this;
        }
    }
}
