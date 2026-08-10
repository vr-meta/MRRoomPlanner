using System.Collections.Generic;
using UnityEngine;
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
                    };
                    foreach (var op in s.Openings)
                        w.Openings.Add(new ProjectOpening
                        {
                            Along = op.AlongFraction, Width = op.Width,
                            Height = op.Height, Sill = op.SillHeight,
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
                    Open = s.Kind == StairKind.Open,
                    Painted = ssel != null && ssel.IsPainted,
                    Paint = ssel != null && ssel.IsPainted ? ssel.Paint : Color.clear,
                });
            }

            foreach (var m in TeleportCommand.CollectMep())
            {
                if (m == null || !m.gameObject.activeSelf) continue;
                var mf = m.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var pm = new ProjectMep { Name = m.name, Origin = m.transform.position };
                pm.Vertices.AddRange(mf.sharedMesh.vertices);
                pm.Triangles.AddRange(mf.sharedMesh.triangles);
                data.Plumbing.Add(pm);
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
            if (blueprint != null)
                blueprint.SetPlacement(data.PlanScale, data.PlanRotationDeg,
                    data.PlanOffsetX, data.PlanOffsetZ);
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
                        IsDoor = op.Sill < 0.01f,
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
                    Open = s.Open,
                    HasPaint = s.Painted,
                    PaintColor = s.Paint,
                });
            foreach (var m in data.Plumbing)
                b.Plumbing.Add(new ImportedMep
                {
                    Name = m.Name, Origin = m.Origin,
                    Vertices = new List<Vector3>(m.Vertices),
                    Triangles = new List<int>(m.Triangles),
                    StoreyIndex = m.Storey,
                });
            return b;
        }
    }
}
