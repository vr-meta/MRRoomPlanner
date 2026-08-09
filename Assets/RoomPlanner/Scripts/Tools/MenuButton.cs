using UnityEngine;

namespace RoomPlanner.Tools
{
    public enum MenuAction
    {
        SelectMeasure,
        SelectWall,
        ThicknessDown,
        ThicknessUp,
        HeightDown,
        HeightUp,
        CycleOffset,
        CyclePlace,
        ToggleSnapCorner,
        ToggleSnapEdge,
        ToggleSnapGrid,
        ToggleSnapAngle,
        AngleDown,
        AngleUp,
        CycleJoin,
        LevelDown,
        LevelUp,
        ToggleScan,
        SelectFloor,
        PlanDown,
        PlanUp
    }

    /// <summary>
    /// Кнопка меню (на слое IgnoreRaycast). Хранит действие; при наведении подсвечивается,
    /// у кнопок-инструментов есть отметка «активен».
    /// </summary>
    public class MenuButton : MonoBehaviour
    {
        [SerializeField] private MenuAction action;
        [SerializeField] private GameObject activeMark;

        public MenuAction Action => action;

        private Vector3 _baseScale;
        private bool _hovered;

        private void Awake() => _baseScale = transform.localScale;

        public void SetHighlight(bool on)
        {
            if (_hovered == on) return;
            _hovered = on;
            transform.localScale = on ? _baseScale * 1.12f : _baseScale;
        }

        public void SetActiveTool(bool on)
        {
            if (activeMark != null) activeMark.SetActive(on);
        }
    }
}
