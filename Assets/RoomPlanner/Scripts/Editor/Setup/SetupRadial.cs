#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RoomPlanner.Tools;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// The tool radial (design/20 §1): just the component shell + materials — all geometry
    /// (sectors, hub, scrim, icons) is generated at runtime on first open, and the slot
    /// table lives in ToolManager. No colliders: ray hits are computed analytically.
    /// </summary>
    internal static class SetupRadial
    {
        public static RadialMenu Build(RigContext ctx)
        {
            var root = new GameObject("RadialMenu");
            root.AddComponent<RigMarker>();
            var radial = root.AddComponent<RadialMenu>();

            var so = new SerializedObject(radial);
            so.FindProperty("sectorMaterial").objectReferenceValue = ctx.BtnMat;
            so.FindProperty("scrimMaterial").objectReferenceValue = ctx.ScrimMat;
            so.FindProperty("iconMaterial").objectReferenceValue = ctx.IconMat;
            so.ApplyModifiedProperties();

            root.SetActive(false);   // opened by ToolManager on L3
            ctx.Radial = radial;
            return radial;
        }
    }
}
#endif
