using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>A slab as gravity sees it: a walking level plus the area it covers.</summary>
    public readonly struct WalkSlab
    {
        /// <summary>Y of the walking surface (the slab's top face).</summary>
        public readonly float Top;
        public readonly IReadOnlyList<Vector3> Outline;
        public readonly IReadOnlyList<IReadOnlyList<Vector3>> Holes;

        public WalkSlab(float top, IReadOnlyList<Vector3> outline,
            IReadOnlyList<IReadOnlyList<Vector3>> holes = null)
        {
            Top = top;
            Outline = outline;
            Holes = holes;
        }
    }

    /// <summary>A stair flight as gravity sees it — the parameters that define its treads.</summary>
    public readonly struct WalkFlight
    {
        public readonly Vector3 Base;
        public readonly float YawDeg;
        public readonly float Width;
        public readonly int Risers;
        public readonly float RiserHeight;
        public readonly float TreadDepth;

        public WalkFlight(Vector3 basePoint, float yawDeg, float width,
            int risers, float riserHeight, float treadDepth)
        {
            Base = basePoint;
            YawDeg = yawDeg;
            Width = width;
            Risers = risers;
            RiserHeight = riserHeight;
            TreadDepth = treadDepth;
        }

        /// <summary>Horizontal length of the run (the last riser top IS the landing edge).</summary>
        public float RunLength => Mathf.Max(0, Risers - 1) * TreadDepth;
    }

    /// <summary>
    /// Ground and gravity math (docs/design/25-ground.md, issue #59): where the walkable
    /// surface under a pair of feet is, and how fast you drop towards it. Pure functions —
    /// the scene-side cache and the rig/model shifting live in Tools/GroundService.
    ///
    /// The ground itself is a DERIVED datum (the lowest storey's walking level), not an
    /// object: teleporting moves the model, so a stored ground level would have to be
    /// chased frame by frame. Here it simply arrives as a parameter.
    /// </summary>
    public static class GroundMath
    {
        /// <summary>Highest ledge you can step onto without jumping, metres. Also the veto
        /// threshold for walking: a taller edge cancels the horizontal step, which doubles
        /// as free collision against slab rims and stair sides.</summary>
        public const float StepUp = 0.45f;

        /// <summary>Free-fall acceleration and its terminal speed (comfort cap, not physics).</summary>
        public const float FallAccel = 9.81f;
        public const float FallSpeedMax = 8f;

        /// <summary>Below this the foot level counts as "already on the surface".</summary>
        public const float Skin = 0.005f;

        /// <summary>
        /// The walking surface under (x, z) for feet at <paramref name="footY"/>: the
        /// highest slab top or stair tread no higher than footY + stepUp, with the ground
        /// always in the running — it is treated as INFINITE in XZ, so walking past the rim
        /// of the visible ground plane cannot drop you into the void. When even the ground
        /// is above the feet, the feet's own level is returned (stay put).
        /// </summary>
        public static float Support(IReadOnlyList<WalkSlab> slabs, IReadOnlyList<WalkFlight> flights,
            float groundY, float x, float z, float footY, float stepUp = StepUp)
        {
            float ceiling = footY + stepUp;
            float best = groundY <= ceiling ? groundY : float.NegativeInfinity;
            var p = new Vector3(x, 0f, z);

            if (slabs != null)
                for (int i = 0; i < slabs.Count; i++)
                {
                    var s = slabs[i];
                    if (s.Top <= best || s.Top > ceiling) continue;
                    if (Polygon.ContainsWithHoles(s.Outline, s.Holes, p)) best = s.Top;
                }

            if (flights != null)
                for (int i = 0; i < flights.Count; i++)
                {
                    if (!TryFlightTop(flights[i], x, z, out float y)) continue;
                    if (y > best && y <= ceiling) best = y;
                }

            // Nothing at or below the feet — only possible when the ground itself is above
            // them (a lone mezzanine slab is the model's lowest storey while the walker
            // stands under it). Standing still beats being yanked up to it.
            return float.IsNegativeInfinity(best) ? footY : best;
        }

        /// <summary>
        /// Tread top of a flight at run distance <paramref name="d"/> from its base: step i
        /// spans d ∈ [i·TreadDepth, (i+1)·TreadDepth) and its top sits (i+1) risers up.
        /// Matches the step profile Stair.Build extrudes.
        /// </summary>
        public static float TreadTopY(float d, int risers, float riserHeight, float treadDepth)
        {
            risers = Mathf.Max(1, risers);
            if (treadDepth <= 1e-4f) return risers * riserHeight;
            int step = Mathf.Clamp(Mathf.FloorToInt(d / treadDepth), 0, risers - 1);
            return (step + 1) * riserHeight;
        }

        /// <summary>
        /// Height of the flight's tread at (x, z), or false when the point is off the run.
        /// The flight's frame: +run = forward rotated by YawDeg, +side = its right.
        /// </summary>
        public static bool TryFlightTop(in WalkFlight f, float x, float z, out float y)
        {
            y = 0f;
            if (f.Risers <= 0) return false;
            float rad = f.YawDeg * Mathf.Deg2Rad;
            float sin = Mathf.Sin(rad), cos = Mathf.Cos(rad);
            float dx = x - f.Base.x, dz = z - f.Base.z;
            // inverse of (run = (sin, cos), side = (cos, −sin)) — a plain yaw rotation
            float d = dx * sin + dz * cos;
            float s = dx * cos - dz * sin;
            if (Mathf.Abs(s) > f.Width * 0.5f) return false;
            // the flight's footprint ends at the top riser — past it you are on the landing
            if (d < 0f || d > f.RunLength) return false;
            y = f.Base.y + TreadTopY(d, f.Risers, f.RiserHeight, f.TreadDepth);
            return true;
        }

        /// <summary>
        /// Where the visible ground plane is drawn: just under the datum — unless the datum
        /// is over the walker's head (the model's only slab is a mezzanine), in which case
        /// it stays under their feet instead of roofing them in. Falling never triggers
        /// that branch: you always fall TOWARDS the ground, never past it.
        /// </summary>
        public static float PlaneLevel(float groundY, float footY, float drop, float stepUp = StepUp)
            => (groundY > footY + stepUp ? footY : groundY) - drop;

        /// <summary>
        /// One gravity step for a pair of feet. Steps up instantly when the surface is at
        /// most <paramref name="stepUp"/> above; otherwise accelerates downwards and lands
        /// exactly on the support. <paramref name="velocity"/> carries the fall speed
        /// between frames (positive = falling) and is zeroed on contact.
        /// </summary>
        public static float Fall(float footY, float supportY, ref float velocity, float dt,
            float stepUp = StepUp)
        {
            if (dt <= 0f) return footY;
            if (supportY >= footY - Skin)
            {
                // on the surface, or a ledge low enough to step onto
                velocity = 0f;
                return supportY <= footY + stepUp ? supportY : footY;
            }
            velocity = Mathf.Min(velocity + FallAccel * dt, FallSpeedMax);
            float next = footY - velocity * dt;
            if (next <= supportY) { velocity = 0f; return supportY; }
            return next;
        }
    }
}
