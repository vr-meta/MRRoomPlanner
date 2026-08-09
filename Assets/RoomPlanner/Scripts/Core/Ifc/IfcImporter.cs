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
            MapVoidsAndFills(ctx);
            ImportWalls(ctx, b);
            ImportColumns(ctx, b);
            ImportSlabs(ctx, b);
            ImportStairs(ctx, b);
            return b;
        }

        // ---------------------------------------------------------------- stairs

        /// <summary>
        /// Stair flights as PARAMETERS: IfcStairFlight carries riser/tread counts and sizes
        /// (Revit writes the sizes in FEET regardless of file units — normalised here), and
        /// the flight's Brep bounding box supplies width, run direction and the base point.
        /// Landings arrive separately as ordinary slabs.
        /// </summary>
        private static void ImportStairs(Ctx c, ImportedBuilding b)
        {
            foreach (int id in c.F.OfType("IFCSTAIRFLIGHT"))
            {
                // (…, Placement5, Representation6, Tag7, NumberOfRiser8, NumberOfTreads9,
                //  RiserHeight10, TreadLength11)
                var a = c.F.Args(id);
                if (a == null || a.Count < 12
                    || a[8].Kind != StepKind.Number || a[10].Kind != StepKind.Number
                    || a[11].Kind != StepKind.Number) { b.SkippedStairs++; continue; }

                int risers = (int)a[8].Number;
                float riserH = NormalizeStairSize(a[10].AsFloat, c.Scale);
                float treadD = NormalizeStairSize(a[11].AsFloat, c.Scale);
                if (risers < 1 || riserH <= 0f || treadD <= 0f) { b.SkippedStairs++; continue; }

                if (!TryFlightFrame(c, a, risers, riserH, treadD, out var basePoint, out float yaw, out float width))
                {
                    b.SkippedStairs++;
                    continue;
                }

                b.Stairs.Add(new ImportedStair
                {
                    Base = basePoint,
                    YawDeg = yaw,
                    Width = width,
                    Risers = risers,
                    RiserHeight = riserH,
                    TreadDepth = treadD,
                    StoreyIndex = c.StoreyOfElement.TryGetValue(id, out int s) ? s : -1,
                });
            }
        }

        /// <summary>
        /// RiserHeight/TreadLength land in one of three unit regimes in the wild: honest
        /// file units (mm), metres, or Revit's internal feet. A real riser is 0.12–0.25 m —
        /// pick the interpretation that lands there.
        /// </summary>
        public static float NormalizeStairSize(float raw, float fileScale)
        {
            float asFile = raw * fileScale;                    // e.g. mm → m
            if (asFile > 0.05f && asFile < 1.2f) return asFile;
            if (raw > 0.05f && raw < 1.2f)
            {
                float asFeet = raw * 0.3048f;
                // 0.574 ft = 175 mm (plausible); 0.574 m is no riser/tread on Earth
                return raw > 0.35f && asFeet > 0.05f && asFeet < 0.35f ? asFeet : raw;
            }
            return asFile;
        }

        /// <summary>
        /// Base point / yaw / width from the flight's Brep points, measured in the solid's
        /// LOCAL space: the horizontal axis whose extent best matches the run length is the
        /// run; ascent points toward the z-heavy end.
        /// </summary>
        private static bool TryFlightFrame(Ctx c, List<StepValue> flightArgs,
            int risers, float riserH, float treadD, out Vector3 basePoint, out float yaw, out float width)
        {
            basePoint = default; yaw = 0f; width = 1f;
            var place = Placement(c, flightArgs[5]);

            var pts = BrepLocalPoints(c, flightArgs[6]);
            if (pts == null || pts.Count < 4) return false;

            Vector3 min = pts[0], max = pts[0];
            foreach (var p in pts) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            var ext = max - min;

            float runMeters = (risers - 1) * treadD;
            float runFile = runMeters / c.Scale;               // compare in file units
            bool runIsX = Mathf.Abs(ext.x - runFile) <= Mathf.Abs(ext.y - runFile);
            float widthFile = runIsX ? ext.y : ext.x;
            width = widthFile * c.Scale;

            // ascend toward the end whose points sit higher (average z of each end slice)
            float lo = runIsX ? min.x : min.y, hi = runIsX ? max.x : max.y;
            float band = Mathf.Max((hi - lo) * 0.2f, 1e-3f);
            float zLo = 0f, zHi = 0f; int nLo = 0, nHi = 0;
            foreach (var p in pts)
            {
                float r = runIsX ? p.x : p.y;
                if (r < lo + band) { zLo += p.z; nLo++; }
                else if (r > hi - band) { zHi += p.z; nHi++; }
            }
            bool ascendsToHi = nLo == 0 || nHi == 0 || zHi / Mathf.Max(1, nHi) >= zLo / Mathf.Max(1, nLo);

            // local frame: start of the run at its centerline, on the bottom plane
            float runStart = ascendsToHi ? lo : hi;
            float widthMid = runIsX ? (min.y + max.y) * 0.5f : (min.x + max.x) * 0.5f;
            var baseLocal = runIsX ? new Vector3(runStart, widthMid, min.z)
                                   : new Vector3(widthMid, runStart, min.z);
            var stepLocal = runIsX ? new Vector3(ascendsToHi ? 1f : -1f, 0f, 0f)
                                   : new Vector3(0f, ascendsToHi ? 1f : -1f, 0f);

            var baseWorldFile = place.MultiplyPoint3x4(baseLocal);
            var dirWorldFile = place.MultiplyVector(stepLocal);
            basePoint = ToUnity(c, baseWorldFile);
            var dirUnity = new Vector3(dirWorldFile.x, 0f, dirWorldFile.y);
            if (dirUnity.sqrMagnitude < 1e-8f) return false;
            yaw = Mathf.Atan2(dirUnity.x, dirUnity.z) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>All vertices of the body's FacetedBrep(s), in SOLID-LOCAL coordinates.</summary>
        private static List<Vector3> BrepLocalPoints(Ctx c, StepValue pdsRef)
        {
            var items = FindRepresentation(c, pdsRef, "Body");
            if (items == null) return null;
            var pts = new List<Vector3>();
            foreach (var it in items)
            {
                if (it.Kind != StepKind.Ref || c.F.TypeOf(it.Ref) != "IFCFACETEDBREP") continue;
                var brep = c.F.Args(it.Ref);            // (Outer shell)
                var shell = c.F.Deref(brep[0]);         // IFCCLOSEDSHELL((faces))
                if (shell == null || shell.Count < 1 || shell[0].Kind != StepKind.List) continue;
                foreach (var faceRef in shell[0].Items)
                {
                    var face = c.F.Deref(faceRef);      // IFCFACE((bounds))
                    if (face == null || face.Count < 1 || face[0].Kind != StepKind.List) continue;
                    foreach (var boundRef in face[0].Items)
                    {
                        var bound = c.F.Deref(boundRef); // IFCFACEOUTERBOUND(loop, orientation)
                        if (bound == null || bound.Count < 1) continue;
                        var loop = c.F.Deref(bound[0]);  // IFCPOLYLOOP((points))
                        if (loop == null || loop.Count < 1 || loop[0].Kind != StepKind.List) continue;
                        foreach (var ptRef in loop[0].Items)
                            pts.Add(Point(c, ptRef));
                    }
                }
            }
            return pts;
        }

        private sealed class Ctx
        {
            public StepFile F;
            public float Scale;
            public readonly Dictionary<int, Matrix4x4> Placements = new();
            public readonly Dictionary<int, int> StoreyIndexByRecord = new(); // storey record id → sorted index
            public readonly Dictionary<int, int> StoreyOfElement = new();   // element id → storey index
            public readonly Dictionary<int, float> LayerThickness = new();  // element id → summed layers (file units)
            public readonly Dictionary<int, List<int>> VoidsOfElement = new(); // element id → opening ids
            public readonly Dictionary<int, int> FillerOfOpening = new();   // opening id → door/window id
        }

        private static void MapVoidsAndFills(Ctx c)
        {
            // IFCRELVOIDSELEMENT(…, RelatingBuildingElement, RelatedOpeningElement)
            foreach (int id in c.F.OfType("IFCRELVOIDSELEMENT"))
            {
                var a = c.F.Args(id);
                if (a == null || a.Count < 6 || a[4].Kind != StepKind.Ref || a[5].Kind != StepKind.Ref) continue;
                if (!c.VoidsOfElement.TryGetValue(a[4].Ref, out var list))
                    c.VoidsOfElement[a[4].Ref] = list = new List<int>();
                list.Add(a[5].Ref);
            }
            // IFCRELFILLSELEMENT(…, RelatingOpeningElement, RelatedBuildingElement)
            foreach (int id in c.F.OfType("IFCRELFILLSELEMENT"))
            {
                var a = c.F.Args(id);
                if (a == null || a.Count < 6 || a[4].Kind != StepKind.Ref || a[5].Kind != StepKind.Ref) continue;
                c.FillerOfOpening[a[4].Ref] = a[5].Ref;
            }
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
                ImportWallOpenings(c, b, id, b.Walls.Count - 1);
            }
        }

        /// <summary>
        /// Doors/windows of one wall, from its voiding IfcOpeningElements. The opening's
        /// profile corners are lifted to world space and measured against the wall axis —
        /// no assumptions about which profile dimension is which (Revit rotates them).
        /// </summary>
        private static void ImportWallOpenings(Ctx c, ImportedBuilding b, int wallId, int wallIndex)
        {
            if (!c.VoidsOfElement.TryGetValue(wallId, out var openings)) return;
            var wall = b.Walls[wallIndex];
            Vector3 a = wall.Path[0], z = wall.Path[wall.Path.Count - 1];
            var axis = new Vector2(z.x - a.x, z.z - a.z);
            float len = axis.magnitude;
            if (len < 1e-4f) return;
            axis /= len;

            foreach (int openingId in openings)
            {
                var corners = OpeningWorldCorners(c, openingId);
                if (corners == null) { b.SkippedOpenings++; continue; }

                float alongMin = float.MaxValue, alongMax = float.MinValue;
                float yMin = float.MaxValue, yMax = float.MinValue;
                foreach (var pFile in corners)
                {
                    var p = ToUnity(c, pFile);
                    float along = (p.x - a.x) * axis.x + (p.z - a.z) * axis.y;
                    alongMin = Mathf.Min(alongMin, along); alongMax = Mathf.Max(alongMax, along);
                    yMin = Mathf.Min(yMin, p.y); yMax = Mathf.Max(yMax, p.y);
                }

                bool isDoor = c.FillerOfOpening.TryGetValue(openingId, out int filler)
                    ? c.F.TypeOf(filler) == "IFCDOOR"
                    : yMin - a.y < 0.1f;                         // unfilled: floor-level = a doorway
                b.Openings.Add(new ImportedOpening
                {
                    WallIndex = wallIndex,
                    AlongFraction = Mathf.Clamp01((alongMin + alongMax) * 0.5f / len),
                    Width = alongMax - alongMin,
                    Height = yMax - yMin,
                    Sill = Mathf.Max(0f, yMin - a.y),
                    IsDoor = isDoor,
                });
            }
        }

        /// <summary>Profile outline of an opening's extruded solid, in world FILE units; null if mesh-only.</summary>
        private static List<Vector3> OpeningWorldCorners(Ctx c, int openingId)
        {
            var oa = c.F.Args(openingId);
            if (oa == null || oa.Count < 7) return null;
            var place = Placement(c, oa[5]);
            int solid = ResolveExtruded(c, FindRepresentation(c, oa[6], "Body"), out var extra);
            if (solid == 0) return null;
            var sa = c.F.Args(solid);
            var local = ProfileOutline(c, sa[0]);
            if (local == null || local.Count < 3) return null;
            var m = place * extra * Axis2Placement3D(c, sa[1]);
            var world = new List<Vector3>(local.Count * 2);
            // Both extremes of the extrusion matter: for a vertical profile plane the
            // depth spans the wall thickness, but nothing in IFC forbids the opposite.
            var dir = Direction(c, sa[2]) * sa[3].AsFloat;
            foreach (var p in local)
            {
                world.Add(m.MultiplyPoint3x4(p));
                world.Add(m.MultiplyPoint3x4(p + dir));
            }
            return world;
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

                // Holes: inner profile curves…
                foreach (var ring in ProfileVoidRings(c, sa[0]))
                    AddHoleRing(c, slab, m, ring);
                // …and IfcOpeningElements voiding this slab (how Revit writes stairwells).
                if (c.VoidsOfElement.TryGetValue(id, out var voidIds))
                    foreach (int openingId in voidIds)
                    {
                        var hole = OpeningRing(c, openingId, out var mo);
                        if (hole == null) { b.SkippedOpenings++; continue; }
                        AddHoleRing(c, slab, mo, hole);
                    }

                b.Slabs.Add(slab);
            }
        }

        private static void AddHoleRing(Ctx c, ImportedSlab slab, Matrix4x4 m, List<Vector3> localRing)
        {
            var ring = new List<Vector3>(localRing.Count);
            foreach (var p in localRing)
            {
                var u = ToUnity(c, m.MultiplyPoint3x4(p));
                u.y = slab.Level;                       // holes are vertical cuts — project to the top
                ring.Add(u);
            }
            slab.Holes.Add(ring);
        }

        /// <summary>Inner rings of an IFCARBITRARYPROFILEDEFWITHVOIDS profile (empty otherwise).</summary>
        private static List<List<Vector3>> ProfileVoidRings(Ctx c, StepValue profileRef)
        {
            var rings = new List<List<Vector3>>();
            if (profileRef.Kind != StepKind.Ref
                || c.F.TypeOf(profileRef.Ref) != "IFCARBITRARYPROFILEDEFWITHVOIDS") return rings;
            var profile = c.F.Args(profileRef.Ref);
            if (profile == null || profile.Count < 4 || profile[3].Kind != StepKind.List) return rings;
            foreach (var curveRef in profile[3].Items)
            {
                if (curveRef.Kind != StepKind.Ref || c.F.TypeOf(curveRef.Ref) != "IFCPOLYLINE") continue;
                var pts = new List<Vector3>();
                foreach (var ptRef in c.F.Args(curveRef.Ref)[0].Items)
                    pts.Add(Point(c, ptRef));
                if (pts.Count > 1 && (pts[0] - pts[pts.Count - 1]).sqrMagnitude < 1e-6f)
                    pts.RemoveAt(pts.Count - 1);
                if (pts.Count >= 3) rings.Add(pts);
            }
            return rings;
        }

        /// <summary>Ordered profile ring of an opening's solid + its full world matrix (file units).</summary>
        private static List<Vector3> OpeningRing(Ctx c, int openingId, out Matrix4x4 m)
        {
            m = Matrix4x4.identity;
            var oa = c.F.Args(openingId);
            if (oa == null || oa.Count < 7) return null;
            var place = Placement(c, oa[5]);
            int solid = ResolveExtruded(c, FindRepresentation(c, oa[6], "Body"), out var extra);
            if (solid == 0) return null;
            var sa = c.F.Args(solid);
            var ring = ProfileOutline(c, sa[0]);
            if (ring == null || ring.Count < 3) return null;
            m = place * extra * Axis2Placement3D(c, sa[1]);
            return ring;
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
