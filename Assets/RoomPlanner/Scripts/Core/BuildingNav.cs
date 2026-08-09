using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>
    /// Teleport math (docs/design/18-ifc-import.md, I6). In MR the camera never moves —
    /// passthrough is the real room — so "walking" a virtual building means shifting the
    /// MODEL so the aimed spot arrives at the user.
    /// </summary>
    public static class BuildingNav
    {
        /// <summary>
        /// Model delta that brings <paramref name="aimedPoint"/> (a spot on some floor slab)
        /// under the user's feet: XZ to the head position, the slab's surface to the real
        /// floor. Aiming at an upper-storey slab therefore also brings that storey down to
        /// standing height — that IS switching floors.
        /// </summary>
        public static Vector3 TeleportDelta(Vector3 aimedPoint, Vector3 headPosition, float floorY = 0f)
            => new Vector3(headPosition.x - aimedPoint.x, floorY - aimedPoint.y, headPosition.z - aimedPoint.z);
    }
}
