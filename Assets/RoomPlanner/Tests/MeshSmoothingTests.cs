using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Core;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Angle-based normals for imported meshes (issue #132, headset 2026-08-16: «на
    /// квадратных брусках ощущение что там чуть ли не цилиндр»). A shared-vertex cube
    /// must come out with six flat faces; a tessellated cylinder must stay smooth; and
    /// the triangle list must survive untouched, because the per-material parts of
    /// design/29 are ranges over it.
    /// </summary>
    public class MeshSmoothingTests
    {
        /// <summary>Unit cube with WELDED corners — 8 vertices shared by three faces
        /// each, exactly what IfcImporter's extrusion path produces.</summary>
        private static void WeldedCube(List<Vector3> verts, List<int> tris)
        {
            verts.Clear(); tris.Clear();
            verts.AddRange(new[]
            {
                new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0),
                new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1),
            });
            void Quad(int a, int b, int c, int d)
            {
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(d);
            }
            Quad(0, 3, 2, 1);   // -Z
            Quad(4, 5, 6, 7);   // +Z
            Quad(0, 1, 5, 4);   // -Y
            Quad(3, 7, 6, 2);   // +Y
            Quad(1, 2, 6, 5);   // +X
            Quad(0, 4, 7, 3);   // -X
        }

        [Test]
        public void SharpCornersSplit_CubeGetsSixFaceNormals()
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            WeldedCube(verts, tris);
            var normals = new List<Vector3>();

            MeshSmoothing.Apply(verts, tris, normals);

            Assert.AreEqual(verts.Count, normals.Count);
            Assert.AreEqual(24, verts.Count, "each corner splits into its three faces");

            // every normal is axis-aligned — no averaged diagonals left
            foreach (var n in normals)
            {
                float best = Mathf.Max(Mathf.Abs(n.x), Mathf.Max(Mathf.Abs(n.y), Mathf.Abs(n.z)));
                Assert.AreEqual(1f, best, 1e-4, $"normal {n} is not a face normal");
            }

            // and each triangle's vertices agree with its own face normal
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                var face = Vector3.Cross(verts[tris[i + 1]] - verts[tris[i]],
                    verts[tris[i + 2]] - verts[tris[i]]).normalized;
                for (int k = 0; k < 3; k++)
                    Assert.AreEqual(1f, Vector3.Dot(face, normals[tris[i + k]]), 1e-3);
            }
        }

        [Test]
        public void SoftEdgesStaySmooth_TessellatedCylinderKeepsOneNormalPerRingVertex()
        {
            // a 32-segment cylinder side: neighbouring facets meet at ~11°, well under
            // the 40° threshold, so the ring vertices must NOT split
            const int seg = 32;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                verts.Add(new Vector3(Mathf.Cos(a), 1f, Mathf.Sin(a)));
            }
            for (int i = 0; i < seg; i++)
            {
                int lo = i * 2, hi = lo + 1;
                int loN = ((i + 1) % seg) * 2, hiN = loN + 1;
                tris.Add(lo); tris.Add(hi); tris.Add(hiN);
                tris.Add(lo); tris.Add(hiN); tris.Add(loN);
            }
            var normals = new List<Vector3>();

            MeshSmoothing.Apply(verts, tris, normals);

            Assert.AreEqual(seg * 2, verts.Count, "smooth ring vertices are not split");
            // the normal at a ring vertex points outward, roughly along its radius
            for (int i = 0; i < verts.Count; i++)
            {
                var radial = new Vector3(verts[i].x, 0f, verts[i].z).normalized;
                Assert.Greater(Vector3.Dot(radial, normals[i]), 0.95f);
            }
        }

        [Test]
        public void TriangleListSurvives_OrderCountAndGeometryUnchanged()
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            WeldedCube(verts, tris);
            var before = new List<Vector3>();
            for (int i = 0; i < tris.Count; i++) before.Add(verts[tris[i]]);
            int count = tris.Count;

            MeshSmoothing.Apply(verts, tris, new List<Vector3>());

            Assert.AreEqual(count, tris.Count, "part ranges address this list — it may not move");
            for (int i = 0; i < tris.Count; i++)
                Assert.AreEqual(before[i], verts[tris[i]], $"corner {i} moved");
        }

        [Test]
        public void DegenerateInputIsSurvivable()
        {
            var verts = new List<Vector3> { Vector3.zero, Vector3.zero, Vector3.zero };
            var tris = new List<int> { 0, 1, 2 };
            var normals = new List<Vector3>();

            MeshSmoothing.Apply(verts, tris, normals);

            Assert.AreEqual(verts.Count, normals.Count);
            foreach (var n in normals)
                Assert.IsFalse(float.IsNaN(n.x) || float.IsNaN(n.y) || float.IsNaN(n.z));
        }
    }
}
