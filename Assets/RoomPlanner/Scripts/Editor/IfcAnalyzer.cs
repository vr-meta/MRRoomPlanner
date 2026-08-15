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

        /// <summary>
        /// Material report (design/29): how many baked products split into parts, and
        /// which material names our catalog map answers. Point it at a real export with
        /// RP_IFC=&lt;path&gt;; otherwise it walks TestData/ifc like Analyze.
        /// Headless: ci/unity-run.ps1 -Method RoomPlanner.EditorTools.IfcAnalyzer.AnalyzeMaterials
        /// </summary>
        [MenuItem("RoomPlanner/Analyze IFC Materials (design/29)")]
        public static void AnalyzeMaterials()
        {
            var files = new List<string>();
            string single = System.Environment.GetEnvironmentVariable("RP_IFC");
            if (!string.IsNullOrEmpty(single) && File.Exists(single)) files.Add(single);
            else
            {
                string dir = Path.Combine(Directory.GetCurrentDirectory(), "TestData/ifc");
                if (Directory.Exists(dir)) files.AddRange(Directory.GetFiles(dir, "*.ifc"));
            }
            if (files.Count == 0)
            {
                Debug.LogError("[IfcMat] nothing to analyze (set RP_IFC or fill TestData/ifc)");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            foreach (string path in files)
            {
                var b = IfcImporter.Import(StepFile.Parse(File.ReadAllText(path)));
                var histogram = new Dictionary<int, int>();
                var names = new Dictionary<string, int>();
                int dressed = 0, parts = 0, unnamed = 0;
                foreach (var mep in b.Plumbing)
                {
                    int n = Mathf.Max(1, mep.Parts.Count);
                    histogram[n] = histogram.TryGetValue(n, out int c) ? c + 1 : 1;
                    parts += n;
                    foreach (var part in mep.Parts)
                    {
                        if (string.IsNullOrEmpty(part.Name)) { unnamed++; continue; }
                        names[part.Name] = names.TryGetValue(part.Name, out int k) ? k + 1 : 1;
                        if (!IfcMaterialMap.Resolve(part.Name).IsNone) dressed++;
                    }
                }

                // doors and windows (issue #133): the frame material picked per opening
                var frames = new Dictionary<string, int>();
                int bare = 0;
                foreach (var op in b.Openings)
                {
                    if (string.IsNullOrEmpty(op.FrameMaterial)) { bare++; continue; }
                    frames[op.FrameMaterial] = frames.TryGetValue(op.FrameMaterial, out int n) ? n + 1 : 1;
                }

                var report = new System.Text.StringBuilder();
                report.AppendLine($"[IfcMat] === {Path.GetFileName(path)} ===");
                report.AppendLine($"[IfcMat] openings: {b.Openings.Count}, without a frame material: {bare}");
                foreach (var kv in frames)
                {
                    var m = IfcMaterialMap.Resolve(kv.Key);
                    report.AppendLine($"[IfcMat]   frame {kv.Value,3}× {kv.Key} → "
                        + (m.IsNone ? "— (joinery material)" : m.FinishId));
                }
                report.AppendLine($"[IfcMat] baked elements: {b.Plumbing.Count}, parts: {parts}, "
                    + $"dressed by name: {dressed}, nameless parts: {unnamed}");
                foreach (var kv in histogram) report.AppendLine($"[IfcMat]   {kv.Key} part(s): {kv.Value} element(s)");
                report.AppendLine("[IfcMat] material name → finish:");
                foreach (var kv in names)
                {
                    var m = IfcMaterialMap.Resolve(kv.Key);
                    report.AppendLine($"[IfcMat]   {kv.Value,4}× {kv.Key} → {(m.IsNone ? "— (file colour)" : m.FinishId)}");
                }
                Debug.Log(report.ToString());
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
