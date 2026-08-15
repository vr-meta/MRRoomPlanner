using System.Collections.Generic;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Core.Ifc;
using RoomPlanner.Core.Project;
using RoomPlanner.Floors;
using RoomPlanner.Stairs;
using RoomPlanner.Tools;
using RoomPlanner.Walls;

namespace RoomPlanner.Import
{
    /// <summary>
    /// Scene ↔ ProjectData (docs/design/06-project-format.md, v1). Capture reads the live
    /// parameters; Apply converts back into an ImportedBuilding and reuses the import
    /// pipeline, so a loaded project is built by the exact same code path as an IFC file.
    /// Hidden (deleted) objects are not saved — the file is the scene the user sees.
    /// </summary>
    public static class ProjectStore
    {
        public static ProjectData Capture(WallGraphRenderer walls, BlueprintController blueprint)
        {
            var data = new ProjectData();

            var graph = walls != null ? walls.Graph : null;
            if (graph != null)
            {
                var nodeIndex = new Dictionary<WallNode, int>();
                foreach (var n in graph.Nodes)
                {
                    nodeIndex[n] = data.Nodes.Count;
                    data.Nodes.Add(new ProjectNode { Position = n.Position });
                }
                foreach (var s in graph.Segments)
                {
                    if (!walls.IsVisible(s)) continue;      // deleted walls stay deleted
                    var view = walls.ViewOf(s);
                    var sel = view != null ? view.GetComponent<Editing.Selectable>() : null;
                    var w = new ProjectWall
                    {
                        NodeA = nodeIndex[s.A],
                        NodeB = nodeIndex[s.B],
                        Thickness = s.Thickness,
                        Height = s.Height,
                        BaseHeight = s.BaseHeight,
                        SideSign = s.SideSign,
                        Offset = (int)s.Offset,
                        Join = (int)s.Join,
                        Painted = sel != null && sel.IsPainted,
                        Paint = sel != null && sel.IsPainted ? sel.Paint : Color.clear,
                        // v3 (issue #34): Finish = inner side, FinishB = outer side
                        Finish = CaptureFinish(sel),
                        FinishB = Capture(sel != null
                            ? sel.FinishOf(WallSide.Outer) : SurfaceFinish.None),
                    };
                    foreach (var op in s.Openings)
                        w.Openings.Add(new ProjectOpening
                        {
                            Along = op.AlongFraction, Width = op.Width,
                            Height = op.Height, Sill = op.SillHeight,
                            IsDoor = op.IsDoor,
                            Kind = (int)op.Kind,
                            Swing = op.SwingDir, Hinge = op.HingeDir,
                            Open = op.OpenFraction,
                        });
                    data.Walls.Add(w);
                }
            }

            foreach (var f in TeleportCommand.CollectFloors())
            {
                if (f == null || !f.gameObject.activeSelf) continue;
                var fsel = f.GetComponent<Editing.Selectable>();
                var pf = new ProjectFloor
                {
                    Level = f.Level,
                    Thickness = f.Thickness,
                    Outline = new List<Vector3>(f.Outline),
                    Painted = fsel != null && fsel.IsPainted,
                    Paint = fsel != null && fsel.IsPainted ? fsel.Paint : Color.clear,
                    Finish = CaptureFinish(fsel),
                };
                foreach (var hole in f.Holes)
                    pf.Holes.Add(new ProjectRing { Points = new List<Vector3>(hole) });
                data.Floors.Add(pf);
            }

            foreach (var s in TeleportCommand.CollectStairs())
            {
                if (s == null || !s.gameObject.activeSelf) continue;
                var ssel = s.GetComponent<Editing.Selectable>();
                data.Stairs.Add(new ProjectStair
                {
                    Base = s.Base, Yaw = s.YawDeg, Width = s.Width,
                    Risers = s.Risers, RiserHeight = s.RiserHeight, TreadDepth = s.TreadDepth,
                    Open = s.Kind == StairKind.Open,   // legacy readers
                    Kind = (int)s.Kind,
                    Painted = ssel != null && ssel.IsPainted,
                    Paint = ssel != null && ssel.IsPainted ? ssel.Paint : Color.clear,
                    Finish = CaptureFinish(ssel),
                });
            }

            foreach (var m in TeleportCommand.CollectMep())
            {
                if (m == null || !m.gameObject.activeSelf) continue;
                var mf = m.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var msel = m.GetComponent<Editing.Selectable>();
                var pm = new ProjectMep
                {
                    Name = m.name, Origin = m.transform.position,
                    Category = (int)m.Category,
                    Transparency = m.Transparency,
                    Storey = m.StoreyIndex,
                    Painted = msel != null && msel.IsPainted,
                    Paint = msel != null && msel.IsPainted ? msel.Paint : Color.clear,
                    Finish = CaptureFinish(msel),
                };
                pm.Vertices.AddRange(mf.sharedMesh.vertices);
                // mesh.triangles concatenates the submeshes in order, so the parts'
                // (start, count) ranges keep pointing at their own triangles (v5)
                pm.Triangles.AddRange(mf.sharedMesh.triangles);
                pm.Uvs.AddRange(mf.sharedMesh.uv);
                foreach (var part in m.Parts)
                {
                    if (part == null) continue;
                    pm.Parts.Add(new ProjectMepPart
                    {
                        Name = part.Name,
                        Painted = part.HasColor,
                        Paint = part.Color,
                        Transparency = part.Transparency,
                        TriStart = part.TriStart,
                        TriCount = part.TriCount,
                        Finish = Capture(part.Finish),
                    });
                }
                data.Plumbing.Add(pm);
            }

            // v2: the electrical layer (audit B1) — REGISTERED objects only: the tool's
            // ghost preview and the parked prefab template are not in the model. Hidden
            // (deleted, undo-able) ones stay out, same as every other layer.
            var model = Editing.SceneModel.Instance;
            if (model != null)
            {
                foreach (var item in model.Items)
                {
                    if (item is not Editing.Selectable s || !s.IsAlive || s.IsHidden) continue;
                    if (s.Fixture != null)
                    {
                        data.Fixtures.Add(new ProjectFixture
                        {
                            Id = s.Id,
                            Kind = (int)s.Fixture.Kind,
                            Posts = s.Fixture.Posts,
                            Keys = s.Fixture.Keys,
                            Reserve = s.Fixture.ReservePercent,
                            Position = s.Fixture.transform.position,
                            Rotation = s.Fixture.transform.rotation,
                            BaseLevel = s.Fixture.BaseLevel,
                        });
                    }
                    else if (s.Kind == Editing.SelectableKind.Wire)
                    {
                        var route = s.GetComponent<Electrical.WireRoute>();
                        if (route == null) continue;
                        var pw = new ProjectWire
                        {
                            Cable = (int)route.Cable,
                            StartId = route.StartFixtureId,
                            EndId = route.EndFixtureId,
                        };
                        pw.Points.AddRange(route.Points);
                        data.Wires.Add(pw);
                    }
                    else if (s.Kind == Editing.SelectableKind.Furniture)
                    {
                        // v4 (design/27): the catalog address travels, the model does not.
                        var view = s.GetComponent<Furniture.FurnitureItemView>();
                        if (view == null) continue;
                        data.Furniture.Add(new ProjectFurniture
                        {
                            Id = s.Id,
                            Key = view.CatalogKey,
                            Name = view.DisplayName,
                            Position = view.transform.position,
                            Yaw = view.Yaw,
                            Size = view.Size,
                            Anchor = (int)view.Anchor,
                        });
                    }
                    else if (s.Kind == Editing.SelectableKind.Measurement)
                    {
                        // Kind falls back to Measurement for unknown components (MEP views
                        // among them) — the component check keeps them out of this section.
                        var meas = s.GetComponent<Measure.Measurement>();
                        if (meas != null)
                            data.Measures.Add(new ProjectMeasure { A = meas.PointA, B = meas.PointB });
                    }
                }
            }

            if (blueprint != null)
            {
                data.PlanScale = blueprint.PlanScale;
                data.PlanRotationDeg = blueprint.PlanRotationDeg;
                data.PlanOffsetX = blueprint.PlanOffsetX;
                data.PlanOffsetZ = blueprint.PlanOffsetZ;
            }
            return data;
        }

