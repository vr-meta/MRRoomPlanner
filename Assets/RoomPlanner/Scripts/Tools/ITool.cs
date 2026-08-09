namespace RoomPlanner.Tools
{
    /// <summary>
    /// Инструмент, которым управляет ToolManager. Активный инструмент получает Tick()
    /// каждый кадр; blocked=true — указатель над меню, ввод инструментом игнорируется.
    /// </summary>
    public interface ITool
    {
        void Tick(bool blocked);
        void OnActivate();
        void OnDeactivate();
    }
}
