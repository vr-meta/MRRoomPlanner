using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Tools;
using RoomPlanner.Editing;

namespace RoomPlanner.Electrical
{
    /// <summary>One validator for the ghost, placement click and parameter edits.</summary>
    public sealed class ElectricPlacement
    {
        private readonly MountSurfaceQuery _query = new();
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly Collider[] _overlaps = new Collider[64];
        public readonly MountSurface Surface = new();

        public static Vector2 Size(FixtureKind kind, int posts) => kind switch
        {
            FixtureKind.Panel => new Vector2(ElectricalDefaults.PanelBoxWidth, ElectricalDefaults.PanelBoxHeight),
            FixtureKind.Junction => Vector2.one * ElectricalDefaults.JunctionBoxSize,
            _ => new Vector2((kind == FixtureKind.Outlet ? Mathf.Clamp(posts, 1, ElectricalDefaults.MaxPosts) : 1)
                * ElectricalDefaults.PostModule, ElectricalDefaults.PostModule),
        };

        public bool Resolve(GameObject host, Vector3 point, Vector3 normal) =>
            _query.Resolve(host, point, normal, Surface);

        public PlacementFailure Validate(GameObject host, Vector3 position, Quaternion rotation,
            FixtureKind kind, int posts, SceneModel model, ElectricFixture exclude = null)
        {
            if (!Resolve(host, position, rotation * Vector3.forward)
                || (Surface.Kind != MountSurfaceKind.Wall
                    && !(kind == FixtureKind.Junction && Surface.Kind == MountSurfaceKind.Ceiling)))
                return PlacementFailure.WrongSurface;
            var size = Size(kind, posts);
            var failure = ServicePlacement.Validate(Surface, position, rotation, size, _corners);
            if (failure != PlacementFailure.None) return failure;
            if (model != null)
                foreach (var item in model.Items)
                {
                    if (item is not Selectable s || !s.IsAlive || s.IsHidden || s.Fixture == null
                        || s.Fixture == exclude) continue;
                    var other = s.Fixture;
                    if (ServicePlacement.PlatesOverlap(position, rotation, size,
                        other.transform.position, other.transform.rotation,
                        new Vector2(other.BlockWidth, other.BlockHeight), ElectricalDefaults.FixtureClearance))
                        return PlacementFailure.Overlap;
                }
            float depth = kind == FixtureKind.Panel ? ElectricalDefaults.PanelBoxDepth
                : kind == FixtureKind.Junction ? ElectricalDefaults.JunctionBoxDepth : ElectricalDefaults.PlateDepth;
            int count = Physics.OverlapBoxNonAlloc(position + rotation * Vector3.forward * (depth * 0.5f),
                new Vector3(size.x, size.y, depth) * 0.5f, _overlaps, rotation, 1 << 6, QueryTriggerInteraction.Ignore);
            if (count == _overlaps.Length) return PlacementFailure.Overlap; // saturated query cannot certify a clear placement
            var hostOwner = host != null ? host.GetComponentInParent<Selectable>() : null;
            for (int i = 0; i < count; i++)
            {
                var s = _overlaps[i].GetComponentInParent<Selectable>();
                if (s == null || s == hostOwner || (exclude != null && s.Fixture == exclude)
                    || s.Kind == SelectableKind.Wire || s.Kind == SelectableKind.Measurement) continue;
                if (s.IsAlive && !s.IsHidden) return PlacementFailure.Overlap;
            }
            return PlacementFailure.None;
        }
    }
}
