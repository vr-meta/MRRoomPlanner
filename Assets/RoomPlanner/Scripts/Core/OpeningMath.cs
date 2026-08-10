using UnityEngine;

namespace RoomPlanner.Walls
{
    /// <summary>
    /// Placement rules for wall openings (design/03, audit F1): pure math so the
    /// Openings tool's validation is unit-testable. All positions are METRES along
    /// the wall from node A (fractions only live at the WallOpening boundary).
    /// </summary>
    public static class OpeningMath
    {
        /// <summary>Minimum solid pier to a neighbouring opening or the wall end.</summary>
        public const float MinPier = 0.05f;
        /// <summary>Minimum solid header above the opening.</summary>
        public const float MinHeader = 0.05f;
        public const float MinWidth = 0.3f;

        /// <summary>
        /// Can an opening (centre, width, sill+height) be placed on this segment?
        /// Piers to both ends and every existing opening ≥ MinPier, top clears the
        /// header, width sane. Hidden walls are the caller's business.
        /// </summary>
        public static bool CanPlace(WallSegment s, float centerMeters, float width, float topAboveBase)
        {
            if (s == null || width < MinWidth) return false;
            float len = s.Length;
            if (len < width + 2f * MinPier) return false;
            if (topAboveBase > s.Height - MinHeader) return false;

            float half = width * 0.5f;
            if (centerMeters - half < MinPier || centerMeters + half > len - MinPier) return false;

            foreach (var o in s.Openings)
            {
                float oc = o.AlongFraction * len;
                float oh = o.Width * 0.5f;
                bool clear = centerMeters + half + MinPier <= oc - oh
                          || oc + oh + MinPier <= centerMeters - half;
                if (!clear) return false;
            }
            return true;
        }

        /// <summary>Index of the opening whose centre is nearest to the aim point along
        /// the wall (within maxDist metres); -1 = nothing close enough. Drives v1
        /// deletion: aim near the opening, press B.</summary>
        public static int NearestOpening(WallSegment s, float alongMeters, float maxDist)
        {
            if (s == null) return -1;
            float len = s.Length;
            int best = -1;
            float bestD = maxDist;
            for (int i = 0; i < s.Openings.Count; i++)
            {
                float d = Mathf.Abs(s.Openings[i].AlongFraction * len - alongMeters);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }
    }
}
