using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Stairs;

namespace RoomPlanner.Tests.Play
{
    /// <summary>Stairs get their first UI (audit F2): per-instance rows, one undoable
    /// command per commit, kind switchable — imported flights are no longer locked.</summary>
    public class StairParametersPlayTests
    {
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private (StairParameters p, Stair s, SceneModel model) MakeStair()
        {
            var rig = new GameObject("Rig");
            _spawned.Add(rig);
            var model = rig.AddComponent<SceneModel>();

            var go = new GameObject("Stair");
            _spawned.Add(go);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            var s = go.AddComponent<Stair>();
            s.Build(Vector3.zero, 0f, 1f, 15, 0.2f, 0.25f, StairKind.Waist);
            var p = go.AddComponent<StairParameters>();
            go.AddComponent<Selectable>();
            model.Register(go.GetComponent<Selectable>());
            return (p, s, model);
        }

        [Test]
        public void Schema_CarriesTheFlightRows_AndLiveTotal()
        {
            var (p, s, _) = MakeStair();
            var schema = p.GetSettings();
            var ids = new List<string>();
            foreach (var f in schema.Fields) ids.Add(f.Id);
            CollectionAssert.AreEqual(new[] { "srs", "srh", "std", "swd", "skind", "stot" }, ids);
            StringAssert.Contains("300 cm up", schema.Fields[5].Value(), "15 × 20 cm = 3 m");
        }

        [Test]
        public void CommittingSteps_RebuildsTheFlight_AndIsUndoable()
        {
            var (p, s, model) = MakeStair();
            var schema = p.GetSettings();

            schema.Fields[0].CommitNumber(15f, 10f);
            Assert.AreEqual(10, s.Risers, "the flight really rebuilt");
            Assert.AreEqual(2.0f, s.TotalHeight, 1e-4f);

            model.History.Undo();
            Assert.AreEqual(15, s.Risers, "undo restores the exact previous shape");
            Assert.AreEqual(0.25f, s.TreadDepth, 1e-4f, "untouched fields survive the round-trip");
        }

        [Test]
        public void SwitchingKind_IsUndoable()
        {
            var (p, s, model) = MakeStair();
            var schema = p.GetSettings();

            schema.Fields[4].SetIndex((int)StairKind.Open);
            Assert.AreEqual(StairKind.Open, s.Kind);
            model.History.Undo();
            Assert.AreEqual(StairKind.Waist, s.Kind);
        }
    }
}
