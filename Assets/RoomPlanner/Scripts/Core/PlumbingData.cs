using System;
using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core.Furniture;

namespace RoomPlanner.Core
{
    public enum PlumbingKind { Basin, Toilet, Bath, Shower, Drain, Connection }
    public enum WaterSystem { Cold, Hot, Waste }

    [Serializable]
    public class PlumbingPort
    {
        public WaterSystem System;
        public Vector3 LocalPosition;
        public float Diameter;
        public bool Confirmed;
    }

    [Serializable]
    public class PlumbingFixtureData
    {
        public string MountKey;
        public string Id;
        public PlumbingKind Kind;
        public Vector3 Position, Size;
        public Quaternion Rotation = Quaternion.identity;
        public float BaseLevel;
        public bool ShowDimensions;
        public List<PlumbingPort> Ports = new();
    }

    [Serializable]
    public class PipeRouteData
    {
        public string Id, StartId, EndId;
        public int StartPort = -1, EndPort = -1;
        public WaterSystem System;
        public PipeDimensions Dimensions;
        public float SlopePercent;
        public bool ShowDimensions;
        public List<Vector3> Points = new();
    }

    /// <summary>Editable schematic templates, not manufacturer connection coordinates.</summary>
    public static class PlumbingCatalog
    {
        public static readonly string[] Names = { "Basin", "Toilet", "Bath", "Shower tray", "Drain", "Connection" };
        public static bool WallMounted(PlumbingKind kind) => kind == PlumbingKind.Basin || kind == PlumbingKind.Connection;
        public static Vector3 Size(PlumbingKind kind) => kind switch
        {
            PlumbingKind.Basin => new Vector3(.6f, .2f, .5f),
            PlumbingKind.Toilet => new Vector3(.4f, .75f, .7f),
            PlumbingKind.Bath => new Vector3(.8f, .6f, 1.7f),
            PlumbingKind.Shower => new Vector3(.9f, .08f, .9f),
            PlumbingKind.Drain => new Vector3(.1f, .03f, .1f),
            _ => new Vector3(.06f, .06f, .03f),
        };

        public static PlumbingFixtureData Create(PlumbingKind kind, Vector3 size, WaterSystem connectionSystem)
        {
            var data = new PlumbingFixtureData { Kind = kind, Size = size };
            if (kind == PlumbingKind.Connection)
                data.Ports.Add(new PlumbingPort { System = connectionSystem, LocalPosition = new Vector3(0,0,size.z) });
            else
            {
                if (kind == PlumbingKind.Basin)
                {
                    data.Ports.Add(new PlumbingPort { System = WaterSystem.Cold, LocalPosition = new Vector3(-size.x * .15f, -size.y * .5f, 0) });
                    data.Ports.Add(new PlumbingPort { System = WaterSystem.Hot, LocalPosition = new Vector3(size.x * .15f, -size.y * .5f, 0) });
                }
                if (kind == PlumbingKind.Toilet)
                    data.Ports.Add(new PlumbingPort { System = WaterSystem.Cold, LocalPosition = new Vector3(size.x * .5f, size.y * .6f, -size.z * .4f) });
                data.Ports.Add(new PlumbingPort { System = WaterSystem.Waste,
                    LocalPosition = kind == PlumbingKind.Basin ? new Vector3(0, -size.y * .5f, size.z * .5f)
                        : new Vector3(0, size.y * .1f, -size.z * .4f) });
            }
            return data;
        }

        public static bool Compatible(PlumbingPort a, PlumbingPort b, PipeDimensions pipe)
            => a != null && b != null && a.System == b.System && a.Confirmed && b.Confirmed && pipe.IsValid
                && Mathf.Abs(a.Diameter - pipe.OuterDiameter) < ServicePlacement.Tolerance
                && Mathf.Abs(b.Diameter - pipe.OuterDiameter) < ServicePlacement.Tolerance;

