using UnityEngine;

namespace RoomPlanner.Import
{
    /// <summary>
    /// An imported MEP fixture (design/18 I12 — plumbing terminals for now): the mesh is
    /// baked in LOCAL space around the transform, so moving the whole model (teleport)
    /// is a plain transform shift — no mesh rebuild.
    /// </summary>
    public class MepView : MonoBehaviour
    {
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
