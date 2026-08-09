using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RoomPlanner.Core.Ifc
{
    public enum StepKind
    {
        Null,   // $
        Star,   // *
        Number,
        Text,   // 'string'
        Enum,   // .NAME.
        Ref,    // #123
        List,   // (a,b,c)
        Typed,  // IFCPLANEANGLEMEASURE(0.017)
    }

    /// <summary>
    /// One parsed STEP (ISO-10303-21) argument. A small closed tree: numbers, strings,
    /// enums, entity references, nested lists and wrapped typed values — everything the
    /// IFC subset in docs/design/18-ifc-import.md needs.
    /// </summary>
    public sealed class StepValue
    {
        public StepKind Kind;
        public double Number;
        public string Text;              // Text payload, Enum name, or Typed-value name
        public int Ref;
        public List<StepValue> Items;    // List members or Typed-value inner args

        public bool IsNull => Kind == StepKind.Null;
        public float AsFloat => (float)Number;
        public int Count => Items?.Count ?? 0;
        public StepValue this[int i] => Items[i];

        public override string ToString() => Kind switch
        {
            StepKind.Number => Number.ToString(CultureInfo.InvariantCulture),
            StepKind.Text => $"'{Text}'",
            StepKind.Enum => $".{Text}.",
            StepKind.Ref => $"#{Ref}",
            StepKind.Typed => $"{Text}(…)",
            StepKind.List => $"({Count} items)",
            _ => Kind.ToString(),
        };
    }

    /// <summary>
    /// Parses single STEP records: `#id=TYPE(arg,arg,…)`. Header lines and record
    /// framing are handled by <see cref="StepFile"/>; this class owns the grammar of
    /// one record body, including `''` escapes and `\X2\…\X0\` unicode sequences.
    /// </summary>
    public static class StepTokenizer
    {
        /// <summary>Split `#id=TYPE(body)` without parsing the body (cheap index pass).</summary>
        public static bool TryParseRecord(string record, out int id, out string type, out string body)
        {
            id = 0; type = null; body = null;
            int i = 0, n = record.Length;
            while (i < n && char.IsWhiteSpace(record[i])) i++;
            if (i >= n || record[i] != '#') return false;
            i++;
            int idStart = i;
            while (i < n && char.IsDigit(record[i])) i++;
            if (i == idStart || i >= n || record[i] != '=') return false;
            id = int.Parse(record.Substring(idStart, i - idStart), CultureInfo.InvariantCulture);
            i++;
            int typeStart = i;
            while (i < n && record[i] != '(') i++;
            if (i >= n) return false;
            type = record.Substring(typeStart, i - typeStart).Trim();
            int open = i;
            int close = record.LastIndexOf(')');
            if (close <= open) return false;
            body = record.Substring(open + 1, close - open - 1);
            return true;
        }

        /// <summary>Parse a record body into its argument list.</summary>
        public static List<StepValue> ParseArgs(string body)
        {
            int pos = 0;
            return ParseList(body, ref pos);
        }

        private static List<StepValue> ParseList(string s, ref int pos)
        {
            var items = new List<StepValue>();
            SkipWs(s, ref pos);
            while (pos < s.Length && s[pos] != ')')
            {
                items.Add(ParseValue(s, ref pos));
                SkipWs(s, ref pos);
                if (pos < s.Length && s[pos] == ',') { pos++; SkipWs(s, ref pos); }
            }
            return items;
        }

        private static StepValue ParseValue(string s, ref int pos)
        {
            SkipWs(s, ref pos);
            char c = s[pos];
            switch (c)
            {
                case '$': pos++; return new StepValue { Kind = StepKind.Null };
                case '*': pos++; return new StepValue { Kind = StepKind.Star };
                case '#':
                {
                    pos++;
                    int start = pos;
                    while (pos < s.Length && char.IsDigit(s[pos])) pos++;
                    return new StepValue
                    {
                        Kind = StepKind.Ref,
                        Ref = int.Parse(s.Substring(start, pos - start), CultureInfo.InvariantCulture),
                    };
                }
                case '\'': return new StepValue { Kind = StepKind.Text, Text = ParseString(s, ref pos) };
                case '.':
                {
                    pos++;
                    int start = pos;
                    while (pos < s.Length && s[pos] != '.') pos++;
                    var name = s.Substring(start, pos - start);
                    pos++; // closing '.'
                    return new StepValue { Kind = StepKind.Enum, Text = name };
                }
                case '(':
                {
                    pos++;
                    var items = ParseList(s, ref pos);
                    pos++; // ')'
                    return new StepValue { Kind = StepKind.List, Items = items };
                }
                default:
                {
                    if (c == '-' || c == '+' || char.IsDigit(c))
                    {
                        int start = pos;
                        while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.' || s[pos] == '-'
                            || s[pos] == '+' || s[pos] == 'e' || s[pos] == 'E')) pos++;
                        return new StepValue
                        {
                            Kind = StepKind.Number,
                            Number = double.Parse(s.Substring(start, pos - start), CultureInfo.InvariantCulture),
                        };
                    }
                    // typed value: IDENT(args)
                    {
                        int start = pos;
                        while (pos < s.Length && s[pos] != '(') pos++;
                        var name = s.Substring(start, pos - start).Trim();
                        pos++; // '('
                        var items = ParseList(s, ref pos);
                        pos++; // ')'
                        return new StepValue { Kind = StepKind.Typed, Text = name, Items = items };
                    }
                }
            }
        }

        private static void SkipWs(string s, ref int pos)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
        }

        /// <summary>
        /// STEP string literal: `''` is a literal quote, `\\` a backslash, `\X2\hhhh…\X0\`
        /// big-endian UTF-16 (how Revit writes cyrillic), `\X\hh` latin-1. Unknown
        /// directives are kept verbatim rather than dropped.
        /// </summary>
        private static string ParseString(string s, ref int pos)
        {
            var sb = new StringBuilder();
            pos++; // opening '
            while (pos < s.Length)
            {
                char c = s[pos];
                if (c == '\'')
                {
                    if (pos + 1 < s.Length && s[pos + 1] == '\'') { sb.Append('\''); pos += 2; continue; }
                    pos++;
                    break;
                }
                if (c == '\\')
                {
                    if (TryDirective(s, ref pos, sb)) continue;
                    sb.Append(c);
                    pos++;
                    continue;
                }
                sb.Append(c);
                pos++;
            }
            return sb.ToString();
        }

        private static bool TryDirective(string s, ref int pos, StringBuilder sb)
        {
            if (s.Length - pos >= 2 && s[pos + 1] == '\\') { sb.Append('\\'); pos += 2; return true; }
            if (Matches(s, pos, "\\X2\\"))
            {
                int end = s.IndexOf("\\X0\\", pos + 4, System.StringComparison.Ordinal);
                if (end < 0) return false;
                for (int i = pos + 4; i + 4 <= end; i += 4)
                {
                    int code = int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    sb.Append((char)code);
                }
                pos = end + 4;
                return true;
            }
            if (Matches(s, pos, "\\X\\") && s.Length - pos >= 5)
            {
                int code = int.Parse(s.Substring(pos + 3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                sb.Append((char)code);
                pos += 5;
                return true;
            }
            return false;
        }

        private static bool Matches(string s, int pos, string token)
        {
            if (pos + token.Length > s.Length) return false;
            for (int i = 0; i < token.Length; i++)
                if (s[pos + i] != token[i]) return false;
            return true;
        }
    }
}
