using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Paint;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// PlayMode coverage for wall painting (design/04 v1): the undoable stroke command,
    /// highlight/paint interplay through the real Selectable, and the tool schema.
    /// </summary>
    public class PaintPlayTests
    {
        private readonly List<GameObject> _spawned = new();
        private SceneModel _model;

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("SceneModelTest");
            _spawned.Add(go);
            _model = go.AddComponent<SceneModel>();
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.Destroy(go);
            _spawned.Clear();
            foreach (var m in _mats) if (m != null) Object.Destroy(m);
            _mats.Clear();
        }

        private readonly List<Material> _mats = new();

        private Wall MakeWall()
        {
            var go = new GameObject("Wall");
            _spawned.Add(go);
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            // two slots like the real wall prefab (surface + glass) — paint targets index 0
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Standard");
            var a = new Material(shader);
            var b = new Material(shader);
            _mats.Add(a); _mats.Add(b);
            renderer.sharedMaterials = new[] { a, b };
            var wall = go.AddComponent<Wall>();
            wall.Build(new List<Vector3> { Vector3.zero, new(3f, 0f, 0f) },
                0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, new Vector3(0f, 0f, 1f));
            go.AddComponent<Selectable>();
            _model.Register(go.GetComponent<Selectable>());
            return wall;
        }

        private static Color BlockColor(Wall wall)
        {
            var mpb = new MaterialPropertyBlock();
            wall.GetComponent<MeshRenderer>().GetPropertyBlock(mpb, 0);
            return mpb.GetColor("_BaseColor");
        }

        [Test]
        public void PaintCommand_UndoRestoresBareConcrete()
        {
            var wall = MakeWall();
            var sel = wall.GetComponent<Selectable>();

            _model.History.Execute(new PaintCommand(sel, wall, afterHas: true, PaintPalette.ColorAt(3)));
            Assert.IsTrue(wall.HasPaint);

            _model.History.Undo();
            Assert.IsFalse(wall.HasPaint, "the wall had never been painted before the stroke");
            _model.History.Redo();
            Assert.IsTrue(wall.HasPaint);
            Assert.AreEqual(PaintPalette.ColorAt(3), wall.PaintColor);
        }

        [Test]
        public void PaintCommand_UndoRestoresPreviousColor()
        {
            var wall = MakeWall();
            var sel = wall.GetComponent<Selectable>();
            wall.SetPaint(PaintPalette.ColorAt(0));

            _model.History.Execute(new PaintCommand(sel, wall, afterHas: true, PaintPalette.ColorAt(5)));
            Assert.AreEqual(PaintPalette.ColorAt(5), wall.PaintColor);
            _model.History.Undo();
            Assert.AreEqual(PaintPalette.ColorAt(0), wall.PaintColor, "undo = previous color, not concrete");
        }

        [Test]
        public void ClearCommand_IsUndoableToo()
        {
            var wall = MakeWall();
            var sel = wall.GetComponent<Selectable>();
            wall.SetPaint(PaintPalette.ColorAt(2));

            _model.History.Execute(new PaintCommand(sel, wall, afterHas: false, Color.white));
            Assert.IsFalse(wall.HasPaint);
            _model.History.Undo();
            Assert.IsTrue(wall.HasPaint);
            Assert.AreEqual(PaintPalette.ColorAt(2), wall.PaintColor);
        }

        [Test]
        public void HighlightCycle_PreservesPaint()
        {
            var wall = MakeWall();
            var sel = wall.GetComponent<Selectable>();
            var paint = PaintPalette.ColorAt(6);
            wall.SetPaint(paint);

            sel.SetHighlight(HighlightState.Selected);
            Assert.AreNotEqual(paint, BlockColor(wall), "selection must be visible over paint");
            sel.SetHighlight(HighlightState.None);
            Assert.AreEqual(paint, BlockColor(wall), "un-highlighting restores paint, not concrete (P1.4)");
        }

        [Test]
        public void PaintTool_SchemaAndIdentity()
        {
            var go = new GameObject("Paint");
            _spawned.Add(go);
            var tool = go.AddComponent<PaintController>();

            Assert.AreEqual("paint", tool.Id);
            Assert.AreEqual("Paint", tool.PaletteLabel);
            var schema = tool.GetSettings();
            CollectionAssert.AreEqual(new[] { "pcolor", "pact" },
                schema.Fields.Select(f => f.Id).ToArray());

            string first = schema.Fields[0].Value();
            schema.Fields[0].Increase();
            Assert.AreNotEqual(first, schema.Fields[0].Value(), "color row cycles the catalog");
            Assert.AreEqual("Paint", schema.Fields[1].Value());
            schema.Fields[1].Increase();
            Assert.AreEqual("Clear", schema.Fields[1].Value());
        }
    }
}
