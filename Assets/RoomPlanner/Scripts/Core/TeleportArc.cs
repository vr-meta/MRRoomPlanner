using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>
    /// Ballistic aim curve for the portal teleport (docs/design/21-locomotion.md):
    /// pure parabola sampling with no scene dependencies — the locomotion component
    /// casts rays along consecutive samples to find the landing spot.
    /// </summary>
    public static class TeleportArc
    {
        public const float Speed = 7f;          // m/s along the controller forward
        public const float Gravity = 9.81f;     // m/s², pulls samples down
        public const float TimeStep = 0.05f;    // s between samples
        public const int MaxSamples = 40;       // ~2 s of flight ≈ 10 m of range

        /// <summary>
        /// Fills <paramref name="result"/> with arc points starting AT origin:
        /// p(t) = origin + forward·speed·t − ŷ·g·t²/2. Returns the point count
        /// (0 when the inputs are degenerate).
        /// </summary>
        public static int Sample(Vector3 origin, Vector3 forward, float speed, float gravity,
            float timeStep, int maxSamples, List<Vector3> result)
        {
            result.Clear();
            if (maxSamples < 2 || timeStep <= 0f || forward.sqrMagnitude < 1e-8f) return 0;
            Vector3 v0 = forward.normalized * speed;
            for (int i = 0; i < maxSamples; i++)
            {
                float t = i * timeStep;
                result.Add(origin + v0 * t + (0.5f * gravity * t * t) * Vector3.down);
            }
            return result.Count;
        }
    }
}
