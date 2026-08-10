using UnityEngine;

namespace RoomPlanner.Stairs
{
    /// <summary>
    /// Headroom math for stair flights (audit 2026-08-10, 05 §Б1): the slab above a
    /// flight must be open wherever its underside comes closer than MinHeadroom to the
    /// walking line — otherwise the user walks face-first into the ceiling (the imported
    /// first-floor stairwell bug). Pure functions; the scene-side merge-and-cut lives in
    /// Stair.CutHeadroomIn.
    /// </summary>
    public static class StairMath
    {
        /// <summary>Minimum clear height above the walking line, metres.</summary>
        public const float MinHeadroom = 2.0f;

        /// <summary>Extra opening width beyond the flight on each side, metres.</summary>
        public const float SideMargin = 0.05f;

        /// <summary>
        /// Walking-line height above the stair base at horizontal distance d along the run
        /// (0 = first riser, RunLength = top riser edge): linear between the first tread
        /// top and the flight's total height.
        /// </summary>
        public static float StepLineY(float d, int risers, float riserHeight, float treadDepth)
        {
            risers = Mathf.Max(1, risers);
            float total = risers * riserHeight;
            float run = Mathf.Max(0f, (risers - 1) * treadDepth);
            if (run < 1e-4f) return total;
            return Mathf.Lerp(riserHeight, total, Mathf.Clamp01(d / run));
        }

        /// <summary>
        /// The along-run range [dStart, dEnd] that must be OPEN in a slab whose underside
        /// sits slabBottomAboveBase metres above the stair base. dEnd is always the top
        /// edge of the flight — that is where the walker emerges onto the landing.
        /// False = the whole flight clears MinHeadroom under this slab, nothing to cut.
        /// </summary>
        public static bool CutRange(int risers, float riserHeight, float treadDepth,
            float slabBottomAboveBase, out float dStart, out float dEnd)
        {
            risers = Mathf.Max(1, risers);
            float total = risers * riserHeight;
            float run = Mathf.Max(0f, (risers - 1) * treadDepth);
            dStart = 0f;
            dEnd = run;
            if (slabBottomAboveBase - total >= MinHeadroom) return false;   // clears everywhere
            if (run < 1e-4f) return true;

            // Solve StepLineY(d) == slabBottom - MinHeadroom for d: below that height the
            // walking line still has clearance, above it the slab must be open.
            float yCut = slabBottomAboveBase - MinHeadroom;
            if (yCut <= riserHeight) return true;                            // violated from step one
            dStart = Mathf.Clamp((yCut - riserHeight) / (total - riserHeight) * run, 0f, run);
            return dStart < run - 1e-4f;
        }
    }
}
