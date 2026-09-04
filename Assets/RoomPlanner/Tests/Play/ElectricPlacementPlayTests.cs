using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Walls;
using RoomPlanner.Electrical;
using RoomPlanner.Editing;

namespace RoomPlanner.Tests.Play
{
    public class ElectricPlacementPlayTests
    {
        private readonly List<GameObject> _objects = new();
        private SceneModel _model;
        private Wall _wall;
        private readonly ElectricPlacement _placement = new();

        private GameObject Object(string name)
        {
            var go = new GameObject(name) { layer = 6 }; _objects.Add(go); return go;
        }

        [SetUp]
        public void Setup()
        {
            _model = Object("Model").AddComponent<SceneModel>();
            var graph = new WallGraph();
            var a = graph.CreateNode(Vector3.zero);
            var b = graph.CreateNode(new Vector3(4,0,0));
            var s = graph.AddSegment(a,b); s.BaseHeight = 3;
            s.Openings.Add(new WallOpening { AlongFraction = .5f, Width = .6f, SillHeight = .2f, Height = 1f });
            _wall = Object("Wall").AddComponent<Wall>(); _wall.BuildSegment(s);
            _model.Register(_wall.gameObject.AddComponent<Selectable>());
            Physics.SyncTransforms();
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _objects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _objects.Clear();
        }

        [Test]
        public void FinalPresetHeightAndFullFootprintAreValidated()
        {
            Assert.AreEqual(PlacementFailure.None, _placement.Validate(_wall.gameObject,
                new Vector3(1,3.3f,.002f), Quaternion.identity, FixtureKind.Outlet, 1, _model));
            Assert.AreEqual(PlacementFailure.Opening, _placement.Validate(_wall.gameObject,
                new Vector3(2,3.3f,.002f), Quaternion.identity, FixtureKind.Outlet, 1, _model));
            Assert.AreEqual(PlacementFailure.OutsideSurface, _placement.Validate(_wall.gameObject,
                new Vector3(.1f,3.3f,.002f), Quaternion.identity, FixtureKind.Outlet, 5, _model));
        }

        [Test]
        public void FurnitureSideIsNotAWall()
        {
            var box = Object("Furniture"); box.AddComponent<BoxCollider>(); box.AddComponent<Selectable>();
            Assert.AreEqual(PlacementFailure.WrongSurface, _placement.Validate(box,
                new Vector3(1,3.3f,.002f), Quaternion.identity, FixtureKind.Outlet, 1, _model));
        }

        [Test]
        public void ExactGapIsUndoableAndInvalidHeightDoesNotEnterHistory()
        {
            var go = Object("Outlet"); var fx = go.AddComponent<ElectricFixture>();
            fx.Build(FixtureKind.Outlet,1,1); fx.BaseLevel = 3; fx.MountHost = _wall.gameObject;
            go.transform.position = new Vector3(1,4.5f,.002f);
            var settings = go.AddComponent<ElectricFixtureParameters>();
            _model.Register(go.AddComponent<Selectable>());
            var schema = settings.GetSettings();
            int before = _model.History.UndoCount;
            var gap = schema.TabPages[1].Fields.Single(f => f.Id == "gap");
            gap.CommitNumber(0,.15f);
            Assert.That(go.transform.position.x, Is.EqualTo(.19f).Within(1e-5));
            Assert.AreEqual(before + 1, _model.History.UndoCount);
            _model.History.Undo();
            Assert.That(go.transform.position.x, Is.EqualTo(1f).Within(1e-5));
            go.transform.position = new Vector3(2,4.5f,.002f);
            before = _model.History.UndoCount;
            schema.ActivePage().Fields.Single(f => f.Id == "fh").CommitNumber(1.5f,.3f);
            Assert.AreEqual(before, _model.History.UndoCount);
            Assert.That(go.transform.position.y, Is.EqualTo(4.5f).Within(1e-5));
        }
    }
}
