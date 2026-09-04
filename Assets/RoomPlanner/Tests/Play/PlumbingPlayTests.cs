using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core;
using RoomPlanner.Core.Project;
using RoomPlanner.Editing;
using RoomPlanner.Import;
using RoomPlanner.Tools;
using RoomPlanner.Floors;
using RoomPlanner.Measure;

namespace RoomPlanner.Tests.Play
{
    public class PlumbingPlayTests
    {
        private class InputProbe : MeasureInput
        {
            public bool Pressed, Clear;
            public override bool ConfirmPressed()=>Pressed;
            public override bool ClearPressed()=>Clear;
            public override void Pulse(float amplitude=.5f,float duration=.06f){}
        }
        private class PointerProbe : PointerProvider
        {
            public Ray Ray;
            public override Ray GetRay()=>Ray;
        }
        private static void SetField(object target,string name,object value)=>target.GetType()
            .GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        private readonly List<GameObject> _objects=new();
        private SceneModel _model;
        private PlumbingController _tool;
        private Floor _floor;

        private GameObject Track(GameObject go){_objects.Add(go);return go;}
        [SetUp]
        public void Setup()
        {
            var rig=Track(new GameObject("Plumbing test"));_model=rig.AddComponent<SceneModel>();_tool=rig.AddComponent<PlumbingController>();
            _floor=Track(new GameObject("Floor"){layer=6}).AddComponent<Floor>();
            _floor.BuildOutline(new List<Vector3>{new(-2,0,-2),new(2,0,-2),new(2,0,2),new(-2,0,2)},null,0,.2f,5,0,0,0);
            _model.Register(_floor.gameObject.AddComponent<Selectable>());
            Physics.SyncTransforms();
        }
        [TearDown]
        public void Cleanup(){foreach(var go in _objects)if(go!=null)Object.DestroyImmediate(go);_objects.Clear();}

        private PlumbingObject Fixture()
        {
            var data=PlumbingCatalog.Create(PlumbingKind.Toilet,PlumbingCatalog.Size(PlumbingKind.Toilet),WaterSystem.Cold);
            data.Position=Vector3.up*ServicePlacement.VisualOffset;data.MountKey=MountIdentity.GetOrCreate(_floor.gameObject);
            var view=_tool.RestoreFixture(data);Track(view.gameObject);return view;
        }

        [Test]
        public void CreationSelectionDimensionsAndUndoUseRealSceneComponents()
        {
            var view=Fixture();var selectable=view.GetComponent<Selectable>();
            Assert.AreEqual(SelectableKind.Plumbing,selectable.Kind);
            Assert.That(selectable.Describe(),Does.Contain("Toilet"));
            Assert.AreEqual(_floor.gameObject,view.MountHost);
            var width=view.GetSettings().ActivePage().Fields.Single(f=>f.Id=="width");
            float before=view.Fixture.Size.x;width.CommitNumber(before,.5f);
            Assert.AreEqual(.5f,view.Fixture.Size.x);
            _model.History.Undo();Assert.AreEqual(before,view.Fixture.Size.x);
            _model.History.Redo();Assert.AreEqual(.5f,view.Fixture.Size.x);
            Assert.IsFalse(selectable.IsHidden,"restoring a project does not create user history");
        }

        [Test]
        public void PipeConnectionsSurviveCaptureAndDisconnectIsUndoable()
        {
            var fixture=Fixture();
            var data=new PipeRouteData{StartId=fixture.Id,StartPort=0,System=WaterSystem.Cold,
                Dimensions=new PipeDimensions{OuterDiameter=.02f,WallThickness=.002f},
                Points=new List<Vector3>{fixture.PortWorld(0),fixture.PortWorld(0)+Vector3.right}};
            var pipe=_tool.RestorePipe(data);Track(pipe.gameObject);
            Assert.IsTrue(fixture.HasConnections());Assert.IsFalse(fixture.CanMove(Vector3.right));
            var captured=ProjectStore.Capture(null,null);
            Assert.AreEqual(1,captured.PlumbingFixtures.Count);Assert.AreEqual(1,captured.PipeRoutes.Count);
            Assert.AreEqual(fixture.Id,captured.PipeRoutes[0].StartId);
            fixture.Disconnect();Assert.IsTrue(string.IsNullOrEmpty(pipe.Pipe.StartId));
            _model.History.Undo();Assert.AreEqual(fixture.Id,pipe.Pipe.StartId);
            Assert.AreEqual(fixture.Id,captured.PipeRoutes[0].StartId,"capture must own its snapshot");
        }

