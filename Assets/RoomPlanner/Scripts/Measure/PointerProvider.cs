using UnityEngine;

namespace RoomPlanner.Measure
{
    /// <summary>
    /// Источник луча-указателя. Приоритет:
    /// назначенный анкер → OVRCameraRig.rightControllerAnchor → поиск по имени →
    /// луч от головы (Camera.main). В редакторе — мышь.
    /// </summary>
    public class PointerProvider : MonoBehaviour
    {
        [SerializeField] private Transform controllerAnchor;
        [SerializeField] private bool useEditorMouseFallback = true;

        private OVRCameraRig _rig;
        private bool _logged;

        private Transform ResolveAnchor()
        {
            if (controllerAnchor != null) return controllerAnchor;

            if (_rig == null) _rig = Object.FindFirstObjectByType<OVRCameraRig>();
            if (_rig != null && _rig.rightControllerAnchor != null)
            {
                controllerAnchor = _rig.rightControllerAnchor;
                return controllerAnchor;
            }

            foreach (var n in new[] { "RightControllerAnchor", "RightHandAnchor" })
            {
                var go = GameObject.Find(n);
                if (go != null) { controllerAnchor = go.transform; return controllerAnchor; }
            }
            return null;
        }

        public Ray GetRay()
        {
            var a = ResolveAnchor();
            if (!_logged)
            {
                Debug.Log($"[Measure] pointer source = {(a != null ? a.name : "HEAD (Camera.main) fallback")}");
                _logged = true;
            }
            if (a != null) return new Ray(a.position, a.forward);

#if UNITY_EDITOR
            if (useEditorMouseFallback && Camera.main != null)
                return Camera.main.ScreenPointToRay(Input.mousePosition);
#endif
            var cam = Camera.main;
            return cam != null
                ? new Ray(cam.transform.position, cam.transform.forward)
                : new Ray(Vector3.zero, Vector3.forward);
        }
    }
}
