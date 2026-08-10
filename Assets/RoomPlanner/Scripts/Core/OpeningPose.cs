using UnityEngine;

namespace RoomPlanner.Walls
{
    /// <summary>
    /// Pure kinematics of openable leaves (issue #50, design/03 §«Открывающиеся»):
    /// a swing door rotates around its hinge jamb, a sectional garage door rides a
    /// track — up along the jambs, then horizontally under the ceiling. All results
    /// are in the leaf's LOCAL frame (X along the wall, Y up, Z toward the fold side).
    /// </summary>
    public static class OpeningPose
    {
        /// <summary>Fully open swing angle; imported IFC doors historically stood at
        /// 75° = fraction 0.75 of this.</summary>
        public const float MaxDoorYawDeg = 100f;

        /// <summary>Sectional leaf panel count (matches the closed-leaf panelisation).</summary>
        public const int GaragePanels = 4;

        public static float DoorYawDeg(float fraction) =>
            Mathf.Clamp01(fraction) * MaxDoorYawDeg;

        /// <summary>
        /// Pose of one sectional panel's BOTTOM edge. The track runs up the jambs
        /// (y: 0..leafHeight at z = 0) and then folds horizontal (y = leafHeight,
        /// z growing inward). A panel straddling the bend tilts proportionally —
        /// an approximation of the real curved-rail kinematics that keeps panels
        /// rigid and the maths allocation-free.
        /// </summary>
        public static void GaragePanel(float leafHeight, int panels, int index, float fraction,
            out float y, out float z, out float tiltDeg)
        {
            float ph = panels > 0 ? leafHeight / panels : leafHeight;
            float s = index * ph + Mathf.Clamp01(fraction) * leafHeight;
            if (s >= leafHeight)
            {
                y = leafHeight;
                z = s - leafHeight;
                tiltDeg = 90f;
                return;
            }
            y = s;
            z = 0f;
            tiltDeg = Mathf.Clamp01((s + ph - leafHeight) / ph) * 90f;
        }
    }
}
