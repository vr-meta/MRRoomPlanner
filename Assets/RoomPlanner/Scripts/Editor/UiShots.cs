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

                ShotRadial(cam, ctx);
                ShotStrip(cam, ctx);
                ShotInspectorShowcase(cam, ctx);
                ShotNumpad(cam, ctx);
                ShotEveryTool(cam, ctx);

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
            radial.Configure(SlotDefs());
            radial.Open(head, 8);   // Electric is the active tool
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

                var wall = host.AddComponent<RoomPlanner.Walls.WallController>();
                Wire(wall, "manager", manager);
                RenderInspector(cam, ctx, wall.GetSettings(), "tool-wall");

                var floor = host.AddComponent<RoomPlanner.Floors.FloorController>();
                Wire(floor, "manager", manager);
                RenderInspector(cam, ctx, floor.GetSettings(), "tool-floor");

                var blueprint = host.AddComponent<RoomPlanner.Floors.BlueprintController>();
                RenderInspector(cam, ctx, blueprint.GetSettings(), "tool-blueprint");

                var import = host.AddComponent<RoomPlanner.Import.ImportController>();
                RenderInspector(cam, ctx, import.GetSettings(), "tool-import");

                var paint = host.AddComponent<PaintController>();
                ctx.Finishes = SetupPaintTool.BuildFinishLibrary(host);
                Wire(paint, "library", ctx.Finishes);
                RenderInspector(cam, ctx, paint.GetSettings(), "tool-paint");
                // second shot: the Walls texture tab
                paint.GetSettings().SelectTab(1);
                RenderInspector(cam, ctx, paint.GetSettings(), "tool-paint-walls");
                paint.GetSettings().SelectTab(2);
                RenderInspector(cam, ctx, paint.GetSettings(), "tool-paint-floors");
                paint.GetSettings().SelectTab(3);
                RenderInspector(cam, ctx, paint.GetSettings(), "tool-paint-tiles");
                paint.GetSettings().SelectTab(4);
                RenderInspector(cam, ctx, paint.GetSettings(), "tool-paint-ceiling");
                paint.GetSettings().SelectTab(5);   // object materials (design/29)
                RenderInspector(cam, ctx, paint.GetSettings(), "tool-paint-objects");
                paint.GetSettings().SelectTab(0);

                var electric = host.AddComponent<RoomPlanner.Electrical.ElectricController>();
                var schema = electric.GetSettings();
                for (int tab = 0; tab < schema.Tabs.Length; tab++)
                {
                    schema.SelectTab(tab);
                    RenderInspector(cam, ctx, schema,
                        $"tool-electric-{schema.Tabs[tab].ToLowerInvariant()}");
                }

                // Select: the selection group with a sample pick
                cam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var inspector = SetupInspector.Build(ctx);
                inspector.SetSelection("Wall #3", "Length 3.2 m · Height 2.7 m · 20 cm");
                inspector.ShowFor(null, showSelection: true);
                RebuildVisuals(inspector.gameObject);
                Shoot(cam, new Vector3(0f, 0f, -0.22f), inspector.Panel.position + Vector3.down * 0.13f,
                    "tool-select", 1200, 1500);
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

            RenderInspector(cam, ctx, schema, "inspector-widgets");
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
            inspector.ShowFor(schema, false);
            inspector.Popups.OpenNumpad(field, null);
            RebuildVisuals(inspector.gameObject);

            Shoot(cam, new Vector3(0f, 0f, -0.22f), inspector.Panel.position + Vector3.down * 0.10f,
                "numpad", 1200, 1500);
            UnityEngine.Object.DestroyImmediate(inspector.gameObject);
        }

        private static void RenderInspector(Camera cam, RigContext ctx, SettingsSchema schema,
            string shotName)
        {
            // PlaceInFront reads the camera's CURRENT forward — reset it, or every next
            // panel spirals away from the previous shot's aim and shows its back side
            cam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var inspector = SetupInspector.Build(ctx);
            inspector.ShowFor(schema, false);
            RebuildVisuals(inspector.gameObject);
            // panel origin is TOP-center — aim at the content middle so nothing crops
            Shoot(cam, new Vector3(0f, 0f, -0.22f), inspector.Panel.position + Vector3.down * 0.13f,
                shotName, 1200, 1500);
            UnityEngine.Object.DestroyImmediate(inspector.gameObject);
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

        private static RadialSlotDef[] SlotDefs()
        {
            (string icon, string label, Color tint, int tool)[] slots =
            {
                ("select-cursor", "Select", new Color(0.91f, 0.93f, 0.96f), 0),
                ("tape-measure", "Measure", new Color(0.61f, 0.48f, 1f), 1),
                ("wall", "Wall", new Color(0.60f, 0.65f, 0.75f), 2),
                ("floor-slab", "Floor", new Color(0.60f, 0.65f, 0.75f), 3),
                ("door-window", "Openings", Color.gray, -1),
                ("furniture", "Furniture", Color.gray, -1),
                ("blueprint", "Blueprint", new Color(0.54f, 0.82f, 0.78f), 6),
                ("import-file", "Import", new Color(0.91f, 0.93f, 0.96f), 7),
                ("electric-plug", "Electric", new Color(1f, 0.79f, 0.30f), 8),
                ("radiator", "Heating", Color.gray, -1),
                ("pipe", "Plumbing", Color.gray, -1),
                ("paint-roller", "Paint", new Color(0.88f, 0.66f, 0.42f), 11),
            };
            var defs = new RadialSlotDef[slots.Length];
            for (int i = 0; i < slots.Length; i++)
                defs[i] = new RadialSlotDef
                {
                    IconId = slots[i].icon, Label = slots[i].label,
                    Tint = slots[i].tint, ToolIndex = slots[i].tool,
                };
            return defs;
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
