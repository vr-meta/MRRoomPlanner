using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core.Ifc
{
    /// <summary>
    /// Extracts the MVP subset of docs/design/18-ifc-import.md from an indexed IFC file:
    /// storeys, walls (axis + thickness + height), rectangular columns (as short wall
    /// segments) and slab outlines. All math stays in IFC space (file units, Z up) and
    /// is converted to Unity (metres, Y up) only at the edges.
    /// </summary>
    public static class IfcImporter
    {
        public static ImportedBuilding Import(StepFile f)
        {
            var ctx = new Ctx { F = f, Scale = (float)f.LengthToMeters };
            var b = new ImportedBuilding();
            ImportStoreys(ctx, b);
            MapElementsToStoreys(ctx, b);
            MapLayerThickness(ctx);
            ImportWalls(ctx, b);
            ImportColumns(ctx, b);
            ImportSlabs(ctx, b);
            return b;
        }

        private sealed class Ctx
        {
            public StepFile F;
            public float Scale;
            public readonly Dictionary<int, Matrix4x4> Placements = new();
            public readonly Dictionary<int, int> StoreyIndexByRecord = new(); // storey record id → sorted index
            public readonly Dictionary<int, int> StoreyOfElement = new();   // element id → storey index
            public readonly Dictionary<int, float> LayerThickness = new();  // element id → summed layers (file units)
        }

        // IFC is right-handed Z-up; Unity is left-handed Y-up. Swapping Y/Z keeps the
        // plan view identical (X east, IFC-Y north → Unity-Z north).
        private static Vector3 ToUnity(Ctx c, Vector3 pFileUnits) =>
            new Vector3(pFileUnits.x, pFileUnits.z, pFileUnits.y) * c.Scale;

        // ---------------------------------------------------------------- storeys

        private static void ImportStoreys(Ctx c, ImportedBuilding b)
        {
            // IFCBUILDINGSTOREY(GlobalId, OwnerHistory, Name, Desc, ObjectType,
            //                   Placement, Representation, LongName, Composition, Elevation)
            var ids = new List<int>(c.F.OfType("IFCBUILDINGSTOREY"));
            var items = new List<(int Id, string Name, float Elev)>();
            foreach (int id in ids)
            {
                var a = c.F.Args(id);
                if (a == null || a.Count < 10) continue;
                float elev = a[9].Kind == StepKind.Number ? a[9].AsFloat * c.Scale : 0f;
                string name = a[2].Kind == StepKind.Text ? a[2].Text : $"#{id}";
                items.Add((id, name, elev));
            }
            items.Sort((x, y) => x.Elev.CompareTo(y.Elev));
            foreach (var it in items)
                b.Storeys.Add(new ImportedStorey { Name = it.Name, Elevation = it.Elev });
            // remember index for containment mapping
            for (int i = 0; i < items.Count; i++)
                c.StoreyIndexByRecord[items[i].Id] = i;
        }

        private static void MapElementsToStoreys(Ctx c, ImportedBuilding b)
        {
            // IFCRELCONTAINEDINSPATIALSTRUCTURE(…, RelatedElements, RelatingStructure)
            foreach (int id in c.F.OfType("IFCRELCONTAINEDINSPATIALSTRUCTURE"))
            {
                var a = c.F.Args(id);
                if (a == null || a.Count < 6 || a[4].Kind != StepKind.List || a[5].Kind != StepKind.Ref) continue;
                if (!c.StoreyIndexByRecord.TryGetValue(a[5].Ref, out int storey)) continue;
                foreach (var el in a[4].Items)
                    if (el.Kind == StepKind.Ref)
                        c.StoreyOfElement[el.Ref] = storey;
            }
        }

        private static void MapLayerThickness(Ctx c)
        {
            // IFCRELASSOCIATESMATERIAL(…, RelatedObjects, RelatingMaterial)
            foreach (int id in c.F.OfType("IFCRELASSOCIATESMATERIAL"))
            {
                var a = c.F.Args(id);
                if (a == null || a.Count < 6 || a[4].Kind != StepKind.List) continue;
                float total = LayerSetThickness(c, a[5]);
                if (total <= 0f) continue;
                foreach (var el in a[4].Items)
                    if (el.Kind == StepKind.Ref)
                        c.LayerThickness[el.Ref] = total;
            }
        }

        private static float LayerSetThickness(Ctx c, StepValue relating)
        {
            var usage = c.F.Deref(relating);
            if (usage == null) return 0f;
            // IFCMATERIALLAYERSETUSAGE(LayerSet, …) or directly IFCMATERIALLAYERSET(Layers, Name)
            var layerSet = c.F.TypeOf(relating.Ref) == "IFCMATERIALLAYERSETUSAGE"
                ? c.F.Deref(usage[0])
                : usage;
            if (layerSet == null || layerSet.Count < 1 || layerSet[0].Kind != StepKind.List) return 0f;
            float total = 0f;
            foreach (var layerRef in layerSet[0].Items)
            {
                var layer = c.F.Deref(layerRef); // IFCMATERIALLAYER(Material, Thickness, IsVentilated)
                if (layer != null && layer.Count > 1 && layer[1].Kind == StepKind.Number)
                    total += layer[1].AsFloat;
            }
            return total;
        }

        // ---------------------------------------------------------------- walls

        private static void ImportWalls(Ctx c, ImportedBuilding b)
        {
            foreach (string type in new[] { "IFCWALLSTANDARDCASE", "IFCWALL" })
            foreach (int id in c.F.OfType(type))
            {
                // IFCWALL*(GlobalId, OwnerHistory, Name, Desc, ObjectType, Placement, Representation, Tag)
                var a = c.F.Args(id);
                if (a == null || a.Count < 7) { b.SkippedWalls++; continue; }
                var place = Placement(c, a[5]);

                var axisItems = FindRepresentation(c, a[6], "Axis");
                var poly = FirstOfType(c, axisItems, "IFCPOLYLINE");
                if (poly == 0) { b.SkippedWalls++; continue; }

                float height = 0f, profileThickness = 0f;
                var bodyItems = FindRepresentation(c, a[6], "Body");
                int solid = ResolveExtruded(c, bodyItems, out var bodyExtra);
                if (solid != 0)
                {
                    var sa = c.F.Args(solid);
                    height = sa[3].AsFloat;
                    var profile = c.F.Deref(sa[0]);
                    if (profile != null && c.F.TypeOf(sa[0].Ref) == "IFCRECTANGLEPROFILEDEF")
                        profileThickness = Mathf.Min(profile[3].AsFloat, profile[4].AsFloat);
                }
                if (height <= 0f) { b.SkippedWalls++; continue; } // Brep-only wall — no parametric height

                float thickness = c.LayerThickness.TryGetValue(id, out float lt) ? lt : profileThickness;
                if (thickness <= 0f) thickness = 150f; // last-resort default, file units (mm)

                var wall = new ImportedWall
                {
                    Thickness = thickness * c.Scale,
                    Height = height * c.Scale,
                    StoreyIndex = c.StoreyOfElement.TryGetValue(id, out int s) ? s : -1,
                };
                foreach (var ptRef in c.F.Args(poly)[0].Items)
                    wall.Path.Add(ToUnity(c, place.MultiplyPoint3x4(Point(c, ptRef))));
                if (wall.Path.Count < 2) { b.SkippedWalls++; continue; }
                b.Walls.Add(wall);
            }
        }

        // ---------------------------------------------------------------- columns

        private static void ImportColumns(Ctx c, ImportedBuilding b)
        {
            foreach (int id in c.F.OfType("IFCCOLUMN"))
            {
                var a = c.F.Args(id);
                if (a == null || a.Count < 7) { b.SkippedColumns++; continue; }
                var place = Placement(c, a[5]);

                var bodyItems = FindRepresentation(c, a[6], "Body");
                int solid = ResolveExtruded(c, bodyItems, out var extra);
                if (solid == 0) { b.SkippedColumns++; continue; }
                var sa = c.F.Args(solid);
                if (c.F.TypeOf(sa[0].Ref) != "IFCRECTANGLEPROFILEDEF") { b.SkippedColumns++; continue; } // circles etc.

                var profile = c.F.Args(sa[0].Ref);
                float xd = profile[3].AsFloat, yd = profile[4].AsFloat;
                float len = Mathf.Max(xd, yd), thick = Mathf.Min(xd, yd);
                var alongX = xd >= yd;

                // profile-space endpoints of the long mid-line, lifted through:
                // profile 2D placement → solid position → mapped-item transform → object placement
                var m = place * extra * Axis2Placement3D(c, sa[1]) * Axis2Placement2D(c, profile[2]);
                var e0 = alongX ? new Vector3(-len * 0.5f, 0f, 0f) : new Vector3(0f, -len * 0.5f, 0f);
                var e1 = -e0;

                var wall = new ImportedWall
                {
                    Thickness = thick * c.Scale,
                    Height = sa[3].AsFloat * c.Scale,
                    StoreyIndex = c.StoreyOfElement.TryGetValue(id, out int s) ? s : -1,
                    FromColumn = true,
                };
                wall.Path.Add(ToUnity(c, m.MultiplyPoint3x4(e0)));
                wall.Path.Add(ToUnity(c, m.MultiplyPoint3x4(e1)));
                b.Walls.Add(wall);
            }
        }

        // ---------------------------------------------------------------- slabs

        private static void ImportSlabs(Ctx c, ImportedBuilding b)
        {
            foreach (int id in c.F.OfType("IFCSLAB"))
            {
                var a = c.F.Args(id);
                if (a == null || a.Count < 7) { b.SkippedSlabs++; continue; }
                var place = Placement(c, a[5]);

                var bodyItems = FindRepresentation(c, a[6], "Body");
                int solid = ResolveExtruded(c, bodyItems, out var extra);
                if (solid == 0) { b.SkippedSlabs++; continue; }
                var sa = c.F.Args(solid);
                var m = place * extra * Axis2Placement3D(c, sa[1]);
                float depth = sa[3].AsFloat;

                var local = ProfileOutline(c, sa[0]);
                if (local == null || local.Count < 3) { b.SkippedSlabs++; continue; }

                // Everything below stays in IFC space until the final conversion.
                var world = new List<Vector3>(local.Count);
                foreach (var p in local) world.Add(m.MultiplyPoint3x4(p));

                // The outline sits on the profile plane; the solid grows along the
                // extrusion direction, so the TOP is plane+depth when it points up.
                var dirWorld = m.MultiplyVector(Direction(c, sa[2]));
                float topZ = world[0].z + (dirWorld.z > 0f ? depth : 0f);

                var slab = new ImportedSlab
                {
                    Thickness = depth * c.Scale,
                    Level = topZ * c.Scale,
                    StoreyIndex = c.StoreyOfElement.TryGetValue(id, out int s) ? s : -1,
                };
                foreach (var p in world)
                {
                    var u = ToUnity(c, p);
                    u.y = slab.Level;
                    slab.Outline.Add(u);
                }
                b.Slabs.Add(slab);
            }
        }

        private static List<Vector3> ProfileOutline(Ctx c, StepValue profileRef)
        {
            var profile = c.F.Deref(profileRef);
            if (profile == null) return null;
            switch (c.F.TypeOf(profileRef.Ref))
            {
                case "IFCRECTANGLEPROFILEDEF":
                {
                    float hx = profile[3].AsFloat * 0.5f, hy = profile[4].AsFloat * 0.5f;
                    var m2 = Axis2Placement2D(c, profile[2]);
                    return new List<Vector3>
                    {
                        m2.MultiplyPoint3x4(new Vector3(-hx, -hy, 0f)),
                        m2.MultiplyPoint3x4(new Vector3(hx, -hy, 0f)),
                        m2.MultiplyPoint3x4(new Vector3(hx, hy, 0f)),
                        m2.MultiplyPoint3x4(new Vector3(-hx, hy, 0f)),
                    };
                }
                case "IFCARBITRARYCLOSEDPROFILEDEF":
                case "IFCARBITRARYPROFILEDEFWITHVOIDS": // voids ignored by the MVP subset
                {
                    if (profile[2].Kind != StepKind.Ref || c.F.TypeOf(profile[2].Ref) != "IFCPOLYLINE") return null;
                    var pts = new List<Vector3>();
                    foreach (var ptRef in c.F.Args(profile[2].Ref)[0].Items)
                        pts.Add(Point(c, ptRef));
                    // IFC closes the polyline by repeating the first point — our outlines don't.
                    if (pts.Count > 1 && (pts[0] - pts[pts.Count - 1]).sqrMagnitude < 1e-6f)
                        pts.RemoveAt(pts.Count - 1);
                    return pts;
                }
                default:
                    return null;
            }
        }

        // ---------------------------------------------------------------- geometry plumbing

        private static Vector3 Point(Ctx c, StepValue ptRef)
        {
            var a = c.F.Deref(ptRef); // IFCCARTESIANPOINT((x,y[,z]))
            if (a == null || a.Count < 1 || a[0].Kind != StepKind.List) return Vector3.zero;
            var n = a[0].Items;
            return new Vector3(
                n.Count > 0 ? n[0].AsFloat : 0f,
                n.Count > 1 ? n[1].AsFloat : 0f,
                n.Count > 2 ? n[2].AsFloat : 0f);
        }

        private static Vector3 Direction(Ctx c, StepValue dirRef)
        {
            var v = Point(c, dirRef); // IFCDIRECTION has the same layout
            return v.sqrMagnitude > 1e-12f ? v.normalized : Vector3.forward;
        }

        private static Matrix4x4 Placement(Ctx c, StepValue placeRef)
        {
            if (placeRef == null || placeRef.Kind != StepKind.Ref) return Matrix4x4.identity;
            int id = placeRef.Ref;
            if (c.Placements.TryGetValue(id, out var cached)) return cached;

            var m = Matrix4x4.identity;
            var a = c.F.Args(id);
            if (a != null && c.F.TypeOf(id) == "IFCLOCALPLACEMENT")
            {
                // IFCLOCALPLACEMENT(PlacementRelTo, RelativePlacement)
                var parent = Placement(c, a[0]);
                m = parent * Axis2Placement3D(c, a[1]);
            }
            c.Placements[id] = m;
            return m;
        }

        private static Matrix4x4 Axis2Placement3D(Ctx c, StepValue axisRef)
        {
            var a = c.F.Deref(axisRef); // (Location, Axis(Z), RefDirection(X))
            if (a == null) return Matrix4x4.identity;
            var loc = Point(c, a[0]);
            var z = a.Count > 1 && a[1].Kind == StepKind.Ref ? Direction(c, a[1]) : new Vector3(0f, 0f, 1f);
            var x = a.Count > 2 && a[2].Kind == StepKind.Ref ? Direction(c, a[2]) : new Vector3(1f, 0f, 0f);
            x = (x - z * Vector3.Dot(x, z)).normalized;                 // re-orthogonalize
            if (x.sqrMagnitude < 1e-12f) x = Vector3.right;
            var y = Vector3.Cross(z, x);                                // right-handed IFC frame
            return Frame(x, y, z, loc);
        }

        private static Matrix4x4 Axis2Placement2D(Ctx c, StepValue axisRef)
        {
            var a = c.F.Deref(axisRef); // (Location, RefDirection)
            if (a == null) return Matrix4x4.identity;
            var loc = Point(c, a[0]);
            var xd = a.Count > 1 && a[1].Kind == StepKind.Ref ? Direction(c, a[1]) : Vector3.right;
            var x = new Vector3(xd.x, xd.y, 0f).normalized;
            var y = new Vector3(-x.y, x.x, 0f);
            return Frame(x, y, new Vector3(0f, 0f, 1f), loc);
        }

        private static Matrix4x4 Frame(Vector3 x, Vector3 y, Vector3 z, Vector3 t)
        {
            var m = Matrix4x4.identity;
            m.SetColumn(0, new Vector4(x.x, x.y, x.z, 0f));
            m.SetColumn(1, new Vector4(y.x, y.y, y.z, 0f));
            m.SetColumn(2, new Vector4(z.x, z.y, z.z, 0f));
            m.SetColumn(3, new Vector4(t.x, t.y, t.z, 1f));
            return m;
        }

        /// <summary>Shape-representation items for the given identifier ('Axis'/'Body'), or null.</summary>
        private static List<StepValue> FindRepresentation(Ctx c, StepValue pdsRef, string identifier)
        {
            var pds = c.F.Deref(pdsRef); // IFCPRODUCTDEFINITIONSHAPE(Name, Desc, Representations)
            if (pds == null || pds.Count < 3 || pds[2].Kind != StepKind.List) return null;
            foreach (var repRef in pds[2].Items)
            {
                var rep = c.F.Deref(repRef); // IFCSHAPEREPRESENTATION(Ctx, Identifier, Type, Items)
                if (rep == null || rep.Count < 4) continue;
                if (rep[1].Kind == StepKind.Text && rep[1].Text == identifier && rep[3].Kind == StepKind.List)
                    return rep[3].Items;
            }
            return null;
        }

        private static int FirstOfType(Ctx c, List<StepValue> items, string type)
        {
            if (items == null) return 0;
            foreach (var it in items)
                if (it.Kind == StepKind.Ref && c.F.TypeOf(it.Ref) == type)
                    return it.Ref;
            return 0;
        }

        /// <summary>
        /// Follows body items to an IFCEXTRUDEDAREASOLID id, unwrapping one level of
        /// IFCMAPPEDITEM (how Revit shares type geometry). `extra` accumulates the
        /// mapped-item transform; identity for direct solids. Returns 0 when the body
        /// is mesh-only (Brep) — the MVP subset skips those.
        /// </summary>
        private static int ResolveExtruded(Ctx c, List<StepValue> items, out Matrix4x4 extra)
        {
            extra = Matrix4x4.identity;
            if (items == null) return 0;
            foreach (var it in items)
            {
                if (it.Kind != StepKind.Ref) continue;
                switch (c.F.TypeOf(it.Ref))
                {
                    case "IFCEXTRUDEDAREASOLID":
                        return it.Ref;
                    case "IFCMAPPEDITEM":
                    {
                        // IFCMAPPEDITEM(MappingSource, MappingTarget)
                        var mi = c.F.Args(it.Ref);
                        var map = c.F.Deref(mi[0]); // IFCREPRESENTATIONMAP(Origin, MappedRepresentation)
                        if (map == null || map.Count < 2) continue;
                        var op = c.F.Deref(mi[1]);  // IFCCARTESIANTRANSFORMATIONOPERATOR3D(Axis1, Axis2, Origin, Scale)
                        var opM = Matrix4x4.identity;
                        if (op != null && op.Count >= 4)
                        {
                            // Revit writes identity axes; translation+scale is all we honor here.
                            float s = op[3].Kind == StepKind.Number ? op[3].AsFloat : 1f;
                            opM = Matrix4x4.TRS(
                                op[2].Kind == StepKind.Ref ? Point(c, op[2]) : Vector3.zero,
                                Quaternion.identity, new Vector3(s, s, s));
                        }
                        extra = opM * Axis2Placement3D(c, map[0]).inverse;
                        var rep = c.F.Deref(map[1]); // IFCSHAPEREPRESENTATION
                        if (rep == null || rep.Count < 4 || rep[3].Kind != StepKind.List) continue;
                        int inner = FirstOfType(c, rep[3].Items, "IFCEXTRUDEDAREASOLID");
                        if (inner != 0) return inner;
                        break;
                    }
                }
            }
            return 0;
        }
    }
}