        /// <summary>Rebuild the scene from a project. Clears whatever is there first.</summary>
        public static void Apply(ProjectData data, ImportController import, BlueprintController blueprint)
        {
            if (data == null || import == null) return;
            import.ClearScene();
            import.BuildScene(ToBuilding(data));
            RestoreElectrical(data);
            RestoreMeasurements(data);
            RestoreFurniture(data);
            if (blueprint != null)
                blueprint.SetPlacement(data.PlanScale, data.PlanRotationDeg,
                    data.PlanOffsetX, data.PlanOffsetZ);
        }

        /// <summary>
        /// Recreate the electrical layer (format v2, audit B1). The controller is found
        /// at load time instead of being rig-wired — one call per load, and restore must
        /// work even on rigs saved before the field existed. Public for round-trip tests.
        /// </summary>
        public static void RestoreElectrical(ProjectData data)
        {
            if (data.Fixtures.Count == 0 && data.Wires.Count == 0) return;
            var electric = Object.FindFirstObjectByType<Electrical.ElectricController>();
            if (electric == null)
            {
                Debug.LogWarning("[Project] file carries electrical data but the rig has no ElectricController");
                return;
            }
            foreach (var f in data.Fixtures) electric.RestoreFixture(f);
            foreach (var w in data.Wires) electric.RestoreWire(w);
        }

