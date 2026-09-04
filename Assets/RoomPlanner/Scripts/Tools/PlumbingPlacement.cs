using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;

namespace RoomPlanner.Tools
{
    public sealed class PlumbingPlacement
    {
        public readonly MountSurface Surface = new();
        private readonly MountSurfaceQuery _query = new();
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly Collider[] _hits = new Collider[64];
        public bool Resolve(GameObject host, Vector3 point, Vector3 normal) => _query.Resolve(host,point,normal,Surface);

        public PlacementFailure Validate(GameObject host, PlumbingFixtureData data, Component exclude = null)
        {
            bool wall = PlumbingCatalog.WallMounted(data.Kind);
            Vector3 normal = wall ? data.Rotation * Vector3.forward : Vector3.up;
            if (!Resolve(host,data.Position,normal) || Surface.Kind != (wall ? MountSurfaceKind.Wall : MountSurfaceKind.Floor))
                return PlacementFailure.WrongSurface;
            if (!ServicePlacement.Finite(data.Size) || data.Size.x <= 0 || data.Size.y <= 0 || data.Size.z <= 0)
                return PlacementFailure.InvalidSize;
            var failure = ServicePlacement.Validate(Surface, data.Position,
                wall ? data.Rotation : data.Rotation * Quaternion.Euler(-90,0,0),
                wall ? new Vector2(data.Size.x,data.Size.y) : new Vector2(data.Size.x,data.Size.z), _corners);
            if (failure != PlacementFailure.None) return failure;
            Vector3 center = data.Position + data.Rotation * (wall ? Vector3.forward * data.Size.z * .5f : Vector3.up * data.Size.y * .5f);
            int count = Physics.OverlapBoxNonAlloc(center, data.Size * .5f, _hits, data.Rotation,1 << 6, QueryTriggerInteraction.Ignore);
            if (count == _hits.Length) return PlacementFailure.Overlap;
            var owner = host.GetComponentInParent<Selectable>();
            for (int i=0;i<count;i++)
            {
                var s = _hits[i].GetComponentInParent<Selectable>();
                if (s == null || s == owner || (exclude != null && s.gameObject == exclude.gameObject)
                    || s.Kind == SelectableKind.Measurement || s.Kind == SelectableKind.Wire) continue;
                if (s.IsAlive && !s.IsHidden) return PlacementFailure.Overlap;
            }
            return PlacementFailure.None;
        }
    }
}
