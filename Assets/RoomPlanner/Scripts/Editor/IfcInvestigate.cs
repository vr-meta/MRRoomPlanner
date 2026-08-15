#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEngine;
using RoomPlanner.Core.Ifc;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Headless dump for issues #116/#117: imports an IFC file and prints, per storey,
    /// the slab outlines/holes, the stair flights (base → top) and how close each slab
    /// outline edge runs to a wall axis — the evidence for the stairwell-hole and
    /// slab-vs-wall-face questions. Run:
    ///   powershell -File ci/unity-run.ps1 -Method RoomPlanner.EditorTools.IfcInvestigate.Run
    /// Reads the path from IFC_PATH env var (default: the Project1 sample in Downloads).
    /// </summary>
    public static class IfcInvestigate
    {
        public static void Run()
        {
            string path = System.Environment.GetEnvironmentVariable("IFC_PATH");
            if (string.IsNullOrEmpty(path))
                path = @"C:\Users\butsc\Downloads\Project1 (2).ifc";
            var sb = new StringBuilder();
            sb.AppendLine($"[IfcInvestigate] {path}");
            var b = IfcImporter.Import(StepFile.Parse(File.ReadAllText(path)));
            if (b == null) { Debug.Log(sb + "IMPORT FAILED"); return; }

            sb.AppendLine($"storeys={b.Storeys.Count} walls={b.Walls.Count} slabs={b.Slabs.Count} stairs={b.Stairs.Count} pipes={b.Pipes.Count} baked={b.Plumbing.Count}");
            foreach (var p in b.Pipes)
                sb.AppendLine($"PIPE r={p.Radius:0.###} ({p.Start.x:0.##},{p.Start.y:0.##},{p.Start.z:0.##})->({p.End.x:0.##},{p.End.y:0.##},{p.End.z:0.##}) '{p.Name}'");
            for (int i = 0; i < b.Storeys.Count; i++)
                sb.AppendLine($"  storey[{i}] '{b.Storeys[i].Name}' elev={b.Storeys[i].Elevation:0.###}");

            foreach (var s in b.Stairs)
            {
                float top = s.Base.y + s.Risers * s.RiserHeight;
                Vector3 dir = Quaternion.Euler(0f, s.YawDeg, 0f) * Vector3.forward;
                Vector3 topEdge = s.Base + dir * (s.Risers * s.TreadDepth);
                sb.AppendLine($"STAIR base=({s.Base.x:0.##},{s.Base.y:0.##},{s.Base.z:0.##}) yaw={s.YawDeg:0.#} w={s.Width:0.##} risers={s.Risers} rise={s.RiserHeight:0.###} tread={s.TreadDepth:0.###} -> topY={top:0.##} topEdge=({topEdge.x:0.##},{topEdge.z:0.##})");
            }

            for (int i = 0; i < b.Slabs.Count; i++)
            {
                var sl = b.Slabs[i];
                sb.AppendLine($"SLAB[{i}] level={sl.Level:0.##} thick={sl.Thickness:0.###} pts={sl.Outline.Count} holes={sl.Holes.Count}");
                foreach (var hole in sl.Holes)
                {
                    var min = new Vector3(float.MaxValue, 0, float.MaxValue);
                    var max = new Vector3(float.MinValue, 0, float.MinValue);
                    foreach (var p in hole)
                    {
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);
                    }
                    sb.AppendLine($"  HOLE bbox=({min.x:0.##},{min.z:0.##})..({max.x:0.##},{max.z:0.##}) size=({max.x - min.x:0.##}x{max.z - min.z:0.##})");
                }
                // #117: how far does each outline edge sit from the nearest wall axis?
                int onAxis = 0, offAxis = 0;
                for (int e = 0; e < sl.Outline.Count; e++)
                {
                    Vector3 mid = Vector3.Lerp(sl.Outline[e], sl.Outline[(e + 1) % sl.Outline.Count], 0.5f);
                    float bestD = float.MaxValue;
                    float bestThick = 0f;
                    foreach (var w in b.Walls)
                    {
                        if (w.Path.Count < 2) continue;
                        Vector3 a = w.Path[0], c = w.Path[w.Path.Count - 1];
                        a.y = 0; c.y = 0;
                        var m2 = new Vector3(mid.x, 0f, mid.z);
                        Vector3 ab = c - a;
                        if (ab.sqrMagnitude < 1e-6f) continue;
                        float t = Mathf.Clamp01(Vector3.Dot(m2 - a, ab) / ab.sqrMagnitude);
                        float d = Vector3.Distance(a + ab * t, m2);
                        if (d < bestD) { bestD = d; bestThick = w.Thickness; }
                    }
                    if (bestD < 0.02f) onAxis++;
                    else if (bestD < bestThick) offAxis++;
                }
                sb.AppendLine($"  edges: onWallAxis(<2cm)={onAxis} withinThickness={offAxis} of {sl.Outline.Count}");
            }
            Debug.Log(sb.ToString());
        }
    }
}
#endif
