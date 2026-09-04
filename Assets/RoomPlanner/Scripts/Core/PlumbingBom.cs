using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace RoomPlanner.Plumbing
{
    /// <summary>One pipe run as the BOM sees it.</summary>
    public struct PipeBomEntry
    {
        public PipeDiameter Diameter;
        public float LengthMeters;
        public int Connections;    // attached ends, each adds the connection allowance
        public int Elbows90, Elbows45;

        public PipeBomEntry(PipeDiameter diameter, float lengthMeters, int connections,
            int elbows90, int elbows45)
        {
            Diameter = diameter;
            LengthMeters = lengthMeters;
            Connections = connections;
            Elbows90 = elbows90;
            Elbows45 = elbows45;
        }
    }

    /// <summary>
    /// Pipe bill of materials (docs/design/30-plumbing.md): polyline lengths by
    /// diameter + a per-connection allowance + a reserve percent, plus fitting counts
    /// (90/45 elbows). Pure math — the mirror of ElectricalBom.
    /// </summary>
    public static class PlumbingBom
    {
        /// <summary>Meters per diameter with allowances and reserve applied,
        /// indexed by (int)PipeDiameter.</summary>
        public static float[] MetersByDiameter(IReadOnlyList<PipeBomEntry> entries, int reservePercent)
        {
            var meters = new float[PipeSpec.TypeCount];
            if (entries == null) return meters;
            float factor = 1f + Mathf.Max(0, reservePercent) / 100f;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.LengthMeters <= 0f) continue;
                meters[(int)e.Diameter] +=
                    (e.LengthMeters + e.Connections * PlumbingDefaults.ConnectionAllowance) * factor;
            }
            return meters;
        }

        public static float Total(float[] metersByDiameter)
        {
            float sum = 0f;
            if (metersByDiameter != null)
                for (int i = 0; i < metersByDiameter.Length; i++) sum += metersByDiameter[i];
            return sum;
        }

        /// <summary>Riser summary for the inspector selection group, e.g.
        /// "D110 — 6.4 m · D50 — 12.1 m · Total — 18.5 m (+10%) · elbows 90°×3 45°×1".</summary>
        public static string Describe(IReadOnlyList<PipeBomEntry> entries, int reservePercent)
        {
            var meters = MetersByDiameter(entries, reservePercent);
            int e90 = 0, e45 = 0;
            if (entries != null)
                for (int i = 0; i < entries.Count; i++)
                {
                    e90 += entries[i].Elbows90;
                    e45 += entries[i].Elbows45;
                }

            var sb = new StringBuilder();
            for (int i = 0; i < meters.Length; i++)
            {
                if (meters[i] <= 0f) continue;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append('D').Append(PipeSpec.Label((PipeDiameter)i))
                  .Append(" — ").Append(FormatMeters(meters[i]));
            }
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append("Total — ").Append(FormatMeters(Total(meters)));
            sb.Append(" (+").Append(reservePercent).Append("%)");
            if (e90 > 0 || e45 > 0)
            {
                sb.Append(" · elbows");
                if (e90 > 0) sb.Append(" 90°×").Append(e90);
                if (e45 > 0) sb.Append(" 45°×").Append(e45);
            }
            return sb.ToString();
        }

        public static string FormatMeters(float meters) =>
            meters.ToString("0.0", CultureInfo.InvariantCulture) + " m";
    }
}
