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
        public bool IsDoor;
        public Vector3 Swing, Hinge;   // door swing directions; zero = closed leaf
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
        /// <summary>Legacy two-kind flag (files written before StairKind.Waist).</summary>
        public bool Open;
        /// <summary>StairKind as int; -1 = absent in the file, fall back to Open.</summary>
        public int Kind = -1;
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
        public int Category;               // MepCategory as int (0 = plumbing, old files)
        public float Transparency;
        public bool Painted;               // IFC colour or user paint
        public Color Paint;
    }

    // ---- v2: the electrical layer (audit 2026-08-10, 12 §Р1 / B1). Before this the
    // autosave silently dropped every outlet and wire while load DESTROYED them. ----

    [Serializable]
    public class ProjectFixture
    {
        /// <summary>SceneModel id, kept verbatim — wire ends re-attach by this id.</summary>
        public string Id;
        public int Kind;                  // FixtureKind as int
        public int Posts = 1, Keys = 1;
        public int Reserve = -1;          // panel BOM reserve %; -1 = default
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public float BaseLevel;
    }

    [Serializable]
    public class ProjectWire
    {
        public List<Vector3> Points = new();
        public int Cable;                 // CableType as int
        public string StartId, EndId;     // attached fixture ids; null/empty = free end
    }

    /// <summary>v2: a tape measurement — two world points is its whole state (audit B3;
    /// they used to be neither saved nor cleared, haunting the next loaded project).</summary>
    [Serializable]
    public class ProjectMeasure
    {
        public Vector3 A, B;
    }

    [Serializable]
    public class ProjectData
    {
        /// <summary>Current format version. 1 = walls/floors/stairs/MEP only;
        /// 2 = + electrical layer. Readers accept anything up to this and refuse newer.</summary>
        public const int CurrentVersion = 2;

        public int Version = CurrentVersion;
        public List<ProjectNode> Nodes = new();
        public List<ProjectWall> Walls = new();
        public List<ProjectFloor> Floors = new();
        public List<ProjectStair> Stairs = new();
        public List<ProjectMep> Plumbing = new();
        public List<ProjectFixture> Fixtures = new();
        public List<ProjectWire> Wires = new();
        public List<ProjectMeasure> Measures = new();

        // blueprint placement travels with the project — the plan image is a file next to it
        public float PlanScale = 5f;
        public float PlanRotationDeg;
        public float PlanOffsetX;
        public float PlanOffsetZ;

        public string ToJson() => JsonUtility.ToJson(this);

        /// <summary>Null for unparsable json OR a file from a NEWER format version —
        /// reading it partially would silently drop whatever the newer build saved
        /// (audit 12 §Б2). v1 files load fine: the new sections default to empty.</summary>
        public static ProjectData FromJson(string json)
        {
            var d = JsonUtility.FromJson<ProjectData>(json);
            return d != null && d.Version <= CurrentVersion ? d : null;
        }
    }
}
