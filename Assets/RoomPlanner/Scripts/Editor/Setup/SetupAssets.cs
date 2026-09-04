#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Tools;

namespace RoomPlanner.EditorTools
{
    /// <summary>
    /// Materials/textures (mutated in place — GUIDs stay stable) and the small UI factory
    /// shared by the setup modules.
    /// </summary>
    internal static class SetupAssets
    {
        public const string PrefabDir = "Assets/RoomPlanner/Prefabs";
        public const string MatDir = "Assets/RoomPlanner/Materials";

        public static void CreateAll(RigContext ctx)
        {
            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory(PrefabDir);

            // Colors follow UiTokens (design/16 P1.6): states keep the reserved
            // cyan/mint; data hues come from the layer palette — measurement visuals moved to
            // the Measurements violet family, freeing green (selected) and yellow (Electrical).
            ctx.LineMat = CreateMat("Measure_Line", new Color(0.94f, 0.96f, 1f));            // neutral
            ctx.MarkerMat = CreateMat("Measure_Marker", UiTokens.LayerMeasurements);
            ctx.ReticleMat = CreateMat("Measure_Reticle", new Color(0.94f, 0.96f, 1f));      // yellow → Electrical reserve
            ctx.ContMat = CreateMat("Measure_Continue", UiTokens.Selected);
            Texture2D badgeTex = CreateRoundedBadgeTexture();
            ctx.BadgeMat = CreateBadgeMat("Measure_Badge", new Color(0.45f, 0.32f, 0.82f), badgeTex);

            // Concrete: one seamless procedural texture, applied with METRIC UVs
            // (design/04-surfaces-materials.md), so the grain is the same size on a 1 m and a
            // 10 m wall. Generated rather than imported — no binary assets in the repo.
            Texture2D concreteTex = CreateConcreteTexture();
            ctx.WallMat = CreateSurfaceMat("Wall_Surface", new Color(0.82f, 0.82f, 0.80f), concreteTex);
            ctx.WallEdgeMat = CreateMat("Wall_Edge", new Color(0.10f, 0.16f, 0.32f));
            // The floor TOP is the blueprint surface: concrete is only its default look, and
            // BlueprintController replaces the texture as soon as a plan is loaded.
            ctx.FloorMat = CreateFloorMat("Floor_Top", new Color(0.78f, 0.78f, 0.76f), concreteTex);

            // virtual ground for the scan-off mode (design/18 I10) — muted; PLAIN lit:
            // the Unity plane primitive has no vertex colors for the AO shader
            ctx.GroundMat = CreatePlainLitMat("Env_Ground", new Color(0.24f, 0.27f, 0.24f));
            // procedural sky for the scan-off mode and render shots — as an ASSET so the
            // skybox shader survives build stripping
            ctx.SkyMat = CreateSkyMat("Env_Sky");

            // window glass (wall submesh 1, design/18 I8) — pale blue, mostly transparent.
            // Glass must NOT cast shadows: sun comes through the window into the house
            // (feedback 2026-08-11) — belt-and-braces, the badge shader has no caster pass.
            ctx.GlassMat = CreateBadgeMat("Wall_Glass", new Color(0.65f, 0.82f, 0.95f, 0.22f), null);
            ctx.GlassMat.SetShaderPassEnabled("ShadowCaster", false);
            // door leaves + frames (wall submesh 2, design/18 I12) — warm wood tone, lit
            ctx.JoineryMat = CreateSurfaceMat("Wall_Joinery", new Color(0.55f, 0.42f, 0.30f), null);
            // stairs share the concrete look of walls/floors until painting lands
            ctx.StairMat = CreateSurfaceMat("Stair_Surface", new Color(0.80f, 0.79f, 0.77f), concreteTex);
            // plumbing FIXTURES are white porcelain (feedback 2026-08-10) — the blue
            // LayerPlumbing color stays reserved for pipes/routes when they arrive.
            // Double-sided (fixture Breps have arbitrary winding); plain lit: imported
            // meshes carry no vertex colors for the AO shader.
            ctx.PlumbingMat = CreatePlainLitMat("MEP_Plumbing", new Color(0.95f, 0.96f, 0.97f));
            // Baked imports beyond plumbing (design/18 I17). Neutral defaults — the IFC's
            // own IfcStyledItem colour overrides per object via the paint machinery.
            ctx.FurnitureMat = CreatePlainLitMat("MEP_Furniture", new Color(0.62f, 0.50f, 0.38f)); // warm wood
            ctx.ProxyMat = CreatePlainLitMat("MEP_Generic", new Color(0.78f, 0.78f, 0.80f));       // trade plastic
            ctx.RailingMat = CreatePlainLitMat("MEP_Railing", new Color(0.35f, 0.36f, 0.38f));     // dark metal
            // TVs/monitors (matched by IFC name): near-black glossy glass, not wood
            ctx.ScreenMat = CreatePlainLitMat("MEP_Screen", new Color(0.05f, 0.05f, 0.06f));
            MakeGlossy(ctx.ScreenMat, 0.85f);

            // Electrical layer (design/19): wires graphite, not pure black — #000 reads as a
            // hole on passthrough; fixtures near-white, so they read as trade plastic.
            // LIT since 2026-08-11 (headset feedback: unlit fixtures read as flat decals):
            // outlets/switches = glossy trade plastic, wires = satin PVC sheath.
            ctx.WireMat = CreatePlainLitMat("Electric_Wire", new Color(0.102f, 0.102f, 0.102f));
            MakeGlossy(ctx.WireMat, 0.30f);
            ctx.FixtureMat = CreatePlainLitMat("Electric_Fixture", new Color(0.92f, 0.92f, 0.91f));
            MakeGlossy(ctx.FixtureMat, 0.55f);

            ctx.PanelMat = CreateMat("Menu_Panel", UiTokens.PanelBg);   // opaque (no shader-variant stripping on device)
            ctx.RimMat = CreateMat("Menu_Rim", UiTokens.PanelRim);
            ctx.BtnMat = CreateMat("Menu_Button", UiTokens.ButtonBg);
            ctx.ActiveMat = CreateMat("Menu_Active", UiTokens.Selected);

            // UI v2 (design/20 §6): icons/plates are white and MPB-tinted per state;
            // inset wells are darker "carved-in" surfaces; the radial scrim is a
            // procedural radial-falloff texture (texture > vertex color: survives
            // shader-variant stripping on device).
            ctx.IconMat = CreateMat("Ui_Icon", Color.white);
            ctx.InsetMat = CreateMat("Ui_Inset", RoomPlanner.Core.UiTokens.InsetBg);
            ctx.ScrimMat = CreateBadgeMat("Ui_RadialScrim",
                new Color(RoomPlanner.Core.UiTokens.PanelBg.r, RoomPlanner.Core.UiTokens.PanelBg.g,
                    RoomPlanner.Core.UiTokens.PanelBg.b, RoomPlanner.Core.UiTokens.RadialScrimAlpha),
                CreateScrimTexture());
        }

