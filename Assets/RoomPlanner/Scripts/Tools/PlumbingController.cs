using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Measure;
using RoomPlanner.Editing;
using RoomPlanner.Electrical;

namespace RoomPlanner.Tools
{
    public sealed class PlumbingController : MonoBehaviour, ITool
    {
        [SerializeField] private PointerProvider pointer;
        [SerializeField] private MeasureInput input;
        [SerializeField] private SceneRaycaster raycaster;
        [SerializeField] private SceneModel sceneModel;
        [SerializeField] private ToolManager manager;
        [SerializeField] private Transform reticle;
        [SerializeField] private Material fixtureMaterial;
        [SerializeField] private Material pipeMaterial;
        [SerializeField] private Material dimensionMaterial;
        private readonly PlumbingPlacement _placement = new();
        private readonly List<Vector3> _points = new(), _preview = new(), _elbows = new();
        private readonly List<int> _clickSizes = new();
        private readonly RaycastHit[] _hits = new RaycastHit[64];
        private PlumbingObject _ghost;
        private LineRenderer _line;
        private PlumbingFixtureData _candidate;
        private PlumbingKind _kind;
        private WaterSystem _system;
        private float _height = .85f, _yaw, _slope = 2f;
        private PipeDimensions _pipe = new() { OuterDiameter=.02f, WallThickness=.002f };
        private float _offset = .02f, _nextClick;
        private int _mode;
        private string _status="Choose a surface", _startId;
        private int _startPort=-1;
        private SettingsSchema _schema;
        private bool _ortho=true;
        private int _dimensionSignature=int.MinValue;
        private string _dimensionText;
        public string Id=>"plumbing";
        public string PaletteLabel=>"Plumbing";
        public string IconId=>"pipe";

        public SettingsSchema GetSettings()
        {
            if(_schema!=null)return _schema;
            var fixture=new SettingsSchema()
                .Select("kind","Fixture",()=>PlumbingCatalog.Names,()=> (int)_kind,i=>{_kind=(PlumbingKind)i;_candidate=null;})
                .Numeric("height","Wall mount height",0f,10f,()=>_height,(_,v)=>_height=v,()=>$"{_height*100f:0.#} cm",displayScale:100f)
                .Numeric("yaw","Floor rotation",0f,360f,()=>_yaw,(_,v)=>_yaw=v,()=>$"{_yaw:0}°")
                .Segmented("service","Connection",new[]{"Cold","Hot","Waste"},()=> (int)_system,i=>{_system=(WaterSystem)i;_candidate=null;})
                .Readout("size","W × D × H",()=>{var s=PlumbingCatalog.Size(_kind);return $"{s.x*100:0} × {s.z*100:0} × {s.y*100:0} cm";})
                .Readout("status","Placement",()=>_status);
            var pipe=new SettingsSchema()
                .Segmented("system","System",new[]{"Cold","Hot","Waste"},()=> (int)_system,i=>{Finish();_system=(WaterSystem)i;})
                .Numeric("diameter","Outer diameter",.001f,.5f,()=>_pipe.OuterDiameter,(_,v)=>{Finish();_pipe.OuterDiameter=v;},()=>$"{_pipe.OuterDiameter*1000:0.#} mm",displayScale:1000f)
                .Numeric("thickness","Wall thickness",.0001f,.05f,()=>_pipe.WallThickness,(_,v)=>{Finish();_pipe.WallThickness=v;},()=>$"{_pipe.WallThickness*1000:0.#} mm",displayScale:1000f)
                .Numeric("slope","Waste slope",.01f,100f,()=>_slope,(_,v)=>{Finish();_slope=v;},()=>$"{_slope:0.##} %")
                .Segmented("route","Routing",new[]{"Ortho","Free"},()=>_ortho?0:1,i=>_ortho=i==0)
                .Header("drawing","Drawing")
                .Action("finish","Finish","check",Finish)
                .Readout("status","Status",()=>_status);
            var details=new SettingsSchema()
                .Numeric("offset","Axis from surface",0f,1f,()=>_offset,(_,v)=>_offset=v,()=>$"{_offset*100:0.#} cm",displayScale:100f)
                .Numeric("insulation","Insulation",0f,.2f,()=>_pipe.InsulationThickness,(_,v)=>{Finish();_pipe.InsulationThickness=v;},()=>$"{_pipe.InsulationThickness*1000:0.#} mm",displayScale:1000f)
                .Readout("inside","Inside diameter",()=>_pipe.IsValid?$"{_pipe.InnerDiameter*1000:0.#} mm":"Invalid specification")
                .Action("back","Back point","undo",BackPoint)
                .Action("cancel","Cancel route","cross",Cancel,destructive:true);
            var report=new SettingsSchema()
                .Readout("cold","Cold",()=>Report(WaterSystem.Cold))
                .Readout("hot","Hot",()=>Report(WaterSystem.Hot))
                .Readout("waste","Waste",()=>Report(WaterSystem.Waste))
                .Readout("scope","Scope",()=>"Project · geometry only");
            return _schema=SettingsSchema.Tabbed(new[]{"Fixture","Pipe","Options","Report"},()=>_mode,i=>{if(i==0||i==3)Finish();_mode=i;},fixture,pipe,details,report);
        }

