using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>Laminate/tile laying pattern (design/22, 23). Chevron (45°) is
    /// deferred — its period is not square without mitre cuts.</summary>
    public enum LaminatePattern
    {
        Deck,          // running bond, rows offset by deckOffset (laminate ⅓, subway ½)
        Herringbone,   // 0°/90° staircase
        Basket,        // 0.6 m squares of 3 half-planks, alternating direction
        Grid,          // square tiles 2×2 (ceramic, design/23)
    }

    /// <summary>
    /// One plank instance inside the seamless tile. Size is the AXIS-ALIGNED footprint
    /// in tile UV (every rotation is 0/90, so footprints are axis-aligned rectangles);
    /// RotationDeg tells the baker how the plank texture is oriented inside that
    /// footprint (90 = the plank's length runs along tile V). Quads may cross the
    /// [0..1] tile edge — the baker draws wrap duplicates shifted by ±1.
    /// </summary>
    public struct PlankQuad
    {
        public Vector2 Center;       // tile UV, kept inside [0..1)
        public Vector2 Size;         // tile UV footprint
        public float RotationDeg;    // 0 or 90
        public int SourceIndex;      // which source plank texture
        public bool Flip;            // rotate the plank texture 180° for variety
        public float CropU0, CropU1; // used stretch of the plank (cut boards)
    }

    /// <summary>
    /// Pure layout math for the laminate baker (design/22): pattern → plank quads of
    /// one seamless square tile. Deterministic — plank/flip choices come from the seed,
    /// never from time or global randomness, so a re-bake reproduces the same texture.
    /// The plank must have the classic 6:1 ratio (1.2×0.2 m): Basket needs 3W = L/2
    /// and Herringbone assumes an integer cell ratio n = L/W = 6.
    /// </summary>
    public static class LaminateLayout
    {
        /// <summary>Physical side of the square tile the pattern repeats over.</summary>
        public static float TileMeters(LaminatePattern pattern, float plankL, float plankW)
        {
            // Herringbone's cell lattice repeats every 2n cells of size W = 2L metres;
            // Grid bakes a 2×2 block for variety; Deck rows and Basket squares both
            // close after exactly one plank length.
            return pattern is LaminatePattern.Herringbone or LaminatePattern.Grid
                ? 2f * plankL : plankL;
        }

        public static List<PlankQuad> Generate(LaminatePattern pattern,
            float plankL, float plankW, int sources, int seed, float deckOffset = 1f / 3f)
        {
            if (plankL <= 0f || plankW <= 0f) throw new ArgumentOutOfRangeException(nameof(plankL));
            int n = Mathf.RoundToInt(plankL / plankW);
            int minRatio = pattern == LaminatePattern.Grid ? 1 : 2;   // square ceramic tiles
            if (Mathf.Abs(plankL / plankW - n) > 1e-3f || n < minRatio)
                throw new ArgumentException($"plank ratio must be an integer, got {plankL / plankW}");
            if (sources < 1) throw new ArgumentOutOfRangeException(nameof(sources));

            var rng = new System.Random(seed);
            var quads = new List<PlankQuad>();
            switch (pattern)
            {
                case LaminatePattern.Deck: GenerateDeck(n, deckOffset, sources, rng, quads); break;
                case LaminatePattern.Herringbone: GenerateHerringbone(n, sources, rng, quads); break;
                case LaminatePattern.Basket: GenerateBasket(n, sources, rng, quads); break;
                case LaminatePattern.Grid: GenerateGrid(sources, rng, quads); break;
                default: throw new ArgumentOutOfRangeException(nameof(pattern));
            }
            return quads;
        }

        /// <summary>n rows of full-length planks; row r shifted by r·offset·L (mod L) —
        /// laminate cycles by thirds, subway tile (design/23) by halves.</summary>
        private static void GenerateDeck(int n, float offset, int sources,
            System.Random rng, List<PlankQuad> quads)
        {
            float w = 1f / n;
            for (int r = 0; r < n; r++)
            {
                float x0 = Frac(r * offset);
                quads.Add(NewQuad(rng, sources,
                    center: new Vector2(Frac(x0 + 0.5f), (r + 0.5f) * w),
                    size: new Vector2(1f, w),
                    rotationDeg: 0f, crop0: 0f, crop1: 1f));
            }
        }

        /// <summary>2×2 block of square tiles (ceramic grid, design/23) — four sources
        /// per bake tile keep the glaze variation from repeating too obviously.</summary>
        private static void GenerateGrid(int sources, System.Random rng, List<PlankQuad> quads)
        {
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                    quads.Add(NewQuad(rng, sources,
                        center: new Vector2((i + 0.5f) * 0.5f, (j + 0.5f) * 0.5f),
                        size: new Vector2(0.5f, 0.5f),
                        rotationDeg: 0f, crop0: 0f, crop1: 1f));
        }

        /// <summary>
        /// Cell grid of 2n×2n (cell = plank width W). Along any row you see one plank
        /// lengthwise (n cells) then n crosswise columns — the classic 90° herringbone.
        /// H planks: lower-left cell (y, y) mod 2n, one per row. V planks: column c
        /// hosts cells in rows c+1 … c+n (mod 2n), i.e. one V starting at row c+1.
        /// Verified seamless by the coverage test (every cell exactly once).
        /// </summary>
        private static void GenerateHerringbone(int n, int sources, System.Random rng, List<PlankQuad> quads)
        {
            int cells = 2 * n;
            float cell = 1f / cells;
            for (int y = 0; y < cells; y++)   // horizontal planks, staircase (y, y)
                quads.Add(NewQuad(rng, sources,
                    center: new Vector2(Frac((y + n * 0.5f) * cell), (y + 0.5f) * cell),
                    size: new Vector2(n * cell, cell),
                    rotationDeg: 0f, crop0: 0f, crop1: 1f));
            for (int c = 0; c < cells; c++)   // vertical planks, start row c+1
                quads.Add(NewQuad(rng, sources,
                    center: new Vector2((c + 0.5f) * cell, Frac((c + 1 + n * 0.5f) * cell)),
                    size: new Vector2(cell, n * cell),
                    rotationDeg: 90f, crop0: 0f, crop1: 1f));
        }

        /// <summary>2×2 squares of L/2 side, each of 3 half-planks (a random half of the
        /// source board via CropU); direction alternates checkerboard-wise.</summary>
        private static void GenerateBasket(int n, int sources, System.Random rng, List<PlankQuad> quads)
        {
            float w = 1f / n;
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                {
                    bool horizontal = (i + j) % 2 == 0;
                    for (int k = 0; k < n / 2; k++)
                    {
                        bool firstHalf = rng.Next(2) == 0;
                        quads.Add(NewQuad(rng, sources,
                            center: horizontal
                                ? new Vector2(i * 0.5f + 0.25f, j * 0.5f + (k + 0.5f) * w)
                                : new Vector2(i * 0.5f + (k + 0.5f) * w, j * 0.5f + 0.25f),
                            size: horizontal ? new Vector2(0.5f, w) : new Vector2(w, 0.5f),
                            rotationDeg: horizontal ? 0f : 90f,
                            crop0: firstHalf ? 0f : 0.5f,
                            crop1: firstHalf ? 0.5f : 1f));
                    }
                }
        }

        private static PlankQuad NewQuad(System.Random rng, int sources,
            Vector2 center, Vector2 size, float rotationDeg, float crop0, float crop1)
        {
            return new PlankQuad
            {
                Center = center,
                Size = size,
                RotationDeg = rotationDeg,
                SourceIndex = rng.Next(sources),
                Flip = rng.Next(2) == 0,
                CropU0 = crop0,
                CropU1 = crop1,
            };
        }

        private static float Frac(float v) => v - Mathf.Floor(v);
    }
}
