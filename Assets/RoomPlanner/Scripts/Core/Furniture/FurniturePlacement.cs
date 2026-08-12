using UnityEngine;

namespace RoomPlanner.Core.Furniture
{
    /// <summary>
    /// Resolved placement of one item: world position of the item's BOTTOM CENTRE and its
    /// yaw. Bottom-centre is the pivot everywhere in this module — it is what "stands on
    /// the floor" and "hangs on the wall" both reduce to, and it makes the placement math
    /// independent of the model's own origin.
    /// </summary>
    public struct FurniturePose
    {
        public Vector3 Position;
        public float Yaw;
        public bool Valid;

        public static readonly FurniturePose Invalid = new() { Valid = false };
    }

    /// <summary>Tool-side knobs of the placement solver (panel rows in design/27 §3).</summary>
    public struct PlacementOptions
    {
        /// <summary>Back-to-wall snap enabled (panel toggle).</summary>
        public bool SnapToWall;
        /// <summary>Gap at which the snap engages, metres.</summary>
        public float SnapDistance;
        /// <summary>Yaw quantisation step, degrees; 0 = free rotation.</summary>
        public float YawStep;
        /// <summary>
        /// Mounting height for wall-anchored items (bottom edge above the floor, metres).
        /// Negative = follow the aim point instead of a fixed height.
        /// </summary>
        public float WallMountHeight;

        public static PlacementOptions Default => new()
        {
            SnapToWall = true,
            SnapDistance = DefaultSnapDistance,
            YawStep = DefaultYawStep,
            WallMountHeight = -1f,
        };

        public const float DefaultSnapDistance = 0.35f;
        public const float DefaultYawStep = 15f;
    }

    /// <summary>
    /// Where a piece of furniture may sit (design/27 §2): the floor carries it, the wall
    /// constrains it. Pure math, no Unity scene access, so every rule is unit-testable —
    /// and struct-only so the per-frame aiming path allocates nothing (rules 12 §4.1).
    /// </summary>
    public static class FurniturePlacement
    {
        /// <summary>A surface counts as horizontal above this |dot(normal, up)|.</summary>
        public const float HorizontalDot = 0.5f;

        /// <summary>Default mounting heights for wall-hung items, metres (bottom edge).</summary>
        public static float DefaultMountHeight(FurnitureCategory category) => category switch
        {
            FurnitureCategory.Storage => 1.40f,   // wall cabinet
            FurnitureCategory.Kitchen => 1.40f,   // upper kitchen unit
            FurnitureCategory.Bath => 0.85f,      // washbasin
            _ => 1.10f,
        };

        /// <summary>
        /// Solve a pose from an aim hit. Returns an invalid pose when the surface does not
        /// match the anchor (a sofa aimed at a wall, a cabinet aimed at the floor) — the
        /// tool paints the ghost in Danger instead of placing something impossible.
        /// </summary>
        public static FurniturePose Solve(Vector3 hitPoint, Vector3 hitNormal, Vector3 size,
            FurnitureAnchor anchor, float yaw, in PlacementOptions options)
        {
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f) return FurniturePose.Invalid;

            Vector3 n = hitNormal.sqrMagnitude > 1e-8f ? hitNormal.normalized : Vector3.up;
            float up = Vector3.Dot(n, Vector3.up);
            float snappedYaw = QuantizeYaw(yaw, options.YawStep);

            switch (anchor)
            {
                case FurnitureAnchor.Floor:
                case FurnitureAnchor.Counter:
                    // Needs an up-facing surface; the bottom lands exactly on it.
                    if (up < HorizontalDot) return FurniturePose.Invalid;
                    return new FurniturePose { Position = hitPoint, Yaw = snappedYaw, Valid = true };

                case FurnitureAnchor.Ceiling:
                    if (up > -HorizontalDot) return FurniturePose.Invalid;
                    return new FurniturePose
                    {
                        Position = new Vector3(hitPoint.x, hitPoint.y - size.y, hitPoint.z),
                        Yaw = snappedYaw,
                        Valid = true,
                    };

                case FurnitureAnchor.Wall:
                {
                    // Needs a vertical face; the back plane lies ON the face, so the centre
                    // sits half a depth into the room along the wall normal.
                    if (Mathf.Abs(up) >= HorizontalDot) return FurniturePose.Invalid;
                    Vector3 flat = new Vector3(n.x, 0f, n.z);
                    if (flat.sqrMagnitude < 1e-8f) return FurniturePose.Invalid;
                    flat.Normalize();

                    Vector3 pos = hitPoint + flat * (size.z * 0.5f);
                    pos.y = options.WallMountHeight >= 0f ? options.WallMountHeight : hitPoint.y;
                    return new FurniturePose { Position = pos, Yaw = YawFromNormal(flat), Valid = true };
                }
            }

            return FurniturePose.Invalid;
        }

