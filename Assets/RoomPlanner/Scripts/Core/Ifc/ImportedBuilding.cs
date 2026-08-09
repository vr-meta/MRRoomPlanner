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
        public float Thickness;
        /// <summary>Top level (Unity Y, metres).</summary>
        public float Level;
        public int StoreyIndex = -1;
    }

    public sealed class ImportedBuilding
    {
        public List<ImportedStorey> Storeys = new();
        public List<ImportedWall> Walls = new();
        public List<ImportedSlab> Slabs = new();

        // Honest import status: what the MVP subset could not represent (shown in UI).
        public int SkippedWalls;
        public int SkippedColumns;
        public int SkippedSlabs;
    }
}
