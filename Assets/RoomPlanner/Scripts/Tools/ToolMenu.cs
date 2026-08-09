using UnityEngine;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// Left-hand tool palette: object/tool TYPE selection + global snap toggles only.
    /// Follows the left controller and faces the camera. Per-tool settings live in the
    /// floating InspectorPanel, not here.
    /// </summary>
    public class ToolMenu : MonoBehaviour
    {
        [SerializeField] private Transform follow;                 // left controller
        [SerializeField] private Vector3 followOffset = new Vector3(0f, 0.10f, 0f);
        [SerializeField] private MenuButton measureBtn;
        [SerializeField] private MenuButton wallBtn;
        [SerializeField] private MenuButton floorBtn;
        [SerializeField] private MenuButton snapCornerBtn;
        [SerializeField] private MenuButton snapEdgeBtn;
        [SerializeField] private MenuButton snapGridBtn;
        [SerializeField] private MenuButton snapAngleBtn;
        [SerializeField] private MenuButton scanBtn;

        private Transform _cam;
        private MenuButton _hi;

        public void Highlight(MenuButton b)
        {
            if (_hi == b) return;
            if (_hi != null) _hi.SetHighlight(false);
            _hi = b;
            if (_hi != null) _hi.SetHighlight(true);
        }

        public void Refresh(int activeTool, bool snapCorner, bool snapEdge, bool snapGrid, bool snapAngle, bool scanOn)
        {
            if (measureBtn != null) measureBtn.SetActiveTool(activeTool == 0);
            if (wallBtn != null) wallBtn.SetActiveTool(activeTool == 1);
            if (floorBtn != null) floorBtn.SetActiveTool(activeTool == 2);
            if (snapCornerBtn != null) snapCornerBtn.SetActiveTool(snapCorner);
            if (snapEdgeBtn != null) snapEdgeBtn.SetActiveTool(snapEdge);
            if (snapGridBtn != null) snapGridBtn.SetActiveTool(snapGrid);
            if (snapAngleBtn != null) snapAngleBtn.SetActiveTool(snapAngle);
            if (scanBtn != null) scanBtn.SetActiveTool(scanOn);
        }

        private void LateUpdate()
        {
            if (follow != null) transform.position = follow.position + followOffset;
            if (_cam == null)
            {
                if (Camera.main == null) return;
                _cam = Camera.main.transform;
            }
            Vector3 dir = transform.position - _cam.position;
            if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir);
        }
    }
}
