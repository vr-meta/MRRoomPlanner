using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>
    /// Minimal SVG path-data parser for the icon system (docs/design/20-ui-design-system.md §5).
    ///
    /// Supported commands: M/m L/l H/h V/v C/c Q/q Z/z — the exact subset the icon guide
    /// allows (no arcs; circles are authored as cubic Béziers). Curves are flattened to
    /// polylines at parse time: icons are viewed at ~1–2.5°, where a fixed subdivision is
    /// visually exact and keeps the output deterministic for tests.
    ///
    /// Output is a list of closed subpaths in SVG coordinates (y grows DOWN — the
    /// tessellator flips the axis). Pure C#, unit-testable.
    /// </summary>
    public static class SvgPath
    {
        /// <summary>Segments per Bézier span. 8 keeps a 24-grid circle within ~0.05 px of true.</summary>
        public const int CurveSegments = 8;

        /// <summary>
        /// Parse path data into closed subpaths. Open subpaths are closed implicitly
        /// (fill semantics — the guide only allows filled shapes). Returns an empty list
        /// on malformed input rather than a partial guess (same refusal contract as
        /// <see cref="Polygon.Triangulate"/>).
        /// </summary>
        public static List<List<Vector2>> Parse(string data)
        {
            var result = new List<List<Vector2>>();
            if (string.IsNullOrEmpty(data)) return result;

            var tokens = Tokenize(data);
            if (tokens == null) return result;

            var current = new List<Vector2>();
            Vector2 pen = Vector2.zero;
            Vector2 subpathStart = Vector2.zero;
            char lastCmd = '\0';

            int i = 0;
            while (i < tokens.Count)
            {
                char cmd;
                if (tokens[i].IsCommand)
                {
                    cmd = tokens[i].Command;
                    i++;
                }
                else
                {
                    // implicit repetition: numbers after a command repeat it,
                    // except after M/m the implicit command is L/l (SVG spec)
                    if (lastCmd == '\0') return Fail(result);
                    cmd = lastCmd == 'M' ? 'L' : lastCmd == 'm' ? 'l' : lastCmd;
                }

                bool rel = char.IsLower(cmd);
                switch (char.ToUpperInvariant(cmd))
                {
                    case 'M':
                    {
                        if (!TakeNumbers(tokens, ref i, 2, out var n)) return Fail(result);
                        FlushSubpath(result, current);
                        current = new List<Vector2>();
                        pen = rel ? pen + new Vector2(n[0], n[1]) : new Vector2(n[0], n[1]);
                        subpathStart = pen;
                        current.Add(pen);
                        break;
                    }
                    case 'L':
                    {
                        if (!TakeNumbers(tokens, ref i, 2, out var n)) return Fail(result);
                        pen = rel ? pen + new Vector2(n[0], n[1]) : new Vector2(n[0], n[1]);
                        current.Add(pen);
                        break;
                    }
                    case 'H':
                    {
                        if (!TakeNumbers(tokens, ref i, 1, out var n)) return Fail(result);
                        pen = new Vector2(rel ? pen.x + n[0] : n[0], pen.y);
                        current.Add(pen);
                        break;
                    }
                    case 'V':
                    {
                        if (!TakeNumbers(tokens, ref i, 1, out var n)) return Fail(result);
                        pen = new Vector2(pen.x, rel ? pen.y + n[0] : n[0]);
                        current.Add(pen);
                        break;
                    }
                    case 'C':
                    {
                        if (!TakeNumbers(tokens, ref i, 6, out var n)) return Fail(result);
                        Vector2 c1 = rel ? pen + new Vector2(n[0], n[1]) : new Vector2(n[0], n[1]);
                        Vector2 c2 = rel ? pen + new Vector2(n[2], n[3]) : new Vector2(n[2], n[3]);
                        Vector2 end = rel ? pen + new Vector2(n[4], n[5]) : new Vector2(n[4], n[5]);
                        FlattenCubic(current, pen, c1, c2, end);
                        pen = end;
                        break;
                    }
                    case 'Q':
                    {
                        if (!TakeNumbers(tokens, ref i, 4, out var n)) return Fail(result);
                        Vector2 c = rel ? pen + new Vector2(n[0], n[1]) : new Vector2(n[0], n[1]);
                        Vector2 end = rel ? pen + new Vector2(n[2], n[3]) : new Vector2(n[2], n[3]);
                        FlattenQuadratic(current, pen, c, end);
                        pen = end;
                        break;
                    }
                    case 'Z':
                    {
                        FlushSubpath(result, current);
                        current = new List<Vector2>();
                        pen = subpathStart;
                        break;
                    }
                    default:
                        return Fail(result);   // unsupported command (A, S, T, …)
                }
                lastCmd = cmd;
            }

            FlushSubpath(result, current);
            return result;
        }

        // ---- internals ----

        private static List<List<Vector2>> Fail(List<List<Vector2>> result)
        {
            result.Clear();
            return result;
        }

        /// <summary>A ring needs 3+ distinct points to enclose area; shorter leftovers are
        /// author artefacts (a stray M) and are dropped silently.</summary>
        private static void FlushSubpath(List<List<Vector2>> result, List<Vector2> current)
        {
            if (current.Count == 0) return;
            // drop the duplicated closing point if the author drew back to the start
            while (current.Count > 1 &&
                   (current[current.Count - 1] - current[0]).sqrMagnitude < 1e-8f)
                current.RemoveAt(current.Count - 1);
            if (current.Count >= 3) result.Add(current);
        }

        private static void FlattenCubic(List<Vector2> into, Vector2 p0, Vector2 c1, Vector2 c2, Vector2 p3)
        {
            for (int s = 1; s <= CurveSegments; s++)
            {
                float t = (float)s / CurveSegments;
                float u = 1f - t;
                Vector2 p = u * u * u * p0
                          + 3f * u * u * t * c1
                          + 3f * u * t * t * c2
                          + t * t * t * p3;
                into.Add(p);
            }
        }

        private static void FlattenQuadratic(List<Vector2> into, Vector2 p0, Vector2 c, Vector2 p2)
        {
            for (int s = 1; s <= CurveSegments; s++)
            {
                float t = (float)s / CurveSegments;
                float u = 1f - t;
                Vector2 p = u * u * p0 + 2f * u * t * c + t * t * p2;
                into.Add(p);
            }
        }

        private struct Token
        {
            public bool IsCommand;
            public char Command;
            public float Number;
        }

        private static bool TakeNumbers(List<Token> tokens, ref int i, int count, out float[] numbers)
        {
            numbers = new float[count];
            for (int k = 0; k < count; k++)
            {
                if (i >= tokens.Count || tokens[i].IsCommand) return false;
                numbers[k] = tokens[i].Number;
                i++;
            }
            return true;
        }

        private static List<Token> Tokenize(string data)
        {
            var tokens = new List<Token>();
            int i = 0;
            while (i < data.Length)
            {
                char ch = data[i];
                if (ch == ' ' || ch == ',' || ch == '\t' || ch == '\n' || ch == '\r')
                {
                    i++;
                    continue;
                }
                if (char.IsLetter(ch))
                {
                    tokens.Add(new Token { IsCommand = true, Command = ch });
                    i++;
                    continue;
                }
                // number: sign, digits, dot, exponent-free (icon grid needs none)
                int start = i;
                if (ch == '-' || ch == '+') i++;
                bool sawDigit = false, sawDot = false;
                while (i < data.Length)
                {
                    char d = data[i];
                    if (char.IsDigit(d)) { sawDigit = true; i++; }
                    else if (d == '.' && !sawDot) { sawDot = true; i++; }
                    else break;
                }
                if (!sawDigit) return null;   // stray symbol → malformed
                float value = float.Parse(data.Substring(start, i - start),
                    CultureInfo.InvariantCulture);
                tokens.Add(new Token { Number = value });
            }
            return tokens;
        }
    }
}
