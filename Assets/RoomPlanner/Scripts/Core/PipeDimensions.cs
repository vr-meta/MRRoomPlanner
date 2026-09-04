using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>Physical dimensions in metres. Nominal markings are labels, never geometry.</summary>
    [Serializable]
    public struct PipeDimensions
    {
        public string Nominal;
        public float OuterDiameter, WallThickness, InsulationThickness;
        public float InnerDiameter => OuterDiameter - 2f * WallThickness;
        public float EnvelopeDiameter => OuterDiameter + 2f * InsulationThickness;
        public bool IsValid => ServicePlacement.Finite(OuterDiameter)
            && ServicePlacement.Finite(WallThickness) && ServicePlacement.Finite(InsulationThickness)
            && OuterDiameter > 0f && WallThickness > 0f && InnerDiameter > 0f && InsulationThickness >= 0f;
        public float BottomAt(float axisHeight) => axisHeight - EnvelopeDiameter * 0.5f;
        public float GapTo(PipeDimensions other, float axisDistance) =>
            axisDistance - (EnvelopeDiameter + other.EnvelopeDiameter) * 0.5f;
    }

    public static class DrainSlope
    {
        /// <summary>Build a gravity route along an XZ polyline. A fixed outlet is checked,
        /// never moved to force a match. Input and output may alias.</summary>
        public static bool TryApply(IReadOnlyList<Vector3> plan, float startHeight,
            float slopePercent, float? fixedEndHeight, List<Vector3> result, out float mismatch)
        {
            mismatch = 0f;
            if (plan == null || result == null || plan.Count < 2
                || !ServicePlacement.Finite(startHeight) || !ServicePlacement.Finite(slopePercent)
                || slopePercent <= 0f || (fixedEndHeight.HasValue && !ServicePlacement.Finite(fixedEndHeight.Value))) return false;
            float horizontalLength = 0f;
            for (int i = 0; i < plan.Count; i++)
            {
                if (!ServicePlacement.Finite(plan[i])) return false;
                if (i > 0)
                {
                    var delta = plan[i] - plan[i - 1]; delta.y = 0f;
                    if (delta.sqrMagnitude < 1e-10f) return false; // vertical drops are explicit, separate segments
                    horizontalLength += delta.magnitude;
                }
            }
            float end = startHeight - horizontalLength * slopePercent / 100f;
            if (fixedEndHeight.HasValue)
            {
                mismatch = end - fixedEndHeight.Value;
                if (Mathf.Abs(mismatch) > ServicePlacement.Tolerance) return false;
            }
            // Validate before touching output. Preserve the source when the caller passes result itself.
            bool alias = ReferenceEquals(plan, result);
            if (!alias) result.Clear();
            float distance = 0f;
            Vector3 previous = plan[0];
            for (int i = 0; i < plan.Count; i++)
            {
                var p = plan[i];
                var delta = p - previous; delta.y = 0f;
                distance += delta.magnitude;
                previous = p;
                p.y = startHeight - distance * slopePercent / 100f;
                if (alias) result[i] = p; else result.Add(p);
            }
            return true;
        }
    }
}
