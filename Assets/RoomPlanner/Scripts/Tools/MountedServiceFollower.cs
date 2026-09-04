using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Electrical;
using RoomPlanner.Walls;
using RoomPlanner.Floors;

namespace RoomPlanner.Tools
{
    /// <summary>Re-resolves a mount when construction changes. User edits rebase the
    /// local coordinates; moving the whole model never applies the displacement twice.</summary>
    public sealed class MountedServiceFollower : MonoBehaviour
    {
        private ElectricFixture _electric;
        private PlumbingObject _plumbing;
        private readonly MountSurfaceQuery _query = new();
        private readonly MountSurface _surface = new();
        private readonly ElectricPlacement _electricPlacement = new();
        private readonly PlumbingPlacement _plumbingPlacement = new();
        private GameObject _host;
        private Vector3 _lastPosition, _local;
        private Quaternion _lastRotation;
        private int _signature;
        private bool _initialized, _plus;
        public string Status { get; private set; } = "Unhosted";

        public void Initialize()
        {
            _electric=GetComponent<ElectricFixture>();_plumbing=GetComponent<PlumbingObject>();
            _host=_electric!=null?_electric.MountHost:_plumbing!=null?_plumbing.MountHost:null;
            if(_host==null)return;
            var wall=_host.GetComponentInParent<Wall>();
            if(wall!=null&&wall.Segment!=null)
                _plus=Vector3.Dot(transform.forward,wall.transform.TransformDirection(WallMesh.RightNormal(WallMesh.Direction(wall.Segment))))>0;
            if(!Resolve())return;
            Remember();Status="Hosted";_initialized=true;
        }

        private bool Resolve()
        {
            if(_host==null||!_host.activeInHierarchy)return false;
            var wall=_host.GetComponentInParent<Wall>();
            Vector3 normal=_electric!=null||(_plumbing!=null&&PlumbingCatalog.WallMounted(_plumbing.Fixture.Kind))?transform.forward:Vector3.up;
            if(wall!=null&&wall.Segment!=null)
                normal=wall.transform.TransformDirection(WallMesh.RightNormal(WallMesh.Direction(wall.Segment)))*(_plus?1:-1);
            return _query.Resolve(_host,transform.position,normal,_surface);
        }

        private int Signature()
        {
            if(_host==null)return 0;
            int hash=_host.transform.localToWorldMatrix.GetHashCode();
            var wall=_host.GetComponentInParent<Wall>();
            if(wall!=null&&wall.Segment!=null)
            {
                var s=wall.Segment;hash^=s.A.Position.GetHashCode()^s.B.Position.GetHashCode()*31
                    ^s.Height.GetHashCode()^s.BaseHeight.GetHashCode()^s.Thickness.GetHashCode()^(int)s.Offset;
                foreach(var o in s.Openings)hash=unchecked(hash*31+o.AlongFraction.GetHashCode()+o.Width.GetHashCode()+o.Height.GetHashCode()+o.SillHeight.GetHashCode());
            }
            var floor=_host.GetComponentInParent<Floor>();
            if(floor!=null)
            {
                hash^=floor.Level.GetHashCode()^floor.Thickness.GetHashCode();
                foreach(var p in floor.Outline)hash=unchecked(hash*31+p.GetHashCode());
                foreach(var ring in floor.Holes)foreach(var p in ring)hash=unchecked(hash*31+p.GetHashCode());
            }
            return hash^(_host.activeInHierarchy?1:0);
        }

        private void Remember()
        {
            _lastPosition=transform.position;_lastRotation=transform.rotation;
            _local=_surface.Project(_lastPosition);_signature=Signature();
        }

        private void LateUpdate()
        {
            if(!_initialized){Initialize();return;}
            if(_host==null){Status="Missing support";return;}
            var wall=_host.GetComponentInParent<Wall>();
            if(wall!=null&&wall.DeferCollider)return;
            int signature=Signature();
            // Explicit edits, including a teleport that already moved this object,
            // take precedence over derived movement.
            if(transform.position!=_lastPosition||transform.rotation!=_lastRotation)
            {if(Resolve())Remember();return;}
            if(signature==_signature)return;
            _signature=signature;
            if(!Resolve()){Status="Missing support";return;}
            Vector3 target=_surface.Point(_local.x,_local.z)+_surface.Normal*ServicePlacement.VisualOffset;
            Quaternion rotation=_surface.Kind==MountSurfaceKind.Wall?Quaternion.LookRotation(_surface.Normal,Vector3.up):transform.rotation;
            PlacementFailure failure;
            if(_electric!=null)
                failure=_electricPlacement.Validate(_host,target,rotation,_electric.Kind,_electric.Posts,SceneModel.Instance,_electric);
            else
            {
                if(_plumbing.HasConnections()){Status="Needs rehost: connected pipes";return;}
                var data=_plumbing.Fixture;var old=data.Position;var oldRot=data.Rotation;
                data.Position=target;data.Rotation=rotation;
                failure=_plumbingPlacement.Validate(_host,data,_plumbing);
                data.Position=old;data.Rotation=oldRot;
            }
            if(failure!=PlacementFailure.None){Status="Needs rehost";return;}
            var selectable=GetComponent<Selectable>();
            var delta=target-transform.position;
            Vector3 terminalBefore=_electric!=null?_electric.TerminalWorld:Vector3.zero;
            if(_electric!=null)_electric.MoveBy(delta);
            else if(selectable!=null)selectable.MoveBy(delta);else transform.position=target;
            transform.rotation=rotation;
            if(_electric!=null)
            {
                _electric.BaseLevel=_surface.BaseLevel;
                var model=SceneModel.Instance;
                if(model!=null&&selectable!=null)
                    foreach(var item in model.Items)
                        if(item is Selectable s&&s.IsAlive&&s.Route!=null)
                            s.Route.TryMoveAttachedEnd(selectable.Id,_electric.TerminalWorld-terminalBefore);
            }
            else{_plumbing.Fixture.Rotation=rotation;_plumbing.Fixture.BaseLevel=_surface.BaseLevel;}
            Remember();Status="Hosted";
        }
    }
}
