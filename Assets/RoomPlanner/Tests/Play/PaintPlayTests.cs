using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Editing;
using RoomPlanner.Floors;
using RoomPlanner.Tools;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Paint v1 (design/04): paint is the object's OWN color — it survives hover/select
    /// tints, only dresses the body material on multi-material walls, and undoes cleanly.
    /// </summary>
    public class PaintPlayTests
    {
        private readonly List<GameObject> _spawned = new();
        private static readonly Color Terracotta = new(0.77f, 0.39f, 0.23f);

        private GameObject Track(GameObject go)
        {
            _spawned.Add(go);
            return go;
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private (Selectable sel, MeshRenderer renderer) MakeSlab()
        {
            var go = Track(new GameObject("Slab"));
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var slab = go.AddComponent<Floor>();
            var sel = go.AddComponent<Selectable>();
            slab.BuildOutline(new List<Vector3>
            {
                new(0f, 0f, 0f), new(2f, 0f, 0f), new(2f, 0f, 2f), new(0f, 0f, 2f),
            }, 0f, 0.2f, 5f, 0f, 0f, 0f);
            return (sel, mr);
        }

        private static Color BlockColor(Renderer r)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            return mpb.GetColor("_BaseColor");
        }

        private static void AssertColor(Color expected, Color actual, string why)
        {
            Assert.AreEqual(expected.r, actual.r, 0.01f, why);
            Assert.AreEqual(expected.g, actual.g, 0.01f, why);
            Assert.AreEqual(expected.b, actual.b, 0.01f, why);
        }

        [UnityTest]
        public IEnumerator PaintSurvivesHighlightRoundTrip()
        {
            var (sel, renderer) = MakeSlab();
            yield return null;

            sel.SetPaint(Terracotta);
            AssertColor(Terracotta, BlockColor(renderer), "painted");

            sel.SetHighlight(HighlightState.Hover);
            AssertColor(Color.Lerp(Terracotta, UiColors.Hover, 0.30f), BlockColor(renderer),
                "hover tint lerps from the PAINT, not the material");

            sel.SetHighlight(HighlightState.None);
            AssertColor(Terracotta, BlockColor(renderer), "paint stays after the highlight");
            Assert.IsTrue(sel.IsPainted);
        }

        [UnityTest]
        public IEnumerator PaintCommandUndoRestoresOriginalLook()
        {
            var (sel, renderer) = MakeSlab();
            var history = new RoomPlanner.Core.EditHistory();
            yield return null;

            history.Execute(new PaintCommand(sel, Terracotta));
            Assert.IsTrue(sel.IsPainted);

            history.Undo();
            Assert.IsFalse(sel.IsPainted, "never painted before — back to the material");
            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb);
            Assert.IsTrue(mpb.isEmpty, "no property block left behind");

            history.Redo();
            AssertColor(Terracotta, BlockColor(renderer), "redo repaints");
        }

        [UnityTest]
        public IEnumerator SecondPaintUndoesToTheFirst()
        {
            var (sel, _) = MakeSlab();
            var history = new RoomPlanner.Core.EditHistory();
            yield return null;

            history.Execute(new PaintCommand(sel, Terracotta));
            history.Execute(new PaintCommand(sel, Color.white));
            history.Undo();
            AssertColor(Terracotta, sel.Paint, "undo of a repaint returns the previous paint");
        }

        [UnityTest]
        public IEnumerator MultiMaterialBody_PaintsOnlySubmeshZero()
        {
            var go = Track(new GameObject("Wall"));
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            mr.sharedMaterials = new[]
            {
                new Material(shader), new Material(shader), new Material(shader),
            };
            var sel = go.AddComponent<Selectable>();
            yield return null;

            sel.SetPaint(Terracotta);
            var mpb = new MaterialPropertyBlock();
            mr.GetPropertyBlock(mpb, 0);
            AssertColor(Terracotta, mpb.GetColor("_BaseColor"), "body painted");
            mr.GetPropertyBlock(mpb, 1);
            Assert.IsTrue(mpb.isEmpty, "glass untouched");
            mr.GetPropertyBlock(mpb, 2);
            Assert.IsTrue(mpb.isEmpty, "joinery untouched");
        }

        // ---- texture finishes (design/04 «Текстуры v1») ----

        private readonly List<Texture2D> _textures = new();

        [TearDown]
        public void CleanupTextures()
        {
            foreach (var t in _textures) if (t != null) Object.DestroyImmediate(t);
            _textures.Clear();
        }

        private Texture2D MakeTexture()
        {
            var t = new Texture2D(4, 4);
            _textures.Add(t);
            return t;
        }

        [UnityTest]
        public IEnumerator TextureFinish_SetsMapAndMetricTiling_UndoRestores()
        {
            var (sel, renderer) = MakeSlab();
            var history = new RoomPlanner.Core.EditHistory();
            var tex = MakeTexture();
            yield return null;

            var finish = RoomPlanner.Core.SurfaceFinish.OfTexture("parquet-051", 2f);
            history.Execute(new PaintCommand(sel, finish, tex));

            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb);
            Assert.AreEqual(tex, mpb.GetTexture("_BaseMap"), "wallpaper/wood rides the MPB");
            var st = mpb.GetVector("_BaseMap_ST");
            Assert.AreEqual(0.5f, st.x, 1e-4f, "metric tiling: 1 m UV / 2 m tile");
            AssertColor(Color.white, mpb.GetColor("_BaseColor"), "untinted texture stays white");
            Assert.IsTrue(sel.IsPainted);
            Assert.AreEqual("parquet-051", sel.Finish.TextureId);

            sel.SetHighlight(HighlightState.Hover);
            renderer.GetPropertyBlock(mpb);
            Assert.AreEqual(tex, mpb.GetTexture("_BaseMap"), "hover keeps the texture");
            sel.SetHighlight(HighlightState.None);

            history.Undo();
            renderer.GetPropertyBlock(mpb);
            Assert.IsTrue(mpb.isEmpty, "undo returns the material's own look");
            Assert.IsFalse(sel.IsPainted);
        }

        [UnityTest]
        public IEnumerator TextureOverColor_UndoRestoresTheColor()
        {
            var (sel, renderer) = MakeSlab();
            var history = new RoomPlanner.Core.EditHistory();
            var tex = MakeTexture();
            yield return null;

            history.Execute(new PaintCommand(sel, Terracotta));
            history.Execute(new PaintCommand(sel,
                RoomPlanner.Core.SurfaceFinish.OfTexture("wallpaper-001a", 1f), tex));
            Assert.AreEqual(RoomPlanner.Core.FinishKind.Texture, sel.Finish.Kind);

            history.Undo();
            Assert.AreEqual(RoomPlanner.Core.FinishKind.Color, sel.Finish.Kind,
                "undo of the texture returns the terracotta paint");
            AssertColor(Terracotta, BlockColor(renderer), "color reapplied");
        }
    }
}
