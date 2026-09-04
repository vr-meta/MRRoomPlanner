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
        /// <summary>OpeningKind as int; -1 = pre-Garage file, fall back to IsDoor.</summary>
        public int Kind = -1;
        public Vector3 Swing, Hinge;   // door swing directions; zero = closed leaf
        /// <summary>Leaf openness 0..1 (v3, issue #50). Pre-v3 readers derive 0.75
        /// from a non-zero Swing (the old "imported doors stand open" look).</summary>
        public float Open;
        /// <summary>v5: frame + leaf finish from the IFC material (issue #133); Kind 0 =
        /// the rig's joinery material, as every file before v5.</summary>
        public ProjectFinish Frame = new();
    }

    /// <summary>
    /// v2: the FULL surface finish (audit B2). v1 stored only Painted+Color, so a
    /// textured floor silently degraded to flat white after every save/load.
    /// Kind 0 = no finish recorded (v1 files) — readers fall back to Painted/Paint.
    /// JsonUtility never round-trips null class fields, hence the marker-by-Kind.
    /// </summary>
    [Serializable]
    public class ProjectFinish
    {
        public int Kind;                  // FinishKind as int
        public Color Color = Color.white;
        public string TextureId;
        public float TileW, TileH;        // metric tile, metres; TileH 0 = square
        public float Gloss;               // shader smoothness
        public float RotationDeg;         // texture rotation; 0 in pre-rotation files
    }

    [Serializable]
    public class ProjectWall
    {
        public int NodeA, NodeB;              // indices into ProjectData.Nodes
        public float Thickness, Height, BaseHeight, SideSign;
        public int Offset, Join;
        /// <summary>Optional since v5: imported rectangular columns reuse wall geometry
        /// but paint as one object. Absent in older files and therefore false.</summary>
        public bool FromColumn;
        public bool Painted;
        public Color Paint;
        /// <summary>v3: the INNER side (v2 readers treated it as the whole wall).</summary>
        public ProjectFinish Finish = new();
        /// <summary>v3: the OUTER side; Kind 0 in a v2 file (absent field).</summary>
        public ProjectFinish FinishB = new();
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
        public ProjectFinish Finish = new();
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
        public ProjectFinish Finish = new();
    }

    /// <summary>v5: one material of a baked element (design/29 §6). Triangles are the
    /// range [TriStart, TriStart+TriCount) of the element's own index list — no second
    /// copy of the indices in the file.</summary>
    [Serializable]
    public class ProjectMepPart
    {
        public string Name;
        public bool Painted;
        public Color Paint = Color.white;
        public float Transparency;
        public int TriStart, TriCount;
        public ProjectFinish Finish = new();
    }

    [Serializable]
    public class ProjectMep
    {
        public string Name;
        public Vector3 Origin;
        public List<Vector3> Vertices = new();
        public List<int> Triangles = new();
        /// <summary>v5: metric box UVs; empty in older files (the mesh then has none,
        /// exactly as it did when it was saved).</summary>
        public List<Vector2> Uvs = new();
        /// <summary>v5: per-material parts; empty = one part, the whole element.</summary>
        public List<ProjectMepPart> Parts = new();
        public int Storey = -1;
        public int Category;               // MepCategory as int (0 = plumbing, old files)
        public float Transparency;
        public bool Painted;               // IFC colour or user paint
        public Color Paint;
        public ProjectFinish Finish = new();
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
        public bool Black;                // white trade plastic / matte-black variant
        public bool PanelOpen;            // panel door pose; false for legacy files
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

    /// <summary>
    /// A placed catalog item (v4, design/27). The model is NOT stored — only its catalog
    /// address, so a project stays small and picks up an updated pack. Size travels with
    /// it anyway: if the pack is gone at load time the piece comes back as a labelled
    /// placeholder of the right dimensions instead of vanishing silently.
    /// </summary>
    [Serializable]
    public class ProjectFurniture
    {
        public string Id;
        public string Key;                // "collection/item"
        public string Name;
        public Vector3 Position;          // bottom centre
        public float Yaw;
        public Vector3 Size;              // metres, as placed
        public int Anchor;                // FurnitureAnchor as int
    }

    // ---- v6: the native plumbing layer (design/30). ProjectMep "Plumbing" above is
    // the UNRELATED legacy list of baked IFC meshes — these are parametric. ----

    [Serializable]
    public class ProjectPlumbFixture
    {
        /// <summary>SceneModel id, kept verbatim — pipe ends re-attach by this id.</summary>
        public string Id;
        public int Kind;                  // PlumbFixtureKind as int
        public int Angle;                 // OutletAngle as int
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public float BaseLevel;
    }

    [Serializable]
    public class ProjectPipe
    {
        public string Id;
        public List<Vector3> Points = new();
        public int Diameter;              // PipeDiameter as int
        public bool IsRiser;
        public int Reserve = -1;          // riser BOM reserve %; -1 = default
        public string StartId, EndId;     // attached fixture/riser ids; null/empty = free end
    }

    [Serializable]
    public class ProjectData
    {
        /// <summary>Current format version. 1 = walls/floors/stairs/MEP only;
        /// 2 = + electrical layer; 3 = per-side wall finishes (issue #34) — in v3
        /// ProjectWall.Finish is the INNER side and FinishB the outer, while a v2
        /// file's single Finish paints the whole wall; 4 = + placed furniture
        /// (design/27); 5 = + per-material parts and box UVs of imported elements
        /// (design/29); 6 = + the native plumbing layer (design/30). Readers accept
        /// anything up to this and refuse newer — an older build silently dropping
        /// a layer would be worse than refusing.</summary>
        public const int CurrentVersion = 6;

        public int Version = CurrentVersion;
        public List<ProjectNode> Nodes = new();
        public List<ProjectWall> Walls = new();
        public List<ProjectFloor> Floors = new();
        public List<ProjectStair> Stairs = new();
        public List<ProjectMep> Plumbing = new();
        public List<ProjectFixture> Fixtures = new();
        public List<ProjectWire> Wires = new();
        public List<ProjectMeasure> Measures = new();
        public List<ProjectFurniture> Furniture = new();
        public List<ProjectPlumbFixture> PlumbFixtures = new();
        public List<ProjectPipe> Pipes = new();

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
