using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Core;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// The laminate layout math (design/22): every pattern must cover the seamless
    /// tile exactly once (no gaps, no overlaps, wrap included) and be deterministic
    /// for a seed — a re-bake must reproduce the same texture bit-for-bit.
    /// </summary>
    public class LaminateLayoutTests
    {
        private const float L = 1.2f, W = 0.2f;
        private const int Sources = 18, Seed = 42;

        private static readonly LaminatePattern[] AllPatterns =
        {
            LaminatePattern.Deck, LaminatePattern.Herringbone, LaminatePattern.Basket,
        };

        // ---- coverage: the core seamlessness invariant ----

        [Test]
        public void EveryPattern_CoversTileExactlyOnce()
        {
            // 48 divides all pattern cell grids (3, 6 and 12), so offset samples
            // never land on a plank boundary and "exactly once" is unambiguous.
            const int S = 48;
            foreach (var pattern in AllPatterns)
            {
                var quads = LaminateLayout.Generate(pattern, L, W, Sources, Seed);
                for (int a = 0; a < S; a++)
                    for (int b = 0; b < S; b++)
                    {
                        var p = new Vector2((a + 0.5f) / S, (b + 0.5f) / S);
                        int hits = CountCovering(quads, p);
                        Assert.AreEqual(1, hits,
                            $"{pattern}: point {p} covered {hits} times");
                    }
            }
        }

        private static int CountCovering(List<PlankQuad> quads, Vector2 p)
        {
            int hits = 0;
            foreach (var q in quads)
            {
                float dx = WrapDelta(p.x, q.Center.x);
                float dy = WrapDelta(p.y, q.Center.y);
                if (dx < q.Size.x * 0.5f && dy < q.Size.y * 0.5f) hits++;
            }
            return hits;
        }

        private static float WrapDelta(float a, float b)
        {
            float d = Mathf.Abs(a - b);
            return Mathf.Min(d, 1f - d);
        }

        // ---- physical consistency ----

        [Test]
        public void TileMeters_DeckAndBasketOnePlank_HerringboneTwo()
        {
            Assert.AreEqual(L, LaminateLayout.TileMeters(LaminatePattern.Deck, L, W), 1e-5f);
            Assert.AreEqual(L, LaminateLayout.TileMeters(LaminatePattern.Basket, L, W), 1e-5f);
            Assert.AreEqual(2f * L, LaminateLayout.TileMeters(LaminatePattern.Herringbone, L, W), 1e-5f);
        }

        [Test]
        public void CropSpan_MatchesFootprintLength()
        {
            // the plank stretch used (CropU1-CropU0 of L metres) must equal the
            // footprint's long side in metres — otherwise the wood gets stretched
            foreach (var pattern in AllPatterns)
            {
                float tile = LaminateLayout.TileMeters(pattern, L, W);
                foreach (var q in LaminateLayout.Generate(pattern, L, W, Sources, Seed))
                {
                    float longSide = Mathf.Max(q.Size.x, q.Size.y) * tile;
                    Assert.AreEqual((q.CropU1 - q.CropU0) * L, longSide, 1e-4f,
                        $"{pattern}: crop [{q.CropU0}..{q.CropU1}] vs footprint {longSide} m");
                    Assert.That(q.CropU0, Is.InRange(0f, 1f));
                    Assert.That(q.CropU1, Is.InRange(0f, 1f));
                    Assert.Less(q.CropU0, q.CropU1);
                }
            }
        }

        [Test]
        public void ShortSide_IsAlwaysPlankWidth()
        {
            foreach (var pattern in AllPatterns)
            {
                float tile = LaminateLayout.TileMeters(pattern, L, W);
                foreach (var q in LaminateLayout.Generate(pattern, L, W, Sources, Seed))
                    Assert.AreEqual(W, Mathf.Min(q.Size.x, q.Size.y) * tile, 1e-4f);
            }
        }

        // ---- pattern shape ----

        [Test]
        public void Deck_SixRows_OffsetsCycleByThirds()
        {
            var quads = LaminateLayout.Generate(LaminatePattern.Deck, L, W, Sources, Seed);
            Assert.AreEqual(6, quads.Count);
            for (int r = 0; r < 6; r++)
            {
                Assert.AreEqual((r + 0.5f) / 6f, quads[r].Center.y, 1e-5f, "row placement");
                Assert.AreEqual(0f, quads[r].RotationDeg);
                // seam of row r sits at (r mod 3)/3 — cycling, never stacked
                float seam = Frac(quads[r].Center.x - 0.5f);
                Assert.AreEqual((r % 3) / 3f, seam, 1e-5f, $"row {r} seam");
            }
        }

        [Test]
        public void Herringbone_TwelveByTwelve_HalfHorizontalHalfVertical()
        {
            var quads = LaminateLayout.Generate(LaminatePattern.Herringbone, L, W, Sources, Seed);
            Assert.AreEqual(24, quads.Count);
            int h = 0, v = 0;
            foreach (var q in quads)
            {
                if (q.RotationDeg == 0f) h++;
                else if (q.RotationDeg == 90f) v++;
                else Assert.Fail($"unexpected rotation {q.RotationDeg}");
            }
            Assert.AreEqual(12, h);
            Assert.AreEqual(12, v);
        }

        [Test]
        public void Basket_TwelveHalfPlanks_DirectionsCheckerboard()
        {
            var quads = LaminateLayout.Generate(LaminatePattern.Basket, L, W, Sources, Seed);
            Assert.AreEqual(12, quads.Count);
            foreach (var q in quads)
            {
                // which 0.6 m square the plank sits in decides its direction
                int i = (int)(q.Center.x * 2f), j = (int)(q.Center.y * 2f);
                bool horizontal = (i + j) % 2 == 0;
                Assert.AreEqual(horizontal ? 0f : 90f, q.RotationDeg,
                    $"square ({i},{j}) direction");
                // half-plank: crop is exactly one half of the board
                Assert.AreEqual(0.5f, q.CropU1 - q.CropU0, 1e-5f);
            }
        }

        // ---- determinism ----

        [Test]
        public void SameSeed_SameLayout()
        {
            foreach (var pattern in AllPatterns)
            {
                var a = LaminateLayout.Generate(pattern, L, W, Sources, Seed);
                var b = LaminateLayout.Generate(pattern, L, W, Sources, Seed);
                Assert.AreEqual(a.Count, b.Count);
                for (int i = 0; i < a.Count; i++)
                {
                    Assert.AreEqual(a[i].SourceIndex, b[i].SourceIndex);
                    Assert.AreEqual(a[i].Flip, b[i].Flip);
                    Assert.AreEqual(a[i].CropU0, b[i].CropU0);
                    Assert.AreEqual(a[i].Center, b[i].Center);
                }
            }
        }

        [Test]
        public void DifferentSeed_ChangesPlankChoice_NotGeometry()
        {
            foreach (var pattern in AllPatterns)
            {
                var a = LaminateLayout.Generate(pattern, L, W, Sources, Seed);
                var b = LaminateLayout.Generate(pattern, L, W, Sources, Seed + 1);
                bool anyChoiceDiffers = false;
                for (int i = 0; i < a.Count; i++)
                {
                    Assert.AreEqual(a[i].Center, b[i].Center, "geometry must not depend on seed");
                    Assert.AreEqual(a[i].Size, b[i].Size);
                    Assert.AreEqual(a[i].RotationDeg, b[i].RotationDeg);
                    if (a[i].SourceIndex != b[i].SourceIndex || a[i].Flip != b[i].Flip)
                        anyChoiceDiffers = true;
                }
                Assert.IsTrue(anyChoiceDiffers, $"{pattern}: seed had no effect");
            }
        }

        [Test]
        public void SourceIndex_WithinRange()
        {
            foreach (var pattern in AllPatterns)
                foreach (var q in LaminateLayout.Generate(pattern, L, W, 5, Seed))
                    Assert.That(q.SourceIndex, Is.InRange(0, 4));
        }

        private static float Frac(float v) => v - Mathf.Floor(v);
    }
}
