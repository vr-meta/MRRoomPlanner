using UnityEngine;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// Marks root objects created by RoomPlanner → Setup Measure Rig, so a re-run can find and
    /// replace exactly its own objects instead of destroying anything that happens to share a
    /// generic name like "Inspector".
    /// </summary>
    public class RigMarker : MonoBehaviour { }
}
