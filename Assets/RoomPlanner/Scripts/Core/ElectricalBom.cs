using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RoomPlanner.Electrical
{
    /// <summary>One route as the BOM sees it.</summary>
    public struct RouteBomEntry
    {
        public CableType Cable;
        public float LengthMeters;
        public int Connections;   // attached ends, each adds the connection allowance

        public RouteBomEntry(CableType cable, float lengthMeters, int connections)
        {
            Cable = cable;
            LengthMeters = lengthMeters;
            Connections = connections;
        }
    }

    /// <summary>
    /// Cable bill of materials (docs/design/07-mep-layers.md): spline lengths by cable
    /// type + a per-connection allowance + a global reserve percent. Pure math.
    /// </summary>
    public static class ElectricalBom
    {
        /// <summary>Stripping/termination slack per attached end (~15 cm trade practice).</summary>
        public const float ConnectionAllowance = 0.15f;

        /// <summary>Meters per cable type with allowances and reserve applied,
        /// indexed by (int)CableType.</summary>
        public static float[] MetersByType(IReadOnlyList<RouteBomEntry> entries, int reservePercent)
        {
            var meters = new float[Cable.TypeCount];
            if (entries == null) return meters;
            float factor = 1f + UnityEngine.Mathf.Max(0, reservePercent) / 100f;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.LengthMeters <= 0f) continue;
                meters[(int)e.Cable] += (e.LengthMeters + e.Connections * ConnectionAllowance) * factor;
            }
            return meters;
        }

        public static float Total(float[] metersByType)
        {
            float sum = 0f;
            if (metersByType != null)
                for (int i = 0; i < metersByType.Length; i++) sum += metersByType[i];
            return sum;
        }

        /// <summary>Panel summary for the inspector selection group, e.g.
        /// "3x1.5 — 24.6 m · 3x2.5 — 38.1 m · Total — 62.7 m (+10%)".</summary>
        public static string Describe(IReadOnlyList<RouteBomEntry> entries, int reservePercent, int unrouted)
        {
            var meters = MetersByType(entries, reservePercent);
            var sb = new StringBuilder();
            for (int i = 0; i < meters.Length; i++)
            {
                if (meters[i] <= 0f) continue;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(Cable.Label((CableType)i)).Append(" — ").Append(FormatMeters(meters[i]));
            }
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append("Total — ").Append(FormatMeters(Total(meters)));
            sb.Append(" (+").Append(reservePercent).Append("%)");
            if (unrouted > 0) sb.Append(" · unrouted: ").Append(unrouted);
            return sb.ToString();
        }

        public static string FormatMeters(float meters) =>
            meters.ToString("0.0", CultureInfo.InvariantCulture) + " m";
    }
}
