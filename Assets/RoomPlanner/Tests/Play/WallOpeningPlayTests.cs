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
        private readonly List<Material> _materials = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            foreach (var m in _materials) if (m != null) Object.DestroyImmediate(m);
            _materials.Clear();
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
            // five slots like the real prefab (inner / glass / joinery / outer / rims) —
            // the frame finish of issue #133 rides slot 2
            var shader = Shader.Find("RoomPlanner/LitVertexAO")
                ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
            var mats = new Material[5];
            for (int i = 0; i < mats.Length; i++)
            {
                mats[i] = new Material(shader);
                _materials.Add(mats[i]);
            }
            template.AddComponent<MeshRenderer>().sharedMaterials = mats;
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

        /// <summary>Wall mesh OR its leaf children (#50: the leaf is a child view now).</summary>
        private static bool HitsWall(Wall view, Ray ray, out RaycastHit hit)
        {
            Physics.SyncTransforms();
            hit = default;
            float best = float.MaxValue;
            foreach (var col in view.GetComponentsInChildren<Collider>())
                if (col.Raycast(ray, out var h, 20f) && h.distance < best)
                {
                    best = h.distance;
                    hit = h;
                }
            return best < float.MaxValue;
        }

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
            Assert.Greater(mesh.GetTriangles(2).Length, 0, "the frame lives in the joinery submesh");
            Assert.IsNotNull(view.GetComponentInChildren<OpeningLeafView>(),
                "the leaf itself is a child view (#50)");
        }

        [UnityTest]
        public IEnumerator ImportedDoorLeafStandsOpen_OnItsSwingSide()
        {
            // Same 1 m doorway, but with IFC swing data: hinge on the x = 1.5 jamb,
            // opening toward −Z. OpenFraction 0.75 = the historical 75° stance — the
            // doorway becomes passable and the leaf stands on the swing side.
            var (view, _) = MakeWall(new WallOpening
            {
                Id = 1, AlongFraction = 0.5f, Width = 1f, Height = 2.1f, SillHeight = 0f, IsDoor = true,
                SwingDir = new Vector3(0f, 0f, -1f),
                HingeDir = new Vector3(1f, 0f, 0f),
                OpenFraction = 0.75f,
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
            Assert.AreEqual(5, mesh.subMeshCount, "inner/glass/joinery/outer/rims (#34)");
            Assert.Greater(mesh.GetTriangles(1).Length, 0, "window pane lives in the glass submesh");
            Assert.AreEqual(4 * 6 * 6, mesh.GetTriangles(2).Length,
                "a window is exactly four six-faced frame bars, with no mullions or crossbars");

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

        /// <summary>The IFC material of a door dresses its frame AND its leaf
        /// (issue #133): a property block on the joinery submesh and on the leaf
        /// renderers, never a mutated shared material.</summary>
        [UnityTest]
        public IEnumerator DoorFrameFinishReachesJoineryAndLeaf()
        {
            var tex = new Texture2D(4, 4);
            var op = new WallOpening
            {
                Id = 1, AlongFraction = 0.5f, Width = 0.9f, Height = 2.1f,
                Kind = OpeningKind.Door, SwingDir = Vector3.forward, HingeDir = Vector3.right,
                FrameFinish = RoomPlanner.Core.SurfaceFinish.OfTexture("wood-birch", 0.8f),
                FrameTexture = tex,
            };
            var (view, _) = MakeWall(op);
            yield return null;

            var block = new MaterialPropertyBlock();
            var mr = view.GetComponent<MeshRenderer>();
            mr.GetPropertyBlock(block, 2);
            Assert.IsFalse(block.isEmpty, "joinery submesh carries the frame block");
            Assert.AreSame(tex, block.GetTexture("_BaseMap"), "with the finish texture");

            var leaf = view.GetComponentInChildren<OpeningLeafView>();
            Assert.IsNotNull(leaf, "the door has a leaf child");
            var leafRenderer = leaf.GetComponentInChildren<MeshRenderer>();
            Assert.IsNotNull(leafRenderer);
            var leafBlock = new MaterialPropertyBlock();
            leafRenderer.GetPropertyBlock(leafBlock);
            Assert.IsFalse(leafBlock.isEmpty, "the leaf wears the same finish");

            Object.DestroyImmediate(tex);
        }

        /// <summary>A hand-drawn opening has no finish — the joinery keeps the rig's own
        /// material and no block is left behind.</summary>
        [UnityTest]
        public IEnumerator OpeningWithoutFinishLeavesTheJoineryAlone()
        {
            var (view, _) = MakeWall(new WallOpening
            {
                Id = 1, AlongFraction = 0.5f, Width = 0.9f, Height = 2.1f,
                Kind = OpeningKind.Door,
            });
            yield return null;

            var block = new MaterialPropertyBlock();
            view.GetComponent<MeshRenderer>().GetPropertyBlock(block, 2);
            Assert.IsTrue(block.isEmpty);
        }

        [UnityTest]
        public IEnumerator WallWithoutOpeningsKeepsItsShape()
        {
            var (view, seg) = MakeWall();
            yield return null;

            Assert.IsTrue(HitsWall(view, new Ray(new Vector3(2f, 1f, -2f), Vector3.forward), out _));
            var mesh = view.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(5, mesh.subMeshCount, "empty glass/joinery submeshes always present");
            Assert.AreEqual(0, mesh.GetTriangles(1).Length);
            Assert.AreEqual(0, mesh.GetTriangles(2).Length);
            Assert.Greater(mesh.GetTriangles(3).Length, 0, "outer side populated (#34)");
            Assert.Greater(mesh.GetTriangles(4).Length, 0, "rims populated (#34)");
            Assert.AreEqual(0, seg.Openings.Count);
        }
    }
}
