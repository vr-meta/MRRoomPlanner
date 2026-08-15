using UnityEngine;

namespace RoomPlanner.Core.Ifc
{
    /// <summary>
    /// Recognizing electrical devices in an IFC export (#79). Revit ships outlets as
    /// IfcBuildingElementProxy with a tiny mapped 3D plate (5×5×1 cm) and a telling
    /// name — pure string/geometry rules here, unit-testable; the scene-side conversion
    /// lives in ImportController.
    /// </summary>
    public static class ElectricalImport
    {
        /// <summary>Does this product name mean a power outlet? Case-insensitive;
        /// covers the Russian «розетка» families and the common English exports.</summary>
        public static bool IsOutlet(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.Contains("розетк") || n.Contains("outlet") || n.Contains("socket");
        }

        /// <summary>Mounting axis of a thin plate: the side with the SMALLEST extent
        /// (an outlet pad is flat against its wall). Unsigned — the importer picks the
        /// sign that faces away from the host wall.</summary>
        public static Vector3 PlateNormal(Vector3 size)
        {
            float x = Mathf.Abs(size.x), y = Mathf.Abs(size.y), z = Mathf.Abs(size.z);
            if (x <= y && x <= z) return Vector3.right;
            if (y <= x && y <= z) return Vector3.up;
            return Vector3.forward;
        }
    }
}