        /// <summary>Radial alpha falloff for the tool-wheel scrim (design/20 §6.5).
        /// Deterministic content — same GUID-stable reuse as the badge texture.</summary>
        private static Texture2D CreateScrimTexture()
        {
            string path = $"{MatDir}/Ui_ScrimFalloffTex.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "Ui_ScrimFalloffTex"
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float a = RoomPlanner.Core.UiMeshes.ScrimAlpha(r);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(pixels);
            tex.Apply();
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        /// <summary>Thin rim behind a panel background — keeps the panel silhouette visible on
        /// dark passthrough (UX v2 P1.5). Child of the bg quad, so it follows runtime resizing.</summary>
        public static void AddRim(GameObject bgQuad, Material rimMat)
        {
            var rim = GameObject.CreatePrimitive(PrimitiveType.Quad);
            rim.name = "Rim";
            RemoveCollider(rim);
            rim.transform.SetParent(bgQuad.transform, false);
            rim.transform.localScale = new Vector3(1.03f, 1.05f, 1f);
            rim.transform.localPosition = new Vector3(0f, 0f, 0.002f);   // just behind the bg
            rim.GetComponent<Renderer>().sharedMaterial = rimMat;
        }

        // ---- materials ----

        private static Material CreateMat(string name, Color color)
        {
            var mat = new Material(UnlitShader());
            SetColor(mat, color);
            return SaveMaterial(mat, $"{MatDir}/{name}.mat");
        }

        /// <summary>
        /// Persist a material WITHOUT changing its GUID: mutate the existing asset if one is
        /// already at the path (Delete+Create would re-GUID it every Setup run, breaking any
        /// external reference and churning the scene diff).
        /// </summary>
        private static Material SaveMaterial(Material tmp, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(tmp, path);
                return tmp;
            }
            existing.shader = tmp.shader;
            existing.CopyPropertiesFromMaterial(tmp);
            existing.shaderKeywords = tmp.shaderKeywords;
            existing.renderQueue = tmp.renderQueue;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(tmp);
            return existing;
        }

