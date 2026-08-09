using System.Collections.Generic;
using UnityEngine;

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
        /// <summary>Axis polyline in world space (≥2 points), at the wall's base level.</summary>
        public List<Vector3> Path = new();
        public float Thickness;
        public float Height;
        public int StoreyIndex = -1;
        /// <summary>True when this segment came from a rectangular IfcColumn.</summary>
        public bool FromColumn;
    }

    public sealed class ImportedSlab
    {
        /// <summary>Closed outline on the TOP plane of the slab (no duplicate last point).</summary>
        public List<Vector3> Outline = new();
        /// <summary>Holes cut through the slab (stairwells, shafts) — rings on the top plane.</summary>
        public List<List<Vector3>> Holes = new();
        public float Thickness;
        /// <summary>Top level (Unity Y, metres).</summary>
        public float Level;
        public int StoreyIndex = -1;
    }

    /// <summary>
    /// A door or window hosted by a wall — pure parameters, matching WallOpening in the
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
    }

    /// <summary>A stair flight as PARAMETERS (design/18 I9) — meshed by our Stair module.</summary>
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
        public int StoreyIndex = -1;
    }

    public sealed class ImportedBuilding
    {
        public List<ImportedStorey> Storeys = new();
        public List<ImportedWall> Walls = new();
        public List<ImportedSlab> Slabs = new();
        public List<ImportedOpening> Openings = new();
        public List<ImportedStair> Stairs = new();

        // Honest import status: what the MVP subset could not represent (shown in UI).
        public int SkippedWalls;
        public int SkippedColumns;
        public int SkippedSlabs;
        public int SkippedOpenings;
        public int SkippedStairs;
    }
}
