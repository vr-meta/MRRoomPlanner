using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>
    /// Placement of the floorplan image on the floor: a 2D similarity (uniform scale +
    /// yaw rotation + offset). UV (0..1 across the image) ↔ world XZ. Pure data.
    /// </summary>
    public struct BlueprintPlacement
    {
        public float Scale;        // meters per UV unit (image width in meters)
        public float RotationDeg;  // yaw of the plan on the floor
        public float OriginX;      // world position of the UV origin
        public float OriginZ;

        public static BlueprintPlacement Default => new BlueprintPlacement { Scale = 5f };
    }

    /// <summary>
    /// Pure math for projecting the floorplan (design/15-blueprint.md). With RotationDeg = 0
    /// this reduces to the legacy formula uv = (world − origin) / scale, so metric UVs
    /// (04-surfaces-materials.md) stay intact.
    /// </summary>
    public static class BlueprintMath
    {
        private const float MinScale = 1e-4f;
        private const float MinSegment = 1e-3f;   // shorter calibration segments are noise

        private static float SafeScale(float s) => Mathf.Abs(s) > MinScale ? s : 1f;

        /// <summary>World point → plan UV under the placement (used for floor-top UVs).</summary>
        public static Vector2 WorldToPlanUV(Vector3 world, in BlueprintPlacement p)
        {
            float s = SafeScale(p.Scale);
            float rad = p.RotationDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            float dx = (world.x - p.OriginX) / s;
            float dz = (world.z - p.OriginZ) / s;
            // inverse rotation
            return new Vector2(dx * cos + dz * sin, -dx * sin + dz * cos);
        }

        /// <summary>Plan UV → world point at height y under the placement.</summary>
        public static Vector3 PlanUVToWorld(Vector2 uv, float y, in BlueprintPlacement p)
        {
            float s = SafeScale(p.Scale);
            float rad = p.RotationDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            return new Vector3(
                p.OriginX + s * (uv.x * cos - uv.y * sin),
                y,
                p.OriginZ + s * (uv.x * sin + uv.y * cos));
        }

        /// <summary>
        /// Two-point calibration: given two pairs «point on the PROJECTED plan → where it
        /// must actually be», solve the similarity that makes both pairs coincide (scale,
        /// rotation and offset in one step). Returns <paramref name="current"/> unchanged
        /// when either segment is degenerate.
        /// </summary>
        public static BlueprintPlacement FromPointPairs(Vector3 fromA, Vector3 toA,
            Vector3 fromB, Vector3 toB, in BlueprintPlacement current)
        {
            Vector2 uvA = WorldToPlanUV(fromA, current);
            Vector2 uvB = WorldToPlanUV(fromB, current);
            Vector2 dUV = uvB - uvA;
            Vector2 dW = new Vector2(toB.x - toA.x, toB.z - toA.z);
            if (dUV.magnitude < MinSegment || dW.magnitude < MinSegment) return current;

            float scale = dW.magnitude / dUV.magnitude;
            float rotRad = Mathf.Atan2(dW.y, dW.x) - Mathf.Atan2(dUV.y, dUV.x);
            float cos = Mathf.Cos(rotRad), sin = Mathf.Sin(rotRad);
            // origin so that uvA lands exactly on toA
            float ox = toA.x - scale * (uvA.x * cos - uvA.y * sin);
            float oz = toA.z - scale * (uvA.x * sin + uvA.y * cos);

            return new BlueprintPlacement
            {
                Scale = scale,
                RotationDeg = rotRad * Mathf.Rad2Deg,
                OriginX = ox,
                OriginZ = oz,
            };
        }
    }
}
