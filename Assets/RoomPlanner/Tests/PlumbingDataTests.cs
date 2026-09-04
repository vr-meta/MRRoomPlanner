using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Core.Project;

namespace RoomPlanner.Tests
{
    public class PlumbingDataTests
    {
        [Test]
        public void TemplatesHaveEditablePortsAndMeshesMatchDeclaredDimensions()
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            for(int k=0;k<PlumbingCatalog.Names.Length;k++)
            {
                var kind=(PlumbingKind)k;var size=PlumbingCatalog.Size(kind);
                var data=PlumbingCatalog.Create(kind,size,WaterSystem.Cold);
                Assert.IsTrue(PlumbingCatalog.ValidFixture(data));
                Assert.IsFalse(data.Ports[0].Confirmed,"template coordinates must not masquerade as measured data");
                PlumbingCatalog.BuildMesh(kind,size,vertices,triangles);
                var bounds=new Bounds(vertices[0],Vector3.zero);
                foreach(var vertex in vertices)bounds.Encapsulate(vertex);
                Assert.That(Vector3.Distance(bounds.size,size),Is.LessThan(1e-5),kind.ToString());
                float volume=0;
                for(int i=0;i<triangles.Count;i+=3)
                    volume+=Vector3.Dot(vertices[triangles[i]],Vector3.Cross(vertices[triangles[i+1]],vertices[triangles[i+2]]))/6;
                Assert.Greater(volume,0,"closed parts must wind outward");
            }
        }

        [Test]
        public void SystemDimensionsAndConfirmationAllParticipateInCompatibility()
        {
            var pipe=new PipeDimensions{OuterDiameter=.02f,WallThickness=.002f};
            var a=new PlumbingPort{System=WaterSystem.Cold,Diameter=.02f,Confirmed=true};
            var b=new PlumbingPort{System=WaterSystem.Hot,Diameter=.02f,Confirmed=true};
            Assert.IsFalse(PlumbingCatalog.Compatible(a,b,pipe));
            b.System=WaterSystem.Cold;b.Confirmed=false;Assert.IsFalse(PlumbingCatalog.Compatible(a,b,pipe));
            b.Confirmed=true;b.Diameter=.025f;Assert.IsFalse(PlumbingCatalog.Compatible(a,b,pipe));
            b.Diameter=.02f;Assert.IsTrue(PlumbingCatalog.Compatible(a,b,pipe));
        }

        [Test]
        public void WasteChecksEachSectionRatherThanOnlyTotalFall()
        {
            var route=new PipeRouteData{System=WaterSystem.Waste,SlopePercent=2,
                Dimensions=new PipeDimensions{OuterDiameter=.05f,WallThickness=.003f},
                Points=new List<Vector3>{new(0,1,0),new(1,.98f,0),new(2,.96f,0)}};
            Assert.IsTrue(PlumbingCatalog.ValidPipe(route));
            route.Points[1]=new Vector3(1,1.1f,0);Assert.IsFalse(PlumbingCatalog.ValidPipe(route));
            route.Points[1]=new Vector3(0,.5f,0);route.Points[2]=new Vector3(1,.48f,0);
            Assert.IsTrue(PlumbingCatalog.ValidPipe(route),"explicit vertical drop followed by sloped section");
        }

        [Test]
        public void VersionSixKeepsLegacyMeshesAndNativePlumbingInSeparateCollections()
        {
            var data=new ProjectData();data.Plumbing.Add(new ProjectMep{Name="Legacy IFC basin"});
            var fixture=PlumbingCatalog.Create(PlumbingKind.Basin,PlumbingCatalog.Size(PlumbingKind.Basin),WaterSystem.Cold);
            fixture.Id="basin";fixture.MountKey="wall-key";fixture.ShowDimensions=true;
            data.PlumbingFixtures.Add(fixture);
            var round=JsonUtility.FromJson<ProjectData>(JsonUtility.ToJson(data));
            Assert.AreEqual(6,round.Version);Assert.AreEqual(1,round.Plumbing.Count);Assert.AreEqual(1,round.PlumbingFixtures.Count);
            Assert.AreEqual("wall-key",round.PlumbingFixtures[0].MountKey);Assert.IsTrue(round.PlumbingFixtures[0].ShowDimensions);
            Assert.AreEqual(3,round.PlumbingFixtures[0].Ports.Count);
            var legacy=JsonUtility.FromJson<ProjectData>("{\"Version\":5,\"Plumbing\":[{\"Name\":\"Old fixture\"}]}");
            Assert.AreEqual("Old fixture",legacy.Plumbing[0].Name);
        }
    }
}
