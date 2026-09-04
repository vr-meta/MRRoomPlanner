using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Tools;
using HighlightState = RoomPlanner.Editing.HighlightState;   // TMPro declares one too

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Visual-state logic of the menu system (design/16 P1.3-P1.4): button states
    /// (hover/pressed/disabled), radio-vs-toggle semantics, and lerp-tinting of selectable
    /// objects that preserves their own color.
    /// </summary>
    public class MenuButtonStatePlayTests
    {
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.Destroy(go);
            _spawned.Clear();
        }

        // MPB round-trips colors through the linear color space — compare with tolerance
        private static void AssertColor(Color expected, Color actual, string message)
        {
            Assert.AreEqual(expected.r, actual.r, 0.01f, message + " (r)");
            Assert.AreEqual(expected.g, actual.g, 0.01f, message + " (g)");
            Assert.AreEqual(expected.b, actual.b, 0.01f, message + " (b)");
        }

        private MenuButton MakeButton(MenuButtonKind kind, out Renderer bg, out TMP_Text label)
        {
            var root = new GameObject("Btn");
            _spawned.Add(root);
            var mb = root.AddComponent<MenuButton>();

            var bgGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bgGo.transform.SetParent(root.transform, false);
            Object.Destroy(bgGo.GetComponent<Collider>());
            bg = bgGo.GetComponent<Renderer>();

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(root.transform, false);
            label = textGo.AddComponent<TextMeshPro>();

            mb.InitRuntime(kind, bg, label);
            return mb;
        }

        [Test]
        public void RadioActive_InvertsButton()
        {
            var mb = MakeButton(MenuButtonKind.Radio, out var bg, out var label);

            mb.SetActiveTool(true);
            AssertColor(UiTokens.LabelDark, label.color, "active radio = dark text on light bg");
            var mpb = new MaterialPropertyBlock();
            bg.GetPropertyBlock(mpb);
            AssertColor(UiTokens.LabelLight, mpb.GetColor("_BaseColor"), "active radio bg is inverted");

            mb.SetActiveTool(false);
            AssertColor(UiTokens.LabelLight, label.color, "inactive radio back to light text");
        }

        [Test]
        public void ToggleActive_DoesNotInvert()
        {
            var mb = MakeButton(MenuButtonKind.Toggle, out var bg, out var label);
            mb.SetActiveTool(true);
            AssertColor(UiTokens.LabelLight, label.color,
                "a toggle must NOT look like an active tool (radio inversion)");
            var mpb = new MaterialPropertyBlock();
            bg.GetPropertyBlock(mpb);
            AssertColor(UiTokens.ButtonBg, mpb.GetColor("_BaseColor"), "toggle bg stays normal");
        }

        [Test]
        public void Disabled_IgnoresHover_AndBlocksInteraction()
        {
            var mb = MakeButton(MenuButtonKind.Momentary, out _, out var label);
            Vector3 baseScale = mb.transform.localScale;

            mb.SetEnabled(false);
            Assert.IsFalse(mb.Interactable);
            Assert.AreEqual(UiTokens.DisabledAlpha, label.color.a, 1e-3f, "disabled label fades");

            mb.SetHighlight(true);
            Assert.AreEqual(baseScale, mb.transform.localScale, "disabled button must not react to hover");

            mb.SetEnabled(true);
            Assert.IsTrue(mb.Interactable);
            Assert.AreEqual(1f, label.color.a, 1e-3f);
        }

        [Test]
        public void Hover_LiftsSlightly_Press_Dips()
        {
            var mb = MakeButton(MenuButtonKind.Momentary, out _, out _);
            Vector3 baseScale = mb.transform.localScale;

            mb.SetHighlight(true);
            Assert.AreEqual(baseScale.x * 1.06f, mb.transform.localScale.x, 1e-4f, "hover lift 1.06");

            mb.Press();
            Assert.AreEqual(baseScale.x * 0.97f, mb.transform.localScale.x, 1e-4f, "press dips below base");
        }

        [Test]
        public void ReticleVisual_ExposesToolSnapDimensionAndGestureState()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _spawned.Add(go);
            go.transform.localScale = Vector3.one * 0.04f;
            var visual = go.AddComponent<ReticleVisual>();

            visual.ConfigureTool("wall", "wall", UiTokens.LayerStructure,
                "Trigger: point · B: finish", showGesture: true);
            visual.SetSnap(ReticleSnapKind.Grid);
            visual.SetDimension("2.40 m");

            Assert.AreEqual(ReticleSnapKind.Grid, visual.SnapKind);
            Assert.IsNotNull(go.transform.Find("ToolGlyph"));
            Assert.IsTrue(go.transform.Find("Dimension").gameObject.activeSelf);
            Assert.IsTrue(go.transform.Find("DimensionBadge").gameObject.activeSelf);
            Assert.IsTrue(go.transform.Find("GestureHint").gameObject.activeSelf);
            Assert.IsFalse(go.GetComponent<Renderer>().enabled, "legacy sphere is replaced by the snap outline");
        }

        // ---- object highlight keeps identity (P1.4) ----

        [Test]
        public void Highlight_TintsTowardStateColor_PreservingOwnColor()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _spawned.Add(go);
            var mat = new Material(Shader.Find("Sprites/Default")) { color = Color.red };
            go.GetComponent<Renderer>().sharedMaterial = mat;
            var sel = go.AddComponent<Selectable>();

            sel.SetHighlight(HighlightState.Hover);
            var mpb = new MaterialPropertyBlock();
            go.GetComponent<Renderer>().GetPropertyBlock(mpb);
            Color tinted = mpb.GetColor("_Color");

            Assert.AreNotEqual(UiTokens.Hover, tinted, "not a full repaint — identity must survive");
            Assert.Greater(tinted.r, UiTokens.Hover.r, "red base still shows through the tint");
            Assert.Greater(tinted.b, Color.red.b, "state color is mixed in");

            sel.SetHighlight(HighlightState.None);
            go.GetComponent<Renderer>().GetPropertyBlock(mpb);
            Assert.IsTrue(mpb.isEmpty, "clearing the highlight restores the material's own color");
            Object.Destroy(mat);
        }
    }
}
