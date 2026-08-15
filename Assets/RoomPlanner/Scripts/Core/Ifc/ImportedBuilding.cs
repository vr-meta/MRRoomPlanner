using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Core.Ifc
{
    /// <summary>
    /// Pure-data result of an IFC import (docs/design/18-ifc-import.md): metres, Unity
    /// axes (Y up), ready to be turned into Wall/Floor scene objects by a controller.
    /// </summary>
    public sealed class ImportedStorey
    {
        public string Name;
        public float Elevation; // metres, Unity Y
    }

    public sealed class ImportedWall
    {
        /// <summary>Axis polyline in world space (â‰¥2 points), at the wall's base level.</summary>
        public List<Vector3> Path = new();
        public float Thickness;
        public float Height;
        public int StoreyIndex = -1;
        /// <summary>True when this segment came from a rectangular IfcColumn.</summary>
        public bool FromColumn;

        // Optional per-segment overrides (project round-trip; -1/0 = importer defaults).
        public int OffsetOverride = -1;   // WallOffsetMode
        public int JoinOverride = -1;     // WallJoin
        public float SideSignOverride;    // 0 = leave default
        public float BaseHeight;
        public bool HasPaint;
        public Color PaintColor;
        /// <summary>Full surface finish from a project file (v2); None for IFC imports.
        /// v3 per-side walls (issue #34): this is the INNER side.</summary>
        public SurfaceFinish Finish;
        /// <summary>The OUTER side (v3); loaders mirror Finish here for older files.</summary>
        public SurfaceFinish FinishB;
    }

    public sealed class ImportedSlab
    {
        /// <summary>Closed outline on the TOP plane of the slab (no duplicate last point).</summary>
        public List<Vector3> Outline = new();
        /// <summary>Holes cut through the slab (stairwells, shafts) â€” rings on the top plane.</summary>
        public List<List<Vector3>> Holes = new();
        public float Thickness;
        /// <summary>Top level (Unity Y, metres).</summary>
        public float Level;
        public int StoreyIndex = -1;
        public bool HasPaint;
        public Color PaintColor;
        /// <summary>Full surface finish from a project file (v2); None for IFC imports.</summary>
        public SurfaceFinish Finish;
    }

    /// <summary>
    /// A door or window hosted by a wall â€” pure parameters, matching WallOpening in the
    /// wall graph. The wall mesh renders them once Phase D panelisation lands; importing
    /// them now means they appear the moment it does.
    /// </summary>
    public sealed class ImportedOpening
    {
        public int WallIndex;          // index into ImportedBuilding.Walls
        public float AlongFraction;    // opening centre, 0..1 from Path start to end
        public float Width, Height;    // metres
        public float Sill;             // bottom above the wall base, 0 for doors
        public bool IsDoor;
        /// <summary>OpeningKind as int; -1 = derive from IsDoor (IFC path, v1 files).</summary>
        public int Kind = -1;
        /// <summary>World-horizontal direction the door leaf swings toward (IFC door style
        /// + placement axes); zero = unknown, rendered closed.</summary>
        public Vector3 SwingDir;
        /// <summary>World-horizontal direction along the wall from the hinge jamb toward
        /// the free edge of the leaf; zero = unknown.</summary>
        public Vector3 HingeDir;
        /// <summary>Leaf openness 0..1 (issue #50). IFC doors with known swing arrive
        /// at 0.75 — the historical "imported doors stand open" look.</summary>
        public float OpenFraction;
    }

    /// <summary>A stair flight as PARAMETERS (design/18 I9) â€” meshed by our Stair module.</summary>
    public sealed class ImportedStair
    {
        /// <summary>Bottom of the first riser, run centerline (Unity metres).</summary>
        public Vector3 Base;
        /// <summary>Run direction, degrees around Y (0 = +Z), ascending away from Base.</summary>
        public float YawDeg;
        public float Width;
        public int Risers;
        public float RiserHeight;   // metres
        public float TreadDepth;    // metres
        /// <summary>Construction kind (Solid / Open / Waist). IFC gives no reliable
        /// signal; imports default to WAIST â€” the apartment-stairwell slab flight
        /// (headset feedback 2026-08-10) â€” and project files keep the choice.</summary>
        public RoomPlanner.Stairs.StairKind Kind;
        public int StoreyIndex = -1;
        public bool HasPaint;
        public Color PaintColor;
        /// <summary>Full surface finish from a project file (v2); None for IFC imports.</summary>
        public SurfaceFinish Finish;
    }

    /// <summary>What a baked-mesh element IS â€” drives its material in the scene.</summary>
    public enum MepCategory { Plumbing = 0, Furniture = 1, Proxy = 2, Railing = 3 }

    /// <summary>
    /// A baked-mesh element (plumbing terminal, furniture, proxy, railing): IFC ships
    /// them as Breps, not parameters. Vertices are LOCAL around Origin so the object
    /// moves by transform. Colour comes from IfcStyledItem when the file has one.
    /// </summary>
    public sealed class ImportedMep
    {
        public string Name;
        public MepCategory Category;
        public Vector3 Origin;                  // Unity world, metres
        public List<Vector3> Vertices = new();  // local to Origin
        public List<int> Triangles = new();
        public int StoreyIndex = -1;
        public bool HasColor;
        public Color Color;
        /// <summary>0 = opaque, 1 = fully transparent (IfcSurfaceStyleRendering).</summary>
        public float Transparency;
        /// <summary>Full surface finish from a project file (v2); None for IFC imports.</summary>
        public SurfaceFinish Finish;
    }

    /// <summary>A power outlet recognized in the IFC (#79) — converted into a NATIVE
    /// electrical fixture by the importer instead of a dumb baked mesh.</summary>
    public sealed class ImportedOutlet
    {
        public string Name;
        /// <summary>Plate centre, Unity world metres.</summary>
        public Vector3 Position;
        /// <summary>Unsigned mounting axis (the plate's thin side); the scene side
        /// picks the sign that faces away from the host wall.</summary>
        public Vector3 Normal = Vector3.forward;
        public int StoreyIndex = -1;
    }

    /// <summary>A drain pipe segment recovered natively from IfcFlowSegment (#118):
    /// one straight run of the axis with its circle radius — becomes an editable
    /// PipeRoute of the plumbing layer instead of a frozen baked mesh.</summary>
    public sealed class ImportedPipe
    {
        public string Name;
        /// <summary>Axis endpoints, Unity world metres.</summary>
        public Vector3 Start, End;
        /// <summary>Circle profile radius, metres.</summary>
        public float Radius;
        public int StoreyIndex = -1;
    }

    public sealed class ImportedBuilding
    {
        public List<ImportedStorey> Storeys = new();
        public List<ImportedWall> Walls = new();
        public List<ImportedSlab> Slabs = new();
        public List<ImportedOpening> Openings = new();
        public List<ImportedStair> Stairs = new();
        public List<ImportedMep> Plumbing = new();
        public List<ImportedOutlet> Outlets = new();
        public List<ImportedPipe> Pipes = new();

        // Honest import status: what the MVP subset could not represent (shown in UI).
        public int SkippedWalls;
        public int SkippedColumns;
        public int SkippedSlabs;
        public int SkippedOpenings;
        public int SkippedStairs;
        public int SkippedMep;
    }
}
