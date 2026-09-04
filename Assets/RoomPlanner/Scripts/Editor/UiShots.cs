#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Tools;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Headless renders of the UI v2 (design/20) to Build/ui-shots/*.png — the same
    /// components, materials and generation code the device runs, so what these PNGs
    /// show is what the headset shows. Batchmode WITHOUT -nographics (same constraint
    /// as CiTools.RenderShots). Nothing is saved to the scene.
    /// </summary>
    public static class UiShots
    {
        public static void Render()
        {
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                var camGo = new GameObject("ShotCam") { tag = "MainCamera" };
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.043f, 0.055f, 0.078f);   // dim room stand-in
                cam.nearClipPlane = 0.02f;
                cam.farClipPlane = 20f;
                cam.fieldOfView = 42f;

                var ctx = new RigContext();
                LoadMaterials(ctx);
                System.IO.Directory.CreateDirectory("Build/ui-shots");
                foreach (var old in System.IO.Directory.GetFiles("Build/ui-shots", "*.png"))
                    System.IO.File.Delete(old);

                ShotRadial(cam, ctx);
                ShotStrip(cam, ctx);
                ShotInspectorShowcase(cam, ctx);
                ShotNumpad(cam, ctx);
                ShotSelectPopup(cam, ctx);
                ShotSwatchPopup(cam, ctx);
                ShotEveryTool(cam, ctx);
                WriteIndex();

                Debug.Log("[CI] ui-shots saved to Build/ui-shots");
            }
            catch (Exception e)
            {
                Debug.LogError($"[CI] UiShots failed: {e}");
                EditorApplication.Exit(1);
            }
        }

        // ---- compositions ----

        private static void ShotRadial(Camera cam, RigContext ctx)
        {
            var head = new GameObject("Head").transform;
            head.position = new Vector3(0f, 0f, -0.0f);
            head.rotation = Quaternion.identity;

            var radial = SetupRadial.Build(ctx);
            radial.gameObject.SetActive(true);
            radial.Configure(ToolManager.CreateRadialDefinitions(ToolManager.DefaultToolIndex));
            radial.Open(head, ToolManager.DefaultToolIndex("electric"));
            // stick deflection toward slot 2 (Wall, 60°) highlights it — the state the
            // preview shows; ray misses, no confirm
            radial.Tick(new Vector2(Mathf.Sin(60f * Mathf.Deg2Rad), Mathf.Cos(60f * Mathf.Deg2Rad)) * 0.9f,
                new Ray(new Vector3(0f, -5f, 0f), Vector3.down), false, false, head, null);
            radial.transform.localScale = Vector3.one;   // skip the open animation
            RebuildVisuals(radial.gameObject);

            Shoot(cam, head.position, radial.transform.position, "radial", 1500, 1500);
            UnityEngine.Object.DestroyImmediate(radial.gameObject);
            UnityEngine.Object.DestroyImmediate(head.gameObject);
        }

        private static void ShotStrip(Camera cam, RigContext ctx)
        {
            var menu = SetupPalette.Build(ctx);
            menu.transform.position = new Vector3(0f, 0f, 0.42f);
            menu.Refresh(0, snapCorner: true, snapEdge: true, snapGrid: false,
                snapAngle: false, scanOn: true);
            menu.SetToolChip("electric-plug", "Electric", UiTokens.LayerElectrical);
            RebuildVisuals(menu.gameObject);

            Shoot(cam, Vector3.zero, menu.transform.position, "palette-strip", 1600, 700);
            UnityEngine.Object.DestroyImmediate(menu.gameObject);
        }

        /// <summary>One shot per REAL tool schema — the actual controllers' GetSettings(),
        /// so the gallery always matches the app (user request 2026-08-10: «скриншот от
        /// всех опций, всех разделов»).</summary>
        private static void ShotEveryTool(Camera cam, RigContext ctx)
        {
            var host = new GameObject("ToolsHost");
            try
            {
                var manager = host.AddComponent<ToolManager>();

                var measure = host.AddComponent<RoomPlanner.Measure.MeasureController>();
                RenderInspector(cam, ctx, measure.GetSettings(), "tool-measure", "Measure");

                var wall = host.AddComponent<RoomPlanner.Walls.WallController>();
                Wire(wall, "manager", manager);
                RenderInspector(cam, ctx, wall.GetSettings(), "tool-wall", "Wall");

                var floor = host.AddComponent<RoomPlanner.Floors.FloorController>();
                Wire(floor, "manager", manager);
                RenderInspector(cam, ctx, floor.GetSettings(), "tool-floor", "Floor");

                var blueprint = host.AddComponent<RoomPlanner.Floors.BlueprintController>();
                RenderInspector(cam, ctx, blueprint.GetSettings(), "tool-blueprint", "Blueprint");

                var import = host.AddComponent<RoomPlanner.Import.ImportController>();
                RenderInspector(cam, ctx, import.GetSettings(), "tool-import", "Import");

                var openings = host.AddComponent<RoomPlanner.Walls.OpeningsController>();
                var openingSchema = openings.GetSettings();
                for (int tab = 0; tab < openingSchema.Tabs.Length; tab++)
                {
                    openingSchema.SelectTab(tab);
                    RenderInspector(cam, ctx, openingSchema,
                        $"tool-openings-{openingSchema.Tabs[tab].ToLowerInvariant()}", "Openings");
                }

                var projects = host.AddComponent<RoomPlanner.Import.ProjectsController>();
                RenderInspector(cam, ctx, projects.GetSettings(), "tool-projects", "Projects");

                RenderInspector(cam, ctx, manager.GetRenderingSettings(),
                    "tool-rendering", "Rendering");

                var paint = host.AddComponent<PaintController>();
                ctx.Finishes = SetupPaintTool.BuildFinishLibrary(host);
                Wire(paint, "library", ctx.Finishes);
                RenderInspector(cam, ctx, paint.GetSettings(), "tool-paint", "Paint");
                // second shot: the Walls texture tab
                paint.GetSettings().SelectTab(1);
                RenderInspector(cam, ctx, paint.GetSettings(), "tool-paint-walls", "Paint");
                paint.GetSettings().SelectTab(2);
                RenderInspector(cam, ctx, paint.GetSettings(), "tool-paint-floors", "Paint");
                paint.GetSettings().SelectTab(3);
                RenderInspector(cam, ctx, paint.GetSettings(), "tool-paint-tiles", "Paint");
                paint.GetSettings().SelectTab(4);
                RenderInspector(cam, ctx, paint.GetSettings(), "tool-paint-ceiling", "Paint");
                paint.GetSettings().SelectTab(0);

                var electric = host.AddComponent<RoomPlanner.Electrical.ElectricController>();
                var schema = electric.GetSettings();
                for (int tab = 0; tab < schema.Tabs.Length; tab++)
                {
                    schema.SelectTab(tab);
                    RenderInspector(cam, ctx, schema,
                        $"tool-electric-{schema.Tabs[tab].ToLowerInvariant()}", "Electric");
                }

                // Select: the selection group with a sample pick
                cam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var inspector = SetupInspector.Build(ctx);
                inspector.SetSelection("Wall #3", "Length 3.2 m · Height 2.7 m · 20 cm");
                inspector.ShowFor(null, showSelection: true, title: "Wall #3");
                RebuildVisuals(inspector.gameObject);
                ShootInspector(cam, inspector, "tool-select");
                UnityEngine.Object.DestroyImmediate(inspector.gameObject);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void Wire(Component target, string field, UnityEngine.Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        private static void ShotInspectorShowcase(Camera cam, RigContext ctx)
        {
            float thickness = 0.20f;
            bool scan = true;
            int offset = 1, pick = 4, file = 0;
            var files = new[] { "plan-a.png", "plan-b.png", "kitchen.png" };
            var palette = new[]
            {
                new Color(0.95f, 0.94f, 0.91f), new Color(0.89f, 0.84f, 0.72f),
                new Color(0.77f, 0.39f, 0.23f), new Color(0.61f, 0.69f, 0.53f),
                new Color(0.50f, 0.66f, 0.79f), new Color(0.29f, 0.31f, 0.33f),
                new Color(0.62f, 0.29f, 0.24f), new Color(0.66f, 0.81f, 0.75f),
            };
            var schema = new SettingsSchema()
                .Slider("thk", "Thickness", 0.02f, 1f, 0.01f, () => thickness, v => thickness = v,
                    (_, v) => thickness = v, () => $"{thickness * 100f:0} cm", 100f)
                .Segmented("off", "Offset", new[] { "Outer", "Center", "Inner" },
                    () => offset, i => offset = i)
                .Select("file", "Plan file", () => files, () => file, i => file = i)
                .Toggle("scan", "Scan", () => scan, v => scan = v)
                .Numeric("lvl", "Level", -20f, 20f, () => 2.8f, (_, __) => { },
                    () => "280 cm", 100f)
                .Swatch("color", "Color", palette, () => pick, i => pick = i)
                .Header("grp", "Circuits")
                .Readout("bom", "Total", () => "62.7 m (+10%)")
                .Progress("load", "Parsing", () => 0.62f)
                .Action("fin", "Finish route", "check", () => { })
                .Action("clr", "Clear circuit", "trash", () => { }, destructive: true);

            RenderInspector(cam, ctx, schema, "inspector-widgets", "Widget gallery");
        }

        private static void ShotNumpad(Camera cam, RigContext ctx)
        {
            var field = new SettingsSchema()
                .Numeric("h", "Height", 0.1f, 5f, () => 2.8f, (_, __) => { }, () => "280 cm", 100f)
                .Fields[0];

            cam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var inspector = SetupInspector.Build(ctx);
            var schema = new SettingsSchema()
                .Numeric("h", "Height", 0.1f, 5f, () => 2.8f, (_, __) => { }, () => "280 cm", 100f);
            inspector.ShowFor(schema, false, "Floor");
            inspector.Popups.OpenNumpad(field, null);
            RebuildVisuals(inspector.gameObject);

            ShootInspector(cam, inspector, "numpad");
            UnityEngine.Object.DestroyImmediate(inspector.gameObject);
        }

        private static void ShotSelectPopup(Camera cam, RigContext ctx)
        {
            int selected = 1;
            string[] options = { "Ground floor", "First floor", "Roof" };
            var schema = new SettingsSchema()
                .Select("storey", "Storey", () => options, () => selected, i => selected = i);
            cam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var inspector = SetupInspector.Build(ctx);
            inspector.ShowFor(schema, false, "Import");
            inspector.Popups.OpenSelect(schema.Fields[0], null);
            RebuildVisuals(inspector.gameObject);
            ShootInspector(cam, inspector, "popup-select");
            UnityEngine.Object.DestroyImmediate(inspector.gameObject);
        }

        private static void ShotSwatchPopup(Camera cam, RigContext ctx)
        {
            int selected = 2;
            var palette = new[]
            {
                new Color(0.95f, 0.94f, 0.91f), new Color(0.89f, 0.84f, 0.72f),
                new Color(0.77f, 0.39f, 0.23f), new Color(0.61f, 0.69f, 0.53f),
                new Color(0.50f, 0.66f, 0.79f), new Color(0.29f, 0.31f, 0.33f),
                new Color(0.62f, 0.29f, 0.24f), new Color(0.66f, 0.81f, 0.75f),
            };
            var schema = new SettingsSchema()
                .Swatch("color", "Color", palette, () => selected, i => selected = i);
            cam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var inspector = SetupInspector.Build(ctx);
            inspector.ShowFor(schema, false, "Paint");
            inspector.Popups.OpenSwatch(schema.Fields[0], null);
            RebuildVisuals(inspector.gameObject);
            ShootInspector(cam, inspector, "popup-swatch");
            UnityEngine.Object.DestroyImmediate(inspector.gameObject);
        }

        private static void RenderInspector(Camera cam, RigContext ctx, SettingsSchema schema,
            string shotName, string title)
        {
            // PlaceInFront reads the camera's CURRENT forward — reset it, or every next
            // panel spirals away from the previous shot's aim and shows its back side
            cam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var inspector = SetupInspector.Build(ctx);
            inspector.ShowFor(schema, false, title);
            RebuildVisuals(inspector.gameObject);
            ShootInspector(cam, inspector, shotName);
            UnityEngine.Object.DestroyImmediate(inspector.gameObject);
        }

        private static void ShootInspector(Camera cam, InspectorPanel inspector, string shotName)
        {
            const int width = 1200, height = 1500;
            var renderers = inspector.gameObject.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds bounds = default;
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.gameObject.activeInHierarchy) continue;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!hasBounds) bounds = new Bounds(inspector.Panel.position, Vector3.one * 0.1f);

            float tanHalfVertical = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = (float)width / height;
            float distance = Mathf.Max(bounds.extents.y / tanHalfVertical,
                bounds.extents.x / (tanHalfVertical * aspect));
            distance = Mathf.Max(0.45f, distance * 1.12f);
            Shoot(cam, bounds.center + Vector3.back * distance, bounds.center,
                shotName, width, height);
        }

        // ---- plumbing ----

        /// <summary>Awake never ran (edit mode) — force every generated mesh to exist.</summary>
        private static void RebuildVisuals(GameObject root)
        {
            foreach (var plate in root.GetComponentsInChildren<RoundedPlate>(true))
                plate.Rebuild();
            foreach (var icon in root.GetComponentsInChildren<IconRenderer>(true))
                icon.RebuildNow();
            foreach (var tmp in root.GetComponentsInChildren<TMPro.TMP_Text>(true))
                tmp.ForceMeshUpdate();
        }

        private static void WriteIndex()
        {
            string root = "Build/ui-shots";
            string[] files = System.IO.Directory.GetFiles(root, "*.png");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            var html = new System.Text.StringBuilder();
            html.AppendLine("<!doctype html>");
            html.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
            html.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            html.AppendLine("<title>MR Room Planner UI gallery</title>");
            html.AppendLine("<style>body{margin:0;background:#0b0e14;color:#e8edf7;font:16px system-ui,sans-serif}header{padding:24px}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:18px;padding:0 24px 32px}figure{margin:0;background:#151a24;border:1px solid #283044;border-radius:14px;overflow:hidden}img{display:block;width:100%;height:420px;object-fit:contain;background:#0b0e14}figcaption{padding:12px 14px}</style></head><body>");
            html.AppendLine("<header><h1>MR Room Planner UI gallery</h1><p>Generated from the live Unity UI components.</p></header><main>");
            foreach (string file in files)
            {
                string name = System.IO.Path.GetFileName(file);
                string title = System.IO.Path.GetFileNameWithoutExtension(file).Replace('-', ' ');
                html.Append("<figure><a href=\"").Append(name).Append("\"><img loading=\"lazy\" src=\"")
                    .Append(name).Append("\" alt=\"").Append(title).Append("\"></a><figcaption>")
                    .Append(title).AppendLine("</figcaption></figure>");
            }
            html.AppendLine("</main></body></html>");
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "index.html"), html.ToString());
        }

        private static void LoadMaterials(RigContext ctx)
        {
            Material Load(string name)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(
                    $"{SetupAssets.MatDir}/{name}.mat");
                if (m == null) throw new Exception($"material {name} missing — run SetupRig first");
                return m;
            }
            ctx.PanelMat = Load("Menu_Panel");
            ctx.RimMat = Load("Menu_Rim");
            ctx.BtnMat = Load("Menu_Button");
            ctx.ActiveMat = Load("Menu_Active");
            ctx.IconMat = Load("Ui_Icon");
            ctx.InsetMat = Load("Ui_Inset");
            ctx.ScrimMat = Load("Ui_RadialScrim");
        }

        private static void Shoot(Camera cam, Vector3 from, Vector3 at, string name,
            int w, int h)
        {
            cam.transform.position = from;
            cam.transform.rotation = Quaternion.LookRotation((at - from).normalized);

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;

            System.IO.File.WriteAllBytes($"Build/ui-shots/{name}.png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(rt);
            Debug.Log($"[CI] shot {name}.png");
        }
    }
}
#endif