        /// <summary>
        /// Recreate placed furniture (format v4, design/27). Same late-binding as the
        /// electrical layer: the controller is found at load time, so a project loads on a
        /// rig built before the tool existed. Public for round-trip tests.
        /// </summary>
        public static void RestoreFurniture(ProjectData data)
        {
            if (data.Furniture.Count == 0) return;
            var tool = Object.FindFirstObjectByType<Furniture.FurnitureController>();
            if (tool == null)
            {
                Debug.LogWarning("[Project] file carries furniture but the rig has no FurnitureController");
                return;
            }
            foreach (var f in data.Furniture) tool.RestoreItem(f);
        }

        /// <summary>The full surface finish of an object, v2 (audit B2) — v1 kept only a
        /// flat colour and textured floors came back white after every load.</summary>
        private static ProjectFinish CaptureFinish(Editing.Selectable sel)
        {
            return Capture(sel == null ? SurfaceFinish.None : sel.Finish);
        }

        private static ProjectFinish Capture(SurfaceFinish f)
        {
            return new ProjectFinish
            {
                Kind = (int)f.Kind,
                Color = f.Color,
                TextureId = f.TextureId,
                TileW = f.TileMeters,
                TileH = f.TileMetersY,
                Gloss = f.Smoothness,
                RotationDeg = f.RotationDeg,
            };
        }

        /// <summary>Project finish → runtime finish; Kind 0 (v1 file) = none recorded.</summary>
        internal static SurfaceFinish ToFinish(ProjectFinish p) =>
            p == null || p.Kind == 0
                ? SurfaceFinish.None
                : new SurfaceFinish
                {
                    Kind = (FinishKind)p.Kind,
                    Color = p.Color,
                    TextureId = p.TextureId,
                    TileMeters = p.TileW,
                    TileMetersY = p.TileH,
                    Smoothness = p.Gloss,
                    RotationDeg = p.RotationDeg,
                };

        /// <summary>Recreate saved measurements (format v2, audit B3). Public for tests.</summary>
        public static void RestoreMeasurements(ProjectData data)
        {
            if (data.Measures.Count == 0) return;
            var measure = Object.FindFirstObjectByType<Measure.MeasureController>();
            if (measure == null)
            {
                Debug.LogWarning("[Project] file carries measurements but the rig has no MeasureController");
                return;
            }
            foreach (var m in data.Measures) measure.RestoreMeasurement(m.A, m.B);
        }

