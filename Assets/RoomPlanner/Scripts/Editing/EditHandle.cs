using UnityEngine;

namespace RoomPlanner.Editing
{
    /// <summary>
    /// A draggable point of the selected object (wall node now, floor corner later).
    /// Spawned and pooled by <see cref="SelectController"/>; carries only which provider and
    /// which index it stands for.
    ///
    /// Sized by ANGLE, not in metres: a fixed-size handle is a fat blob up close and an
    /// unhittable dot across the room (docs/design/11-object-operations.md, §7).
    /// </summary>
    public class EditHandle : MonoBehaviour
    {
        /// <summary>Apparent size in degrees — about 2°, roughly 5 cm at 1.5 m.</summary>
        public const float AngularSizeDeg = 2.0f;

        private const float MinScale = 0.02f, MaxScale = 0.25f;

        public IHandleProvider Provider { get; private set; }
        public int Index { get; private set; }

        public void Bind(IHandleProvider provider, int index)
        {
            Provider = provider;
            Index = index;
        }

        /// <summary>Place at the provider's point and keep a constant apparent size.</summary>
        public void Follow(Transform cam)
        {
            if (Provider == null) return;
            Vector3 p = Provider.GetHandlePosition(Index);
            transform.position = p;
            if (cam == null) return;

            float dist = Vector3.Distance(cam.position, p);
            float size = Mathf.Clamp(2f * dist * Mathf.Tan(AngularSizeDeg * Mathf.Deg2Rad * 0.5f), MinScale, MaxScale);
            transform.localScale = Vector3.one * size;
        }
    }
}
