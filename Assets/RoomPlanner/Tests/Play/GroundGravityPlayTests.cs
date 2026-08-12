using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Floors;
using RoomPlanner.Tools;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Ground datum and gravity on real components (design/26-ground.md, #59, reworked
    /// for #65): the ground derives from the live model, the feet are the FIXED Y = 0
    /// datum — settles always move the MODEL to the feet, the rig is never moved (and a
    /// drifted rig gets snapped back), and walking is refused into ledges taller than
    /// a step.
    /// </summary>
    public class GroundGravityPlayTests
    {
        private readonly List<GameObject> _spawned = new();

        private GameObject Track(GameObject go)
        {
            _spawned.Add(go);
            return go;
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        /// <summary>A square slab of side 2·half centred on the origin, at the given level.</summary>
        private Floor Slab(float level, float half = 5f)
        {
            var go = Track(new GameObject($"Slab@{level}"));
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            var f = go.AddComponent<Floor>();
            f.BuildOutline(new List<Vector3>
            {
                new(-half, 0f, -half), new(half, 0f, -half),
                new(half, 0f, half), new(-half, 0f, half),
            }, level, 0.2f, 5f, 0f, 0f, 0f);
            return f;
        }

        private (GroundService ground, Transform rig) MakeRig(float rigY, float headX = 0f, float headZ = 0f)
        {
            var camGo = Track(new GameObject("TestCam"));
            camGo.tag = "MainCamera";
            camGo.AddComponent<Camera>();
            camGo.transform.position = new Vector3(headX, rigY + 1.7f, headZ);

            var rigGo = Track(new GameObject("TestRig"));
            rigGo.transform.position = new Vector3(0f, rigY, 0f);
            var ground = rigGo.AddComponent<GroundService>();
            ground.RigOverride = rigGo.transform;
            return (ground, rigGo.transform);
        }

        [UnityTest]
        public IEnumerator GroundY_DerivesFromTheLowestSlab()
        {
            Slab(3f);
            Slab(0f);
            var (ground, _) = MakeRig(0f);
            yield return null;

            ground.Tick(out _);
            Assert.AreEqual(0f, ground.GroundY, 1e-4f);
        }

        [UnityTest]
        public IEnumerator GroundY_FollowsTheModelDownAfterATeleportUpAStorey()
        {
            var upper = Slab(3f);
            var lower = Slab(0f);
            var (ground, _) = MakeRig(0f);
            yield return null;
            ground.Tick(out _);
            Assert.AreEqual(0f, ground.GroundY, 1e-4f);

            // teleporting onto the upper storey drops the whole model by 3 m — the ground
            // is derived, so it comes along instead of leaving the building hanging
            upper.MoveBy(new Vector3(0f, -3f, 0f));
            lower.MoveBy(new Vector3(0f, -3f, 0f));
            ground.Invalidate();
            ground.Tick(out _);
            Assert.AreEqual(-3f, ground.GroundY, 1e-4f);
        }

        [UnityTest]
        public IEnumerator SlabUnderfootDisappears_ModelSettlesUpToTheFeet()
        {
            var standing = Slab(0f);
            Slab(-3f);                       // the storey below
            var (ground, _) = MakeRig(0f);
            yield return null;

            Assert.IsFalse(ground.Tick(out _), "standing on a slab: nothing to settle");

            standing.gameObject.SetActive(false);   // what DeleteCommand does
            ground.Invalidate();
            Assert.IsTrue(ground.Tick(out Vector3 shift), "hanging in the air must settle");
            // the model comes UP so the surface below arrives at foot level — the camera
            // and the rig never move
            Assert.AreEqual(0f, shift.x, 1e-4f);
            Assert.AreEqual(3f, shift.y, 1e-3f);
            Assert.AreEqual(0f, shift.z, 1e-4f);
        }

        [UnityTest]
        public IEnumerator SunkBelowTheBuilding_IsLiftedBackOntoTheGround()
        {
            // The #65 trap: the whole building sits above the feet (the user sank below
            // the first floor). The ground is the datum — the model comes DOWN to the
            // feet instead of leaving the user buried with no way out.
            Slab(2f);
            var (ground, _) = MakeRig(0f);
            yield return null;

            Assert.IsTrue(ground.Tick(out Vector3 shift), "buried walker must be rescued");
            Assert.AreEqual(-2f, shift.y, 1e-3f, "the model comes down: its floor to the feet");
        }

        [UnityTest]
        public IEnumerator ScannedFloor_CalibratesTheRig_ModelStaysPut()
        {
            // The headset's tracking floor can sit ~20 cm below the real floor (bad
            // floor calibration, seen live). Content heights are floor-relative (door
            // sills, outlet presets), so the MODEL must stay at world zero — the RIG
            // maps the scanned floor onto it with one static offset. Lifting the model
            // instead floated every door above the slabs (headset 2026-08-13).
            Slab(0f);
            var (ground, rig) = MakeRig(0f);
            ground.ScanFloorOverride = 0.22f;   // scan says the real floor is at +0.22
            yield return null;

            Assert.IsFalse(ground.Tick(out _), "the model must not move");
            // Calibration is ON HOLD (diagnostic: the live FLOOR anchor read Y=0.991 —
            // clearly not the walking plane). Until the anchor dump settles the pose
            // conventions, the rig stays untouched and the offset is only logged.
            Assert.AreEqual(0f, rig.position.y, 1e-3f,
                "rig untouched while the calibration is on diagnostic hold");
        }

        [UnityTest]
        public IEnumerator Settle_HasACooldown_NoPingPong()
        {
            Slab(-3f);
            var (ground, _) = MakeRig(0f);
            yield return null;

            Assert.IsTrue(ground.Tick(out _));
            // the caller applies the shift over the next frames; asking again immediately
            // must not queue a second one
            Assert.IsFalse(ground.Tick(out _));
        }

        [UnityTest]
        public IEnumerator DriftedRig_IsSnappedBackToZero_AndNeverMovedAgain()
        {
            // The #65 root cause: the pre-fix gravity moved the rig, poisoning the foot
            // datum. Now the service restores Y = 0 the moment it sees a drift.
            Slab(0f);
            var (ground, rig) = MakeRig(3f);
            yield return null;

            ground.Tick(out _);
            Assert.AreEqual(0f, rig.position.y, 1e-4f, "drifted rig snapped back to the datum");

            for (int i = 0; i < 30; i++)
            {
                ground.Tick(out _);
                Assert.AreEqual(0f, rig.position.y, 1e-4f, "the rig is never moved again");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator EmptyScene_NothingMoves()
        {
            var (ground, rig) = MakeRig(0f);
            yield return null;

            for (int i = 0; i < 60; i++)
            {
                Assert.IsFalse(ground.Tick(out _), "nothing to settle in an empty scene");
                yield return null;
            }
            Assert.AreEqual(0f, rig.position.y, 1e-4f);
            Assert.AreEqual(0f, ground.GroundY, 1e-4f);
        }

        [UnityTest]
        public IEnumerator LowLedgeUnderfoot_IsNotAutoClimbed()
        {
            // The stair ratchet (headset 2026-08-13): a flight lying under the user's
            // REAL walking path read every step as "climbed a tread" and ratcheted the
            // model down 17.5 cm at a time. Stepping UP is teleport-only now — passive
            // gravity never climbs slabs or treads.
            Slab(0f);
            Slab(0.2f, 1f);                  // a podium under the walker
            var (ground, rig) = MakeRig(0f);
            yield return null;

            Assert.IsFalse(ground.Tick(out _), "no auto-climb — stepping up is teleport-only");
            Assert.AreEqual(0f, rig.position.y, 1e-4f, "the rig stays on the datum");
        }

        [UnityTest]
        public IEnumerator CanStepTo_RefusesALedgeTallerThanAStepButLetsYouDuckUnderAStorey()
        {
            Slab(0f);
            Slab(1f, 1f);                    // waist-high block in the middle
            Slab(3f);                        // the storey ceiling above everything
            var (ground, _) = MakeRig(0f, headX: 3f);
            yield return null;
            ground.Tick(out _);

            Assert.IsFalse(ground.CanStepTo(0f, 0f, 0f), "waist-high ledge blocks the step");
            Assert.IsTrue(ground.CanStepTo(3f, 3f, 0f), "a 3 m storey overhead is walk-under");
        }

        [UnityTest]
        public IEnumerator SupportAt_DropsThroughAStairwellHole()
        {
            var upper = Slab(3f);
            upper.AddHole(new List<Vector3>
            {
                new(-1f, 0f, -1f), new(1f, 0f, -1f), new(1f, 0f, 1f), new(-1f, 0f, 1f),
            });
            Slab(0f);
            var (ground, _) = MakeRig(3f, headX: 0f);
            yield return null;
            ground.Tick(out _);

            Assert.AreEqual(0f, ground.SupportAt(0f, 0f, 3f), 1e-4f);     // over the hole
            Assert.AreEqual(3f, ground.SupportAt(3f, 3f, 3f), 1e-4f);     // on the slab
        }
    }
}
