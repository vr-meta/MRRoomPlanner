using UnityEngine;

namespace RoomPlanner.Import
{
    /// <summary>
    /// An imported baked-mesh element (design/18 I12/I17 — plumbing, furniture, proxies,
    /// railings): the mesh is baked in LOCAL space around the transform, so moving the
    /// whole model (teleport) is a plain transform shift — no mesh rebuild.
    /// </summary>
    public class MepView : MonoBehaviour
    {
        /// <summary>What the element is (drives material) — kept for project capture.</summary>
        public RoomPlanner.Core.Ifc.MepCategory Category;
        /// <summary>IFC surface transparency (0 opaque … 1 clear) — kept for capture.</summary>
        public float Transparency;
        /// <summary>Storey the element belongs to (−1 = unknown). ProjectMep.Storey existed
        /// but Capture never filled it — the storey filter died after save/load (audit B6).</summary>
        public int StoreyIndex = -1;

        public void MoveBy(Vector3 delta) => transform.position += delta;

        private void OnDestroy()
        {
            // The runtime-baked mesh is not freed by Destroy(gameObject).
            var mf = GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            if (Application.isPlaying) Destroy(mf.sharedMesh);
            else DestroyImmediate(mf.sharedMesh);
        }
    }
}
