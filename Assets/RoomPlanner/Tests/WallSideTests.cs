using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Core;
using RoomPlanner.Walls;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Per-side wall finishes (issue #34): the body splits into inner (0) / outer (3) /
    /// rims (4) submeshes with glass (1) and joinery (2) untouched, every face keeps
    /// pointing outward, and SideOf maps a world point to the physical side in both
    /// build modes (legacy polyline + graph segment with either SideSign).
    /// </summary>
    public class WallSideTests
    {
        /// <summary>Area-weighted average facet normal of one submesh (Unity winding:
        /// Cross(b−a, c−a) points out of the front face).</summary>
        private static Vector3 AverageNormal(Mesh mesh, int submesh)
        {
            var verts = mesh.vertices;
            var tris = mesh.GetTriangles(submesh);
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < tris.Length; i += 3)
                sum += Vector3.Cross(verts[tris[i + 1]] - verts[tris[i]],
                                     verts[tris[i + 2]] - verts[tris[i]]);
            return sum.normalized;
        }

        [Test]
        public void PolylineWall_BodySplitsIntoSides_FacingOutward()
        {
            var go = new GameObject("Wall");
            try
            {
                var wall = go.AddComponent<Wall>();
                // along +X, the room (interior) at +Z → outer side faces −Z
                wall.Build(new List<Vector3> { Vector3.zero, new(4f, 0f, 0f) },
                    0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, new Vector3(2f, 0f, 1f));
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;

                Assert.AreEqual(5, mesh.subMeshCount, "inner/glass/joinery/outer/rims");
                Assert.Greater(mesh.GetTriangles(0).Length, 0, "inner side populated");
                Assert.AreEqual(0, mesh.GetTriangles(1).Length, "no glass without openings");
                Assert.AreEqual(0, mesh.GetTriangles(2).Length, "no joinery without openings");
                Assert.Greater(mesh.GetTriangles(3).Length, 0, "outer side populated");
                Assert.Greater(mesh.GetTriangles(4).Length, 0, "rims populated");

                Assert.Greater(AverageNormal(mesh, 0).z, 0.9f, "inner side faces the room (+Z)");
                Assert.Less(AverageNormal(mesh, 3).z, -0.9f, "outer side faces away (−Z)");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void PolylineWall_SideOf_SplitsAtTheCenterline()
        {
            var go = new GameObject("Wall");
            try
            {
                var wall = go.AddComponent<Wall>();
                wall.Build(new List<Vector3> { Vector3.zero, new(4f, 0f, 0f) },
                    0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, new Vector3(2f, 0f, 1f));

                Assert.AreEqual(WallSide.Inner, wall.SideOf(new Vector3(2f, 1f, 1f)));
                Assert.AreEqual(WallSide.Outer, wall.SideOf(new Vector3(2f, 1f, -1f)));
                // past the caps the nearest-segment projection still decides by side
                Assert.AreEqual(WallSide.Inner, wall.SideOf(new Vector3(5f, 1f, 0.3f)));
                Assert.AreEqual(WallSide.Outer, wall.SideOf(new Vector3(-1f, 1f, -0.3f)));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void GraphWall_SideOf_HonoursSideSign()
        {
            var go = new GameObject("Wall");
            try
            {
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                var wall = go.AddComponent<Wall>();
                var g = new WallGraph();
                var seg = g.AddSegment(
                    g.SnapOrCreateNode(Vector3.zero),
                    g.SnapOrCreateNode(new Vector3(4f, 0f, 0f)));
                seg.Thickness = 0.2f;
                seg.Height = 2.7f;

                // RightNormal(A→B along +X) = −Z; SideSign = +1 grows toward it
                seg.SideSign = 1f;
                wall.BuildSegment(seg);
                Assert.AreEqual(WallSide.Outer, wall.SideOf(new Vector3(2f, 1f, -1f)));
                Assert.AreEqual(WallSide.Inner, wall.SideOf(new Vector3(2f, 1f, 1f)));

                seg.SideSign = -1f;
                wall.BuildSegment(seg);
                Assert.AreEqual(WallSide.Inner, wall.SideOf(new Vector3(2f, 1f, -1f)));
                Assert.AreEqual(WallSide.Outer, wall.SideOf(new Vector3(2f, 1f, 1f)));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void GraphWallWithOpening_KeepsSidesApart_JambsInRims()
        {
            var go = new GameObject("Wall");
            try
            {
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                var wall = go.AddComponent<Wall>();
                var g = new WallGraph();
                var seg = g.AddSegment(
                    g.SnapOrCreateNode(Vector3.zero),
                    g.SnapOrCreateNode(new Vector3(4f, 0f, 0f)));
                seg.Thickness = 0.2f;
                seg.Height = 2.7f;
                seg.Openings.Add(new WallOpening
                {
                    AlongFraction = 0.5f, Width = 1.2f, SillHeight = 0.9f, Height = 1.4f,
                });
                wall.BuildSegment(seg);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;

                Assert.AreEqual(5, mesh.subMeshCount);
                Assert.Greater(mesh.GetTriangles(0).Length, 0, "inner piers/bands");
                Assert.Greater(mesh.GetTriangles(1).Length, 0, "window glass");
                Assert.Greater(mesh.GetTriangles(2).Length, 0, "window frame");
                Assert.Greater(mesh.GetTriangles(3).Length, 0, "outer piers/bands");
                Assert.Greater(mesh.GetTriangles(4).Length, 0, "top/bottom/caps/jambs");

                // sides keep facing their own way even when panelised
                float innerDot = Vector3.Dot(AverageNormal(mesh, 0), Vector3.forward);
                float outerDot = Vector3.Dot(AverageNormal(mesh, 3), Vector3.back);
                Assert.Greater(innerDot, 0.9f, "inner faces one way");
                Assert.Greater(outerDot, 0.9f, "outer faces the other");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
