#if UNITY_EDITOR
using UnityEngine;
using RoomPlanner.Measure;
using RoomPlanner.Tools;
using RoomPlanner.Walls;
using RoomPlanner.Floors;
using RoomPlanner.Editing;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Shared state passed between the setup modules (design/14-modularity.md §4):
    /// core rig components, controllers and the material set. Each Setup* module fills
    /// and reads only its part.
    /// </summary>
    internal sealed class RigContext
    {
        // core
        public GameObject Rig;
        public SceneRaycaster Raycaster;
        public PointerProvider Pointer;
        public MeasureInput Input;
        public SceneModel SceneModel;
        public ToolManager Manager;
        public GameObject Reticle;

        // tools (order = registry/palette order)
        public SelectController Select;
        public MeasureController Measure;
        public WallController WallTool;
        public FloorController FloorTool;
        public BlueprintController Blueprint;
        public RoomPlanner.Import.ImportController Import;

        // shared assets
        public Material LineMat, MarkerMat, ReticleMat, ContMat, BadgeMat;
        public Material WallMat, WallEdgeMat, FloorMat;
        public Material PanelMat, RimMat, BtnMat, ActiveMat;
    }
}
#endif
