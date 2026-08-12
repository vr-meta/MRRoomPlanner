using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core.Furniture;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Placement rules from design/27 §2: the floor carries furniture, the wall constrains
    /// it. Poses are expressed at the item's BOTTOM CENTRE, so "stands on the floor" is
    /// exactly "position.y equals the surface" — no half-sunk sofas, no wall cabinets
    /// floating inside the wall.
    /// </summary>
    public class FurniturePlacementTests
    {
        private static readonly Vector3 SofaSize = new(2.1f, 0.8f, 0.9f);
        private static readonly Vector3 CabinetSize = new(0.6f, 0.72f, 0.35f);

        private static PlacementOptions FreeYaw()
        {
            var o = PlacementOptions.Default;
            o.YawStep = 0f;
            return o;
        }

        [Test]
        public void Floor_BottomLandsOnTheSurface()
        {
            var hit = new Vector3(1.5f, 0.02f, -0.4f);
            var pose = FurniturePlacement.Solve(hit, Vector3.up, SofaSize,
                FurnitureAnchor.Floor, 0f, FreeYaw());

            Assert.IsTrue(pose.Valid);
            Assert.AreEqual(hit.x, pose.Position.x, 1e-4f);
            Assert.AreEqual(hit.y, pose.Position.y, 1e-4f, "bottom centre sits exactly on the floor");
            Assert.AreEqual(hit.z, pose.Position.z, 1e-4f);
        }

        [Test]
        public void Floor_RejectsVerticalSurfaces()
        {
            var pose = FurniturePlacement.Solve(Vector3.zero, Vector3.forward, SofaSize,
                FurnitureAnchor.Floor, 0f, FreeYaw());
            Assert.IsFalse(pose.Valid, "a sofa aimed at a wall has no valid pose");
        }

        [Test]
        public void Wall_BackPlaneLiesOnTheFace_ForEveryCardinalNormal()
        {
            var opts = FreeYaw();
            opts.WallMountHeight = -1f;   // follow the aim point
            Vector3[] normals = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

            foreach (var n in normals)
            {
                var hit = new Vector3(2f, 1.4f, 3f);
                var pose = FurniturePlacement.Solve(hit, n, CabinetSize,
                    FurnitureAnchor.Wall, 0f, opts);

                Assert.IsTrue(pose.Valid, $"normal {n}");
                // The centre stands off the face by exactly half the depth …
                float alongNormal = Vector3.Dot(pose.Position - hit, n);
                Assert.AreEqual(CabinetSize.z * 0.5f, alongNormal, 1e-4f, $"normal {n}");
                // … and nothing crosses the face into the wall.
                Assert.GreaterOrEqual(alongNormal - CabinetSize.z * 0.5f, -1e-4f, $"normal {n}");
                Assert.AreEqual(hit.y, pose.Position.y, 1e-4f, $"normal {n}");
                // The front looks into the room, i.e. along the face normal.
                Assert.AreEqual(FurniturePlacement.YawFromNormal(n), pose.Yaw, 1e-3f, $"normal {n}");
            }
        }

        [Test]
        public void Wall_MountHeightOverridesTheAimPoint()
        {
            var opts = FreeYaw();
            opts.WallMountHeight = FurniturePlacement.DefaultMountHeight(FurnitureCategory.Bath);

            var pose = FurniturePlacement.Solve(new Vector3(0f, 2.3f, 0f), Vector3.forward,
                CabinetSize, FurnitureAnchor.Wall, 0f, opts);

            Assert.IsTrue(pose.Valid);
            Assert.AreEqual(0.85f, pose.Position.y, 1e-4f, "a washbasin mounts at its own height");
        }

        [Test]
        public void Wall_RejectsFloorAndCeiling()
        {
            Assert.IsFalse(FurniturePlacement.Solve(Vector3.zero, Vector3.up, CabinetSize,
                FurnitureAnchor.Wall, 0f, FreeYaw()).Valid);
            Assert.IsFalse(FurniturePlacement.Solve(Vector3.zero, Vector3.down, CabinetSize,
                FurnitureAnchor.Wall, 0f, FreeYaw()).Valid);
        }

        [Test]
        public void Ceiling_TopTouchesTheSurface()
        {
            var pose = FurniturePlacement.Solve(new Vector3(0f, 2.7f, 0f), Vector3.down,
                new Vector3(0.4f, 0.3f, 0.4f), FurnitureAnchor.Ceiling, 0f, FreeYaw());

            Assert.IsTrue(pose.Valid);
            Assert.AreEqual(2.4f, pose.Position.y, 1e-4f, "bottom hangs one height below the ceiling");
        }

        [Test]
        public void Yaw_QuantisesToTheStep_AndStaysInRange()
        {
            Assert.AreEqual(15f, FurniturePlacement.QuantizeYaw(17f, 15f), 1e-4f);
            Assert.AreEqual(0f, FurniturePlacement.QuantizeYaw(-3f, 15f), 1e-4f);
            Assert.AreEqual(345f, FurniturePlacement.QuantizeYaw(-14f, 15f), 1e-4f);
            Assert.AreEqual(0f, FurniturePlacement.QuantizeYaw(359f, 15f), 1e-4f, "wraps instead of reporting 360");
            Assert.AreEqual(123.4f, FurniturePlacement.QuantizeYaw(123.4f, 0f), 1e-4f, "step 0 = free rotation");
        }

        [Test]
        public void Snap_PullsTheBackFlushWithTheWall()
        {
            // Wall face at z = 0 with its room side facing +Z; sofa standing 0.25 m away.
            var pose = new FurniturePose { Position = new Vector3(0f, 0f, 0.70f), Yaw = 40f, Valid = true };
            bool snapped = FurniturePlacement.TrySnapBackToWall(ref pose, SofaSize,
                Vector3.zero, Vector3.forward, PlacementOptions.DefaultSnapDistance);

            Assert.IsTrue(snapped);
            Assert.AreEqual(SofaSize.z * 0.5f, pose.Position.z, 1e-4f, "rear face touches the wall");
            Assert.AreEqual(0f, pose.Yaw, 1e-3f, "back to the wall, front into the room");
        }

        [Test]
        public void Snap_ReleasesBeyondTheDistance()
        {
            var pose = new FurniturePose { Position = new Vector3(0f, 0f, 1.4f), Yaw = 40f, Valid = true };
            var before = pose;

            Assert.IsFalse(FurniturePlacement.TrySnapBackToWall(ref pose, SofaSize,
                Vector3.zero, Vector3.forward, PlacementOptions.DefaultSnapDistance));
            Assert.AreEqual(before.Position, pose.Position);
            Assert.AreEqual(before.Yaw, pose.Yaw);
        }

        [Test]
        public void Snap_PushesOutAnItemThatOverlapsTheWall()
        {
            // Dropped INSIDE the wall thickness — furniture inside a wall is never an answer.
            var pose = new FurniturePose { Position = new Vector3(0f, 0f, 0.10f), Yaw = 0f, Valid = true };
            Assert.IsTrue(FurniturePlacement.TrySnapBackToWall(ref pose, SofaSize,
                Vector3.zero, Vector3.forward, PlacementOptions.DefaultSnapDistance));
            Assert.AreEqual(SofaSize.z * 0.5f, pose.Position.z, 1e-4f);
        }

        [Test]
        public void Snap_IgnoresWallsBehindTheItem()
        {
            var pose = new FurniturePose { Position = new Vector3(0f, 0f, -2f), Yaw = 0f, Valid = true };
            Assert.IsFalse(FurniturePlacement.TrySnapBackToWall(ref pose, SofaSize,
                Vector3.zero, Vector3.forward, PlacementOptions.DefaultSnapDistance),
                "the wall the user is facing away from is not the snap target");
        }

        [Test]
        public void FitScale_KeepsProportions_AndMatchesTheLongestAxis()
        {
            float s = FurniturePlacement.FitScale(Vector3.one, SofaSize);
            Assert.AreEqual(2.1f, s, 1e-4f, "a unit cube grows to the longest declared side");

            // A model already authored in metres needs no scaling.
            Assert.AreEqual(1f, FurniturePlacement.FitScale(SofaSize, SofaSize), 1e-4f);
            // A centimetre-scale export scales down by exactly 100.
            Assert.AreEqual(0.01f, FurniturePlacement.FitScale(SofaSize * 100f, SofaSize), 1e-6f);
            // Degenerate input must not divide by zero.
            Assert.AreEqual(1f, FurniturePlacement.FitScale(Vector3.zero, SofaSize), 1e-6f);
        }

        [Test]
        public void FitScaleAxes_StretchMatchesEveryAxis_UniformKeepsProportions()
        {
            // Kenney's kitchen unit measures 0.43 × 0.45 × 0.45 where the real one is
            // 0.60 × 0.85 × 0.60 — a carcass must hit the worktop height exactly.
            var model = new Vector3(0.43f, 0.45f, 0.45f);
            var real = new Vector3(0.60f, 0.85f, 0.60f);

            var stretch = FurniturePlacement.FitScaleAxes(model, real, FurnitureFit.Stretch);
            Assert.AreEqual(real.x, model.x * stretch.x, 1e-4f);
            Assert.AreEqual(real.y, model.y * stretch.y, 1e-4f);
            Assert.AreEqual(real.z, model.z * stretch.z, 1e-4f);

            var uniform = FurniturePlacement.FitScaleAxes(model, real, FurnitureFit.Uniform);
            Assert.AreEqual(uniform.x, uniform.y, 1e-6f);
            Assert.AreEqual(uniform.y, uniform.z, 1e-6f);
            Assert.AreEqual(FurniturePlacement.FitScale(model, real), uniform.x, 1e-6f);
        }

        [Test]
        public void FitScaleAxes_DegenerateModel_DoesNotDivideByZero()
        {
            var s = FurniturePlacement.FitScaleAxes(new Vector3(0f, 0.45f, 0f),
                new Vector3(0.6f, 0.85f, 0.6f), FurnitureFit.Stretch);
            Assert.AreEqual(1f, s.x, 1e-6f);
            Assert.AreEqual(0.85f / 0.45f, s.y, 1e-4f);
            Assert.AreEqual(1f, s.z, 1e-6f);
        }

        [Test]
        public void Footprint_RotatesWithYaw()
        {
            var pose = new FurniturePose { Position = Vector3.zero, Yaw = 90f, Valid = true };
            var corners = new Vector3[4];
            FurniturePlacement.Footprint(pose, SofaSize, corners);

            // At 90° the 2.1 m width runs along Z and the 0.9 m depth along X.
            float maxX = 0f, maxZ = 0f;
            foreach (var c in corners)
            {
                maxX = Mathf.Max(maxX, Mathf.Abs(c.x));
                maxZ = Mathf.Max(maxZ, Mathf.Abs(c.z));
            }
            Assert.AreEqual(SofaSize.z * 0.5f, maxX, 1e-4f);
            Assert.AreEqual(SofaSize.x * 0.5f, maxZ, 1e-4f);
        }
    }
}