        public void OnActivate(){}
        public void OnDeactivate(){Finish();Hide();}
        private void Hide(){if(_ghost!=null)_ghost.gameObject.SetActive(false);if(_line!=null)_line.enabled=false;if(reticle!=null)reticle.gameObject.SetActive(false);}

        public void Tick(bool blocked)
        {
            if(blocked||pointer==null||input==null||raycaster==null){Hide();return;}
            if(_mode==3){Hide();return;}
            bool hit=raycaster.TryRaycastSurface(pointer.GetRay(),out var point,out var normal,out var host);
            if(_mode==0)TickFixture(hit,host,point,normal);else TickPipe(hit,host,point,normal);
        }

        private void TickFixture(bool hit,GameObject host,Vector3 point,Vector3 normal)
        {
            if(_line!=null)_line.enabled=false;
            bool wall=PlumbingCatalog.WallMounted(_kind);
            if(!hit||!_placement.Resolve(host,point,normal)||_placement.Surface.Kind!=(wall?MountSurfaceKind.Wall:MountSurfaceKind.Floor))
            {Hide();_status="Wrong surface";if(input.ClearPressed())manager?.ActivateTool("select");return;}
            _candidate??=PlumbingCatalog.Create(_kind,PlumbingCatalog.Size(_kind),_system);
            _candidate.BaseLevel=wall?(_placement.Surface.BaseLevel):(point.y);
            if(wall && host.GetComponentInParent<RoomPlanner.Walls.Wall>()==null)_candidate.BaseLevel=manager!=null?manager.Level:0f;
            _candidate.Position=point+normal*ServicePlacement.VisualOffset;
            if(wall)_candidate.Position.y=_candidate.BaseLevel+_height;
            _candidate.Rotation=wall?Quaternion.LookRotation(normal,Vector3.up):Quaternion.Euler(0,_yaw,0);
            var failure=_placement.Validate(host,_candidate);
            _status=ServicePlacement.Describe(failure);
            if(_ghost==null)_ghost=CreateObject("Plumbing preview",false);
            bool rebuild=_ghost.Fixture==null||_ghost.Fixture.Kind!=_kind;
            if(rebuild)_ghost.SetFixture(PlumbingCatalog.Create(_kind,_candidate.Size,_system));
            _ghost.transform.SetPositionAndRotation(_candidate.Position,_candidate.Rotation);
            _ghost.gameObject.SetActive(true);
            _ghost.GetComponent<MeshCollider>().enabled=false;
            ShowReticle(_candidate.Position,failure==PlacementFailure.None?FixtureDimensions():_status);
            if(input.ConfirmPressed()&&Time.time>=_nextClick)
            {
                _nextClick=Time.time+.25f;
                if(failure==PlacementFailure.None)
                {
                    var data=JsonUtility.FromJson<PlumbingFixtureData>(JsonUtility.ToJson(_candidate));
                    data.MountKey=MountIdentity.GetOrCreate(host);
                    var item=RestoreFixture(data);item.MountHost=host;
                    RecordCreation(item);
                    input.Pulse(.6f,.02f);
                }
                else input.Pulse(.2f,.01f);
            }
            if(input.ClearPressed())manager?.ActivateTool("select");
        }

        private string FixtureDimensions()
        {
            int h=Mathf.RoundToInt((_candidate.Position.y-_candidate.BaseLevel)*100f);
            int signature=((int)_kind*397)^h;
            if(signature!=_dimensionSignature)
            {
                _dimensionSignature=signature;
                var s=_candidate.Size;
                _dimensionText=$"{s.x*100:0} × {s.z*100:0} × {s.y*100:0} cm · Mount {h} cm";
            }
            return _dimensionText;
        }

