using NUnit.Framework;
using RoomPlanner.Core;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>Procedural UI meshes: winding toward the viewer, sane areas (design/20 §6).</summary>
    public class UiMeshesTests
    {
        private static void AssertFacesViewer(SvgMesh mesh, string what)
        {
            Assert.IsFalse(mesh.IsEmpty, $"{what} is empty");
            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                Vector3 a = mesh.Vertices[mesh.Triangles[t]];
                Vector3 b = mesh.Vertices[mesh.Triangles[t + 1]];
                Vector3 c = mesh.Vertices[mesh.Triangles[t + 2]];
                Assert.Less(Vector3.Cross(b - a, c - a).z, 1e-9f,
                    $"{what}: triangle {t / 3} faces away from the viewer");
            }
        }

        private static float MeshArea(SvgMesh mesh)
        {
            float sum = 0f;
            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                Vector3 a = mesh.Vertices[mesh.Triangles[t]];
                Vector3 b = mesh.Vertices[mesh.Triangles[t + 1]];
                Vector3 c = mesh.Vertices[mesh.Triangles[t + 2]];
                sum += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;
            }
            return sum;
        }

        [Test]
        public void Sector_FacesViewer_WithAnnulusArea()
        {
            var m = UiMeshes.Sector(0.05f, 0.11f, -13.75f, 13.75f, 16);
            AssertFacesViewer(m, "sector");
            float expected = Mathf.PI * (0.11f * 0.11f - 0.05f * 0.05f) * (27.5f / 360f);
            Assert.AreEqual(expected, MeshArea(m), expected * 0.01f);
        }

        [Test]
        public void Disc_FacesViewer_WithCircleArea()
        {
            var m = UiMeshes.Disc(0.04f, 48);
            AssertFacesViewer(m, "disc");
            Assert.AreEqual(Mathf.PI * 0.04f * 0.04f, MeshArea(m), 0.0001f);
        }

        [Test]
        public void RoundedRect_FacesViewer_AreaBetweenInnerAndOuterRect()
        {
            float w = 0.045f, h = 0.030f, r = 0.006f;
            var m = UiMeshes.RoundedRect(w, h, r);
            AssertFacesViewer(m, "rounded rect");
            float full = w * h;
            float exact = full - (4f - Mathf.PI) * r * r;
            Assert.Less(MeshArea(m), full);
            Assert.AreEqual(exact, MeshArea(m), full * 0.01f);
        }

        [Test]
        public void RoundedRect_ClampsRadiusToHalfSize()
        {
            // radius bigger than half height must not fold the outline
            var m = UiMeshes.RoundedRect(0.05f, 0.02f, 0.5f);
            AssertFacesViewer(m, "clamped rounded rect");
        }

        [Test]
        public void ScrimAlpha_FadesFromCenterToRim()
        {
            Assert.AreEqual(1f, UiMeshes.ScrimAlpha(0f), 1e-4f);
            Assert.AreEqual(0f, UiMeshes.ScrimAlpha(1f), 1e-4f);
            Assert.AreEqual(0f, UiMeshes.ScrimAlpha(1.5f), 1e-4f, "clamped past the rim");
            Assert.Greater(UiMeshes.ScrimAlpha(0.3f), UiMeshes.ScrimAlpha(0.7f));
        }
    }
}
