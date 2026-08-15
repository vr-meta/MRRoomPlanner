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
        public TeleportLocomotion Locomotion;
        public GroundService Ground;

        // tools (order = registry/palette order)
        public SelectController Select;
        public MeasureController Measure;
        public WallController WallTool;
        public FloorController FloorTool;
        public BlueprintController Blueprint;
        public RoomPlanner.Import.ImportController Import;
        public RoomPlanner.Electrical.ElectricController Electric;
        public PaintController Paint;

        // shared assets
        public Material LineMat, MarkerMat, ReticleMat, ContMat, BadgeMat;
        public Material WallMat, WallEdgeMat, FloorMat;
        public Material PanelMat, RimMat, BtnMat, ActiveMat;
        public Material IconMat, InsetMat, ScrimMat;   // UI v2 (design/20): icons, wells, radial scrim
        public RoomPlanner.Tools.RadialMenu Radial;
        public FinishLibrary Finishes;                 // texture catalog (design/04 «Текстуры v1»)
        public Material GroundMat, GlassMat, StairMat, JoineryMat, PlumbingMat, SkyMat;
        public Material FurnitureMat, ProxyMat, RailingMat, ScreenMat;
        public Material WireMat, FixtureMat;
        public Material PipeMat;
        public UnityEngine.Object Ssao;   // ScreenSpaceAmbientOcclusion renderer feature
        public Light Sun;                 // the shadow-casting directional (Rendering page toggle)
    }
}
#endif