        private void TickPipe(bool hit,GameObject host,Vector3 point,Vector3 normal)
        {
            if(_ghost!=null)_ghost.gameObject.SetActive(false);
            PlumbingObject terminal=null;int port=-1;
            if(hit)FindPort(point,out terminal,out port);
            bool compatible=terminal==null||PortUsable(terminal,port);
            bool surface=hit&&_placement.Resolve(host,point,normal);
            bool valid=hit&&(terminal!=null||surface)&&compatible&&_pipe.IsValid;
            var cursor=terminal!=null?terminal.PortWorld(port):point+normal*Mathf.Max(_offset,_pipe.EnvelopeDiameter*.5f+ServicePlacement.VisualOffset);
            _preview.Clear();_preview.AddRange(_points);
            if(valid && _points.Count>0)
            {
                var previous=_points[_points.Count-1];
                if(_system==WaterSystem.Waste)
                {
                    var delta=cursor-previous;delta.y=0;
                    float calculated=previous.y-delta.magnitude*_slope/100f;
                    if(terminal!=null&&Mathf.Abs(cursor.y-calculated)>ServicePlacement.Tolerance){valid=false;_status="End height does not match slope";}
                    else cursor.y=calculated;
                }
                else if(_ortho){WireMath.OrthoElbow(previous,cursor,_elbows);_preview.AddRange(_elbows);}
                _preview.Add(cursor);
                if(valid)valid=RouteClear(_preview,terminal);
            }
            if(!compatible)_status="Port: verify system, size and availability";
            else if(!_pipe.IsValid)_status="Invalid pipe dimensions";
            else if(valid)_status="Trigger: point · B: finish";
            if(valid)ShowReticle(cursor,_status);else if(hit)ShowReticle(point,_status);else if(reticle!=null)reticle.gameObject.SetActive(false);
            DrawPreview();
            if(input.ConfirmPressed()&&Time.time>=_nextClick)
            {
                _nextClick=Time.time+.25f;
                if(!valid){input.Pulse(.2f,.01f);return;}
                if(_points.Count==0){_clickSizes.Add(0);_points.Add(cursor);_startId=terminal?.Id;_startPort=port;}
                else if(Vector3.Distance(_points[_points.Count-1],cursor)>=.01f)
                {
                    _clickSizes.Add(_points.Count);
                    _points.Clear();_points.AddRange(_preview);
                    if(terminal!=null)FinishInto(terminal,port);
                }
            }
            if(input.ClearPressed()){if(_points.Count>=2)Finish();else if(_points.Count==1)Cancel();else manager?.ActivateTool("select");}
        }

        private void FindPort(Vector3 point,out PlumbingObject best,out int port)
        {
            best=null;port=-1;float distance=.1f;
            if(sceneModel==null)return;
            foreach(var item in sceneModel.Items)
            {
                if(item is not Selectable s||!s.IsAlive||s.IsHidden)continue;
                var fixture=s.GetComponent<PlumbingObject>();
                if(fixture==null||fixture.Fixture==null)continue;
                for(int i=0;i<fixture.Fixture.Ports.Count;i++)
                {float d=Vector3.Distance(point,fixture.PortWorld(i));if(d<distance){distance=d;best=fixture;port=i;}}
            }
        }

        private bool PortUsable(PlumbingObject fixture,int index)
        {
            var p=fixture.Fixture.Ports[index];
            if(p.System!=_system||!p.Confirmed||Mathf.Abs(p.Diameter-_pipe.OuterDiameter)>ServicePlacement.Tolerance)return false;
            if(fixture.Id==_startId&&index==_startPort)return false;
            foreach(var item in sceneModel.Items)
            {
                if(item is not Selectable s||!s.IsAlive||s.IsHidden)continue;
                var route=s.GetComponent<PlumbingObject>()?.Pipe;
                if(route!=null&&((route.StartId==fixture.Id&&route.StartPort==index)||(route.EndId==fixture.Id&&route.EndPort==index)))return false;
            }
            return true;
        }

        private bool RouteClear(List<Vector3> points,PlumbingObject end)
        {
            for(int i=1;i<points.Count;i++)
            {
                var delta=points[i]-points[i-1];if(delta.sqrMagnitude<1e-10f)continue;
                int count=Physics.SphereCastNonAlloc(points[i-1],_pipe.EnvelopeDiameter*.5f,delta.normalized,_hits,delta.magnitude,1<<6,QueryTriggerInteraction.Ignore);
                if(count==_hits.Length){_status="Too many obstacles";return false;}
                for(int j=0;j<count;j++)
                {
                    var s=_hits[j].collider.GetComponentInParent<Selectable>();
                    if(s==null||s.IsHidden||s.Kind==SelectableKind.Measurement||s.Kind==SelectableKind.Wire)continue;
                    if((i==1&&s.Id==_startId)||(i==points.Count-1&&end!=null&&s.Id==end.Id))continue;
                    _status="Pipe overlaps an object";return false;
                }
            }
            return true;
        }

