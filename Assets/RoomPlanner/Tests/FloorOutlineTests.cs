using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Floors;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Phase C / step C2 — the slab built from an arbitrary closed outline
    /// (docs/design/17-floor-outline.md).
    ///
    /// Face orientation is checked for the non-rectangular case too: a downward pick has to land
    /// on the TOP of the slab, and an inward-facing triangle would silently send it to the
    /// underside (coding rule 1.1 / audit WP2).
    /// </summary>
    public class FloorOutlineTests
    {
        // Structure-pinning tests run with vertex-AO subdivision off (see FloorGeometryTests).
        private bool _savedAO;

        [SetUp]
        public void DisableVertexAO()
        {
            _savedAO = RoomPlanner.Core.MeshShading.VertexAO;
            RoomPlanner.Core.MeshShading.VertexAO = false;
        }

        [TearDown]
        public void RestoreVertexAO() => RoomPlanner.Core.MeshShading.VertexAO = _savedAO;

        private const float Thick = 0.2f;

        private static Vector3 P(float x, float z) => new Vector3(x, 0f, z);

        private static List<Vector3> LShape() => new()
        {
            P(0, 0), P(4, 0), P(4, 2), P(2, 2), P(2, 5), P(0, 5)
        };

        private static Floor MakeFloor(out GameObject go)
        {
            go = new GameObject("FloorTest");
            return go.AddComponent<Floor>();
        }

        private static Vector3 TriNormal(Vector3 a, Vector3 b, Vector3 c)
            => Vector3.Cross(b - a, c - a).normalized;

        private static void AssertFaceOutward(Mesh mesh, System.Predicate<Vector3> onFace,
            Vector3 expected, string label)
        {
            var v = mesh.vertices;
            var t = mesh.triangles;
            int found = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                if (!onFace(a) || !onFace(b) || !onFace(c)) continue;
                found++;
                Assert.Greater(Vector3.Dot(TriNormal(a, b, c), expected), 0.9f,
                    $"{label}: triangle normal must point {expected}");
            }
            Assert.Greater(found, 0, $"{label}: no triangles found on that face");
        }

        // ---- the rectangle path still behaves ----

        [Test]
        public void Rectangle_StillBuilds_AndKeepsItsCorners()
        {
            var floor = MakeFloor(out var go);
            try
            {
                var a = P(0, 0);
                var b = new Vector3(4f, 0f, 3f);
                floor.Build(a, b, 0f, Thick, 5f, 0f, 0f);

                Assert.AreEqual(4, floor.Outline.Count, "a rectangle is the 4-point outline");
                Assert.AreEqual(a, floor.CornerA, "the two-corner API reports what it was given");
                Assert.AreEqual(b, floor.CornerB);
                Assert.AreEqual(12f, floor.Area, 1e-3f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---- arbitrary outline ----

        [Test]
        public void Outline_BuildsAnLShapedSlab()
        {
            var floor = MakeFloor(out var go);
            try
            {
                floor.BuildOutline(LShape(), 0f, Thick, 5f, 0f, 0f, 0f);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;

                Assert.AreEqual(6, floor.Outline.Count);
                Assert.AreEqual(36, mesh.vertexCount, "6 points x (top + bottom + 4 side verts)");
                Assert.AreEqual(14f, floor.Area, 1e-3f, "area of the L, not of its bounding box");
                Assert.AreEqual(Thick, mesh.bounds.size.y, 1e-3f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Outline_CornersReportTheBoundingBox()
        {
            var floor = MakeFloor(out var go);
            try
            {
                floor.BuildOutline(LShape(), 0f, Thick, 5f, 0f, 0f, 0f);
                Assert.AreEqual(P(0, 0), floor.CornerA);
                Assert.AreEqual(new Vector3(4f, 0f, 5f), floor.CornerB);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Outline_DrawingDirectionDoesNotMatter()
        {
            var cw = LShape();
            cw.Reverse();

            var floor = MakeFloor(out var go);
            try
            {
                floor.BuildOutline(cw, 0f, Thick, 5f, 0f, 0f, 0f);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;

                Assert.AreEqual(14f, floor.Area, 1e-3f);
                AssertFaceOutward(mesh, p => Mathf.Abs(p.y) < 1e-3f, Vector3.up,
                    "top face of a slab drawn clockwise");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Outline_FacesPointOutward()
        {
            var floor = MakeFloor(out var go);
            try
            {
                floor.BuildOutline(LShape(), 0f, Thick, 5f, 0f, 0f, 0f);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;

                AssertFaceOutward(mesh, p => Mathf.Abs(p.y) < 1e-3f, Vector3.up, "top");
                AssertFaceOutward(mesh, p => Mathf.Abs(p.y + Thick) < 1e-3f, Vector3.down, "bottom");
                // the long south edge (z = 0) must face −Z
                AssertFaceOutward(mesh, p => Mathf.Abs(p.z) < 1e-3f, Vector3.back, "south side");
                // the notch face at x = 2 looks EAST, away from the slab body
                AssertFaceOutward(mesh, p => Mathf.Abs(p.x - 2f) < 1e-3f && p.z > 2f - 1e-3f,
                    Vector3.right, "notch side");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Outline_KeepsEveryPointOnTheLevel()
        {
            var floor = MakeFloor(out var go);
            try
            {
                var sloppy = new List<Vector3> { P(0, 0), new Vector3(4f, 9f, 0f), P(4, 3), P(0, 3) };
                floor.BuildOutline(sloppy, 1.5f, Thick, 5f, 0f, 0f, 0f);

                foreach (var p in floor.Outline)
                    Assert.AreEqual(1.5f, p.y, 1e-4f, "a slab is flat — a stray Y must not tilt it");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---- refusing bad input ----

        [Test]
        public void Outline_TooFewPoints_ProducesNothing()
        {
            var floor = MakeFloor(out var go);
            try
            {
                floor.BuildOutline(new List<Vector3> { P(0, 0), P(4, 0) }, 0f, Thick, 5f, 0f, 0f, 0f);
                Assert.AreEqual(0, go.GetComponent<MeshFilter>().sharedMesh.vertexCount);
                Assert.IsNull(go.GetComponent<MeshCollider>().sharedMesh, "no stale physics either");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Outline_SelfIntersecting_IsRefused()
        {
            var floor = MakeFloor(out var go);
            try
            {
                var bowtie = new List<Vector3> { P(0, 0), P(4, 3), P(4, 0), P(0, 3) };
                floor.BuildOutline(bowtie, 0f, Thick, 5f, 0f, 0f, 0f);

                Assert.AreEqual(0, go.GetComponent<MeshFilter>().sharedMesh.vertexCount,
                    "better no slab than one whose shape the user cannot reason about");
                Assert.IsNull(go.GetComponent<MeshCollider>().sharedMesh);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---- editing ----

        [Test]
        public void MoveCorner_ReshapesTheSlab()
        {
            var floor = MakeFloor(out var go);
            try
            {
                floor.Build(P(0, 0), new Vector3(4f, 0f, 3f), 0f, Thick, 5f, 0f, 0f);
                float before = floor.Area;

                floor.MoveCorner(1, P(6, 0));      // pull one corner out

                Assert.Greater(floor.Area, before, "the slab actually grew");
                Assert.AreEqual(4, floor.Outline.Count, "moving a corner does not add points");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void MoveBy_ShiftsTheWholeOutline()
        {
            var floor = MakeFloor(out var go);
            try
            {
                floor.BuildOutline(LShape(), 0f, Thick, 5f, 0f, 0f, 0f);
                float area = floor.Area;

                floor.MoveBy(new Vector3(2f, 0f, -1f));

                Assert.AreEqual(area, floor.Area, 1e-3f, "moving must not deform the slab");
                Assert.AreEqual(P(2, -1), floor.Outline[0]);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void SetPlanPlacement_KeepsTheShape()
        {
            // Regression: re-applying the blueprint used to go through the two-corner Build(),
            // which flattened a polygonal slab into its bounding box the moment the plan moved.
            var floor = MakeFloor(out var go);
            try
            {
                floor.BuildOutline(LShape(), 0f, Thick, 5f, 0f, 0f, 0f);

                floor.SetPlanPlacement(3f, 30f, 1f, 2f);

                Assert.AreEqual(6, floor.Outline.Count, "the L must not become a rectangle");
                Assert.AreEqual(14f, floor.Area, 1e-3f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---- holes (C6) ----

        [Test]
        public void AddHole_CutsTheSlab_AndReducesArea()
        {
            var floor = MakeFloor(out var go);
            try
            {
                floor.Build(P(0, 0), new Vector3(6f, 0f, 4f), 0f, Thick, 5f, 0f, 0f);   // 24
                var shaft = new List<Vector3> { P(2, 1), P(4, 1), P(4, 3), P(2, 3) };   // 4

                Assert.IsTrue(floor.AddHole(shaft));
                Assert.AreEqual(1, floor.Holes.Count);
                Assert.AreEqual(20f, floor.Area, 1e-2f, "the stairwell is not floor area");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AddHole_OverlappingAnotherHole_IsRefused_AndSlabSurvives()
        {
            // Audit 2026-08-10 (04 §Б1): a refused hole used to clear the mesh, drop the
            // collider and still report success — the slab silently vanished.
            var floor = MakeFloor(out var go);
            try
            {
                floor.Build(P(0, 0), new Vector3(6f, 0f, 4f), 0f, Thick, 5f, 0f, 0f);
                Assert.IsTrue(floor.AddHole(new List<Vector3> { P(1, 1), P(3, 1), P(3, 3), P(1, 3) }));
                int vertsAfterFirst = go.GetComponent<MeshFilter>().sharedMesh.vertexCount;

                var crossing = new List<Vector3> { P(2, 2), P(4, 2), P(4, 3.5f), P(2, 3.5f) };
                Assert.IsFalse(floor.AddHole(crossing), "a hole crossing a hole is refused");
                Assert.AreEqual(1, floor.Holes.Count, "the refused ring must not linger in the model");
                Assert.AreEqual(vertsAfterFirst, go.GetComponent<MeshFilter>().sharedMesh.vertexCount,
                    "refusal must leave the mesh untouched");
                Assert.IsNotNull(go.GetComponent<MeshCollider>().sharedMesh, "collider survives too");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AddHole_HoleCornerOnTheSlabDiagonal_Works()
        {
            // Regression (audit 04 §Б1 diagnosis): the bridge seam from hole corner (3,3)
            // to outline corner (0,0) passed exactly through hole corner (2,2) — the
            // spliced ring pinched itself and ear clipping deadlocked. SeamIsClear now
            // rejects seams through a vertex and bridges elsewhere.
            var floor = MakeFloor(out var go);
            try
            {
                floor.Build(P(0, 0), new Vector3(6f, 0f, 6f), 0f, Thick, 5f, 0f, 0f);
                Assert.IsTrue(floor.AddHole(new List<Vector3> { P(2, 2), P(3, 2), P(3, 3), P(2, 3) }));
                Assert.AreEqual(35f, floor.Area, 1e-2f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AddHole_SwallowingAnExistingHole_IsRefused()
        {
            var floor = MakeFloor(out var go);
            try
            {
                floor.Build(P(0, 0), new Vector3(6f, 0f, 6f), 0f, Thick, 5f, 0f, 0f);
                Assert.IsTrue(floor.AddHole(new List<Vector3> { P(2, 2), P(3, 2), P(3, 3), P(2, 3) }));

                // No edges cross, but the new ring contains the old hole entirely.
                var swallowing = new List<Vector3> { P(1, 1), P(4, 1), P(4, 4), P(1, 4) };
                Assert.IsFalse(floor.AddHole(swallowing));
                Assert.AreEqual(1, floor.Holes.Count);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AddHole_DisjointSecondHole_StillWorks()
        {
            var floor = MakeFloor(out var go);
            try
            {
                floor.Build(P(0, 0), new Vector3(8f, 0f, 4f), 0f, Thick, 5f, 0f, 0f);
                Assert.IsTrue(floor.AddHole(new List<Vector3> { P(1, 1), P(3, 1), P(3, 3), P(1, 3) }));
                Assert.IsTrue(floor.AddHole(new List<Vector3> { P(5, 1), P(7, 1), P(7, 3), P(5, 3) }),
                    "the overlap guard must not block honest disjoint holes");
                Assert.AreEqual(2, floor.Holes.Count);
                Assert.AreEqual(32f - 8f, floor.Area, 1e-2f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AddHole_RefusesARingThatPokesOutside()
        {
            var floor = MakeFloor(out var go);
            try
            {
                floor.Build(P(0, 0), new Vector3(4f, 0f, 3f), 0f, Thick, 5f, 0f, 0f);
                var sticksOut = new List<Vector3> { P(3, 1), P(9, 1), P(9, 2), P(3, 2) };

                Assert.IsFalse(floor.AddHole(sticksOut), "a hole through the edge is a notch, not a hole");
                Assert.AreEqual(0, floor.Holes.Count);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Holes_SurviveEditsAndMoveWithTheSlab()
        {
            var floor = MakeFloor(out var go);
            try
            {
                floor.Build(P(0, 0), new Vector3(6f, 0f, 4f), 0f, Thick, 5f, 0f, 0f);
                floor.AddHole(new List<Vector3> { P(2, 1), P(4, 1), P(4, 3), P(2, 3) });

                floor.SetThickness(0.3f);
                floor.SetPlanPlacement(3f, 15f, 1f, 1f);
                Assert.AreEqual(1, floor.Holes.Count, "edits must not silently drop the hole");

                floor.MoveBy(new Vector3(10f, 0f, 0f));
                Assert.AreEqual(1, floor.Holes.Count);
                Assert.AreEqual(12f, floor.Holes[0][0].x, 1e-3f, "the hole travels with the slab");
                Assert.AreEqual(20f, floor.Area, 1e-2f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void RemoveHole_RestoresTheSolidSlab()
        {
            var floor = MakeFloor(out var go);
            try
            {
                floor.Build(P(0, 0), new Vector3(6f, 0f, 4f), 0f, Thick, 5f, 0f, 0f);
                floor.AddHole(new List<Vector3> { P(2, 1), P(4, 1), P(4, 3), P(2, 3) });

                floor.RemoveHole(0);

                Assert.AreEqual(0, floor.Holes.Count);
                Assert.AreEqual(24f, floor.Area, 1e-2f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Rebuild_KeepsTheOutline()
        {
            var floor = MakeFloor(out var go);
            try
            {
                floor.BuildOutline(LShape(), 0f, Thick, 5f, 0f, 0f, 0f);
                var before = new List<Vector3>(floor.Outline);

                floor.Rebuild();

                CollectionAssert.AreEqual(before, floor.Outline);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
