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
            MapStyles(ctx);
            ImportWalls(ctx, b);
            ImportColumns(ctx, b);
            ImportSlabs(ctx, b);
            ImportStairs(ctx, b);
            ImportBakedElements(ctx, b);
            return b;
        }

        // ---------------------------------------------------------------- baked meshes

        private static readonly (string Type, MepCategory Category)[] BakedTypes =
        {
            ("IFCFLOWTERMINAL", MepCategory.Plumbing),          // sanitary fixtures
            ("IFCFURNISHINGELEMENT", MepCategory.Furniture),    // furniture (IKEA Breps)
            ("IFCBUILDINGELEMENTPROXY", MepCategory.Proxy),     // shower boxes, decor, …
            ("IFCRAILING", MepCategory.Railing),                // stair/balcony railings
        };

        /// <summary>
        /// Elements that only exist as meshes (IfcFlowTerminal / furniture / proxies /
        /// railings) — baked Breps with the file's own colour when IfcStyledItem carries
        /// one. Pipes/wiring would be IfcFlowSegment / cable entities; this export has
        /// none. 2D-only proxies (annotation curves) are skipped with a counter.
        /// </summary>
        private static void ImportBakedElements(Ctx c, ImportedBuilding b)
        {
            foreach (var (type, category) in BakedTypes)
            foreach (int id in c.F.OfType(type))
            {
                var a = c.F.Args(id);
                if (a == null || a.Count < 7) { b.SkippedMep++; continue; }
                var place = Placement(c, a[5]);

                var verts = new List<Vector3>();
                var tris = new List<int>();
                if (!BrepWorldMesh(c, a[6], place, verts, tris, out bool hasColor, out Color color,
                        out float transparency) || verts.Count == 0)
                {
                    b.SkippedMep++;
                    continue;
                }

                // bake local around the bbox centre so a teleport is a transform shift
                Vector3 min = verts[0], max = verts[0];
                foreach (var p in verts) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                var origin = (min + max) * 0.5f;
                for (int i = 0; i < verts.Count; i++) verts[i] -= origin;

                b.Plumbing.Add(new ImportedMep
                {
                    Name = a[2].Kind == StepKind.Text ? a[2].Text : $"#{id}",
                    Category = category,
                    Origin = origin,
                    Vertices = verts,
                    Triangles = tris,
                    StoreyIndex = c.StoreyOfElement.TryGetValue(id, out int s) ? s : -1,
                    HasColor = hasColor,
                    Color = color,
                    Transparency = transparency,
                });
            }
        }

        /// <summary>
        /// Triangulated FacetedBrep(s) of a Body representation (direct or one mapped-item
        /// level), fan per polyloop, in Unity world space. Returns false if the body holds
        /// no Brep geometry. The first styled solid (IfcStyledItem on the Brep or on the
        /// mapped item) supplies the element's colour and transparency.
        /// </summary>
        private static bool BrepWorldMesh(Ctx c, StepValue pdsRef, Matrix4x4 place,
            List<Vector3> verts, List<int> tris,
            out bool hasColor, out Color color, out float transparency)
        {
            hasColor = false; color = default; transparency = 0f;
            var items = FindRepresentation(c, pdsRef, "Body");
            if (items == null) return false;
            bool any = false;
            bool foundStyle = false; Color styleColor = default; float styleTr = 0f;

            void TakeStyle(int itemId)
            {
                if (foundStyle || !c.StyleOfItem.TryGetValue(itemId, out var s)) return;
                foundStyle = true;
                styleColor = s.Color;
                styleTr = s.Transparency;
            }

            void EmitBrep(int brepId, Matrix4x4 m)
            {
                var brep = c.F.Args(brepId);              // (Outer shell)
                var shell = c.F.Deref(brep[0]);           // IFCCLOSEDSHELL((faces))
                if (shell == null || shell.Count < 1 || shell[0].Kind != StepKind.List) return;
                foreach (var faceRef in shell[0].Items)
                {
                    var face = c.F.Deref(faceRef);        // IFCFACE((bounds))
                    if (face == null || face.Count < 1 || face[0].Kind != StepKind.List) continue;
                    foreach (var boundRef in face[0].Items)
                    {
                        // outer bounds only — inner FACEBOUNDs are holes, rare on fixtures
                        if (c.F.TypeOf(boundRef.Ref) != "IFCFACEOUTERBOUND") continue;
                        var bound = c.F.Args(boundRef.Ref);   // (loop, orientation)
                        var loop = c.F.Deref(bound[0]);       // IFCPOLYLOOP((points))
                        if (loop == null || loop.Count < 1 || loop[0].Kind != StepKind.List) continue;
                        bool reversed = bound.Count > 1 && bound[1].Kind == StepKind.Enum && bound[1].Text == "F";

                        int start = verts.Count;
                        foreach (var ptRef in loop[0].Items)
                            verts.Add(ToUnity(c, m.MultiplyPoint3x4(Point(c, ptRef))));
                        int n = verts.Count - start;
                        for (int i = 1; i + 1 < n; i++)
                        {
                            // IFC loops are CCW seen from OUTSIDE (right-handed) — that maps
                            // to clockwise in Unity's left-handed frame, which IS front-facing.
                            if (reversed) { tris.Add(start); tris.Add(start + i + 1); tris.Add(start + i); }
                            else { tris.Add(start); tris.Add(start + i); tris.Add(start + i + 1); }
                        }
                        any = true;
                    }
                }
            }

            // Furniture ships as SweptSolid stacks (IKEA sofa = 7 extrusions), not Breps —
            // tessellate them into the same mesh: side quads + ear-clipped caps.
            void EmitExtruded(int solidId, Matrix4x4 m)
            {
                var sa = c.F.Args(solidId);
                if (sa == null || sa.Count < 4) return;
                var ring = ProfileOutline(c, sa[0]);
                if (ring == null || ring.Count < 3) return;
                var pm = m * Axis2Placement3D(c, sa[1]);
                var dir = Direction(c, sa[2]) * sa[3].AsFloat;

                float area2 = 0f;                      // normalize the ring CCW in-plane
                for (int i = 0; i < ring.Count; i++)
                {
                    var p0 = ring[i]; var p1 = ring[(i + 1) % ring.Count];
                    area2 += p0.x * p1.y - p1.x * p0.y;
                }
                if (area2 < 0f) ring.Reverse();
                bool up = dir.z >= 0f;                 // extrusion along the profile normal?

                int n = ring.Count;
                int lo = verts.Count;
                foreach (var p in ring) verts.Add(ToUnity(c, pm.MultiplyPoint3x4(p)));
                int hi = verts.Count;
                foreach (var p in ring) verts.Add(ToUnity(c, pm.MultiplyPoint3x4(p + dir)));

                // sides: outward for a CCW ring extruded along +normal (order survives the
                // Unity axis swap for the same reason the Brep loops above do)
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    if (up) { tris.Add(lo + i); tris.Add(lo + j); tris.Add(hi + j); tris.Add(lo + i); tris.Add(hi + j); tris.Add(hi + i); }
                    else { tris.Add(lo + j); tris.Add(lo + i); tris.Add(hi + i); tris.Add(lo + j); tris.Add(hi + i); tris.Add(hi + j); }
                }

                // caps: ear-clip the profile, establish the +normal order numerically
                var flat = new List<Vector3>(n);
                foreach (var p in ring) flat.Add(new Vector3(p.x, 0f, p.y));
                var capTris = Polygon.Triangulate(flat);
                if (capTris.Count >= 3)
                {
                    var a2 = ring[capTris[0]]; var b2 = ring[capTris[1]]; var c2 = ring[capTris[2]];
                    bool flip = (b2.x - a2.x) * (c2.y - a2.y) - (b2.y - a2.y) * (c2.x - a2.x) < 0f;
                    for (int i = 0; i + 2 < capTris.Count; i += 3)
                    {
                        int ca = capTris[i];
                        int cb = flip ? capTris[i + 2] : capTris[i + 1];
                        int cc = flip ? capTris[i + 1] : capTris[i + 2];
                        int top = up ? hi : lo, bot = up ? lo : hi;
                        tris.Add(top + ca); tris.Add(top + cb); tris.Add(top + cc);
                        tris.Add(bot + ca); tris.Add(bot + cc); tris.Add(bot + cb);
                    }
                }
                any = true;
            }

            void EmitItem(StepValue itemRef, Matrix4x4 m)
            {
                switch (c.F.TypeOf(itemRef.Ref))
                {
                    case "IFCFACETEDBREP":
                        EmitBrep(itemRef.Ref, m);
                        TakeStyle(itemRef.Ref);
                        break;
                    case "IFCEXTRUDEDAREASOLID":
                        EmitExtruded(itemRef.Ref, m);
                        TakeStyle(itemRef.Ref);
                        break;
                }
            }

            foreach (var it in items)
            {
                if (it.Kind != StepKind.Ref) continue;
                if (c.F.TypeOf(it.Ref) == "IFCMAPPEDITEM")
                {
                    var mi = c.F.Args(it.Ref);
                    var map = c.F.Deref(mi[0]);       // IFCREPRESENTATIONMAP(Origin, Rep)
                    if (map == null || map.Count < 2) continue;
                    var opM = MapOperator(c, mi[1]);   // honors mounting-rotation axes
                    var extra = opM * Axis2Placement3D(c, map[0]).inverse;
                    var rep = c.F.Deref(map[1]);
                    if (rep == null || rep.Count < 4 || rep[3].Kind != StepKind.List) continue;
                    TakeStyle(it.Ref);
                    foreach (var inner in rep[3].Items)
                        if (inner.Kind == StepKind.Ref)
                            EmitItem(inner, place * extra);
                }
                else
                {
                    EmitItem(it, place);
                }
            }
            hasColor = foundStyle;
            color = styleColor;
            transparency = styleTr;
            return any;
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
                    // Monolithic Revit stairs read as a wall of concrete in MR — imports
                    // default to the waist-slab kind, the familiar stairwell flight
                    // (headset feedback 2026-08-10); the choice round-trips through
                    // project files either way.
                    Kind = RoomPlanner.Stairs.StairKind.Waist,
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
            public readonly Dictionary<int, int> TypeOfElement = new();     // element id → style/type record
            public readonly Dictionary<int, (Color Color, float Transparency)> StyleOfItem = new();
        }

        /// <summary>
        /// IFCSTYLEDITEM(Item, Styles, Name) → geometry-item id → surface colour (+
        /// transparency). Chain: PresentationStyleAssignment → SurfaceStyle →
        /// SurfaceStyleRendering/Shading → ColourRgb.
        /// </summary>
        private static void MapStyles(Ctx c)
        {
            foreach (int id in c.F.OfType("IFCSTYLEDITEM"))
            {
                var a = c.F.Args(id);
                if (a == null || a.Count < 2 || a[0].Kind != StepKind.Ref || a[1].Kind != StepKind.List) continue;
                foreach (var assignRef in a[1].Items)
                {
                    var assign = c.F.Deref(assignRef);   // IFCPRESENTATIONSTYLEASSIGNMENT((styles))
                    if (assign == null || assign.Count < 1 || assign[0].Kind != StepKind.List) continue;
                    foreach (var styleRef in assign[0].Items)
                    {
                        if (styleRef.Kind != StepKind.Ref || c.F.TypeOf(styleRef.Ref) != "IFCSURFACESTYLE") continue;
                        var surf = c.F.Args(styleRef.Ref);   // (Name, Side, (renderings))
                        if (surf == null || surf.Count < 3 || surf[2].Kind != StepKind.List) continue;
                        foreach (var rendRef in surf[2].Items)
                        {
                            if (rendRef.Kind != StepKind.Ref) continue;
                            string t = c.F.TypeOf(rendRef.Ref);
                            if (t != "IFCSURFACESTYLERENDERING" && t != "IFCSURFACESTYLESHADING") continue;
                            var rend = c.F.Args(rendRef.Ref);   // (SurfaceColour, Transparency?, …)
                            if (rend == null || rend.Count < 1 || rend[0].Kind != StepKind.Ref) continue;
                            var rgb = c.F.Args(rend[0].Ref);    // IFCCOLOURRGB(Name, R, G, B)
                            if (rgb == null || rgb.Count < 4) continue;
                            float tr = rend.Count > 1 && rend[1].Kind == StepKind.Number
                                ? Mathf.Clamp01(rend[1].AsFloat) : 0f;
                            c.StyleOfItem[a[0].Ref] =
                                (new Color(rgb[1].AsFloat, rgb[2].AsFloat, rgb[3].AsFloat), tr);
                            break;
                        }
                        if (c.StyleOfItem.ContainsKey(a[0].Ref)) break;
                    }
                    if (c.StyleOfItem.ContainsKey(a[0].Ref)) break;
                }
            }
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
            // IFCRELDEFINESBYTYPE(…, RelatedObjects, RelatingType) — doors need their
            // IfcDoorStyle for the swing side.
            foreach (int id in c.F.OfType("IFCRELDEFINESBYTYPE"))
            {
                var a = c.F.Args(id);
                if (a == null || a.Count < 6 || a[4].Kind != StepKind.List || a[5].Kind != StepKind.Ref) continue;
                foreach (var el in a[4].Items)
                    if (el.Kind == StepKind.Ref)
                        c.TypeOfElement[el.Ref] = a[5].Ref;
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

            // IFCRELAGGREGATES(…, RelatingObject, RelatedObjects): stair flights and
            // landings live under their IfcStair, not in the storey container — they
            // inherit the parent's storey. Two passes cover parent-before-child order.
            for (int pass = 0; pass < 2; pass++)
                foreach (int id in c.F.OfType("IFCRELAGGREGATES"))
                {
                    var a = c.F.Args(id);
                    if (a == null || a.Count < 6 || a[4].Kind != StepKind.Ref || a[5].Kind != StepKind.List) continue;
                    if (!c.StoreyOfElement.TryGetValue(a[4].Ref, out int storey)) continue;
                    foreach (var el in a[5].Items)
                        if (el.Kind == StepKind.Ref && !c.StoreyOfElement.ContainsKey(el.Ref))
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
                Vector3 swingDir = default, hingeDir = default;
                if (isDoor && filler != 0) DoorSwing(c, filler, out swingDir, out hingeDir);
                b.Openings.Add(new ImportedOpening
                {
                    WallIndex = wallIndex,
                    AlongFraction = Mathf.Clamp01((alongMin + alongMax) * 0.5f / len),
                    Width = alongMax - alongMin,
                    Height = yMax - yMin,
                    Sill = Mathf.Max(0f, yMin - a.y),
                    IsDoor = isDoor,
                    SwingDir = swingDir,
                    HingeDir = hingeDir,
                });
            }
        }

        /// <summary>
        /// Which way a door opens, from its IfcDoorStyle + placement axes (IFC convention:
        /// the leaf swings toward the local +Y; hinges sit on the ±X side per OperationType,
        /// and Revit encodes mirrored instances in the placement itself). Both vectors are
        /// world-horizontal Unity directions; zero = unknown, the leaf stays closed.
        /// </summary>
        private static bool DoorSwing(Ctx c, int doorId, out Vector3 swingDir, out Vector3 hingeDir)
        {
            swingDir = default; hingeDir = default;
            if (!c.TypeOfElement.TryGetValue(doorId, out int styleId)) return false;
            if (c.F.TypeOf(styleId) != "IFCDOORSTYLE") return false;
            var sa = c.F.Args(styleId);   // (…, Tag7, OperationType8, …)
            if (sa == null || sa.Count < 9 || sa[8].Kind != StepKind.Enum) return false;
            bool left = sa[8].Text == "SINGLE_SWING_LEFT";
            bool right = sa[8].Text == "SINGLE_SWING_RIGHT";
            if (!left && !right) return false;   // double/sliding doors keep the closed leaf

            var da = c.F.Args(doorId);
            if (da == null || da.Count < 6) return false;
            var place = Placement(c, da[5]);
            Vector4 xf = place.GetColumn(0), yf = place.GetColumn(1);   // file space (Z up)
            var along = new Vector3(xf.x, 0f, xf.y);                    // file XY → Unity XZ
            var swing = new Vector3(yf.x, 0f, yf.y);
            if (along.sqrMagnitude < 1e-8f || swing.sqrMagnitude < 1e-8f) return false;
            swingDir = swing.normalized;
            // hinge on the -X side for LEFT → the leaf runs toward +X, and vice versa
            hingeDir = (left ? along : -along).normalized;
            return true;
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
                if (solid == 0)
                {
                    // Stair landings (Revit "Monolithic Landing") are Brep-only slabs:
                    // recover the outline from the shell's top face instead of skipping.
                    if (BrepTopRing(c, a[6], place, out var topRing, out float ringTopZ, out float ringThick))
                    {
                        var landing = new ImportedSlab
                        {
                            Thickness = ringThick * c.Scale,
                            Level = ringTopZ * c.Scale,
                            StoreyIndex = c.StoreyOfElement.TryGetValue(id, out int ls) ? ls : -1,
                        };
                        foreach (var p in topRing)
                        {
                            var u = ToUnity(c, p);
                            u.y = landing.Level;
                            landing.Outline.Add(u);
                        }
                        b.Slabs.Add(landing);
                    }
                    else b.SkippedSlabs++;
                    continue;
                }
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

        /// <summary>
        /// Outline of a Brep-only slab: the face of its closed shell whose vertices all sit
        /// on the highest Z plane (world FILE units, after placement). Thickness is the full
        /// shell height — honest for landings, whose soffit slopes into the flights.
        /// </summary>
        private static bool BrepTopRing(Ctx c, StepValue pdsRef, Matrix4x4 place,
            out List<Vector3> ring, out float topZ, out float thickness)
        {
            ring = null; topZ = 0f; thickness = 0f;
            var items = FindRepresentation(c, pdsRef, "Body");
            if (items == null) return false;

            var faces = new List<List<Vector3>>();
            float zMin = float.MaxValue, zMax = float.MinValue;
            foreach (var it in items)
            {
                if (it.Kind != StepKind.Ref || c.F.TypeOf(it.Ref) != "IFCFACETEDBREP") continue;
                var brep = c.F.Args(it.Ref);              // (Outer shell)
                var shell = c.F.Deref(brep[0]);           // IFCCLOSEDSHELL((faces))
                if (shell == null || shell.Count < 1 || shell[0].Kind != StepKind.List) continue;
                foreach (var faceRef in shell[0].Items)
                {
                    var face = c.F.Deref(faceRef);        // IFCFACE((bounds))
                    if (face == null || face.Count < 1 || face[0].Kind != StepKind.List) continue;
                    foreach (var boundRef in face[0].Items)
                    {
                        if (c.F.TypeOf(boundRef.Ref) != "IFCFACEOUTERBOUND") continue;
                        var bound = c.F.Args(boundRef.Ref);
                        var loop = c.F.Deref(bound[0]);   // IFCPOLYLOOP((points))
                        if (loop == null || loop.Count < 1 || loop[0].Kind != StepKind.List) continue;
                        var pts = new List<Vector3>(loop[0].Items.Count);
                        foreach (var ptRef in loop[0].Items)
                        {
                            var p = place.MultiplyPoint3x4(Point(c, ptRef));
                            zMin = Mathf.Min(zMin, p.z);
                            zMax = Mathf.Max(zMax, p.z);
                            pts.Add(p);
                        }
                        if (pts.Count >= 3) faces.Add(pts);
                    }
                }
            }
            if (faces.Count == 0 || zMax - zMin < 1e-4f) return false;

            float tol = Mathf.Max(1e-4f, (zMax - zMin) * 0.02f);
            float bestArea = 0f;
            foreach (var pts in faces)
            {
                bool onTop = true;
                foreach (var p in pts) onTop &= zMax - p.z <= tol;
                if (!onTop) continue;
                float area2 = 0f;                          // signed, in the XY plane
                for (int i = 0; i < pts.Count; i++)
                {
                    var p0 = pts[i]; var p1 = pts[(i + 1) % pts.Count];
                    area2 += p0.x * p1.y - p1.x * p0.y;
                }
                if (Mathf.Abs(area2) <= Mathf.Abs(bestArea)) continue;
                bestArea = area2;
                ring = pts;
            }
            if (ring == null) return false;
            if (bestArea < 0f) ring.Reverse();             // outlines are CCW like IFC profiles
            topZ = zMax;
            thickness = zMax - zMin;
            return true;
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
                case "IFCCIRCLEPROFILEDEF": // furniture legs and the like — a 12-gon is plenty
                {
                    float r = profile[3].AsFloat;
                    if (r <= 0f) return null;
                    var m2 = Axis2Placement2D(c, profile[2]);
                    var pts = new List<Vector3>(12);
                    for (int i = 0; i < 12; i++)
                    {
                        float ang = i * Mathf.PI * 2f / 12f;
                        pts.Add(m2.MultiplyPoint3x4(new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, 0f)));
                    }
                    return pts;
                }
                case "IFCARBITRARYCLOSEDPROFILEDEF":
                case "IFCARBITRARYPROFILEDEFWITHVOIDS": // voids ignored by the MVP subset
                {
                    if (profile[2].Kind != StepKind.Ref) return null;
                    var pts = c.F.TypeOf(profile[2].Ref) switch
                    {
                        "IFCPOLYLINE" => PolylinePoints(c, profile[2].Ref),
                        "IFCCOMPOSITECURVE" => CompositePoints(c, profile[2].Ref),
                        _ => null,
                    };
                    if (pts == null) return null;
                    // IFC closes the ring by repeating the first point — our outlines don't.
                    if (pts.Count > 1 && (pts[0] - pts[pts.Count - 1]).sqrMagnitude < 1e-6f)
                        pts.RemoveAt(pts.Count - 1);
                    return pts.Count >= 3 ? pts : null;
                }
                default:
                    return null;
            }
        }

        private static List<Vector3> PolylinePoints(Ctx c, int polyId)
        {
            var a = c.F.Args(polyId);
            if (a == null || a.Count < 1 || a[0].Kind != StepKind.List) return null;
            var pts = new List<Vector3>();
            foreach (var ptRef in a[0].Items) pts.Add(Point(c, ptRef));
            return pts;
        }

        /// <summary>
        /// IFCCOMPOSITECURVE((segments), …) → ring points. Polyline pieces come verbatim;
        /// trimmed arcs contribute their trim CARTESIANPOINTs — a chord. In Revit exports
        /// the arcs are small corner fillets (sofa arms), where a chord is invisible.
        /// </summary>
        private static List<Vector3> CompositePoints(Ctx c, int curveId)
        {
            var a = c.F.Args(curveId);
            if (a == null || a.Count < 1 || a[0].Kind != StepKind.List) return null;
            var pts = new List<Vector3>();
            void Push(Vector3 p)
            {
                if (pts.Count == 0 || (pts[pts.Count - 1] - p).sqrMagnitude > 1e-6f) pts.Add(p);
            }
            foreach (var segRef in a[0].Items)
            {
                // IFCCOMPOSITECURVESEGMENT(Transition, SameSense, ParentCurve)
                var seg = c.F.Deref(segRef);
                if (seg == null || seg.Count < 3 || seg[2].Kind != StepKind.Ref) continue;
                int parent = seg[2].Ref;
                switch (c.F.TypeOf(parent))
                {
                    case "IFCPOLYLINE":
                    {
                        var pl = PolylinePoints(c, parent);
                        if (pl == null) break;
                        bool same = seg[1].Kind != StepKind.Enum || seg[1].Text != "F";
                        if (!same) pl.Reverse();
                        foreach (var p in pl) Push(p);
                        break;
                    }
                    case "IFCTRIMMEDCURVE":
                    {
                        // (BasisCurve, Trim1, Trim2, SenseAgreement, Master) — take the
                        // cartesian trim points when present; parameter-only trims are
                        // skipped (the neighbours' endpoints still close the ring).
                        var tc = c.F.Args(parent);
                        if (tc == null || tc.Count < 3) break;
                        Vector3? t1 = TrimPoint(c, tc[1]), t2 = TrimPoint(c, tc[2]);
                        bool same = seg[1].Kind != StepKind.Enum || seg[1].Text != "F";
                        if (same) { if (t1 != null) Push(t1.Value); if (t2 != null) Push(t2.Value); }
                        else { if (t2 != null) Push(t2.Value); if (t1 != null) Push(t1.Value); }
                        break;
                    }
                }
            }
            return pts;
        }

        private static Vector3? TrimPoint(Ctx c, StepValue trim)
        {
            if (trim == null || trim.Kind != StepKind.List) return null;
            foreach (var it in trim.Items)
                if (it.Kind == StepKind.Ref && c.F.TypeOf(it.Ref) == "IFCCARTESIANPOINT")
                    return Point(c, it);
            return null;
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

        /// <summary>
        /// IFCCARTESIANTRANSFORMATIONOPERATOR3D(Axis1, Axis2, LocalOrigin, Scale, Axis3) →
        /// matrix. The AXES matter: wall-mounted Revit families store their mounting
        /// rotation here, and ignoring it lays shower columns flat on the floor.
        /// </summary>
        private static Matrix4x4 MapOperator(Ctx c, StepValue opRef)
        {
            var op = c.F.Deref(opRef);
            if (op == null || op.Count < 4) return Matrix4x4.identity;
            float s = op[3].Kind == StepKind.Number ? op[3].AsFloat : 1f;
            var origin = op[2].Kind == StepKind.Ref ? Point(c, op[2]) : Vector3.zero;
            var x = op[0].Kind == StepKind.Ref ? Direction(c, op[0]) : Vector3.right;
            var y = op[1].Kind == StepKind.Ref ? Direction(c, op[1]) : Vector3.up;
            var z = op.Count > 4 && op[4].Kind == StepKind.Ref ? Direction(c, op[4]) : Vector3.Cross(x, y);
            if (z.sqrMagnitude < 1e-12f) z = new Vector3(0f, 0f, 1f);
            return Frame(x * s, y * s, z * s, origin);
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
