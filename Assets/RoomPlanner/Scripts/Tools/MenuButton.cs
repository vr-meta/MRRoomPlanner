using UnityEngine;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// GLOBAL palette actions only. Per-tool parameter buttons are runtime-bound via
    /// <see cref="MenuButton.OnClick"/> and never appear here (design/14-modularity.md).
    /// </summary>
    public enum MenuAction
    {
        None,
        SelectTool,        // activates the tool at MenuButton.ToolIndex
        ToggleSnapCorner,
        ToggleSnapEdge,
        ToggleSnapGrid,
        ToggleSnapAngle,
        ToggleScan
    }

    /// <summary>
    /// Кнопка меню (на слое IgnoreRaycast). Действие — либо рантайм-делегат
    /// <see cref="OnClick"/> (ряды инспектора из схемы), либо глобальный
    /// <see cref="MenuAction"/>; при наведении подсвечивается, у кнопок-инструментов
    /// есть отметка «активен».
    /// </summary>
    public class MenuButton : MonoBehaviour
    {
        [SerializeField] private MenuAction action;
        [SerializeField] private int toolIndex = -1;   // for MenuAction.SelectTool
        [SerializeField] private GameObject activeMark;

        public MenuAction Action => action;
        public int ToolIndex => toolIndex;

        /// <summary>Runtime-bound handler (schema rows); takes precedence over Action.</summary>
        public System.Action OnClick;

        /// <summary>Holding the trigger auto-repeats OnClick (stepper −/+ rows; UX v2 P0.2).</summary>
        public bool Repeatable;

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
