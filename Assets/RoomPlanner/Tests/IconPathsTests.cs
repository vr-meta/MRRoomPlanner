using System;
using NUnit.Framework;
using RoomPlanner.Core;
using RoomPlanner.Tools;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Contract tests for the whole icon set (design/20 §6.1): every icon must parse,
    /// tessellate, face the viewer and stay inside the triangle budget. A new icon added
    /// to <see cref="IconPaths.All"/> is covered automatically.
    /// </summary>
    public class IconPathsTests
    {
        private const int TriangleBudget = 200;

        [Test]
        public void EveryIcon_ParsesIntoRings()
        {
            foreach (var (id, path) in IconPaths.All)
            {
                var rings = SvgPath.Parse(path);
                Assert.Greater(rings.Count, 0, $"icon '{id}' failed to parse");
                foreach (var ring in rings)
                    Assert.GreaterOrEqual(ring.Count, 3, $"icon '{id}' has a degenerate ring");
            }
        }

        [Test]
        public void EveryIcon_StaysInViewBox()
        {
            foreach (var (id, path) in IconPaths.All)
                foreach (var ring in SvgPath.Parse(path))
                    foreach (var p in ring)
                    {
                        Assert.That(p.x, Is.InRange(-0.01f, 24.01f), $"icon '{id}' leaves the viewBox in x");
                        Assert.That(p.y, Is.InRange(-0.01f, 24.01f), $"icon '{id}' leaves the viewBox in y");
                    }
        }

        [Test]
        public void EveryIcon_TessellatesWithinBudget()
        {
            foreach (var (id, path) in IconPaths.All)
            {
                var mesh = SvgTessellator.Build(path);
                Assert.IsFalse(mesh.IsEmpty, $"icon '{id}' failed to tessellate");
                Assert.LessOrEqual(mesh.Triangles.Length / 3, TriangleBudget,
                    $"icon '{id}' exceeds the {TriangleBudget}-triangle budget");
                Assert.Greater(SvgTessellator.Area(mesh), 0.05f,
                    $"icon '{id}' is suspiciously empty — a hole probably ate its body");
            }
        }

        [Test]
        public void EveryIcon_FacesTheViewer()
        {
            foreach (var (id, path) in IconPaths.All)
            {
                var mesh = SvgTessellator.Build(path);
                for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
                {
                    Vector3 a = mesh.Vertices[mesh.Triangles[t]];
                    Vector3 b = mesh.Vertices[mesh.Triangles[t + 1]];
                    Vector3 c = mesh.Vertices[mesh.Triangles[t + 2]];
                    Assert.Less(Vector3.Cross(b - a, c - a).z, 0f,
                        $"icon '{id}' triangle {t / 3} faces away from the viewer");
                }
            }
        }

        [Test]
        public void ToolIcons_ExistForEveryRadialSlot()
        {
            // fixed compass layout (design/20 §1) — every named slot has art
            string[] slotIcons =
            {
                "select-cursor", "tape-measure", "wall", "floor-slab",
                "door-window", "furniture", "blueprint", "import-file",
                "electric-plug", "radiator", "pipe", "paint-roller",
            };
            Assert.AreEqual(RadialMath.Slots, slotIcons.Length);
            foreach (var id in slotIcons)
                Assert.IsTrue(IconPaths.All.ContainsKey(id), $"radial slot icon '{id}' missing");
        }

        [Test]
        public void RuntimeRadialDefinitions_MatchTheRegisteredToolInventory()
        {
            var definitions = ToolManager.CreateRadialDefinitions(ToolManager.DefaultToolIndex);
            Assert.AreEqual(RadialMath.Slots, definitions.Length);

            int reserved = 0;
            foreach (var definition in definitions)
            {
                Assert.IsTrue(IconPaths.All.ContainsKey(definition.IconId),
                    $"radial slot icon '{definition.IconId}' missing");
                if (definition.Reserved) reserved++;
            }

            Assert.AreEqual(0, reserved, "all twelve radial slots now have registered tools");
            foreach (string id in ToolManager.RegisteredToolIds)
                Assert.GreaterOrEqual(ToolManager.DefaultToolIndex(id), 0,
                    $"registered tool '{id}' has no stable index");

            Assert.IsFalse(Array.Find(definitions, d => d.Label == "Openings").Reserved);
            Assert.IsFalse(Array.Find(definitions, d => d.Label == "Projects").Reserved);
        }

        [Test]
        public void BlueprintIcon_NestedGrooveRegression()
        {
            // regression: the sheet's interior sample used to fall inside the groove ring,
            // flipping the whole sheet into a hole (empty-ish mesh); with the area guard the
            // island re-fills and the icon keeps most of its body
            var mesh = SvgTessellator.Build(IconPaths.Blueprint);
            // (16·18 − 10·10 + 5·5 − 6·1.3) / 576
            Assert.AreEqual((288f - 100f + 25f - 7.8f) / 576f, SvgTessellator.Area(mesh), 1e-3f);
        }
    }
}
