using NUnit.Framework;
using RoomPlanner.Core;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>The finish model (design/04 «Текстуры v1») — pure data.</summary>
    public class SurfaceFinishTests
    {
        [Test]
        public void None_IsNone()
        {
            Assert.IsTrue(SurfaceFinish.None.IsNone);
            Assert.AreEqual(FinishKind.None, default(SurfaceFinish).Kind);
        }

        [Test]
        public void OfColor_KeepsTheColor()
        {
            var f = SurfaceFinish.OfColor(Color.red);
            Assert.AreEqual(FinishKind.Color, f.Kind);
            Assert.AreEqual(Color.red, f.Color);
            Assert.IsFalse(f.IsNone);
        }

        [Test]
        public void OfTexture_DefaultsToWhiteTint()
        {
            var f = SurfaceFinish.OfTexture("wallpaper-001a", 1.0f);
            Assert.AreEqual(FinishKind.Texture, f.Kind);
            Assert.AreEqual("wallpaper-001a", f.TextureId);
            Assert.AreEqual(Color.white, f.Color, "texture without a tint must not darken");
        }

        [Test]
        public void UvScale_IsInverseTileMeters()
        {
            var f = SurfaceFinish.OfTexture("parquet-051", 2.0f);
            var st = f.UvScaleOffset();
            Assert.AreEqual(0.5f, st.x, 1e-5f);
            Assert.AreEqual(0.5f, st.y, 1e-5f);
            Assert.AreEqual(0f, st.z);
            Assert.AreEqual(0f, st.w);
        }

        [Test]
        public void TileMeters_ClampedAboveZero()
        {
            var f = SurfaceFinish.OfTexture("x", 0f);
            Assert.Greater(f.TileMeters, 0f, "zero tile would divide by zero in the ST");
        }

        [Test]
        public void UvScale_PerAxis_WhenTileYSet()
        {
            var f = SurfaceFinish.OfTexture("wallpaper-001a", 2.0f, 0.5f);
            var st = f.UvScaleOffset();
            Assert.AreEqual(0.5f, st.x, 1e-5f, "U scale from TileMeters");
            Assert.AreEqual(2f, st.y, 1e-5f, "V scale from TileMetersY");
        }

        [Test]
        public void UvScale_TileYZero_FallsBackToSquare()
        {
            var f = SurfaceFinish.OfTexture("wood", 2.0f);
            Assert.AreEqual(0f, f.TileMetersY, "unset stays 0 (square marker)");
            Assert.AreEqual(2f, f.EffectiveTileY, 1e-5f);
            Assert.AreEqual(f.UvScaleOffset().x, f.UvScaleOffset().y, 1e-5f);
        }
    }
}
