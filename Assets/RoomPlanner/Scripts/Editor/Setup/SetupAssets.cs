#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
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

            // Colors follow the UiColors system (design/16 P1.6): states keep the reserved
            // cyan/mint; data hues come from the layer palette — measurement visuals moved to
            // the Measurements violet family, freeing green (selected) and yellow (Electrical).
            ctx.LineMat = CreateMat("Measure_Line", new Color(0.94f, 0.96f, 1f));            // neutral
            ctx.MarkerMat = CreateMat("Measure_Marker", UiColors.LayerMeasurements);
            ctx.ReticleMat = CreateMat("Measure_Reticle", new Color(0.94f, 0.96f, 1f));      // yellow → Electrical reserve
            ctx.ContMat = CreateMat("Measure_Continue", UiColors.Selected);
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

            // virtual ground for the scan-off mode (design/18 I10) — muted, not attention-grabbing
            ctx.GroundMat = CreateMat("Env_Ground", new Color(0.24f, 0.27f, 0.24f));

            // window glass (wall submesh 1, design/18 I8) — pale blue, mostly transparent
            ctx.GlassMat = CreateBadgeMat("Wall_Glass", new Color(0.65f, 0.82f, 0.95f, 0.22f), null);
            // door leaves + frames (wall submesh 2, design/18 I12) — warm wood tone
            ctx.JoineryMat = CreateMat("Wall_Joinery", new Color(0.55f, 0.42f, 0.30f));
            // stairs share the concrete look of walls/floors until painting lands
            ctx.StairMat = CreateSurfaceMat("Stair_Surface", new Color(0.80f, 0.79f, 0.77f), concreteTex);
            // plumbing fixtures wear their MEP layer color (design/07); double-sided —
            // fixture Breps arrive with arbitrary winding quality
            ctx.PlumbingMat = CreateFloorMat("MEP_Plumbing", UiColors.LayerPlumbing);

            ctx.PanelMat = CreateMat("Menu_Panel", UiColors.PanelBg);   // opaque (no shader-variant stripping on device)
            ctx.RimMat = CreateMat("Menu_Rim", UiColors.PanelRim);
            ctx.BtnMat = CreateMat("Menu_Button", UiColors.ButtonBg);
            ctx.ActiveMat = CreateMat("Menu_Active", UiColors.Selected);
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

        private static Material CreateFloorMat(string name, Color color, Texture2D tex = null)
        {
            var mat = new Material(UnlitShader());
            SetColor(mat, color);
            ApplyTexture(mat, tex);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            return SaveMaterial(mat, $"{MatDir}/{name}.mat");
        }

        /// <summary>Opaque textured surface (walls). UVs are metric, so tiling stays at 1.</summary>
        private static Material CreateSurfaceMat(string name, Color tint, Texture2D tex)
        {
            var mat = new Material(UnlitShader());
            SetColor(mat, tint);
            ApplyTexture(mat, tex);
            return SaveMaterial(mat, $"{MatDir}/{name}.mat");
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

        // ---- UI factory ----

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
            MenuButtonKind kind = MenuButtonKind.Momentary)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = lp;
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(size.x, size.y, 0.04f);
            var mb = root.AddComponent<MenuButton>();

            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "Bg";
            RemoveCollider(bg);
            bg.transform.SetParent(root.transform, false);
            bg.transform.localScale = new Vector3(size.x, size.y, 1f);
            bg.GetComponent<Renderer>().sharedMaterial = bgMat;

            var text = MakeTextChild(root.transform, "Label", label, size);

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
            // and confuses radio vs toggle semantics (design/16 P1.3).
            GameObject led = null;
            if (kind == MenuButtonKind.Toggle)
            {
                led = GameObject.CreatePrimitive(PrimitiveType.Quad);
                led.name = "Led";
                RemoveCollider(led);
                led.transform.SetParent(root.transform, false);
                float d = size.y * 0.18f;
                led.transform.localScale = new Vector3(d, d, 1f);
                led.transform.localPosition = new Vector3(size.x * 0.36f, size.y * 0.32f, -0.004f);
                led.GetComponent<Renderer>().sharedMaterial = activeMat;
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
            tmp.enableWordWrapping = false;
            // allow text to use the full width of its cell, and keep a high floor so it never
            // shrinks to unreadable — panels are scaled up on top of this (see Setup*).
            tmp.rectTransform.sizeDelta = new Vector2(size.x * 1.6f, size.y * 0.95f);
            tmp.enableAutoSizing = true;
            // readability floor (UX v2 P1.5): auto-sizing is a clamp, not a shrink-to-fit
            tmp.fontSizeMin = size.y * 0.78f;
            tmp.fontSizeMax = size.y * 0.9f;
            return tmp;
        }
    }
}
#endif
