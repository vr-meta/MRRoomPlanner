#if UNITY_EDITOR
using UnityEditor;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// GUI menu entry for the Quest APK build. Delegates to <see cref="CiTools.BuildAndroid"/>
    /// so the menu and the headless CI path share ONE pipeline (output: Build/MRRoomPlanner.apk).
    /// </summary>
    public static class BuildTool
    {
        [MenuItem("RoomPlanner/Build APK (Quest)")]
        public static void BuildQuest() => CiTools.BuildAndroid();
    }
}
#endif
