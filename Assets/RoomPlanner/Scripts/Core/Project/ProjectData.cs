using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core.Project
{
    // Project format v1 (docs/design/06-project-format.md): PARAMETERS, never meshes —
    // geometry regenerates from these on load. JsonUtility-friendly: plain serializable
    // classes, no dictionaries, rings wrapped (JsonUtility can't nest bare lists).
    // The one exception is imported MEP fixtures: they arrive from IFC as meshes with no
    // parameters to keep, so their baked triangles ride along.

    [Serializable]
    public class ProjectNode
    {
        public Vector3 Position;
    }

    [Serializable]
    public class ProjectOpening
    {
        public float Along, Width, Height, Sill;
    }

    [Serializable]
    public class ProjectWall
    {
        public int NodeA, NodeB;              // indices into ProjectData.Nodes
        public float Thickness, Height, BaseHeight, SideSign;
        public int Offset, Join;
        public bool Painted;
        public Color Paint;
        public List<ProjectOpening> Openings = new();
    }

    [Serializable]
    public class ProjectRing
    {
        public List<Vector3> Points = new();
    }

    [Serializable]
    public class ProjectFloor
    {
        public List<Vector3> Outline = new();
        public List<ProjectRing> Holes = new();
        public float Level, Thickness;
        public bool Painted;
        public Color Paint;
    }

    [Serializable]
    public class ProjectStair
    {
        public Vector3 Base;
        public float Yaw, Width, RiserHeight, TreadDepth;
        public int Risers;
        public bool Open;
        public bool Painted;
        public Color Paint;
    }

    [Serializable]
    public class ProjectMep
    {
        public string Name;
        public Vector3 Origin;
        public List<Vector3> Vertices = new();
        public List<int> Triangles = new();
        public int Storey = -1;
    }

    [Serializable]
    public class ProjectData
    {
        public int Version = 1;
        public List<ProjectNode> Nodes = new();
        public List<ProjectWall> Walls = new();
        public List<ProjectFloor> Floors = new();
        public List<ProjectStair> Stairs = new();
        public List<ProjectMep> Plumbing = new();

        // blueprint placement travels with the project — the plan image is a file next to it
        public float PlanScale = 5f;
        public float PlanRotationDeg;
        public float PlanOffsetX;
        public float PlanOffsetZ;

        public string ToJson() => JsonUtility.ToJson(this);
        public static ProjectData FromJson(string json) => JsonUtility.FromJson<ProjectData>(json);
    }
}
