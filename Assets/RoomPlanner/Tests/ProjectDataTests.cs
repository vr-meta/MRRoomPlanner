using NUnit.Framework;
using RoomPlanner.Core.Project;
using UnityEngine;

namespace RoomPlanner.Tests
{
    public class ProjectDataTests
    {
        [Test]
        public void JsonRoundTrip_KeepsEverything()
        {
            var data = new ProjectData { PlanScale = 7.5f, PlanRotationDeg = 90f, PlanOffsetX = 1.5f };
            data.Nodes.Add(new ProjectNode { Position = new Vector3(1f, 0f, 2f) });
            data.Nodes.Add(new ProjectNode { Position = new Vector3(4f, 0f, 2f) });
            var wall = new ProjectWall
            {
                NodeA = 0, NodeB = 1, Thickness = 0.15f, Height = 2.7f,
                SideSign = -1f, Offset = 1, Join = 2,
            };
            wall.Openings.Add(new ProjectOpening { Along = 0.4f, Width = 0.9f, Height = 2.1f, Sill = 0f, IsDoor = true });
            data.Walls.Add(wall);
            var floor = new ProjectFloor { Level = 0f, Thickness = 0.2f };
            floor.Outline.AddRange(new[]
                { new Vector3(0f, 0f, 0f), new Vector3(5f, 0f, 0f), new Vector3(5f, 0f, 5f) });
            floor.Holes.Add(new ProjectRing { Points = { new Vector3(1f, 0f, 1f), new Vector3(2f, 0f, 1f), new Vector3(2f, 0f, 2f) } });
            data.Floors.Add(floor);
            data.Stairs.Add(new ProjectStair
                { Base = new Vector3(3f, 0f, 3f), Yaw = 45f, Width = 1.2f, Risers = 13, RiserHeight = 0.175f, TreadDepth = 0.275f, Open = true, Kind = 2 });
            var mep = new ProjectMep { Name = "Basin", Origin = new Vector3(4f, 0.8f, 4f) };
            mep.Vertices.Add(new Vector3(0.1f, 0.2f, 0.3f));
            mep.Triangles.AddRange(new[] { 0, 0, 0 });
            data.Plumbing.Add(mep);

            var round = ProjectData.FromJson(data.ToJson());

            Assert.AreEqual(1, round.Version);
            Assert.AreEqual(2, round.Nodes.Count);
            Assert.AreEqual(new Vector3(4f, 0f, 2f), round.Nodes[1].Position);
            var w = round.Walls[0];
            Assert.AreEqual(0.15f, w.Thickness, 1e-6);
            Assert.AreEqual(-1f, w.SideSign, 1e-6);
            Assert.AreEqual(2, w.Join);
            Assert.AreEqual(0.9f, w.Openings[0].Width, 1e-6);
            Assert.IsTrue(w.Openings[0].IsDoor, "door/window type survives");
            Assert.AreEqual(3, round.Floors[0].Outline.Count);
            Assert.AreEqual(3, round.Floors[0].Holes[0].Points.Count, "nested rings survive");
            Assert.IsTrue(round.Stairs[0].Open);
            Assert.AreEqual(2, round.Stairs[0].Kind, "stair kind int survives JSON");
            Assert.AreEqual(13, round.Stairs[0].Risers);
            Assert.AreEqual("Basin", round.Plumbing[0].Name);
            Assert.AreEqual(new Vector3(0.1f, 0.2f, 0.3f), round.Plumbing[0].Vertices[0]);
            Assert.AreEqual(7.5f, round.PlanScale, 1e-6);
        }
    }
}