        /// <summary>Project → the import pipeline's input model.</summary>
        public static ImportedBuilding ToBuilding(ProjectData data)
        {
            var b = new ImportedBuilding();
            for (int i = 0; i < data.Walls.Count; i++)
            {
                var w = data.Walls[i];
                if (w.NodeA < 0 || w.NodeA >= data.Nodes.Count
                    || w.NodeB < 0 || w.NodeB >= data.Nodes.Count) continue;
                var iw = new ImportedWall
                {
                    Thickness = w.Thickness,
                    Height = w.Height,
                    BaseHeight = w.BaseHeight,
                    OffsetOverride = w.Offset,
                    JoinOverride = w.Join,
                    SideSignOverride = w.SideSign,
                    HasPaint = w.Painted,
                    PaintColor = w.Paint,
                    Finish = ToFinish(w.Finish),
                    // v2 files carried ONE finish for the whole wall — mirror it onto
                    // the outer side so the old look survives; v3 is verbatim per side.
                    FinishB = data.Version >= 3 ? ToFinish(w.FinishB) : ToFinish(w.Finish),
                };
                iw.Path.Add(data.Nodes[w.NodeA].Position);
                iw.Path.Add(data.Nodes[w.NodeB].Position);
                b.Walls.Add(iw);
                foreach (var op in w.Openings)
                    b.Openings.Add(new ImportedOpening
                    {
                        WallIndex = b.Walls.Count - 1,
                        AlongFraction = op.Along, Width = op.Width,
                        Height = op.Height, Sill = op.Sill,
                        IsDoor = op.IsDoor,
                        Kind = op.Kind,
                        SwingDir = op.Swing, HingeDir = op.Hinge,
                        // v2 files rendered swing-known doors open at 75° — keep that look
                        OpenFraction = data.Version >= 3 ? op.Open
                            : (op.Swing.sqrMagnitude > 1e-6f ? 0.75f : 0f),
                    });
            }
            foreach (var f in data.Floors)
            {
                var slab = new ImportedSlab
                {
                    Outline = new List<Vector3>(f.Outline),
                    Level = f.Level,
                    Thickness = f.Thickness,
                    HasPaint = f.Painted,
                    PaintColor = f.Paint,
                    Finish = ToFinish(f.Finish),
                };
                foreach (var ring in f.Holes)
                    slab.Holes.Add(new List<Vector3>(ring.Points));
                b.Slabs.Add(slab);
            }
            foreach (var s in data.Stairs)
                b.Stairs.Add(new ImportedStair
                {
                    Base = s.Base, YawDeg = s.Yaw, Width = s.Width,
                    Risers = s.Risers, RiserHeight = s.RiserHeight, TreadDepth = s.TreadDepth,
                    Kind = s.Kind >= 0 ? (StairKind)s.Kind : (s.Open ? StairKind.Open : StairKind.Solid),
                    HasPaint = s.Painted,
                    PaintColor = s.Paint,
                    Finish = ToFinish(s.Finish),
                });
            foreach (var m in data.Plumbing)
            {
                var mep = new ImportedMep
                {
                    Name = m.Name, Origin = m.Origin,
                    Vertices = new List<Vector3>(m.Vertices),
                    Triangles = new List<int>(m.Triangles),
                    Uvs = m.Uvs != null ? new List<Vector2>(m.Uvs) : new List<Vector2>(),
                    StoreyIndex = m.Storey,
                    Category = (MepCategory)m.Category,
                    Transparency = m.Transparency,
                    HasColor = m.Painted,
                    Color = m.Paint,
                    Finish = ToFinish(m.Finish),
                };
                if (m.Parts != null)
                    foreach (var p in m.Parts)
                    {
                        if (p == null) continue;
                        mep.Parts.Add(new MepPart
                        {
                            Name = p.Name,
                            HasColor = p.Painted,
                            Color = p.Paint,
                            Transparency = p.Transparency,
                            TriStart = p.TriStart,
                            TriCount = p.TriCount,
                            Finish = ToFinish(p.Finish),
                        });
                    }
                b.Plumbing.Add(mep);
            }
            return b;
        }
    }
}