        /// <summary>Белая текстура скруглённого прямоугольника (альфа) — подложка бейджа.</summary>
        private static Texture2D CreateRoundedBadgeTexture()
        {
            // Deterministic content — reuse the existing asset so its GUID stays stable.
            string existingPath = $"{MatDir}/Measure_BadgeTex.asset";
            var existingTex = AssetDatabase.LoadAssetAtPath<Texture2D>(existingPath);
            if (existingTex != null) return existingTex;

            const int W = 320, H = 160;
            const float radius = 46f;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "Measure_BadgeTex"
            };
            var px = new Color[W * H];
            float hw = W * 0.5f, hh = H * 0.5f;
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - hw) - (hw - radius);
                float qy = Mathf.Abs(y + 0.5f - hh) - (hh - radius);
                float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                float d = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                float a = Mathf.Clamp01(0.5f - d); // ~1px антиалиасинг по краю
                px[y * W + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px);
            tex.Apply();
            AssetDatabase.CreateAsset(tex, existingPath);
            return tex;
        }

        /// <summary>Прозрачный двусторонний URP-Unlit материал бейджа с тинтом-цветом.</summary>
        private static Material CreateBadgeMat(string name, Color color, Texture2D tex)
        {
            var mat = new Material(UnlitShader());
            if (tex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }
            SetColor(mat, color);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = 2990;
            return SaveMaterial(mat, $"{MatDir}/{name}.mat");
        }

        /// <summary>Procedural daylight sky (built-in Skybox/Procedural works under URP).</summary>
        private static Material CreateSkyMat(string name)
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null) return null;
            var mat = new Material(shader);
            if (mat.HasProperty("_AtmosphereThickness")) mat.SetFloat("_AtmosphereThickness", 0.85f);
            if (mat.HasProperty("_SkyTint")) mat.SetColor("_SkyTint", new Color(0.55f, 0.65f, 0.78f));
            if (mat.HasProperty("_GroundColor")) mat.SetColor("_GroundColor", new Color(0.35f, 0.35f, 0.34f));
            if (mat.HasProperty("_Exposure")) mat.SetFloat("_Exposure", 1.15f);
            return SaveMaterial(mat, $"{MatDir}/{name}.mat");
        }

        /// <summary>Lit material WITHOUT vertex AO — ground, MEP meshes.</summary>
        private static Material CreatePlainLitMat(string name, Color color)
        {
            var mat = new Material(LitShader());
            SetColor(mat, color);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            TameSpecular(mat);
            return SaveMaterial(mat, $"{MatDir}/{name}.mat");
        }

        private static Material CreateFloorMat(string name, Color color, Texture2D tex = null)
        {
            var mat = new Material(AOShader());
            SetColor(mat, color);
            ApplyTexture(mat, tex);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            TameSpecular(mat);
            return SaveMaterial(mat, $"{MatDir}/{name}.mat");
        }

        /// <summary>Opaque textured surface (walls). UVs are metric, so tiling stays at 1.</summary>
        private static Material CreateSurfaceMat(string name, Color tint, Texture2D tex)
        {
            var mat = new Material(AOShader());
            SetColor(mat, tint);
            ApplyTexture(mat, tex);
            TameSpecular(mat);
            return SaveMaterial(mat, $"{MatDir}/{name}.mat");
        }

        /// <summary>Matte building surfaces: no smooth plastic sheen on concrete/wood.</summary>
        /// <summary>Opposite of TameSpecular: glossy plastic/glass with a live highlight
        /// (electric fixtures, TV screens) — call AFTER the Create* factory.</summary>
        private static void MakeGlossy(Material mat, float smoothness)
        {
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 1f);
        }

        private static void TameSpecular(Material mat)
        {
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.08f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 0f);
            if (mat.HasProperty("_EnvironmentReflections")) mat.SetFloat("_EnvironmentReflections", 0f);
        }

        private static void ApplyTexture(Material mat, Texture2D tex)
        {
            if (tex == null) return;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            mat.mainTexture = tex;
            mat.mainTextureScale = Vector2.one;   // scale lives in the metric UVs, not here
        }

        /// <summary>
        /// Seamless concrete: a few octaves of PERIODIC value noise plus fine speckle. Periodic
        /// means the lattice wraps, so the tile repeats without a visible seam — which matters
        /// because metric UVs repeat it every metre across a whole flat.
        /// Deterministic (hash, not Random) so re-running setup reproduces the same asset.
        /// </summary>
        private static Texture2D CreateConcreteTexture()
        {
            string path = $"{MatDir}/Surface_ConcreteTex.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int S = 256;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
                name = "Surface_ConcreteTex",
            };

            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float n = 0f, amp = 0.5f;
                for (int lattice = 4; lattice <= 32; lattice *= 2)
                {
                    n += PeriodicNoise(x / (float)S * lattice, y / (float)S * lattice, lattice) * amp;
                    amp *= 0.5f;
                }
                // fine grit: per-texel hash, kept subtle so it reads as concrete, not TV snow
                float grit = (Hash(x * 73856093 ^ y * 19349663) - 0.5f) * 0.10f;

                float shade = Mathf.Clamp01(0.72f + (n - 0.5f) * 0.35f + grit);
                px[y * S + x] = new Color(shade, shade, shade * 0.99f, 1f);
            }
            tex.SetPixels(px);
            tex.Apply();

            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        /// <summary>Value noise on a lattice that wraps at `period` — hence tileable.</summary>
        private static float PeriodicNoise(float x, float y, int period)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);            // smoothstep
            fy = fy * fy * (3f - 2f * fy);

            float v00 = Lattice(x0, y0, period), v10 = Lattice(x0 + 1, y0, period);
            float v01 = Lattice(x0, y0 + 1, period), v11 = Lattice(x0 + 1, y0 + 1, period);
            return Mathf.Lerp(Mathf.Lerp(v00, v10, fx), Mathf.Lerp(v01, v11, fx), fy);
        }

        private static float Lattice(int x, int y, int period)
        {
            // wrapping the lattice index is what makes the result seamless
            int wx = ((x % period) + period) % period;
            int wy = ((y % period) + period) % period;
            return Hash(wx * 374761393 ^ wy * 668265263 ^ period * 2147483647);
        }

        /// <summary>Deterministic 0..1 hash — no Random, so the generated asset is stable.</summary>
        private static float Hash(int n)
        {
            unchecked
            {
                n = (n << 13) ^ n;
                int m = (n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff;
                return m / (float)0x7fffffff;
            }
        }

        private static void SetColor(Material mat, Color color)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            mat.color = color;
        }

        private static Shader UnlitShader()
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Unlit/Color");
            if (s == null) s = Shader.Find("Sprites/Default");
            return s != null ? s : Shader.Find("Standard");
        }

        /// <summary>Plain lit shader — for lit surfaces WITHOUT baked vertex colors
        /// (ground plane, MEP fixture meshes): the AO shader would read missing vertex
        /// color as black on some drivers.</summary>
        private static Shader LitShader()
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            return s != null ? s : UnlitShader();
        }

        /// <summary>Lit + baked vertex-AO shader for OUR procedural surfaces (walls,
        /// floors, stairs, joinery) — their builders always write vertex colors
        /// (design/04 realism pass). UI, lines and markers stay unlit on purpose.</summary>
        private static Shader AOShader()
        {
            Shader s = Shader.Find("RoomPlanner/LitVertexAO");
            return s != null ? s : LitShader();
        }

        // ---- UI factory ----

        /// <summary>Rounded plate GameObject (design/20 §6.3): renderer exists now (so
        /// MenuButton can bind), mesh is generated by RoundedPlate.Awake at runtime.</summary>
        public static GameObject MakePlateGo(Transform parent, string name, Vector3 lp,
            float w, float h, float radius, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = lp;
            var plate = go.AddComponent<RoundedPlate>();
            plate.Configure(w, h, radius, mat);
            return go;
        }

        public static void RemoveCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }

        public static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursively(c.gameObject, layer);
        }

        public static MenuButton MakeMenuButton(Transform parent, string name, string label, MenuAction action,
            Vector3 lp, Vector2 size, Material bgMat, Material activeMat, bool withActiveMark, int toolIndex = -1,
            MenuButtonKind kind = MenuButtonKind.Momentary, string iconId = null, Material iconMat = null)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = lp;
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(size.x, size.y, 0.04f);
            var mb = root.AddComponent<MenuButton>();

            // rounded plate, not a square quad — the design-code standard (20 §6.3)
            var bg = MakePlateGo(root.transform, "Bg", Vector3.zero,
                size.x, size.y, RoomPlanner.Core.UiTokens.RadiusM, bgMat);

            // icon buttons (design/20 §5): mesh built by IconRenderer at runtime Awake —
            // nothing mesh-like is serialized, the id string is enough
            if (!string.IsNullOrEmpty(iconId))
            {
                var icon = new GameObject("Icon");
                icon.transform.SetParent(root.transform, false);
                icon.transform.localPosition = new Vector3(0f, 0f, -0.006f);
                var ir = icon.AddComponent<IconRenderer>();
                var iso = new SerializedObject(ir);
                iso.FindProperty("iconId").stringValue = iconId;
                iso.FindProperty("material").objectReferenceValue = iconMat;
                iso.FindProperty("size").floatValue = Mathf.Min(size.x, size.y) * 0.62f;
                iso.ApplyModifiedProperties();
            }

            var text = string.IsNullOrEmpty(label) ? null : MakeTextChild(root.transform, "Label", label, size);

            GameObject mark = null;
            if (withActiveMark)
            {
                mark = GameObject.CreatePrimitive(PrimitiveType.Quad);
                mark.name = "Active";
                RemoveCollider(mark);
                mark.transform.SetParent(root.transform, false);
                mark.transform.localScale = new Vector3(size.x * 0.9f, size.y * 0.14f, 1f);
                mark.transform.localPosition = new Vector3(0f, -size.y * 0.62f, -0.004f);
                mark.GetComponent<Renderer>().sharedMaterial = activeMat;
                mark.SetActive(false);
            }

            // Toggle buttons get a small LED dot — the strip alone reads like "active tool"
            // and confuses radio vs toggle semantics (design/16 P1.3). Round, per design 20.
            GameObject led = null;
            if (kind == MenuButtonKind.Toggle)
            {
                float d = size.y * 0.13f;
                led = MakePlateGo(root.transform, "Led",
                    new Vector3(size.x * 0.32f, size.y * 0.30f, -0.004f),
                    d, d, d * 0.5f, activeMat);
                led.SetActive(false);
            }

            var so = new SerializedObject(mb);
            so.FindProperty("action").enumValueIndex = (int)action;
            so.FindProperty("toolIndex").intValue = toolIndex;
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("bgRenderer").objectReferenceValue = bg.GetComponent<Renderer>();
            so.FindProperty("label").objectReferenceValue = text;
            if (mark != null) so.FindProperty("activeMark").objectReferenceValue = mark;
            if (led != null) so.FindProperty("ledDot").objectReferenceValue = led;
            so.ApplyModifiedProperties();
            return mb;
        }

        public static TMP_Text MakeValueLabel(Transform parent, string name, string text, Vector3 lp, Vector2 size)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = lp;
            return MakeTextChild(root.transform, "Text", text, size);
        }

        public static TMP_Text MakeTextChild(Transform parent, string name, string text, Vector2 size)
        {
            var t = new GameObject(name);
            t.transform.SetParent(parent, false);
            t.transform.localPosition = new Vector3(0f, 0f, -0.006f);
            var tmp = t.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            // allow text to use the full width of its cell
            tmp.rectTransform.sizeDelta = new Vector2(size.x * 1.6f, size.y * 0.95f);
            // fixed size + ellipsis, same rationale as InspectorPanel.MakeText (issue #55):
            // TMP auto-fit on sub-unit world-space rects sporadically rendered labels giant
            tmp.enableAutoSizing = false;
            tmp.fontSize = size.y * 2.9f;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return tmp;
        }
    }
}
#endif
