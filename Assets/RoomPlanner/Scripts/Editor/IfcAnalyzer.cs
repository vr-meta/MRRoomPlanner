#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using RoomPlanner.Core.Ifc;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Coverage report for IFC samples: what our importer takes from a file vs what
    /// the file actually contains (docs/design/18). Runs over TestData/ifc/*.ifc —
    /// the throwaway samples from github.com/youshengCode/IfcSampleFiles.
    /// Headless: ci/unity-run.ps1 -Method RoomPlanner.EditorTools.IfcAnalyzer.Analyze
    /// </summary>
    public static class IfcAnalyzer
    {
        // product types we NEVER read — listed so the report can show what a file
        // loses; geometry-less relations/styles are not interesting here
        private static readonly string[] KnownUnsupported =
        {
            // coverage v2 bakes coverings/beams/members/plates/curtain walls/roofs and
            // the flow-device family — what remains is non-geometry or out of scope
            "IFCSPACE", "IFCFOOTING", "IFCPILE",
            "IFCFLOWSEGMENT", "IFCFLOWFITTING",
            "IFCDISTRIBUTIONELEMENT", "IFCTRANSPORTELEMENT", "IFCGRID",
        };

        [MenuItem("RoomPlanner/Analyze IFC Samples (TestData)")]
        public static void Analyze()
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "TestData/ifc");
            if (!Directory.Exists(dir))
            {
                Debug.LogError($"[IfcAnalyze] no folder {dir}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            foreach (var file in Directory.GetFiles(dir, "*.ifc"))
            {
                try { AnalyzeOne(file); }
                catch (System.Exception e)
                {
                    Debug.LogError($"[IfcAnalyze] {Path.GetFileName(file)} FAILED: {e.Message}");
                }
            }
        }

        private static void AnalyzeOne(string path)
        {
            var f = StepFile.Parse(File.ReadAllText(path));
            var b = IfcImporter.Import(f);

            var report = new System.Text.StringBuilder();
            report.AppendLine($"[IfcAnalyze] === {Path.GetFileName(path)} ===");
            report.AppendLine(
                $"[IfcAnalyze] imported: storeys={b.Storeys.Count} walls={b.Walls.Count} " +
                $"slabs={b.Slabs.Count} openings={b.Openings.Count} stairs={b.Stairs.Count} " +
                $"baked={b.Plumbing.Count}");
            report.AppendLine(
                $"[IfcAnalyze] skipped:  walls={b.SkippedWalls} columns={b.SkippedColumns} " +
                $"slabs={b.SkippedSlabs} openings={b.SkippedOpenings} stairs={b.SkippedStairs} " +
                $"baked={b.SkippedMep}");

            var lost = new List<string>();
            foreach (var t in KnownUnsupported)
            {
                int n = f.OfType(t).Count;
                if (n > 0) lost.Add($"{t}×{n}");
            }
            report.AppendLine(lost.Count > 0
                ? $"[IfcAnalyze] NOT imported (unsupported types): {string.Join(", ", lost)}"
                : "[IfcAnalyze] no unsupported product types in this file");
            Debug.Log(report.ToString());
        }
    }
}
#endif
