using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests.Play
{
    /// <summary>Openings tool v1 (design/03, audit F1): the tabbed schema and the
    /// create/delete command round-trip on a real wall view. Aim/тригger interaction
    /// needs a headset — device check stays on the checklist.</summary>
    public class OpeningsToolPlayTests
    {
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private (WallGraphRenderer walls, SceneModel model, WallSegment seg) MakeWallRig()
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
            var seg = walls.Graph.AddSegment(
                walls.Graph.SnapOrCreateNode(Vector3.zero),
                walls.Graph.SnapOrCreateNode(new Vector3(4f, 0f, 0f)));
            seg.Thickness = 0.2f;
            seg.Height = 2.7f;
            walls.Sync();
            return (walls, model, seg);
        }

        [Test]
        public void Schema_ThreeTabs_WithNumericSizes()
        {
            var go = new GameObject("Openings");
            _spawned.Add(go);
            var tool = go.AddComponent<OpeningsController>();

            var s = tool.GetSettings();
            Assert.AreSame(s, tool.GetSettings(), "one tabbed root instance");
            Assert.IsTrue(s.HasTabs);
            CollectionAssert.AreEqual(new[] { "Door", "Window", "Garage" }, s.Tabs);
            Assert.AreEqual("openings", tool.Id);
            Assert.AreEqual("door-window", tool.IconId);

            s.SelectTab(1);
            bool hasSill = false;
            foreach (var f in s.ActivePage().Fields) if (f.Id == "sill") hasSill = true;
            Assert.IsTrue(hasSill, "only the window page carries a sill row");
        }

        [UnityTest]
        public IEnumerator CreateAndDeleteCommands_RoundTripOnARealWall()
        {
            var (walls, model, seg) = MakeWallRig();
            yield return null;

            var view = walls.ViewOf(seg);
            var target = view.GetComponent<Selectable>();
            int solidVerts = view.GetComponent<MeshFilter>().sharedMesh.vertexCount;

            var opening = new WallOpening
            {
                Id = 1001, AlongFraction = 0.5f, Width = 0.85f, Height = 2.1f,
                Kind = OpeningKind.Door,
            };
            model.History.Execute(new CreateOpeningCommand(walls, seg, opening, target));
            Assert.AreEqual(1, seg.Openings.Count);
            Assert.Greater(view.GetComponent<MeshFilter>().sharedMesh.vertexCount, solidVerts,
                "panelisation really ran — piers/header/frame carry more verts");

            model.History.Undo();
            Assert.AreEqual(0, seg.Openings.Count, "undo takes the doorway back out");
            Assert.AreEqual(solidVerts, view.GetComponent<MeshFilter>().sharedMesh.vertexCount);

            model.History.Redo();
            Assert.AreEqual(1, seg.Openings.Count);

            model.History.Execute(new DeleteOpeningCommand(walls, seg, opening, target));
            Assert.AreEqual(0, seg.Openings.Count, "B near the opening deletes it");
            model.History.Undo();
            Assert.AreEqual(1, seg.Openings.Count, "and undo puts the SAME opening back");
            Assert.AreSame(opening, seg.Openings[0]);
        }
    }
}
