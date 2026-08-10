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
        public IEnumerator GarageDoor_SectionalLeaf_BlocksFullWidth_WithRealSeams()
        {
            // 2.5 m garage door centred at 2 m (audit F1): a closed sectional leaf —
            // four stacked panels of ALTERNATING thickness, so neighbouring panels are
            // hit at different depths (the seams are geometry, not a texture).
            var (view, _) = MakeWall(new WallOpening
            {
                Id = 1, AlongFraction = 0.5f, Width = 2.5f, Height = 2.1f, SillHeight = 0f,
                Kind = OpeningKind.Garage,
            });
            yield return null;

            // panels stack from y=0; leaf zone is ~2.04 m tall → panel ≈ 0.51 m
            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(2f, 0.25f, -2f), Vector3.forward), out var p0),
                "panel 0 blocks the doorway");
            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(2f, 0.76f, -2f), Vector3.forward), out var p1),
                "panel 1 blocks the doorway");
            Assert.Greater(p1.point.z - p0.point.z, 0.004f,
                "odd panels are thinner — the step between sections is real geometry");

            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(1f, 1f, -2f), Vector3.forward), out _),
                "the leaf spans the full 2.5 m width, not just a door-sized strip");
            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(0.4f, 1f, -2f), Vector3.forward), out var pier),
                "the pier beside the garage door is solid");
            Assert.AreEqual(-0.1f, pier.point.z, 1e-3f, "the pier is hit at the wall FACE");
            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(2f, 2.5f, -2f), Vector3.forward), out _),
                "the header above the garage door stays solid");
        }

        [UnityTest]
        public IEnumerator DoorwayCarriesFrameAndLeaf_PiersAndHeaderAreSolid()
        {
            // 1 m door centred at 2 m: opening span x ∈ [1.5, 2.5], up to y = 2.1.
            // Since I12 the doorway is not an empty hole: a closed LEAF sits at the
            // mid-plane (opening it is a Phase D interaction).
            var (view, _) = MakeWall(new WallOpening
            {
                Id = 1, AlongFraction = 0.5f, Width = 1f, Height = 2.1f, SillHeight = 0f, IsDoor = true,
            });
            yield return null;

            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(2f, 1f, -2f), Vector3.forward), out var leafHit),
                "the closed door leaf blocks the doorway");
            Assert.AreEqual(0f, leafHit.point.z, 0.03f, "the leaf sits at the wall mid-plane");
            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(0.5f, 1f, -2f), Vector3.forward), out var pierHit),
                "the pier beside the door is solid");
            Assert.AreEqual(-0.1f, pierHit.point.z, 1e-3f, "the pier is hit at the wall FACE");
            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(2f, 2.5f, -2f), Vector3.forward), out _),
                "the header above the door is solid");

            var mesh = view.GetComponent<MeshFilter>().sharedMesh;
            Assert.Greater(mesh.GetTriangles(2).Length, 0, "frame + leaf live in the joinery submesh");
        }

        [UnityTest]
        public IEnumerator ImportedDoorLeafStandsOpen_OnItsSwingSide()
        {
            // Same 1 m doorway, but with IFC swing data: hinge on the x = 1.5 jamb,
            // opening toward −Z. The leaf swings 75° out of the wall — the doorway
            // becomes passable and the leaf stands on the swing side.
            var (view, _) = MakeWall(new WallOpening
            {
                Id = 1, AlongFraction = 0.5f, Width = 1f, Height = 2.1f, SillHeight = 0f, IsDoor = true,
                SwingDir = new Vector3(0f, 0f, -1f),
                HingeDir = new Vector3(1f, 0f, 0f),
            });
            yield return null;

            Assert.IsFalse(HitsWall(view, new Ray(new Vector3(2.2f, 1f, -2f), Vector3.forward), out _),
                "an open doorway is passable");
            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(1.7f, 1f, -2f), Vector3.forward), out var leafHit),
                "the swung leaf stands in front of the wall near the hinge");
            Assert.Less(leafHit.point.z, -0.15f, "the leaf sticks out on the swing side");
            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(0.5f, 1f, -2f), Vector3.forward), out _),
                "the pier beside the door stays solid");
        }

        [UnityTest]
        public IEnumerator FloorToCeilingWindowStillGetsGlass()
        {
            // Panoramic window: sill 0 — the IFC TYPE, not the sill, decides glass vs leaf.
            var (view, _) = MakeWall(new WallOpening
            {
                Id = 1, AlongFraction = 0.5f, Width = 1.5f, Height = 2.4f, SillHeight = 0f, IsDoor = false,
            });
            yield return null;

            var m = view.GetComponent<MeshFilter>().sharedMesh;
            Assert.Greater(m.GetTriangles(1).Length, 0, "panoramic window keeps its glass");
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
            Assert.AreEqual(3, mesh.subMeshCount);
            Assert.Greater(mesh.GetTriangles(1).Length, 0, "window pane lives in the glass submesh");
            Assert.Greater(mesh.GetTriangles(2).Length, 0, "window frame lives in the joinery submesh");

            // under-sill band is solid, and its top face sits at sill height;
            // z = 0.08 keeps the probe clear of the 100 mm deep frame bars around the mid-plane
            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(2f, 0.45f, -2f), Vector3.forward), out _),
                "under-sill band is solid");
            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(2f, 2f, 0.08f), Vector3.down), out var sillHit),
                "a downward ray inside the opening lands on the sill");
            Assert.AreEqual(0.9f, sillHit.point.y, 1e-3, "sill top at sill height");
        }

        [UnityTest]
        public IEnumerator DoorHasNoGlass()
        {
            var (view, _) = MakeWall(new WallOpening
            {
                Id = 1, AlongFraction = 0.5f, Width = 0.9f, Height = 2.1f, SillHeight = 0f, IsDoor = true,
            });
            yield return null;

            var mesh = view.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(0, mesh.GetTriangles(1).Length, "doors get a leaf, not a glass pane");
            Assert.Greater(mesh.GetTriangles(2).Length, 0, "the leaf/frame live in joinery");
        }

        [UnityTest]
        public IEnumerator WallWithoutOpeningsKeepsItsShape()
        {
            var (view, seg) = MakeWall();
            yield return null;

            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(2f, 1f, -2f), Vector3.forward), out _));
            var mesh = view.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(3, mesh.subMeshCount, "empty glass/joinery submeshes always present");
            Assert.AreEqual(0, mesh.GetTriangles(1).Length);
            Assert.AreEqual(0, mesh.GetTriangles(2).Length);
            Assert.AreEqual(0, seg.Openings.Count);
        }
    }
}
