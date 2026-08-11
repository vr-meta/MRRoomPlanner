using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core;
using RoomPlanner.Measure;
using RoomPlanner.Tools;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Inspector-widget gestures through the REAL ToolManager menu routing (#61):
    /// destructive rows fire only after the full 0.5 s hold, stepper rows auto-repeat
    /// while held, plain rows click once, a slider drag commits exactly one number.
    /// The manager component stays disabled and its Update runs scripted frame by
    /// frame, so the input flags are deterministic.
    /// </summary>
    public class WidgetGesturePlayTests
    {
        private const int MenuLayer = 2;

        private class FakeInput : MeasureInput
        {
            public bool Pressed, Held;
            public override bool ConfirmPressed() => Pressed;
            public override bool ConfirmHeld() => Held;
            public override bool ClearPressed() => false;
            public override bool TeleportPressed() => false;
            public override bool TeleportHeld() => false;
            public override bool UndoPressed() => false;
            public override bool RedoPressed() => false;
            public override void Pulse(float amplitude = 0.5f, float duration = 0.06f) { }
            public override void PulseLeft(float amplitude = 0.5f, float duration = 0.02f) { }
        }

        private class FakePointer : PointerProvider
        {
            public Ray Ray;
            public override Ray GetRay() => Ray;
        }

        private readonly List<GameObject> _spawned = new();
        private MethodInfo _update;

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
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);

        private (ToolManager manager, FakeInput input, FakePointer pointer) MakeManager()
        {
            var rig = Track(new GameObject("Rig"));
            var manager = rig.AddComponent<ToolManager>();
            manager.enabled = false;   // Update is driven manually — deterministic frames
            var input = rig.AddComponent<FakeInput>();
            var pointer = rig.AddComponent<FakePointer>();
            SetField(manager, "input", input);
            SetField(manager, "pointer", pointer);
            _update = typeof(ToolManager).GetMethod("Update",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (manager, input, pointer);
        }

        private void Frame(ToolManager manager) => _update.Invoke(manager, null);

        /// <summary>A runtime menu button with a collider, like the inspector rows build.</summary>
        private (MenuButton mb, Counter clicks) MakeButton(bool destructive, bool repeatable)
        {
            var go = Track(new GameObject("Row") { layer = MenuLayer });
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(0.1f, 0.05f, 0.02f);
            var mb = go.AddComponent<MenuButton>();
            var counter = new Counter();
            mb.OnClick = () => counter.Value++;
            mb.Destructive = destructive;
            mb.Repeatable = repeatable;
            mb.InitRuntime(MenuButtonKind.Momentary, null, null);
            return (mb, counter);
        }

        private class Counter { public int Value; }

        private static Ray AtButton() => new(new Vector3(0f, 0f, -1f), Vector3.forward);

        [UnityTest]
        public IEnumerator PlainRow_ClickFiresExactlyOnce()
        {
            var (manager, input, pointer) = MakeManager();
            var (_, clicks) = MakeButton(destructive: false, repeatable: false);
            yield return null;
            Physics.SyncTransforms();
            pointer.Ray = AtButton();

            input.Pressed = true; input.Held = true;
            Frame(manager);
            input.Pressed = false;
            Frame(manager);
            input.Held = false;
            Frame(manager);

            Assert.AreEqual(1, clicks.Value, "one press = one click, held frames add nothing");
        }

        [UnityTest]
        public IEnumerator DestructiveRow_FiresOnlyAfterTheFullHold()
        {
            var (manager, input, pointer) = MakeManager();
            var (_, clicks) = MakeButton(destructive: true, repeatable: false);
            yield return null;
            Physics.SyncTransforms();
            pointer.Ray = AtButton();

            input.Pressed = true; input.Held = true;
            Frame(manager);                       // arms the hold — must NOT fire yet
            input.Pressed = false;
            Assert.AreEqual(0, clicks.Value, "arming is not firing");

            float deadline = Time.time + UiTokens.DestructiveHoldSeconds + 0.4f;
            while (clicks.Value == 0 && Time.time < deadline)
            {
                Frame(manager);
                yield return null;                // real time advances toward the 0.5 s hold
            }
            Assert.AreEqual(1, clicks.Value, "the completed hold fires exactly once");

            Frame(manager);
            Assert.AreEqual(1, clicks.Value, "holding past the fire must not re-fire");
        }

        [UnityTest]
        public IEnumerator DestructiveRow_EarlyRelease_NeverFires()
        {
            var (manager, input, pointer) = MakeManager();
            var (_, clicks) = MakeButton(destructive: true, repeatable: false);
            yield return null;
            Physics.SyncTransforms();
            pointer.Ray = AtButton();

            input.Pressed = true; input.Held = true;
            Frame(manager);                       // arm
            input.Pressed = false;
            yield return new WaitForSeconds(0.15f);
            Frame(manager);                       // still holding, still short of 0.5 s
            input.Held = false;
            Frame(manager);                       // released early — disarms silently

            yield return new WaitForSeconds(UiTokens.DestructiveHoldSeconds);
            Frame(manager);
            Assert.AreEqual(0, clicks.Value, "an aborted hold must never fire");
        }

        [UnityTest]
        public IEnumerator StepperRow_AutoRepeatsWhileHeld()
        {
            var (manager, input, pointer) = MakeManager();
            var (_, clicks) = MakeButton(destructive: false, repeatable: true);
            yield return null;
            Physics.SyncTransforms();
            pointer.Ray = AtButton();

            input.Pressed = true; input.Held = true;
            Frame(manager);
            input.Pressed = false;
            Assert.AreEqual(1, clicks.Value, "the press itself steps once");

            float deadline = Time.time + 1.2f;    // 0.4 s delay + a few 8 Hz ticks
            while (Time.time < deadline)
            {
                Frame(manager);
                yield return null;
            }
            Assert.Greater(clicks.Value, 2, "holding keeps stepping (0.4 s delay, then 8 Hz)");

            int atRelease = clicks.Value;
            input.Held = false;
            Frame(manager);
            yield return new WaitForSeconds(0.2f);
            Frame(manager);
            Assert.AreEqual(atRelease, clicks.Value, "release stops the repeat");
        }

        [UnityTest]
        public IEnumerator SliderDrag_CommitsExactlyOneNumber()
        {
            var (manager, input, pointer) = MakeManager();

            // a standalone slider row, built the way InspectorPanel.BuildSlider wires it
            const float trackW = 0.2f;
            var go = Track(new GameObject("Slider") { layer = MenuLayer });
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(trackW * 0.5f, 0f, 0f);
            col.size = new Vector3(trackW, 0.05f, 0.02f);
            var widget = go.AddComponent<SliderWidget>();

            float number = 0.2f;
            int commits = 0;
            float committedBefore = -1f, committedAfter = -1f;
            var field = new SettingField
            {
                Id = "s", Min = 0f, Max = 1f, Step = 0.05f,
                GetNumber = () => number,
                SetNumber = v => number = v,
                CommitNumber = (before, after) =>
                {
                    commits++;
                    committedBefore = before;
                    committedAfter = after;
                },
            };
            var fill = new GameObject("Fill").transform;
            fill.SetParent(go.transform, false);
            var knob = new GameObject("Knob").transform;
            knob.SetParent(go.transform, false);
            widget.Init(field, fill, null, knob, null, trackW);
            yield return null;
            Physics.SyncTransforms();

            pointer.Ray = new Ray(new Vector3(0.05f, 0f, -1f), Vector3.forward);
            input.Pressed = true; input.Held = true;
            Frame(manager);                       // captures the slider, jumps to 0.05/0.2
            input.Pressed = false;

            pointer.Ray = new Ray(new Vector3(0.18f, 0f, -1f), Vector3.forward);
            Frame(manager);                       // drag right
            Assert.AreEqual(0, commits, "no commit while the drag is live");
            Assert.Greater(number, 0.6f, "the preview follows the hand");

            input.Held = false;
            Frame(manager);                       // release → EndDrag

            Assert.AreEqual(1, commits, "one drag = one committed number");
            Assert.AreEqual(0.2f, committedBefore, 1e-4f, "before = value at drag start");
            Assert.AreEqual(number, committedAfter, 1e-4f, "after = the previewed value");
        }
    }
}
