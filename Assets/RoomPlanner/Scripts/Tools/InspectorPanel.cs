using TMPro;
using UnityEngine;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// Floating, world-anchored settings panel. Shows the CURRENT tool's parameters (a pre-built
    /// group per tool); hidden for tools without settings (Measure). Grabbable by the title bar
    /// (grip) to reposition — the panel stays where you drop it and faces you (yaw billboard).
    /// Buttons inside are ordinary MenuButtons (routed via ToolManager.Execute), so this only
    /// manages visibility, value labels, placement and grabbing.
    /// </summary>
    public class InspectorPanel : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;      // toggled visible/hidden
        [SerializeField] private GameObject wallGroup;
        [SerializeField] private GameObject floorGroup;

        [Header("Wall labels")]
        [SerializeField] private TMP_Text thickLabel;
        [SerializeField] private TMP_Text heightLabel;
        [SerializeField] private TMP_Text angleLabel;
        [SerializeField] private TMP_Text offsetLabel;
        [SerializeField] private TMP_Text joinLabel;
        [SerializeField] private TMP_Text placeLabel;

        [Header("Floor labels")]
        [SerializeField] private TMP_Text levelLabel;
        [SerializeField] private TMP_Text floorThickLabel;
        [SerializeField] private TMP_Text planLabel;

        private Transform _cam;

        public bool IsShown => panelRoot != null && panelRoot.activeSelf;
        public Transform Panel => panelRoot != null ? panelRoot.transform : transform;

        /// <summary>activeTool: 0 Measure (hide), 1 Walls, 2 Floor.</summary>
        public void ShowFor(int activeTool)
        {
            bool show = activeTool != 0;
            if (panelRoot == null) return;
            if (show && !panelRoot.activeSelf) PlaceInFront();
            panelRoot.SetActive(show);
            if (wallGroup != null) wallGroup.SetActive(activeTool == 1);
            if (floorGroup != null) floorGroup.SetActive(activeTool == 2);
        }

        public void RefreshValues(float thickness, float height, float angle, float level, float plan,
            string offsetName, string joinName, string placeName)
        {
            string thk = $"{thickness * 100f:0} cm";
            if (thickLabel != null) thickLabel.text = thk;
            if (floorThickLabel != null) floorThickLabel.text = thk;
            if (heightLabel != null) heightLabel.text = $"{height * 100f:0} cm";
            if (angleLabel != null) angleLabel.text = $"{angle:0}°";
            if (levelLabel != null) levelLabel.text = $"{level * 100f:0} cm";
            if (planLabel != null) planLabel.text = $"{plan:0.0} m";
            if (offsetLabel != null) offsetLabel.text = offsetName;
            if (joinLabel != null) joinLabel.text = joinName;
            if (placeLabel != null) placeLabel.text = placeName;
        }

        /// <summary>Move the panel so the grabbed point tracks the pointer (called while dragging).</summary>
        public void MovePanel(Vector3 worldPos)
        {
            if (panelRoot != null) panelRoot.transform.position = worldPos;
        }

        private void PlaceInFront()
        {
            EnsureCam();
            if (_cam == null) return;
            panelRoot.transform.position =
                _cam.position + _cam.forward * 0.55f + _cam.right * 0.12f - _cam.up * 0.12f;
        }

        private void LateUpdate()
        {
            if (panelRoot == null || !panelRoot.activeSelf) return;
            EnsureCam();
            if (_cam == null) return;
            Vector3 dir = panelRoot.transform.position - _cam.position;
            if (dir.sqrMagnitude > 0.0001f)
                panelRoot.transform.rotation = Quaternion.LookRotation(dir);
        }

        private void EnsureCam()
        {
            if (_cam == null && Camera.main != null) _cam = Camera.main.transform;
        }
    }
}