        public static bool ValidFixture(PlumbingFixtureData data)
        {
            if(data==null||(int)data.Kind<0||(int)data.Kind>=Names.Length||!ServicePlacement.Finite(data.Position)
                ||!ServicePlacement.Finite(data.Size)||data.Size.x<=0||data.Size.y<=0||data.Size.z<=0
                ||data.Ports==null||data.Ports.Count==0)return false;
            foreach(var port in data.Ports)
                if(port==null||!ServicePlacement.Finite(port.LocalPosition)||!ServicePlacement.Finite(port.Diameter)
                    ||port.Diameter<0||(int)port.System<0||(int)port.System>2)return false;
            return true;
        }

        public static bool ValidPipe(PipeRouteData data)
        {
            if(data==null||!data.Dimensions.IsValid||data.Points==null||data.Points.Count<2
                ||(int)data.System<0||(int)data.System>2)return false;
            if(data.System==WaterSystem.Waste&&(!ServicePlacement.Finite(data.SlopePercent)||data.SlopePercent<=0))return false;
            for(int i=0;i<data.Points.Count;i++)
            {
                if(!ServicePlacement.Finite(data.Points[i]))return false;
                if(i==0)continue;
                var d=data.Points[i]-data.Points[i-1];
                if(d.sqrMagnitude<.0001f)return false;
                if(data.System!=WaterSystem.Waste)continue;
                float horizontal=new Vector2(d.x,d.z).magnitude;
                if(horizontal<ServicePlacement.Tolerance){if(d.y>=0)return false;}
                else if(Mathf.Abs(d.y+horizontal*data.SlopePercent/100f)>ServicePlacement.Tolerance)return false;
            }
            return true;
        }

        /// <summary>Box-built schematic with real outer dimensions and outward winding.</summary>
        public static void BuildMesh(PlumbingKind kind, Vector3 size, List<Vector3> vertices, List<int> triangles)
        {
            vertices.Clear(); triangles.Clear();
            var center = WallMounted(kind) ? new Vector3(0, 0, size.z * .5f) : new Vector3(0,size.y * .5f,0);
            if (kind == PlumbingKind.Toilet)
            {
                PartitionMesh.AddBox(vertices,triangles,null,new Vector3(0,size.y*.15f,size.z*.1f),new Vector3(size.x*.3f,size.y*.15f,size.z*.25f));
                PartitionMesh.AddBox(vertices,triangles,null,new Vector3(0,size.y*.45f,size.z*.15f),new Vector3(size.x*.5f,size.y*.22f,size.z*.35f));
                PartitionMesh.AddBox(vertices,triangles,null,new Vector3(0,size.y*.6f,-size.z*.38f),new Vector3(size.x*.45f,size.y*.4f,size.z*.12f));
                return;
            }
            if (kind == PlumbingKind.Connection || kind == PlumbingKind.Drain)
            {
                PartitionMesh.AddBox(vertices, triangles, null, center, size * .5f);
                return;
            }
            float rim = Mathf.Min(.04f, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * .2f);
            // Open vessel: base and four sides share the declared outer bounds.
            PartitionMesh.AddBox(vertices, triangles, null, center + Vector3.down * ((size.y-rim)*.5f), new Vector3(size.x,rim,size.z)*.5f);
            PartitionMesh.AddBox(vertices, triangles, null, center + Vector3.left * ((size.x-rim)*.5f), new Vector3(rim,size.y,size.z)*.5f);
            PartitionMesh.AddBox(vertices, triangles, null, center + Vector3.right * ((size.x-rim)*.5f), new Vector3(rim,size.y,size.z)*.5f);
            PartitionMesh.AddBox(vertices, triangles, null, center + Vector3.back * ((size.z-rim)*.5f), new Vector3(size.x,size.y,rim)*.5f);
            PartitionMesh.AddBox(vertices, triangles, null, center + Vector3.forward * ((size.z-rim)*.5f), new Vector3(size.x,size.y,rim)*.5f);
        }
    }
}