        [Test]
        public void UnknownSizeAndDegeneratePipeDataAreRejectedBeforeRegistration()
        {
            int count=_model.Items.Count;
            Assert.IsNull(_tool.RestorePipe(new PipeRouteData()));
            Assert.IsNull(_tool.RestoreFixture(new PlumbingFixtureData()));
            Assert.AreEqual(count,_model.Items.Count);
        }

        [UnityTest]
        public IEnumerator BoundFixtureFollowsFloorAndDimensionsRemainRelative()
        {
            var fixture=Fixture();yield return null;
            _floor.MoveBy(Vector3.up);yield return null;
            Assert.That(fixture.transform.position.y,Is.EqualTo(1f+ServicePlacement.VisualOffset).Within(1e-4));
            Assert.That(fixture.Fixture.BaseLevel,Is.EqualTo(1f).Within(1e-4));
        }

        [Test]
        public void RealToolPlacesOnFloorBlocksUiAndFinishesAPipe()
        {
            var input=_tool.gameObject.AddComponent<InputProbe>();var pointer=_tool.gameObject.AddComponent<PointerProbe>();
            SetField(_tool,"input",input);SetField(_tool,"pointer",pointer);
            SetField(_tool,"raycaster",_tool.gameObject.AddComponent<SceneRaycaster>());SetField(_tool,"sceneModel",_model);
            var schema=_tool.GetSettings();schema.ActivePage().Fields.Single(f=>f.Id=="kind").SetIndex((int)PlumbingKind.Toilet);
            pointer.Ray=new Ray(new Vector3(0,2,0),Vector3.down);input.Pressed=true;
            _tool.Tick(true);Assert.AreEqual(1,_model.Items.Count,"panel blocks creation");
            _tool.Tick(false);
            var fixture=_model.Items.OfType<Selectable>().Single(s=>s.Kind==SelectableKind.Plumbing).GetComponent<PlumbingObject>();Track(fixture.gameObject);
            Assert.That(fixture.transform.position.y,Is.EqualTo(ServicePlacement.VisualOffset).Within(1e-5));
            schema.SelectTab(1);
            pointer.Ray=new Ray(new Vector3(1,2,0),Vector3.down);SetField(_tool,"_nextClick",0f);_tool.Tick(false);
            pointer.Ray=new Ray(new Vector3(1,2,1),Vector3.down);SetField(_tool,"_nextClick",0f);_tool.Tick(false);
            input.Pressed=false;input.Clear=true;_tool.Tick(false);
            var pipe=_model.Items.OfType<Selectable>().Select(s=>s.GetComponent<PlumbingObject>()).Single(p=>p!=null&&p.IsPipe);Track(pipe.gameObject);
            Assert.That(RoomPlanner.Electrical.WireMath.PolylineLength(pipe.Pipe.Points),Is.EqualTo(1f).Within(1e-4));
            Assert.AreEqual(WaterSystem.Cold,pipe.Pipe.System);
            _model.History.Undo();Assert.IsTrue(pipe.GetComponent<Selectable>().IsHidden);
            _model.History.Undo();Assert.IsTrue(fixture.GetComponent<Selectable>().IsHidden);
            _model.History.Redo();Assert.IsFalse(fixture.GetComponent<Selectable>().IsHidden);
            _model.History.Redo();Assert.IsFalse(pipe.GetComponent<Selectable>().IsHidden);
        }
    }
}
