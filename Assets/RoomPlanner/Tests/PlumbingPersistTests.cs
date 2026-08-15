using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Core.Project;
using RoomPlanner.Plumbing;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>Format v5 (design/28): the plumbing layer must survive the JSON
    /// round-trip with ids intact — pipe ends re-attach by them on load.</summary>
    public class PlumbingPersistTests
    {
        [Test]
        public void PlumbingSections_RoundTripThroughJson()
        {
            var data = new ProjectData();
            data.PlumbFixtures.Add(new ProjectPlumbFixture
            {
                Id = "fx-1",
                Kind = (int)PlumbFixtureKind.ToiletOutlet,
                Angle = (int)OutletAngle.Deg45,
                Position = new Vector3(1f, 0.18f, 2f),
                Rotation = Quaternion.LookRotation(Vector3.forward),
                BaseLevel = -3f,
            });
            data.Pipes.Add(new ProjectPipe
            {
                Id = "riser-1",
                Points = new List<Vector3> { new(0f, 0f, 0f), new(0f, 2.7f, 0f) },
                Diameter = (int)PipeDiameter.D110,
                IsRiser = true,
                Reserve = 15,
            });
            data.Pipes.Add(new ProjectPipe
            {
                Id = "pipe-2",
                Points = new List<Vector3> { new(0f, 0.1f, 0f), new(2f, 0.1f, 0f), new(2f, 0.1f, 1f) },
                Diameter = (int)PipeDiameter.D50,
                StartId = "riser-1",
                EndId = "fx-1",
            });

            var back = ProjectData.FromJson(data.ToJson());
            Assert.IsNotNull(back);
            Assert.AreEqual(ProjectData.CurrentVersion, back.Version);

            Assert.AreEqual(1, back.PlumbFixtures.Count);
            var f = back.PlumbFixtures[0];
            Assert.AreEqual("fx-1", f.Id);
            Assert.AreEqual((int)PlumbFixtureKind.ToiletOutlet, f.Kind);
            Assert.AreEqual((int)OutletAngle.Deg45, f.Angle);
            Assert.AreEqual(-3f, f.BaseLevel, 1e-5);

            Assert.AreEqual(2, back.Pipes.Count);
            Assert.IsTrue(back.Pipes[0].IsRiser);
            Assert.AreEqual(15, back.Pipes[0].Reserve);
            Assert.AreEqual(3, back.Pipes[1].Points.Count);
            Assert.AreEqual("riser-1", back.Pipes[1].StartId);
            Assert.AreEqual("fx-1", back.Pipes[1].EndId);
        }

        [Test]
        public void OlderFile_LoadsWithEmptyPlumbing()
        {
            // a v4 file (furniture era) carries no plumbing sections at all
            var back = ProjectData.FromJson("{\"Version\":4}");
            Assert.IsNotNull(back);
            Assert.AreEqual(0, back.PlumbFixtures.Count);
            Assert.AreEqual(0, back.Pipes.Count);
        }

        [Test]
        public void NewerFile_IsRefused()
        {
            Assert.IsNull(ProjectData.FromJson($"{{\"Version\":{ProjectData.CurrentVersion + 1}}}"),
                "partially reading a newer format would silently drop its data (audit Б2)");
        }
    }
}
