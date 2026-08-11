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
    /// Ground datum and gravity on real components (design/26-ground.md, issue #59):
    /// the ground is derived from the live model, deleting the slab under your feet settles
    /// you onto the next surface down, and walking is refused into ledges taller than a step.
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

            ground.Tick(false, out _);
            Assert.AreEqual(0f, ground.GroundY, 1e-4f);
        }

        [UnityTest]
        public IEnumerator GroundY_FollowsTheModelDownAfterATeleportUpAStorey()
        {
            var upper = Slab(3f);
            var lower = Slab(0f);
            var (ground, _) = MakeRig(0f);
            yield return null;
            ground.Tick(false, out _);
            Assert.AreEqual(0f, ground.GroundY, 1e-4f);

            // teleporting onto the upper storey drops the whole model by 3 m — the ground
            // is derived, so it comes along instead of leaving the building hanging
            upper.MoveBy(new Vector3(0f, -3f, 0f));
            lower.MoveBy(new Vector3(0f, -3f, 0f));
            ground.Invalidate();
            ground.Tick(false, out _);
            Assert.AreEqual(-3f, ground.GroundY, 1e-4f);
        }

        [UnityTest]
        public IEnumerator Passthrough_SettlesTheModelWhenTheSlabUnderfootDisappears()
        {
            var standing = Slab(0f);
            Slab(-3f);                       // the storey below
            var (ground, _) = MakeRig(0f);
            yield return null;

            Assert.IsFalse(ground.Tick(true, out _), "standing on a slab: nothing to settle");

            standing.gameObject.SetActive(false);   // what DeleteCommand does
            ground.Invalidate();
            Assert.IsTrue(ground.Tick(true, out Vector3 shift), "hanging in the air must settle");
            // the model comes UP so the surface below arrives at foot level — the camera
            // never moves in passthrough
            Assert.AreEqual(0f, shift.x, 1e-4f);
            Assert.AreEqual(3f, shift.y, 1e-3f);
            Assert.AreEqual(0f, shift.z, 1e-4f);
        }

        [UnityTest]
        public IEnumerator Passthrough_SettleHasACooldown_NoPingPong()
        {
            Slab(-3f);
            var (ground, _) = MakeRig(0f);
            yield return null;

            Assert.IsTrue(ground.Tick(true, out _));
            // the caller applies the shift over the next frames; asking again immediately
            // must not queue a second one
            Assert.IsFalse(ground.Tick(true, out _));
        }

        [UnityTest]
        public IEnumerator ScanOff_TheRigFallsOntoTheSlabInsteadOfHanging()
        {
            Slab(0f);
            var (ground, rig) = MakeRig(3f);
            yield return null;

            // Batchmode runs thousands of frames per second, so the budget is SIMULATED
            // time (a 3 m fall takes ~0.8 s), not a frame count.
            float t = 0f;
            while (t < 2f && rig.position.y > 1e-4f)
            {
                ground.Tick(false, out _);
                Assert.GreaterOrEqual(rig.position.y, 0f, "never falls through the slab");
                t += Time.deltaTime;
                yield return null;
            }
            Assert.AreEqual(0f, rig.position.y, 1e-3f);
        }

        [UnityTest]
        public IEnumerator ScanOff_TheRigNeverFallsThroughTheGroundWithNoContent()
        {
            var (ground, rig) = MakeRig(0f);
            yield return null;

            for (int i = 0; i < 60; i++)
            {
                ground.Tick(false, out _);
                yield return null;
            }
            Assert.AreEqual(0f, rig.position.y, 1e-4f);
            Assert.AreEqual(0f, ground.GroundY, 1e-4f);
        }

        [UnityTest]
        public IEnumerator ScanOff_TheRigStepsUpOntoALowLedge()
        {
            Slab(0f);
            Slab(0.2f, 1f);                  // a podium under the walker
            var (ground, rig) = MakeRig(0f);
            yield return null;

            ground.Tick(false, out _);
            Assert.AreEqual(0.2f, rig.position.y, 1e-4f);
        }

        [UnityTest]
        public IEnumerator CanStepTo_RefusesALedgeTallerThanAStepButLetsYouDuckUnderAStorey()
        {
            Slab(0f);
            Slab(1f, 1f);                    // waist-high block in the middle
            Slab(3f);                        // the storey ceiling above everything
            var (ground, _) = MakeRig(0f, headX: 3f);
            yield return null;
            ground.Tick(false, out _);

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
            ground.Tick(false, out _);

            Assert.AreEqual(0f, ground.SupportAt(0f, 0f, 3f), 1e-4f);     // over the hole
            Assert.AreEqual(3f, ground.SupportAt(3f, 3f, 3f), 1e-4f);     // on the slab
        }
    }
}
