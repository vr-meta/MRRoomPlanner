using UnityEngine;

namespace RoomPlanner.Tools
{
    /// <summary>Grip-grabbable marker: ToolManager drags <see cref="MoveTarget"/> (or, when
    /// unset, the inspector panel — legacy) while the grip is held over this collider.
    /// Lives on the inspector's title bar and on the floating snap panel.</summary>
    public class InspectorGrab : MonoBehaviour
    {
        [SerializeField] private Transform moveTarget;

        public Transform MoveTarget
        {
            get => moveTarget;
            set => moveTarget = value;
        }
    }
}