        public void Finish()=>FinishInto(null,-1);
        private void FinishInto(PlumbingObject end,int port)
        {
            if(_points.Count>=2&&_pipe.IsValid)
            {
                var item=RestorePipe(new PipeRouteData{System=_system,Dimensions=_pipe,SlopePercent=_system==WaterSystem.Waste?_slope:0f,
                    StartId=_startId,StartPort=_startPort,EndId=end?.Id,EndPort=port,Points=new List<Vector3>(_points)});
                RecordCreation(item);
            }
            Cancel();
        }
        public void Cancel(){_points.Clear();_preview.Clear();_clickSizes.Clear();_startId=null;_startPort=-1;if(_line!=null)_line.enabled=false;}
        private void RecordCreation(PlumbingObject item)
        {
            var model=sceneModel!=null?sceneModel:SceneModel.Instance;
            if(item!=null&&model!=null)model.History.Record(new CreateCommand(item.GetComponent<Selectable>()));
        }
        private void BackPoint()
        {
            if(_clickSizes.Count==0)return;
            int count=_clickSizes[_clickSizes.Count-1];_clickSizes.RemoveAt(_clickSizes.Count-1);
            _points.RemoveRange(count,_points.Count-count);
            if(count==0){_startId=null;_startPort=-1;}
        }

        private void DrawPreview()
        {
            if(_line==null){var go=new GameObject("Pipe preview");go.transform.SetParent(transform,false);_line=go.AddComponent<LineRenderer>();_line.sharedMaterial=pipeMaterial;}
            _line.enabled=_preview.Count>=2;_line.widthMultiplier=_pipe.EnvelopeDiameter;_line.positionCount=_preview.Count;
            for(int i=0;i<_preview.Count;i++)_line.SetPosition(i,_preview[i]);
        }
        private void ShowReticle(Vector3 point,string text)
        {if(reticle==null)return;reticle.gameObject.SetActive(true);reticle.position=point;ReticleVisual.For(reticle)?.SetDimension(text);}

        private PlumbingObject CreateObject(string name,bool pipe)
        {
            var go=new GameObject(name){layer=6}; // world-rooted: locomotion must not carry placed objects with the rig
            var view=go.AddComponent<PlumbingObject>();go.GetComponent<MeshRenderer>().sharedMaterial=pipe?pipeMaterial:fixtureMaterial;
            go.AddComponent<ServiceDimensionDisplay>().Material=dimensionMaterial;
            return view;
        }
        public PlumbingObject RestoreFixture(PlumbingFixtureData data)
        {
            if(!PlumbingCatalog.ValidFixture(data))return null;
            var view=CreateObject(PlumbingCatalog.Names[(int)data.Kind],false);view.SetFixture(data);
            view.MountHost=MountIdentity.Find(data.MountKey);
            var s=view.gameObject.AddComponent<Selectable>();if(!string.IsNullOrEmpty(data.Id))s.Id=data.Id;
            (sceneModel!=null?sceneModel:SceneModel.Instance)?.Register(s);data.Id=s.Id;
            view.gameObject.AddComponent<MountedServiceFollower>().Initialize();
            return view;
        }
        public PlumbingObject RestorePipe(PipeRouteData data)
        {
            if(!PlumbingCatalog.ValidPipe(data))return null;
            var view=CreateObject("Pipe",true);view.SetPipe(data);
            var s=view.gameObject.AddComponent<Selectable>();if(!string.IsNullOrEmpty(data.Id))s.Id=data.Id;
            (sceneModel!=null?sceneModel:SceneModel.Instance)?.Register(s);data.Id=s.Id;
            return view;
        }
        private string Report(WaterSystem system)
        {
            float length=0;int loose=0;if(sceneModel==null)return "0 m";
            foreach(var item in sceneModel.Items)
            {
                if(item is not Selectable s||!s.IsAlive||s.IsHidden)continue;
                var p=s.GetComponent<PlumbingObject>()?.Pipe;if(p==null||p.System!=system)continue;
                length+=WireMath.PolylineLength(p.Points);if(!PlumbingObject.EndpointValid(p,true)||!PlumbingObject.EndpointValid(p,false))loose++;
            }
            return $"{length:0.00} m · {loose} open routes";
        }
        private void OnDestroy(){if(_ghost!=null){if(Application.isPlaying)Destroy(_ghost.gameObject);else DestroyImmediate(_ghost.gameObject);}}
    }
}
