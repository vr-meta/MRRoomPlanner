using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Core;
using RoomPlanner.Walls;
using UnityEngine;

namespace RoomPlanner.Tests
{
    public class WallPaintTests
    {
        private GameObject _go;
        private Wall _wall;
        private Material[] _mats;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Wall");
            _go.AddComponent<MeshFilter>();
            var renderer = _go.AddComponent<MeshRenderer>();
            // two slots like the real wall prefab (surface + glass) — paint targets index 0
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Standard");
            _mats = new[] { new Material(shader), new Material(shader) };
            renderer.sharedMaterials = _mats;
            _wall = _go.AddComponent<Wall>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            foreach (var m in _mats) Object.DestroyImmediate(m);
        }

        private void BuildWall() => _wall.Build(
            new List<Vector3> { Vector3.zero, new(3f, 0f, 0f) },
            0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, new Vector3(0f, 0f, 1f));

        private Color BlockColor()
        {
            var mpb = new MaterialPropertyBlock();
            _go.GetComponent<MeshRenderer>().GetPropertyBlock(mpb, 0);
            return mpb.GetColor("_BaseColor");
        }

        [Test]
        public void SetPaint_StoresColor_AndWritesBlock()
        {
            BuildWall();
            var terracotta = PaintPalette.ColorAt(3);
            _wall.SetPaint(terracotta);

            Assert.IsTrue(_wall.HasPaint);
            Assert.AreEqual(terracotta, _wall.PaintColor);
            Assert.AreEqual(terracotta, BlockColor(), "per-material block carries the paint");
        }

        [Test]
        public void ClearPaint_ReturnsToBareMaterial()
        {
            BuildWall();
            _wall.SetPaint(Color.red);
            _wall.ClearPaint();

            Assert.IsFalse(_wall.HasPaint);
            var mpb = new MaterialPropertyBlock();
            _go.GetComponent<MeshRenderer>().GetPropertyBlock(mpb, 0);
            Assert.IsTrue(mpb.isEmpty, "clearing paint removes the block entirely");
        }

        [Test]
        public void Paint_SurvivesRebuild()
        {
            BuildWall();
            _wall.SetPaint(Color.red);
            _wall.Rebuild();

            Assert.IsTrue(_wall.HasPaint, "paint is state, not a side effect of one mesh build");
            Assert.AreEqual(Color.red, BlockColor(), "the block lives on the renderer, not the mesh");
        }

        [Test]
        public void PaintHighlight_LerpsTowardStateColor_AndRestores()
        {
            BuildWall();
            _wall.SetPaint(Color.red);
            var cyan = new Color(0.5f, 0.85f, 1f);
            _wall.ApplyPaintHighlight(cyan, 0.3f);
            Assert.AreEqual(Color.Lerp(Color.red, cyan, 0.3f), BlockColor());

            _wall.ApplyPaintBlock();
            Assert.AreEqual(Color.red, BlockColor(), "un-highlight restores the paint, not concrete");
        }

        [Test]
        public void PaintHighlight_OnUnpaintedWall_WritesNothing()
        {
            BuildWall();
            _wall.ApplyPaintHighlight(Color.cyan, 0.3f);
            var mpb = new MaterialPropertyBlock();
            _go.GetComponent<MeshRenderer>().GetPropertyBlock(mpb, 0);
            Assert.IsTrue(mpb.isEmpty, "no per-material block → the renderer-wide tint shows through");
        }

        [Test]
        public void PaintPalette_IsConsistent()
        {
            Assert.AreEqual(PaintPalette.Names.Length, PaintPalette.Colors.Length);
            Assert.GreaterOrEqual(PaintPalette.Count, 8, "a usable catalog, not a stub");

            var seenNames = new HashSet<string>();
            var seenColors = new HashSet<Color>();
            for (int i = 0; i < PaintPalette.Count; i++)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(PaintPalette.Names[i]));
                Assert.IsTrue(seenNames.Add(PaintPalette.Names[i]), $"duplicate name {PaintPalette.Names[i]}");
                Assert.IsTrue(seenColors.Add(PaintPalette.Colors[i]), $"duplicate color at {i}");
                Assert.AreEqual(1f, PaintPalette.Colors[i].a, 1e-5, "paint is opaque");
            }
        }

        [Test]
        public void PaintPalette_IndexWraps()
        {
            Assert.AreEqual(PaintPalette.ColorAt(0), PaintPalette.ColorAt(PaintPalette.Count));
            Assert.AreEqual(PaintPalette.NameAt(PaintPalette.Count - 1), PaintPalette.NameAt(-1));
        }
    }
}
