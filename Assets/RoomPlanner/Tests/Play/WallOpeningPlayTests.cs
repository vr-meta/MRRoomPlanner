using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Editing;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Panelisation v0 (design/18 I8): a graph wall with openings really has holes — rays
    /// pass through a doorway, piers/headers stay solid, windows carry a glass submesh.
    /// </summary>
    public class WallOpeningPlayTests
    {
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private (Wall view, WallSegment seg) MakeWall(params WallOpening[] openings)
        {
            var rig = new GameObject("Rig");
            _spawned.Add(rig);
            var model = rig.AddComponent<SceneModel>();

            var template = new GameObject("WallTemplate");
            _spawned.Add(template);
            template.SetActive(false);
            template.AddComponent<MeshFilter>();
            template.AddComponent<MeshRenderer>();
            var prefab = template.AddComponent<Wall>();
            template.AddComponent<Selectable>();

            var walls = rig.AddComponent<WallGraphRenderer>();
            walls.Configure(prefab, model);

            var a = walls.Graph.SnapOrCreateNode(new Vector3(0f, 0f, 0f));
            var b = walls.Graph.SnapOrCreateNode(new Vector3(4f, 0f, 0f));
            var seg = walls.Graph.AddSegment(a, b);
            seg.Thickness = 0.2f;
            seg.Height = 2.7f;
            seg.Offset = WallOffsetMode.Center;   // centerline in the z=0 plane
            foreach (var op in openings) seg.Openings.Add(op);
            walls.Sync();
            return (walls.ViewOf(seg), seg);
        }

        private static bool HitsWall(Wall view, Ray ray, out RaycastHit hit) =>
            view.GetComponent<MeshCollider>().Raycast(ray, out hit, 20f);

        [UnityTest]
        public IEnumerator DoorwayIsAHole_PiersAndHeaderAreSolid()
        {
            // 1 m door centred at 2 m: clear span x ∈ [1.5, 2.5], up to y = 2.1
            var (view, _) = MakeWall(new WallOpening
            {
                Id = 1, AlongFraction = 0.5f, Width = 1f, Height = 2.1f, SillHeight = 0f,
            });
            yield return null;

            Assert.IsFalse(HitsWall(view, new Ray(new Vector3(2f, 1f, -2f), Vector3.forward), out _),
                "a ray through the doorway must pass");
            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(0.5f, 1f, -2f), Vector3.forward), out _),
                "the pier beside the door is solid");
            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(2f, 2.5f, -2f), Vector3.forward), out _),
                "the header above the door is solid");
        }

        [UnityTest]
        public IEnumerator WindowHasGlassAndASill()
        {
            // 1.2 m window, sill 0.9, head 2.1
            var (view, _) = MakeWall(new WallOpening
            {
                Id = 1, AlongFraction = 0.5f, Width = 1.2f, Height = 1.2f, SillHeight = 0.9f,
            });
            yield return null;

            var mesh = view.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(2, mesh.subMeshCount);
            Assert.Greater(mesh.GetTriangles(1).Length, 0, "window pane lives in the glass submesh");

            // under-sill band is solid, and its top face sits at sill height
            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(2f, 0.45f, -2f), Vector3.forward), out _),
                "under-sill band is solid");
            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(2f, 2f, 0.05f), Vector3.down), out var sillHit),
                "a downward ray inside the opening lands on the sill");
            Assert.AreEqual(0.9f, sillHit.point.y, 1e-3, "sill top at sill height");
        }

        [UnityTest]
        public IEnumerator DoorHasNoGlass()
        {
            var (view, _) = MakeWall(new WallOpening
            {
                Id = 1, AlongFraction = 0.5f, Width = 0.9f, Height = 2.1f, SillHeight = 0f,
            });
            yield return null;

            var mesh = view.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(0, mesh.GetTriangles(1).Length, "doors stay open — no pane");
        }

        [UnityTest]
        public IEnumerator WallWithoutOpeningsKeepsItsShape()
        {
            var (view, seg) = MakeWall();
            yield return null;

            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(2f, 1f, -2f), Vector3.forward), out _));
            var mesh = view.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(2, mesh.subMeshCount, "empty glass submesh always present");
            Assert.AreEqual(0, mesh.GetTriangles(1).Length);
            Assert.AreEqual(0, seg.Openings.Count);
        }
    }
}
