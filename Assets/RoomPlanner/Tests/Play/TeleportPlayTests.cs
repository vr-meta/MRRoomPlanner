using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Editing;
using RoomPlanner.Floors;
using RoomPlanner.Tools;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Teleport (design/18 I6): one TeleportCommand shifts the whole model — every graph
    /// node exactly once (shared corners must not move twice) and every slab, hidden ones
    /// included — and undo brings it back.
    /// </summary>
    public class TeleportPlayTests
    {
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

        private (WallGraphRenderer walls, Floor slab, SceneModel model) MakeScene()
        {
            var rig = Track(new GameObject("Rig"));
            var model = rig.AddComponent<SceneModel>();

            var wallTemplate = Track(new GameObject("WallTemplate"));
            wallTemplate.SetActive(false);
            wallTemplate.AddComponent<MeshFilter>();
            wallTemplate.AddComponent<MeshRenderer>();
            var wallPrefab = wallTemplate.AddComponent<Wall>();
            wallTemplate.AddComponent<Selectable>();

            var walls = rig.AddComponent<WallGraphRenderer>();
            walls.Configure(wallPrefab, model);

            // an L-corner: two segments SHARING a node — the double-move trap
            var a = walls.Graph.SnapOrCreateNode(new Vector3(0f, 0f, 0f));
            var b = walls.Graph.SnapOrCreateNode(new Vector3(4f, 0f, 0f));
            var c = walls.Graph.SnapOrCreateNode(new Vector3(4f, 0f, 3f));
            walls.Graph.AddSegment(a, b);
            walls.Graph.AddSegment(b, c);
            walls.Sync();

            var floorGo = Track(new GameObject("Slab"));
            floorGo.AddComponent<MeshFilter>();
            floorGo.AddComponent<MeshRenderer>();
            var slab = floorGo.AddComponent<Floor>();
            slab.BuildOutline(new List<Vector3>
            {
                new(0f, 0f, 0f), new(4f, 0f, 0f), new(4f, 0f, 3f), new(0f, 0f, 3f),
            }, 0f, 0.2f, 5f, 0f, 0f, 0f);

            return (walls, slab, model);
        }

        [UnityTest]
        public IEnumerator TeleportShiftsNodesOnce_AndSlabs_UndoRestores()
        {
            var (walls, slab, model) = MakeScene();
            yield return null;

            var delta = new Vector3(-3f, -3.15f, 2f);
            model.History.Execute(new TeleportCommand(walls, TeleportCommand.CollectFloors(), delta));

            var shared = walls.Graph.FindNode(new Vector3(1f, -3.15f, 2f));
            Assert.IsNotNull(shared, "corner node moved by exactly one delta");
            Assert.AreEqual(2, shared.Degree, "still the shared L-corner");
            Assert.AreEqual(-3.15f, slab.Level, 1e-4f, "slab followed, including vertically");
            Assert.AreEqual(new Vector3(-3f, -3.15f, 2f), slab.Outline[0]);

            model.History.Undo();
            Assert.IsNotNull(walls.Graph.FindNode(new Vector3(4f, 0f, 0f)), "undo puts the corner back");
            Assert.AreEqual(0f, slab.Level, 1e-4f);
        }

        [UnityTest]
        public IEnumerator HiddenSlabsTeleportToo()
        {
            var (walls, slab, model) = MakeScene();
            yield return null;

            slab.gameObject.SetActive(false);   // deleted (hidden) — must still follow
            model.History.Execute(new TeleportCommand(walls, TeleportCommand.CollectFloors(), Vector3.right));

            Assert.AreEqual(1f, slab.Outline[0].x, 1e-4f,
                "a restored slab must reappear where the model IS, not where it was");
        }

        [UnityTest]
        public IEnumerator ElectricalLayerTeleportsToo_UndoRestores()
        {
            // Outlets and wires are world-anchored model data: without the shift they
            // visually "follow the user" after a teleport (headset feedback 2026-08-10).
            var (walls, _, model) = MakeScene();
            var fxGo = Track(new GameObject("Outlet"));
            fxGo.AddComponent<MeshFilter>();
            fxGo.AddComponent<MeshRenderer>();
            var fx = fxGo.AddComponent<RoomPlanner.Electrical.ElectricFixture>();
            fx.Build(RoomPlanner.Electrical.FixtureKind.Outlet, 1, 1);
            fxGo.transform.position = new Vector3(2f, 0.3f, 0f);
            fx.BaseLevel = 0f;

            var wireGo = Track(new GameObject("Wire"));
            wireGo.AddComponent<MeshFilter>();
            wireGo.AddComponent<MeshRenderer>();
            var wire = wireGo.AddComponent<RoomPlanner.Electrical.WireRoute>();
            wire.Build(new List<Vector3> { new(2f, 0.3f, 0f), new(2f, 2.5f, 0f), new(0f, 2.5f, 0f) },
                RoomPlanner.Electrical.CableType.C3x25);
            yield return null;

            var delta = new Vector3(2f, -3.15f, 1f);
            model.History.Execute(new TeleportCommand(walls, TeleportCommand.CollectFloors(), delta,
                TeleportCommand.CollectStairs(), TeleportCommand.CollectMep(),
                TeleportCommand.CollectFixtures(), TeleportCommand.CollectRoutes()));

            Assert.AreEqual(0f, Vector3.Distance(new Vector3(4f, -2.85f, 1f), fx.transform.position), 1e-5f);
            Assert.AreEqual(-3.15f, fx.BaseLevel, 1e-5f, "storey-relative height stays honest");
            Assert.AreEqual(0.3f, fx.HeightAboveLevel, 1e-4f);
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(2f, -0.65f, 1f), wire.GetPoint(2)), 1e-4f);

            model.History.Undo();
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(2f, 0.3f, 0f), fx.transform.position), 1e-5f);
            Assert.AreEqual(0f, fx.BaseLevel, 1e-5f);
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(0f, 2.5f, 0f), wire.GetPoint(2)), 1e-4f);
        }

        [UnityTest]
        public IEnumerator MepFixturesTeleportByTransform()
        {
            var (walls, _, model) = MakeScene();
            var mepGo = Track(new GameObject("Basin"));
            mepGo.transform.position = new Vector3(1f, 0.8f, 1f);
            mepGo.AddComponent<RoomPlanner.Import.MepView>();
            yield return null;

            var delta = new Vector3(2f, -3f, 0.5f);
            model.History.Execute(new TeleportCommand(walls, TeleportCommand.CollectFloors(), delta,
                TeleportCommand.CollectStairs(), TeleportCommand.CollectMep()));
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(3f, -2.2f, 1.5f), mepGo.transform.position), 1e-5f);

            model.History.Undo();
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(1f, 0.8f, 1f), mepGo.transform.position), 1e-5f);
        }
    }
}
