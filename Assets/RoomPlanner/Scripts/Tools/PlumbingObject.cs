using System;
using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Electrical;

namespace RoomPlanner.Tools
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PlumbingObject : MonoBehaviour, ISettingsProvider
    {
        public PlumbingFixtureData Fixture { get; private set; }
        public PipeRouteData Pipe { get; private set; }
        public GameObject MountHost { get; set; }
        public bool IsPipe => Pipe != null;
        public bool ShowDimensions
        {
            get => IsPipe ? Pipe.ShowDimensions : Fixture != null && Fixture.ShowDimensions;
            set { if (IsPipe) Pipe.ShowDimensions=value; else if (Fixture!=null) Fixture.ShowDimensions=value; }
        }
        private Mesh _mesh;
        private readonly List<Vector3> _vertices = new();
        private readonly List<int> _triangles = new();
        private readonly List<Vector2> _uvs = new();
        private readonly PlumbingPlacement _placement = new();
        private SettingsSchema _settings;
        private int _port, _tab;
        private string _status = "Approximate connections";
        public string Status => _status;

        public void SetFixture(PlumbingFixtureData data)
        {
            if(!PlumbingCatalog.ValidFixture(data))return;
            Fixture = data; Pipe = null;
            transform.SetPositionAndRotation(data.Position,data.Rotation);
            PlumbingCatalog.BuildMesh(data.Kind,data.Size,_vertices,_triangles);
            CommitMesh();
        }

        public void SetPipe(PipeRouteData data)
        {
            if(!PlumbingCatalog.ValidPipe(data))return;
            Pipe = data; Fixture = null;
            transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            WireMath.BuildTube(data.Points,data.Dimensions.EnvelopeDiameter*.5f,8,_vertices,_triangles,_uvs);
            CommitMesh();
        }

        private void CommitMesh()
        {
            if (_mesh == null) { _mesh = new Mesh { name = "PlumbingMesh" }; GetComponent<MeshFilter>().sharedMesh = _mesh; }
            _mesh.Clear(); _mesh.SetVertices(_vertices); _mesh.SetTriangles(_triangles,0); _mesh.RecalculateNormals(); _mesh.RecalculateBounds();
            var collider = GetComponent<MeshCollider>();
            if (collider == null) collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = null; collider.sharedMesh = _mesh;
        }

        public Vector3 PortWorld(int index) => transform.TransformPoint(Fixture.Ports[index].LocalPosition);
        public string Id => GetComponent<Selectable>()?.Id;

        public bool HasConnections()
        {
            if (IsPipe) return !string.IsNullOrEmpty(Pipe.StartId) || !string.IsNullOrEmpty(Pipe.EndId);
            var model = SceneModel.Instance;
            if (model == null) return false;
            foreach (var item in model.Items)
                if (item is Selectable s && s.IsAlive && !s.IsHidden)
                {
                    var other = s.GetComponent<PlumbingObject>();
                    if (other != null && other.Pipe != null && (other.Pipe.StartId == Id || other.Pipe.EndId == Id)) return true;
                }
            return false;
        }

        public bool CanMove(Vector3 delta)
        {
            if (HasConnections()) { _status = "Disconnect before moving"; return false; }
            if (IsPipe) { _status = "Edit route through its points"; return false; }
            var position = Fixture.Position;
            Fixture.Position = transform.position + delta;
            var failure = MountHost != null ? _placement.Validate(MountHost,Fixture,this) : PlacementFailure.WrongSurface;
            Fixture.Position = position;
            _status = ServicePlacement.Describe(failure);
            return failure == PlacementFailure.None;
        }

        public static bool EndpointValid(PipeRouteData pipe, bool start)
        {
            string id=start?pipe.StartId:pipe.EndId;
            int port=start?pipe.StartPort:pipe.EndPort;
            var model=SceneModel.Instance;
            if(string.IsNullOrEmpty(id)||port<0||model==null)return false;
            foreach(var item in model.Items)
            {
                if(item is not Selectable s||!s.IsAlive||s.IsHidden||s.Id!=id)continue;
                var target=s.GetComponent<PlumbingObject>();
                if(target==null||target.Fixture==null||port>=target.Fixture.Ports.Count)return false;
                var data=target.Fixture.Ports[port];
                var point=start?pipe.Points[0]:pipe.Points[pipe.Points.Count-1];
                return data.Confirmed&&data.System==pipe.System
                    && Mathf.Abs(data.Diameter-pipe.Dimensions.OuterDiameter)<ServicePlacement.Tolerance
                    && Vector3.Distance(point,target.PortWorld(port))<ServicePlacement.Tolerance;
            }
            return false;
        }

        public void MoveBy(Vector3 delta)
        {
            if (IsPipe)
            {
                for (int i=0;i<Pipe.Points.Count;i++) Pipe.Points[i] += delta;
                SetPipe(Pipe);
            }
            else { Fixture.Position += delta; transform.position = Fixture.Position; }
        }

        public string Describe() => IsPipe
            ? $"{Pipe.System} · Ø{Pipe.Dimensions.OuterDiameter*1000f:0.#} × {Pipe.Dimensions.WallThickness*1000f:0.#} mm · {WireMath.PolylineLength(Pipe.Points):0.00} m"
            : $"{PlumbingCatalog.Names[(int)Fixture.Kind]} · {Fixture.Size.x*100f:0.#} × {Fixture.Size.z*100f:0.#} × {Fixture.Size.y*100f:0.#} cm";

        public SettingsSchema GetSettings()
        {
            if (_settings != null) return _settings;
            if (IsPipe)
                return _settings = new SettingsSchema()
                    .Readout("spec","Pipe",Describe)
                    .Readout("inside","Inside diameter",() => $"{Pipe.Dimensions.InnerDiameter*1000f:0.#} mm")
                    .Readout("envelope","With insulation",() => $"{Pipe.Dimensions.EnvelopeDiameter*1000f:0.#} mm")
                    .Readout("slope","Slope",() => Pipe.System == WaterSystem.Waste ? $"{Pipe.SlopePercent:0.##} %" : "Pressure pipe")
                    .Header("connections","Connections and dimensions")
                    .Readout("status","Connections",()=>EndpointValid(Pipe,true)&&EndpointValid(Pipe,false)?"Connected":"Unconnected / missing target")
                    .Action("disconnect","Disconnect","pipe",Disconnect)
                    .Toggle("dimensions","Dimensions",() => ShowDimensions,v => ShowDimensions=v);
            var placement = new SettingsSchema()
                .Readout("size","W × D × H",Describe)
                .Numeric("height","Mount height",0f,10f,() => Fixture.Position.y-Fixture.BaseLevel,
                    (_,v) => Edit(d => d.Position.y=d.BaseLevel+v),() => $"{Fixture.Position.y-Fixture.BaseLevel:0.00} m")
                .Numeric("width","Width",.03f,5f,() => Fixture.Size.x,(_,v) => Edit(d=>d.Size.x=v),()=>$"{Fixture.Size.x*100f:0.#} cm",displayScale:100f)
                .Numeric("depth","Depth",.01f,5f,() => Fixture.Size.z,(_,v) => Edit(d=>d.Size.z=v),()=>$"{Fixture.Size.z*100f:0.#} cm",displayScale:100f)
                .Numeric("bodyheight","Body height",.01f,5f,() => Fixture.Size.y,(_,v) => Edit(d=>d.Size.y=v),()=>$"{Fixture.Size.y*100f:0.#} cm",displayScale:100f)
                .Toggle("dimensions","Dimensions",() => ShowDimensions,v => ShowDimensions=v)
                .Header("validation","Placement")
                .Readout("status","Status",()=>PlacementStatus());
            string[] ports = new string[Fixture.Ports.Count];
            for (int i=0;i<ports.Length;i++) ports[i]=Fixture.Ports[i].System.ToString();
            var connections = new SettingsSchema()
                .Select("port","Port",()=>ports,()=>_port,i=>_port=i)
                .Numeric("x","Local X",-5f,5f,()=>Fixture.Ports[_port].LocalPosition.x,
                    (_,v)=>Edit(d=>{d.Ports[_port].LocalPosition.x=v; d.Ports[_port].Confirmed=false;}),()=>PortValue(0),displayScale:100f)
                .Numeric("y","Local Y",-5f,5f,()=>Fixture.Ports[_port].LocalPosition.y,
                    (_,v)=>Edit(d=>{d.Ports[_port].LocalPosition.y=v; d.Ports[_port].Confirmed=false;}),()=>PortValue(1),displayScale:100f)
                .Numeric("z","Local Z",-5f,5f,()=>Fixture.Ports[_port].LocalPosition.z,
                    (_,v)=>Edit(d=>{d.Ports[_port].LocalPosition.z=v; d.Ports[_port].Confirmed=false;}),()=>PortValue(2),displayScale:100f)
                .Numeric("diameter","Pipe OD",.001f,.5f,()=>Fixture.Ports[_port].Diameter,
                    (_,v)=>Edit(d=>d.Ports[_port].Diameter=v),()=>$"{Fixture.Ports[_port].Diameter*1000f:0.#} mm",displayScale:1000f)
                .Toggle("confirmed","Verified dimensions",()=>Fixture.Ports[_port].Confirmed,v=>Edit(d=>d.Ports[_port].Confirmed=v && d.Ports[_port].Diameter>0))
                .Header("connections","Connections")
                .Action("disconnect","Disconnect","pipe",Disconnect);
            return _settings = SettingsSchema.Tabbed(new[]{"Placement","Connections"},()=>_tab,i=>_tab=i,placement,connections);
        }

        private string PortValue(int axis) => $"{Fixture.Ports[_port].LocalPosition[axis]*100f:0.#} cm";
        private string PlacementStatus()
        {
            var follower=GetComponent<MountedServiceFollower>();
            return follower!=null&&follower.Status!="Hosted"?follower.Status:_status;
        }

        private void Edit(Action<PlumbingFixtureData> edit)
        {
            if (HasConnections()) { _status="Disconnect before editing"; return; }
            var before = JsonUtility.ToJson(Fixture);
            var next = JsonUtility.FromJson<PlumbingFixtureData>(before);
            edit(next);
            if (!PlumbingCatalog.ValidFixture(next)) return;
            // Port metadata can be completed after load even without a live mount binding.
            bool geometryChanged = next.Position != Fixture.Position || next.Size != Fixture.Size;
            if (geometryChanged)
            {
                var failure = MountHost != null ? _placement.Validate(MountHost,next,this) : PlacementFailure.WrongSurface;
                _status=ServicePlacement.Describe(failure);
                if (failure != PlacementFailure.None) return;
                foreach (var p in next.Ports) p.Confirmed=false;
            }
            var after = JsonUtility.ToJson(next);
            if (before == after) return;
            Execute(new PlumbingChange(this,before,after,false));
        }

        public void Disconnect()
        {
            var model=SceneModel.Instance;
            if (model == null) return;
            var commands=new List<ICommand>();
            foreach (var item in model.Items)
            {
                if (item is not Selectable s || !s.IsAlive || s.IsHidden) continue;
                var other=s.GetComponent<PlumbingObject>();
                if (other == null || !other.IsPipe) continue;
                if (other!=this && other.Pipe.StartId!=Id && other.Pipe.EndId!=Id) continue;
                string before=JsonUtility.ToJson(other.Pipe);
                var next=JsonUtility.FromJson<PipeRouteData>(before);
                if (other==this || next.StartId==Id) {next.StartId=null;next.StartPort=-1;}
                if (other==this || next.EndId==Id) {next.EndId=null;next.EndPort=-1;}
                commands.Add(new PlumbingChange(other,before,JsonUtility.ToJson(next),true));
            }
            if(commands.Count>0) Execute(new PlumbingChanges(commands));
        }

        private static void Execute(ICommand command)
        {
            if(SceneModel.Instance!=null) SceneModel.Instance.History.Execute(command); else command.Do();
        }
        private void OnDestroy() { if(_mesh!=null) { if(Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh); } }
    }

    public sealed class PlumbingChange : ICommand,ISelectableCommand
    {
        private readonly PlumbingObject _target;
        private readonly string _before,_after;
        private readonly bool _pipe;
        public PlumbingChange(PlumbingObject target,string before,string after,bool pipe) { _target=target;_before=before;_after=after;_pipe=pipe; }
        public string Name=>"Edit plumbing";
        public ISelectable Target=>_target!=null?_target.GetComponent<Selectable>():null;
        public void Do()=>Apply(_after);
        public void Undo()=>Apply(_before);
        private void Apply(string data) { if(_target==null)return; if(_pipe)_target.SetPipe(JsonUtility.FromJson<PipeRouteData>(data));else _target.SetFixture(JsonUtility.FromJson<PlumbingFixtureData>(data)); }
    }
    internal sealed class PlumbingChanges : ICommand
    {
        private readonly List<ICommand> _commands;
        public PlumbingChanges(List<ICommand> commands)=>_commands=commands;
        public string Name=>"Disconnect plumbing";
        public void Do(){foreach(var c in _commands)c.Do();}
        public void Undo(){for(int i=_commands.Count-1;i>=0;i--)_commands[i].Undo();}
    }
}
