using System.Collections.Generic;
using TMPro;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tools
{
    /// <summary>
    /// The visual half of placement dimension guides (issue #113): up to two thin
    /// lines from the element being placed to the nearest wall axes, each with a
    /// centimeter label at its midpoint. Placement tools call ShowAt every tick
    /// while previewing and Hide when the preview goes away; quantization itself
    /// is the caller's business (PlacementGuides.Quantize).
    /// </summary>
    public class PlacementGuideOverlay : MonoBehaviour
    {
        [SerializeField] private LineRenderer lineA;
        [SerializeField] private LineRenderer lineB;
        [SerializeField] private TextMeshPro labelA;
        [SerializeField] private TextMeshPro labelB;

        private readonly WallGuide[] _guides = new WallGuide[2];
        private int _count;

        public WallGuide[] Guides => _guides;
        public int Count => _count;

        /// <summary>Finds the guides for the point and draws them. Pass a non-zero
        /// <paramref name="mountNormal"/> for wall-mounted elements — their own wall
        /// must not become a dimension. Returns the guide count.</summary>
        public int ShowAt(Vector3 point, IReadOnlyList<Vector3> wallAxes, Vector3 mountNormal)
        {
            _count = PlacementGuides.FindGuides(point, wallAxes, _guides);
            _count = PlacementGuides.FilterByNormal(_guides, _count, mountNormal);
            Draw(lineA, labelA, point, 0);
            Draw(lineB, labelB, point, 1);
            return _count;
        }

        /// <summary>Quantizes the point over the CURRENT guides (call after ShowAt).</summary>
        public Vector3 Quantize(Vector3 point, float step) =>
            PlacementGuides.Quantize(point, _guides, _count, step);

        public void Hide()
        {
            _count = 0;
            if (lineA != null) lineA.enabled = false;
            if (lineB != null) lineB.enabled = false;
            if (labelA != null) labelA.gameObject.SetActive(false);
            if (labelB != null) labelB.gameObject.SetActive(false);
        }

        private void Draw(LineRenderer line, TextMeshPro label, Vector3 point, int index)
        {
            bool on = index < _count && _guides[index].Valid;
            if (line != null)
            {
                line.enabled = on;
                if (on)
                {
                    line.positionCount = 2;
                    line.SetPosition(0, _guides[index].Closest);
                    line.SetPosition(1, point);
                }
            }
            if (label == null) return;
            label.gameObject.SetActive(on);
            if (!on) return;
            label.text = $"{_guides[index].Distance * 100f:0} cm";
            label.transform.position = Vector3.Lerp(_guides[index].Closest, point, 0.5f)
                + Vector3.up * 0.03f;
            var cam = Camera.main;
            if (cam != null)
                label.transform.rotation = Quaternion.LookRotation(
                    label.transform.position - cam.transform.position);
        }
    }
}
