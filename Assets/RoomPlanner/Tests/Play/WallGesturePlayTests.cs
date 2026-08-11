using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Measure;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Wall drawing gesture through the REAL WallController.Tick (#61): with no surface
    /// under the ray the cursor floats at the default air depth (2 m), so a downward
    /// ray from y=2 lands points on the y=0 plane deterministically. Two trigger
    /// clicks = a segment with a live view; B breaks the chain.
    /// </summary>
    public class WallGesturePlayTests
    {
        private class FakeInput : MeasureInput
        {
            public bool Pressed, Clear;
            public override bool ConfirmPressed() => Pressed;
            public override bool ConfirmHeld() => false;
            public override bool ClearPressed() => Clear;
            public override void Pulse(float amplitude = 0.5f, float duration = 0.06f) { }
            public override void PulseLeft(float amplitude = 0.5f, float duration = 0.02f) { }
        }

        private class FakePointer : PointerProvider
        {
            public Ray Ray;
            public override Ray GetRay() => Ray;
        }

        private readonly List<GameObject> _spawned = new();

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

        private static void SetField(object target, string field, object value) =>
            target.GetType()
                .GetField(field, System.Reflection.BindingFlags.Instance
                                 | System.Reflection.BindingFlags.NonPublic)
                .SetValue(target, value);

        private (WallController tool, FakeInput input, FakePointer pointer,
                 WallGraphRenderer walls) MakeRig()
        {
            var rig = Track(new GameObject("Rig"));
            var model = rig.AddComponent<SceneModel>();

            var template = Track(new GameObject("WallTemplate"));
            template.SetActive(false);
            template.AddComponent<MeshFilter>();
            template.AddComponent<MeshRenderer>();
            var prefab = template.AddComponent<Wall>();
            template.AddComponent<Selectable>();

            var walls = rig.AddComponent<WallGraphRenderer>();
            walls.Configure(prefab, model);

            var host = Track(new GameObject("WallTool"));
            var input = host.AddComponent<FakeInput>();
            var pointer = host.AddComponent<FakePointer>();
            var raycaster = host.AddComponent<SceneRaycaster>();   // no scan → air cursor
            var tool = host.AddComponent<WallController>();
            SetField(tool, "input", input);
            SetField(tool, "pointer", pointer);
            SetField(tool, "raycaster", raycaster);
            SetField(tool, "renderer", walls);
            return (tool, input, pointer, walls);
        }

        /// <summary>Downward ray whose 2 m air cursor lands at (x, 0, z).</summary>
        private static Ray Down(float x, float z) =>
            new(new Vector3(x, 2f, z), Vector3.down);

        private void Click(WallController tool, FakeInput input)
        {
            input.Pressed = true;
            tool.Tick(false);
            input.Pressed = false;
            tool.Tick(false);
        }

        [UnityTest]
        public IEnumerator TwoClicks_DrawOneWall_WithALiveView()
        {
            var (tool, input, pointer, walls) = MakeRig();
            yield return null;

            pointer.Ray = Down(0f, 0f);
            Click(tool, input);
            Assert.AreEqual(0, walls.Graph.Segments.Count, "first click only anchors the chain");

            pointer.Ray = Down(3f, 0f);
            Click(tool, input);

            Assert.AreEqual(1, walls.Graph.Segments.Count, "second click commits the segment");
            var seg = walls.Graph.Segments[0];
            Assert.AreEqual(0f, seg.A.Position.x, 1e-3f);
            Assert.AreEqual(3f, seg.B.Position.x, 1e-3f);
            Assert.IsNotNull(walls.ViewOf(seg), "the wall got a live view");
            Assert.Greater(walls.ViewOf(seg).GetComponent<MeshFilter>().sharedMesh.vertexCount, 0,
                "the view carries real geometry");
        }

        [UnityTest]
        public IEnumerator B_BreaksTheChain_NextClicksStartANewWall()
        {
            var (tool, input, pointer, walls) = MakeRig();
            yield return null;

            pointer.Ray = Down(0f, 0f);
            Click(tool, input);
            pointer.Ray = Down(3f, 0f);
            Click(tool, input);

            input.Clear = true;
            tool.Tick(false);      // B — finish the chain
            input.Clear = false;

            pointer.Ray = Down(0f, 5f);
            Click(tool, input);
            pointer.Ray = Down(3f, 5f);
            Click(tool, input);

            Assert.AreEqual(2, walls.Graph.Segments.Count, "two separate walls");
            Assert.AreEqual(4, walls.Graph.Nodes.Count,
                "the chains do not share nodes — B really broke the chain");
        }
    }
}