        /// <summary>
        /// Back-to-wall snap for floor items (design/27 §2): when the wall is within
        /// <see cref="PlacementOptions.SnapDistance"/> of the item's back, the item slides
        /// back until its rear face touches the face and turns its back to it. Also pushes
        /// out an item that already overlaps the wall — furniture inside a wall is never a
        /// valid answer.
        /// </summary>
        /// <param name="wallPoint">Any point on the wall FACE (the room side).</param>
        /// <param name="wallNormal">Face normal, pointing into the room.</param>
        public static bool TrySnapBackToWall(ref FurniturePose pose, Vector3 size,
            Vector3 wallPoint, Vector3 wallNormal, float snapDistance)
        {
            if (!pose.Valid) return false;
            Vector3 n = new Vector3(wallNormal.x, 0f, wallNormal.z);
            if (n.sqrMagnitude < 1e-8f) return false;
            n.Normalize();

            float half = size.z * 0.5f;
            // Distance from the face to the item's centre, measured along the room-side normal.
            float centre = Vector3.Dot(pose.Position - wallPoint, n);
            float gap = centre - half;                     // negative = the item is inside the wall
            if (gap > snapDistance) return false;          // too far away — leave the pose alone
            if (centre < -half) return false;              // behind the wall entirely: not our wall

            pose.Position += n * (half - centre);          // rear face flush with the face
            pose.Yaw = YawFromNormal(n);                   // back to the wall, front to the room
            return true;
        }

        /// <summary>Yaw (degrees) of an item whose FRONT looks along <paramref name="forward"/>.</summary>
        public static float YawFromNormal(Vector3 forward)
        {
            Vector3 flat = new Vector3(forward.x, 0f, forward.z);
            if (flat.sqrMagnitude < 1e-8f) return 0f;
            return Normalize360(Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg);
        }

        /// <summary>Quantise a yaw to the step (0 = free); result is always in [0, 360).</summary>
        public static float QuantizeYaw(float yaw, float step)
        {
            if (step <= 0f) return Normalize360(yaw);
            return Normalize360(Mathf.Round(yaw / step) * step);
        }

        public static float Normalize360(float deg)
        {
            float v = deg % 360f;
            if (v < 0f) v += 360f;
            return v < 360f ? v : 0f;
        }

        /// <summary>
        /// Uniform scale that makes a catalog model measure its declared real-world size.
        /// Driven by the target's LONGEST axis so proportions survive: bundled CC0 models
        /// are stylised, and stretching them per-axis would look worse than being a few
        /// centimetres off on the short sides (design/27 §1).
        /// </summary>
        public static float FitScale(Vector3 modelSize, Vector3 targetSize)
        {
            int axis = targetSize.x >= targetSize.y && targetSize.x >= targetSize.z ? 0
                     : targetSize.y >= targetSize.z ? 1 : 2;
            float model = axis == 0 ? modelSize.x : axis == 1 ? modelSize.y : modelSize.z;
            float target = axis == 0 ? targetSize.x : axis == 1 ? targetSize.y : targetSize.z;
            if (model <= 1e-6f || target <= 0f) return 1f;
            return target / model;
        }

        /// <summary>
        /// Per-axis scale that makes a model measure its declared size. <see cref="FurnitureFit.Uniform"/>
        /// keeps proportions (one <see cref="FitScale"/> on all axes); <see cref="FurnitureFit.Stretch"/>
        /// matches every axis exactly — right for boxy carcasses, where a 0.85 m worktop
        /// height matters more than the model's original proportions.
        /// </summary>
        public static Vector3 FitScaleAxes(Vector3 modelSize, Vector3 targetSize, FurnitureFit fit)
        {
            if (fit == FurnitureFit.Uniform)
            {
                float s = FitScale(modelSize, targetSize);
                return new Vector3(s, s, s);
            }
            return new Vector3(
                AxisScale(modelSize.x, targetSize.x),
                AxisScale(modelSize.y, targetSize.y),
                AxisScale(modelSize.z, targetSize.z));
        }

        private static float AxisScale(float model, float target) =>
            model <= 1e-6f || target <= 0f ? 1f : target / model;

        /// <summary>Footprint rectangle of a posed item — the ghost box and the fit check
        /// both need it. Returns the four corners in world XZ, y = the item's base.</summary>
        public static void Footprint(in FurniturePose pose, Vector3 size, Vector3[] result)
        {
            if (result == null || result.Length < 4) return;
            float yawRad = pose.Yaw * Mathf.Deg2Rad;
            float c = Mathf.Cos(yawRad), s = Mathf.Sin(yawRad);
            float hx = size.x * 0.5f, hz = size.z * 0.5f;
            // local (±hx, ±hz) rotated by yaw around Y
            for (int i = 0; i < 4; i++)
            {
                float lx = (i == 0 || i == 3) ? -hx : hx;
                float lz = i < 2 ? -hz : hz;
                result[i] = new Vector3(
                    pose.Position.x + lx * c + lz * s,
                    pose.Position.y,
                    pose.Position.z - lx * s + lz * c);
            }
        }
    }
}
