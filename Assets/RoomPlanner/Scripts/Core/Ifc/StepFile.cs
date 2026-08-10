using System.Collections.Generic;

namespace RoomPlanner.Core.Ifc
{
    /// <summary>
    /// Indexed view of a STEP/IFC file: `#id → (type, raw body)`, with argument trees
    /// parsed lazily and cached — a 10 MB Revit export has ~200k records but an import
    /// only ever dereferences a few thousand of them (docs/design/18-ifc-import.md).
    /// </summary>
    public sealed class StepFile
    {
        private readonly Dictionary<int, (string Type, string Body)> _records = new();
        private readonly Dictionary<int, List<StepValue>> _argsCache = new();
        private readonly Dictionary<string, List<int>> _byType = new();
        private static readonly List<int> Empty = new();

        /// <summary>Multiplier from the file's length unit to metres (Revit exports MILLI → 0.001).</summary>
        public double LengthToMeters { get; private set; } = 1.0;

        public int Count => _records.Count;

        public static StepFile Parse(string text)
        {
            var f = new StepFile();
            // Records can span lines and strings can contain anything, so split on ';'
            // while tracking the in-string state instead of splitting on newlines.
            int recordStart = 0;
            bool inString = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\'') inString = !inString; // '' toggles twice — net effect is correct
                if (c == ';' && !inString)
                {
                    // Cheap pre-filter: only entity records start with '#'.
                    int s = recordStart;
                    while (s < i && char.IsWhiteSpace(text[s])) s++;
                    if (s < i && text[s] == '#')
                    {
                        var record = text.Substring(s, i - s);
                        if (StepTokenizer.TryParseRecord(record, out int id, out string type, out string body))
                        {
                            f._records[id] = (type, body);
                            if (!f._byType.TryGetValue(type, out var list))
                                f._byType[type] = list = new List<int>();
                            list.Add(id);
                        }
                    }
                    recordStart = i + 1;
                }
            }
            f.DetectUnits();
            return f;
        }

        public bool Has(int id) => _records.ContainsKey(id);

        public string TypeOf(int id) => _records.TryGetValue(id, out var r) ? r.Type : null;

        /// <summary>Parsed argument list of a record (lazily cached). Null if the id is unknown.</summary>
        public List<StepValue> Args(int id)
        {
            if (_argsCache.TryGetValue(id, out var cached)) return cached;
            if (!_records.TryGetValue(id, out var r)) return null;
            var args = StepTokenizer.ParseArgs(r.Body);
            _argsCache[id] = args;
            return args;
        }

        /// <summary>All record ids of an entity type (e.g. "IFCWALLSTANDARDCASE").</summary>
        public IReadOnlyList<int> OfType(string type) =>
            _byType.TryGetValue(type, out var list) ? list : Empty;

        /// <summary>Follow a Ref argument to its record's args; null for $ or dangling refs.</summary>
        public List<StepValue> Deref(StepValue v) =>
            v != null && v.Kind == StepKind.Ref ? Args(v.Ref) : null;

        private void DetectUnits()
        {
            // Imperial exports FIRST: their factor to metres sits in
            // IFCCONVERSIONBASEDUNIT → IFCMEASUREWITHUNIT (feet: 0.3048), and the SI
            // metre they also contain is merely that conversion's BASE unit — probing
            // IFCSIUNIT first would claim it and silently leave the model 3.28× too
            // large (audit 09 §Б3). Metric files carry no length conversion unit.
            foreach (int id in OfType("IFCCONVERSIONBASEDUNIT"))
            {
                // IFCCONVERSIONBASEDUNIT(Dimensions, .LENGTHUNIT., Name, ConversionFactor)
                var a = Args(id);
                if (a == null || a.Count < 4) continue;
                if (a[1].Kind != StepKind.Enum || a[1].Text != "LENGTHUNIT") continue;
                var mwu = Deref(a[3]);   // IFCMEASUREWITHUNIT(ValueComponent, UnitComponent)
                if (mwu == null || mwu.Count < 1) continue;
                var value = mwu[0];
                // ValueComponent is a typed number: IFCRATIOMEASURE(0.3048) / IFCLENGTHMEASURE(…)
                double factor = value.Kind switch
                {
                    StepKind.Number => value.Number,
                    StepKind.Typed when value.Count > 0 && value[0].Kind == StepKind.Number
                        => value[0].Number,
                    _ => 0.0,
                };
                if (factor > 1e-9)
                {
                    LengthToMeters = factor;
                    return;
                }
            }

            foreach (int id in OfType("IFCSIUNIT"))
            {
                // IFCSIUNIT(*, .LENGTHUNIT., .MILLI.|$, .METRE.)
                var a = Args(id);
                if (a == null || a.Count < 4) continue;
                if (a[1].Kind != StepKind.Enum || a[1].Text != "LENGTHUNIT") continue;
                LengthToMeters = a[2].Kind == StepKind.Enum ? a[2].Text switch
                {
                    "MILLI" => 0.001,
                    "CENTI" => 0.01,
                    "DECI" => 0.1,
                    "KILO" => 1000.0,
                    _ => 1.0,
                } : 1.0;
                return;
            }
        }
    }
}
