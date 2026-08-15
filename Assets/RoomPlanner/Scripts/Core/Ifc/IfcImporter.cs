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
            MapMaterials(ctx);
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
            // coverage v2 (Duplex/SampleHouse gap analysis 2026-08-11): IFC4 renames
            // and structural/finish elements we can at least SHOW as baked meshes
            ("IFCFURNITURE", MepCategory.Furniture),            // IFC4 furniture
            ("IFCSANITARYTERMINAL", MepCategory.Plumbing),      // IFC4 basins/WCs
            ("IFCFLOWCONTROLLER", MepCategory.Proxy),           // switches, valves
            ("IFCFLOWMOVINGDEVICE", MepCategory.Plumbing),      // pumps, fans
            ("IFCENERGYCONVERSIONDEVICE", MepCategory.Proxy),   // boilers, AHUs
            ("IFCCOVERING", MepCategory.Proxy),                 // finish layers, ceilings
            ("IFCBEAM", MepCategory.Proxy),
            ("IFCMEMBER", MepCategory.Proxy),                   // frame members (pergolas)
            ("IFCPLATE", MepCategory.Proxy),                    // curtain-wall panels
            ("IFCCURTAINWALL", MepCategory.Proxy),              // glazed facades
            ("IFCROOF", MepCategory.Proxy),                     // usually an aggregate; baked when it owns geometry
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
                if (!BakeProduct(c, b, id, category)) b.SkippedMep++;
        }

        /// <summary>Bake one product's Body geometry as a mesh element. Also the visual
        /// FALLBACK for elements whose parametric route failed (Brep-only walls,
        /// non-parametric stair flights) — better a frozen mesh than a hole in the house
        /// (coverage v2, 2026-08-11). False when the product has no meshable body.</summary>
        private static bool BakeProduct(Ctx c, ImportedBuilding b, int id, MepCategory category)
        {
            var a = c.F.Args(id);
            if (a == null || a.Count < 7) return false;
            var place = Placement(c, a[5]);

            var verts = new List<Vector3>();
            var tris = new List<int>();
            var spans = new List<(int Start, int Count, StyleRec Style, bool Styled)>();
            if (!BrepWorldMesh(c, a[6], place, verts, tris, spans, out bool hasColor, out Color color,
                    out float transparency) || verts.Count == 0)
                return false;

            // bake local around the bbox centre so a teleport is a transform shift
            Vector3 min = verts[0], max = verts[0];
            foreach (var p in verts) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            var origin = (min + max) * 0.5f;
            string name = a[2].Kind == StepKind.Text ? a[2].Text : $"#{id}";

            // Electrical conversion (#79): Revit outlets are proxy plates (5×5×1 cm).
            // They become NATIVE electrical fixtures — editable, wireable, in the panel
            // BOM — instead of invisible-small baked meshes.
            if (category == MepCategory.Proxy && ElectricalImport.IsOutlet(name))
            {
                b.Outlets.Add(new ImportedOutlet
                {
                    Name = name,
                    Position = origin,
                    Normal = ElectricalImport.PlateNormal(max - min),
                    StoreyIndex = c.StoreyOfElement.TryGetValue(id, out int os) ? os : -1,
                });
                return true;
            }

            for (int i = 0; i < verts.Count; i++) verts[i] -= origin;

            var mep = new ImportedMep
            {
                Name = name,
                Category = category,
                Origin = origin,
                Vertices = verts,
                Triangles = tris,
                StoreyIndex = c.StoreyOfElement.TryGetValue(id, out int s) ? s : -1,
                HasColor = hasColor,
                Color = color,
                Transparency = transparency,
            };

            // The product's material is the fallback name/colour for parts the geometry
            // did not style — 152 of ~260 products in the reference export (design/29 §1.3).
            bool hasMat = c.MaterialOfElement.TryGetValue(id, out int matId);
            string matName = hasMat && c.MaterialName.TryGetValue(matId, out string mn) ? mn : null;
            StyleRec matStyle = default;
            bool matStyled = hasMat && c.StyleOfMaterial.TryGetValue(matId, out matStyle);

            BuildParts(tris, spans, matName, matStyled, matStyle, mep.Parts);
            if (!mep.HasColor && matStyled)
            {
                mep.HasColor = true;
                mep.Color = matStyle.Color;
                mep.Transparency = matStyle.Transparency;
            }
            BoxUv.Fill(verts, tris, mep.Uvs);

            b.Plumbing.Add(mep);
            return true;
        }

        /// <summary>
        /// Group the emitted spans by style into contiguous parts, reordering
        /// <paramref name="tris"/> so each part is a plain (start, count) range — the
        /// project file then stores six fields per part instead of a second index list
        /// (design/29 §2). Spans without a style of their own fall back to the product's
        /// material; a product with a single style yields exactly one part, i.e. the
        /// behaviour that existed before parts.
        /// </summary>
        private static void BuildParts(List<int> tris,
            List<(int Start, int Count, StyleRec Style, bool Styled)> spans,
            string materialName, bool materialStyled, StyleRec materialStyle,
            List<MepPart> parts)
        {
            parts.Clear();
            if (tris.Count == 0) return;

            // key = style name (or "" for the material fallback); order of first appearance
            var order = new List<string>();
            var byKey = new Dictionary<string, MepPart>();
            var indices = new Dictionary<string, List<int>>();

            void Add(string key, MepPart part, int start, int count)
            {
                if (!byKey.ContainsKey(key))
                {
                    byKey[key] = part;
                    indices[key] = new List<int>();
                    order.Add(key);
                }
                var list = indices[key];
                for (int i = 0; i < count; i++) list.Add(tris[start + i]);
            }

            int covered = 0;
            foreach (var span in spans)
            {
                covered += span.Count;
                var part = span.Styled
                    ? new MepPart
                    {
                        Name = span.Style.Name ?? materialName,
                        HasColor = true,
                        Color = span.Style.Color,
                        Transparency = span.Style.Transparency,
                    }
                    : new MepPart
                    {
                        Name = materialName,
                        HasColor = materialStyled,
                        Color = materialStyled ? materialStyle.Color : default,
                        Transparency = materialStyled ? materialStyle.Transparency : 0f,
                    };
                Add(Key(part), part, span.Start, span.Count);
            }

            // triangles nobody claimed (a geometry kind that emits outside EmitItem)
            if (covered < tris.Count)
            {
                var rest = new MepPart
                {
                    Name = materialName,
                    HasColor = materialStyled,
                    Color = materialStyled ? materialStyle.Color : default,
                    Transparency = materialStyled ? materialStyle.Transparency : 0f,
                };
                Add(Key(rest), rest, covered, tris.Count - covered);
            }

            tris.Clear();
            foreach (string key in order)
            {
                var part = byKey[key];
                var list = indices[key];
                part.TriStart = tris.Count;
                part.TriCount = list.Count;
                tris.AddRange(list);
                parts.Add(part);
            }
        }

        /// <summary>Identity of a part for merging: the style name when the file gives
        /// one, otherwise the colour (Revit reuses unnamed styles per colour).</summary>
        private static string Key(MepPart p) =>
            !string.IsNullOrEmpty(p.Name)
                ? p.Name
                : (p.HasColor ? $"#{ColorUtility.ToHtmlStringRGB(p.Color)}/{p.Transparency:0.00}" : "");

        /// <summary>
        /// Triangulated FacetedBrep(s) of a Body representation (direct or one mapped-item
        /// level), fan per polyloop, in Unity world space. Returns false if the body holds
        /// no Brep geometry. The first styled solid (IfcStyledItem on the Brep or on the
        /// mapped item) supplies the element's colour and transparency.
        /// </summary>
        private static bool BrepWorldMesh(Ctx c, StepValue pdsRef, Matrix4x4 place,
            List<Vector3> verts, List<int> tris, List<(int Start, int Count, StyleRec Style, bool Styled)> spans,
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

            void EmitFaces(StepValue faceList, Matrix4x4 m)
            {
                foreach (var faceRef in faceList.Items)
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

            void EmitBrep(int brepId, Matrix4x4 m)
            {
                var brep = c.F.Args(brepId);              // (Outer shell)
                var shell = c.F.Deref(brep[0]);           // IFCCLOSEDSHELL((faces))
                if (shell == null || shell.Count < 1 || shell[0].Kind != StepKind.List) return;
                EmitFaces(shell[0], m);
            }

            // Open face soups (the SMEG fridge ships this way): face sets, no closed shell.
            void EmitSurfaceModel(int modelId, Matrix4x4 m)
            {
                var a = c.F.Args(modelId);                // ((face sets))
                if (a == null || a.Count < 1 || a[0].Kind != StepKind.List) return;
                foreach (var setRef in a[0].Items)
                {
                    if (setRef.Kind != StepKind.Ref) continue;
                    var set = c.F.Args(setRef.Ref);       // IFCCONNECTEDFACESET((faces))
                    if (set == null || set.Count < 1 || set[0].Kind != StepKind.List) continue;
                    EmitFaces(set[0], m);
                }
            }

            // Furniture ships as SweptSolid stacks (IKEA sofa = 7 extrusions), not Breps —
            // tessellate them into the same mesh: side quads + ear-clipped caps. Profile
            // VOIDS matter: a window trim is a frame with a hole, and ignoring the inner
            // rings would board the window up with a solid slab.
            void EmitExtruded(int solidId, Matrix4x4 m)
            {
                var sa = c.F.Args(solidId);
                if (sa == null || sa.Count < 4) return;
                var ring = ProfileOutline(c, sa[0]);
                if (ring == null || ring.Count < 3) return;
                var holes = ProfileVoidRings(c, sa[0]);
                var pm = m * Axis2Placement3D(c, sa[1]);
                var dir = Direction(c, sa[2]) * sa[3].AsFloat;
                bool up = dir.z >= 0f;                 // extrusion along the profile normal?

                // sides: outward for a CCW outer ring extruded along +normal (order
                // survives the Unity axis swap for the same reason the Brep loops do);
                // hole rings run CW so the same emitter faces them into the cavity
                void SideWalls(List<Vector3> r, bool ccw)
                {
                    float area2 = 0f;
                    for (int i = 0; i < r.Count; i++)
                    {
                        var p0 = r[i]; var p1 = r[(i + 1) % r.Count];
                        area2 += p0.x * p1.y - p1.x * p0.y;
                    }
                    if (ccw != area2 > 0f) r.Reverse();

                    int n = r.Count;
                    int lo = verts.Count;
                    foreach (var p in r) verts.Add(ToUnity(c, pm.MultiplyPoint3x4(p)));
                    int hi = verts.Count;
                    foreach (var p in r) verts.Add(ToUnity(c, pm.MultiplyPoint3x4(p + dir)));
                    for (int i = 0; i < n; i++)
                    {
                        int j = (i + 1) % n;
                        if (up) { tris.Add(lo + i); tris.Add(lo + j); tris.Add(hi + j); tris.Add(lo + i); tris.Add(hi + j); tris.Add(hi + i); }
                        else { tris.Add(lo + j); tris.Add(lo + i); tris.Add(hi + i); tris.Add(lo + j); tris.Add(hi + i); tris.Add(hi + j); }
                    }
                }

                SideWalls(ring, ccw: true);
                foreach (var hole in holes) SideWalls(hole, ccw: false);

                // caps: ear-clip the profile with its holes bridged in, establish the
                // +normal order numerically from the first emitted triangle
                var flat = new List<Vector3>(ring.Count);
                foreach (var p in ring) flat.Add(new Vector3(p.x, 0f, p.y));
                var flatHoles = new List<IReadOnlyList<Vector3>>(holes.Count);
                foreach (var hole in holes)
                {
                    var fh = new List<Vector3>(hole.Count);
                    foreach (var p in hole) fh.Add(new Vector3(p.x, 0f, p.y));
                    flatHoles.Add(fh);
                }
                List<Vector3> mergedFlat;
                var capTris = holes.Count > 0
                    ? Polygon.TriangulateWithHoles(flat, flatHoles, out mergedFlat)
                    : Polygon.Triangulate(mergedFlat = flat);
                if (capTris.Count >= 3)
                {
                    var merged = new List<Vector3>(mergedFlat.Count);
                    foreach (var p in mergedFlat) merged.Add(new Vector3(p.x, p.z, 0f));

                    var a2 = merged[capTris[0]]; var b2 = merged[capTris[1]]; var c2 = merged[capTris[2]];
                    bool flip = (b2.x - a2.x) * (c2.y - a2.y) - (b2.y - a2.y) * (c2.x - a2.x) < 0f;

                    int lo = verts.Count;
                    foreach (var p in merged) verts.Add(ToUnity(c, pm.MultiplyPoint3x4(p)));
                    int hi = verts.Count;
                    foreach (var p in merged) verts.Add(ToUnity(c, pm.MultiplyPoint3x4(p + dir)));

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

            // Each geometry item is one SPAN of triangles with one style — that is what
            // turns a sofa into leather + aluminium legs instead of one leather blob
            // (design/29 §2). A mapped item's own style is inherited by its contents.
            void Span(int start, int itemId, bool hasInherited, StyleRec inherited)
            {
                int count = tris.Count - start;
                if (count <= 0) return;
                if (c.StyleOfItem.TryGetValue(itemId, out var own))
                    spans?.Add((start, count, own, true));
                else
                    spans?.Add((start, count, inherited, hasInherited));
            }

            void EmitItem(StepValue itemRef, Matrix4x4 m, bool hasInherited, StyleRec inherited)
            {
                int start = tris.Count;
                switch (c.F.TypeOf(itemRef.Ref))
                {
                    case "IFCFACETEDBREP":
                        EmitBrep(itemRef.Ref, m);
                        break;
                    case "IFCEXTRUDEDAREASOLID":
                        EmitExtruded(itemRef.Ref, m);
                        break;
                    case "IFCFACEBASEDSURFACEMODEL":
                        EmitSurfaceModel(itemRef.Ref, m);
                        break;
                    default:
                        return;
                }
                TakeStyle(itemRef.Ref);
                Span(start, itemRef.Ref, hasInherited, inherited);
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
                    bool mapped = c.StyleOfItem.TryGetValue(it.Ref, out var mappedStyle);
                    foreach (var inner in rep[3].Items)
                        if (inner.Kind == StepKind.Ref)
                            EmitItem(inner, place * extra, mapped, mappedStyle);
                }
                else
                {
                    EmitItem(it, place, false, default);
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
                    || a[11].Kind != StepKind.Number)
                {
                    // non-parametric flight (no riser/tread numbers) → bake the mesh
                    if (!BakeProduct(c, b, id, MepCategory.Proxy)) b.SkippedStairs++;
                    continue;
                }

                int risers = (int)a[8].Number;
                float riserH = NormalizeStairSize(a[10].AsFloat, c.Scale);
                float treadD = NormalizeStairSize(a[11].AsFloat, c.Scale);
                if (risers < 1 || riserH <= 0f || treadD <= 0f)
                {
                    if (!BakeProduct(c, b, id, MepCategory.Proxy)) b.SkippedStairs++;
                    continue;
                }

                if (!TryFlightFrame(c, a, risers, riserH, treadD, out var basePoint, out float yaw, out float width))
                {
                    if (!BakeProduct(c, b, id, MepCategory.Proxy)) b.SkippedStairs++;
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
            public readonly Dictionary<int, StyleRec> StyleOfItem = new();  // geometry item id → style
            public readonly Dictionary<int, StyleRec> StyleOfMaterial = new(); // IfcMaterial id → style
            public readonly Dictionary<int, int> MaterialOfElement = new(); // element/type id → IfcMaterial id
            public readonly Dictionary<int, string> MaterialName = new();   // IfcMaterial id → name
        }

        /// <summary>One IfcSurfaceStyle as we use it: the NAME (the only reliable signal
        /// about what the thing is made of — design/29 §1), the colour and transparency.</summary>
        private readonly struct StyleRec
        {
            public readonly string Name;
            public readonly Color Color;
            public readonly float Transparency;
            public StyleRec(string name, Color color, float transparency)
            {
                Name = name; Color = color; Transparency = transparency;
            }
        }

        /// <summary>
        /// IFCSTYLEDITEM(Item, Styles, Name) → geometry-item id → surface style. Chain:
        /// PresentationStyleAssignment → SurfaceStyle → SurfaceStyleRendering/Shading →
        /// ColourRgb. Styled items with a null Item belong to a material definition
        /// (see MapMaterials) and are read there.
        /// </summary>
        private static void MapStyles(Ctx c)
        {
            foreach (int id in c.F.OfType("IFCSTYLEDITEM"))
            {
                var a = c.F.Args(id);
                if (a == null || a.Count < 2 || a[0].Kind != StepKind.Ref) continue;
                if (TryReadStyle(c, id, out var rec)) c.StyleOfItem[a[0].Ref] = rec;
            }
        }

        /// <summary>Reusable one-element list for the IFC4 «style attached directly»
        /// branch — the parse is not a per-frame path, but the allocation is pointless.</summary>
        private static readonly List<StepValue> OneRefBuffer = new() { null };

        private static List<StepValue> OneRef(StepValue v)
        {
            OneRefBuffer[0] = v;
            return OneRefBuffer;
        }

        /// <summary>Parse one IFCSTYLEDITEM into a style record; false when it carries no
        /// surface style with a colour (curve/text styles do not).</summary>
        private static bool TryReadStyle(Ctx c, int styledItemId, out StyleRec rec)
        {
            rec = default;
            var a = c.F.Args(styledItemId);
            if (a == null || a.Count < 2 || a[1].Kind != StepKind.List) return false;
            foreach (var assignRef in a[1].Items)
            {
                if (assignRef.Kind != StepKind.Ref) continue;
                // IFC4 attaches the style directly; IFC2X3 wraps it in an assignment
                List<StepValue> styles;
                if (c.F.TypeOf(assignRef.Ref) == "IFCPRESENTATIONSTYLEASSIGNMENT")
                {
                    var assign = c.F.Deref(assignRef);   // ((styles))
                    if (assign == null || assign.Count < 1 || assign[0].Kind != StepKind.List) continue;
                    styles = assign[0].Items;
                }
                else styles = OneRef(assignRef);
                if (styles == null) continue;
                foreach (var styleRef in styles)
                {
                    if (styleRef.Kind != StepKind.Ref || c.F.TypeOf(styleRef.Ref) != "IFCSURFACESTYLE") continue;
                    var surf = c.F.Args(styleRef.Ref);   // (Name, Side, (renderings))
                    if (surf == null || surf.Count < 3 || surf[2].Kind != StepKind.List) continue;
                    string name = surf[0].Kind == StepKind.Text ? surf[0].Text : null;
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
                        rec = new StyleRec(name,
                            new Color(rgb[1].AsFloat, rgb[2].AsFloat, rgb[3].AsFloat), tr);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Materials of products (design/29 §1.3): 152 of ~260 baked products in the
        /// reference Revit export carry NO IfcStyledItem at all — their look lives in the
        /// material, either as a name we can dress from the catalog or as a colour inside
        /// IfcMaterialDefinitionRepresentation. Revit also assigns the material to the
        /// TYPE more often than to the instance, so IfcRelDefinesByType is followed too.
        /// </summary>
        private static void MapMaterials(Ctx c)
        {
            foreach (int id in c.F.OfType("IFCMATERIAL"))
            {
                var a = c.F.Args(id);
                if (a != null && a.Count > 0 && a[0].Kind == StepKind.Text)
                    c.MaterialName[id] = a[0].Text;
            }

            // IFCMATERIALDEFINITIONREPRESENTATION(Name, Desc, Representations, Material)
            foreach (int id in c.F.OfType("IFCMATERIALDEFINITIONREPRESENTATION"))
            {
                var a = c.F.Args(id);
                if (a == null || a.Count < 4 || a[2].Kind != StepKind.List
                    || a[3].Kind != StepKind.Ref) continue;
                foreach (var repRef in a[2].Items)
                {
                    var rep = c.F.Deref(repRef);    // IFCSTYLEDREPRESENTATION(…, items)
                    if (rep == null || rep.Count < 4 || rep[3].Kind != StepKind.List) continue;
                    foreach (var itemRef in rep[3].Items)
                    {
                        if (itemRef.Kind != StepKind.Ref) continue;
                        if (c.F.TypeOf(itemRef.Ref) != "IFCSTYLEDITEM") continue;
                        if (!TryReadStyle(c, itemRef.Ref, out var rec)) continue;
                        // the material's own name beats the style's when both exist
                        string nm = c.MaterialName.TryGetValue(a[3].Ref, out string mn) ? mn : rec.Name;
                        c.StyleOfMaterial[a[3].Ref] = new StyleRec(nm, rec.Color, rec.Transparency);
                        break;
                    }
                }
            }

            // IFCRELASSOCIATESMATERIAL(…, RelatedObjects, RelatingMaterial)
            foreach (int id in c.F.OfType("IFCRELASSOCIATESMATERIAL"))
            {
                var a = c.F.Args(id);
                if (a == null || a.Count < 6 || a[4].Kind != StepKind.List
                    || a[5].Kind != StepKind.Ref) continue;
                int mat = FirstMaterial(c, a[5].Ref, 0);
                if (mat == 0) continue;
                foreach (var el in a[4].Items)
                    if (el.Kind == StepKind.Ref && !c.MaterialOfElement.ContainsKey(el.Ref))
                        c.MaterialOfElement[el.Ref] = mat;
            }

            // IFCRELDEFINESBYTYPE(…, RelatedObjects, RelatingType) — inherit the type's
            // material where the instance has none of its own
            foreach (int id in c.F.OfType("IFCRELDEFINESBYTYPE"))
            {
                var a = c.F.Args(id);
                if (a == null || a.Count < 6 || a[4].Kind != StepKind.List
                    || a[5].Kind != StepKind.Ref) continue;
                if (!c.MaterialOfElement.TryGetValue(a[5].Ref, out int mat)) continue;
                foreach (var el in a[4].Items)
                    if (el.Kind == StepKind.Ref && !c.MaterialOfElement.ContainsKey(el.Ref))
                        c.MaterialOfElement[el.Ref] = mat;
            }
        }

        /// <summary>First IfcMaterial inside a RelatingMaterial (which may be a material,
        /// a list, a layer set or a layer-set usage); 0 when there is none.</summary>
        private static int FirstMaterial(Ctx c, int id, int depth)
        {
            if (id == 0 || depth > 4) return 0;
            string t = c.F.TypeOf(id);
            if (t == "IFCMATERIAL") return id;
            var a = c.F.Args(id);
            if (a == null) return 0;
            foreach (var v in a)
            {
                if (v.Kind == StepKind.Ref)
                {
                    int m = FirstMaterial(c, v.Ref, depth + 1);
                    if (m != 0) return m;
                }
                else if (v.Kind == StepKind.List)
                {
                    foreach (var it in v.Items)
                    {
                        if (it.Kind != StepKind.Ref) continue;
                        int m = FirstMaterial(c, it.Ref, depth + 1);
                        if (m != 0) return m;
                    }
                }
            }
            return 0;
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
                // no axis (curved / IFC4 body-only walls) → show the mesh at least
                if (poly == 0)
                {
                    if (!BakeProduct(c, b, id, MepCategory.Proxy)) b.SkippedWalls++;
                    continue;
                }

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
                if (height <= 0f)   // Brep-only wall — no parametric height → bake it
                {
                    if (!BakeProduct(c, b, id, MepCategory.Proxy)) b.SkippedWalls++;
                    continue;
                }

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
                    // swing known → the door stands open (the pre-#50 imported look)
                    OpenFraction = swingDir.sqrMagnitude > 1e-6f ? 0.75f : 0f,
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

            // Mark this id BEFORE recursing: a cyclic PlacementRelTo chain in a broken
            // file then resolves to identity instead of a StackOverflow no catch can
            // stop (audit 09 §Б2). The placeholder is overwritten with the real matrix.
            c.Placements[id] = Matrix4x4.identity;

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
