using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests
{
    /// <summary>Edge-lines overlay is hidden by default (outlined walls stood out
    /// against floors/stairs — headset feedback 2026-08-13) and follows the global
    /// MeshShading.ShowEdges flag flipped by the Rendering-page toggle.</summary>
    public class WallEdgesVisibilityTests
    {
        private bool _saved;

        [SetUp]
        public void Save()
        {
            _saved = MeshShading.ShowEdges;
            MeshShading.ShowEdges = false;
        }

        [TearDown]
        public void Restore() => MeshShading.ShowEdges = _saved;

        private static void BuildTwoPoint(Wall wall) =>
            wall.Build(new List<Vector3> { Vector3.zero, new Vector3(1f, 0f, 0f) },
                0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, new Vector3(0.5f, 0f, -1f));

        [Test]
        public void EdgesRenderer_FollowsGlobalFlag()
        {
            var go = new GameObject("Wall");
            try
            {
                var wall = go.AddComponent<Wall>();
                var edges = new GameObject("Edges");
                edges.transform.SetParent(go.transform, false);
                var filter = edges.AddComponent<MeshFilter>();
                var renderer = edges.AddComponent<MeshRenderer>();
                // the setup wires this serialized field on the prefab (SetupWallTool)
                typeof(Wall).GetField("edgesFilter", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(wall, filter);

                BuildTwoPoint(wall);
                Assert.IsFalse(renderer.enabled, "edges hidden by default");

                MeshShading.ShowEdges = true;
                wall.RefreshEdgesVisibility();
                Assert.IsTrue(renderer.enabled, "toggle shows edges on a live wall");

                MeshShading.ShowEdges = false;
                BuildTwoPoint(wall);
                Assert.IsFalse(renderer.enabled, "rebuild re-applies the flag");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void WallWithoutEdgesChild_DoesNotThrow()
        {
            var go = new GameObject("Wall");
            try
            {
                var wall = go.AddComponent<Wall>();
                BuildTwoPoint(wall);
                wall.RefreshEdgesVisibility();   // edgesFilter is null — must be a no-op
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
