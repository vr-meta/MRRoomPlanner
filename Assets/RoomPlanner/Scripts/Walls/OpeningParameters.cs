using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Editing;

namespace RoomPlanner.Walls
{
    /// <summary>
    /// Per-door settings page (issue #50, StairParameters pattern): sits on the
    /// opening's leaf child next to its Selectable. Width/Height are undoable
    /// commands validated with OpeningMath (invalid input is clamped to the last
    /// valid value); Hinge/Swing flip sides; "Open" drives the leaf directly —
    /// a VIEW action, deliberately outside the undo history.
    /// </summary>
    public class OpeningParameters : MonoBehaviour, ISettingsProvider
    {
        private WallGraphRenderer _walls;
        private WallSegment _seg;
        private Selectable _wallSel;
        private OpeningLeafView _leaf;
        private SettingsSchema _schema;

        private static readonly string[] HingeOptions = { "Left", "Right" };
        private static readonly string[] SwingOptions = { "In", "Out" };

        public void Configure(WallGraphRenderer walls, WallSegment seg, Selectable wallSel)
        {
            _walls = walls;
            _seg = seg;
            _wallSel = wallSel;
            if (_leaf == null) _leaf = GetComponent<OpeningLeafView>();
        }

        private WallOpening Op => _leaf != null ? _leaf.Opening : null;

        /// <summary>B on a selected door deletes the OPENING (not just the leaf view).</summary>
        public ICommand BuildDeleteCommand() =>
            Op == null ? null : new DeleteOpeningCommand(_walls, _seg, Op, _wallSel);

        public SettingsSchema GetSettings()
        {
            if (_leaf == null) _leaf = GetComponent<OpeningLeafView>();
            if (Op == null) return null;
            if (_schema != null) return _schema;

            bool garage = Op.Kind == OpeningKind.Garage;
            (float minW, float maxW, float minH, float maxH) lim = garage
                ? (1.8f, 5.0f, 1.8f, 3.0f)
                : (0.6f, 1.2f, 1.8f, 2.4f);

            var s = new SettingsSchema()
                .Readout("kind", "Kind", () => garage ? "Garage door" : "Door")
                .Numeric("w", "Width", lim.minW, lim.maxW,
                    () => Op.Width,
                    (_, v) => Resize(Mathf.Clamp(v, lim.minW, lim.maxW), Op.Height),
                    () => $"{Op.Width * 100f:0} cm", displayScale: 100f)
                .Numeric("h", "Height", lim.minH, lim.maxH,
                    () => Op.Height,
                    (_, v) => Resize(Op.Width, Mathf.Clamp(v, lim.minH, lim.maxH)),
                    () => $"{Op.Height * 100f:0} cm", displayScale: 100f);

            if (!garage)
            {
                s.Segmented("hinge", "Hinge", HingeOptions,
                        () => HingeIndex(), i => SetHinge(i))
                 .Segmented("swing", "Swing", SwingOptions,
                        () => SwingIndex(), i => SetSwing(i));
            }

            s.Slider("open", "Open", 0f, 1f, 0.05f,
                    () => Op.OpenFraction,
                    f => _leaf.SetFraction(f, animate: false),
                    (_, f) => _leaf.SetFraction(f, animate: false),
                    () => $"{Op.OpenFraction * 100f:0}%", displayScale: 100f)
             .Action("delete", "Delete opening", "eraser", DeleteSelf, destructive: true);

            _schema = s;
            return s;
        }

        /// <summary>One undoable size edit; a size that no longer fits the wall
        /// (piers/header/neighbours) is refused and nothing changes.</summary>
        private void Resize(float w, float h)
        {
            if (Op == null || _seg == null) return;
            float center = Op.AlongFraction * _seg.Length;
            if (!OpeningMath.CanPlace(_seg, center, w, Op.SillHeight + h, ignore: Op)) return;
            Execute(new OpeningEditCommand(_walls, _seg, Op, w, h, Op.SillHeight, _wallSel));
        }

        // ---- hinge / swing sides, expressed against the wall's A→B direction ----

        private Vector3 AlongDir()
        {
            if (_seg == null) return Vector3.right;
            var d = _seg.B.Position - _seg.A.Position; d.y = 0f;
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.right;
        }

        private Vector3 OutwardDir()
        {
            var d = AlongDir();
            var rn = new Vector3(d.z, 0f, -d.x);
            return _seg != null && _seg.SideSign < 0f ? -rn : rn;
        }

        private int HingeIndex() =>
            Op != null && Op.HingeDir.sqrMagnitude > 1e-6f
                && Vector3.Dot(Op.HingeDir, AlongDir()) < 0f ? 1 : 0;

        private int SwingIndex() =>
            Op != null && Op.SwingDir.sqrMagnitude > 1e-6f
                && Vector3.Dot(Op.SwingDir, OutwardDir()) >= 0f ? 1 : 0;

        private void SetHinge(int i)
        {
            if (Op == null || i == HingeIndex()) return;
            var hinge = i == 1 ? -AlongDir() : AlongDir();
            var swing = Op.SwingDir.sqrMagnitude > 1e-6f ? Op.SwingDir : -OutwardDir();
            Execute(new OpeningSwingCommand(_walls, _seg, Op, swing, hinge, _wallSel));
        }

        private void SetSwing(int i)
        {
            if (Op == null || i == SwingIndex()) return;
            var swing = i == 1 ? OutwardDir() : -OutwardDir();
            var hinge = Op.HingeDir.sqrMagnitude > 1e-6f ? Op.HingeDir : AlongDir();
            Execute(new OpeningSwingCommand(_walls, _seg, Op, swing, hinge, _wallSel));
        }

        private void DeleteSelf()
        {
            var cmd = BuildDeleteCommand();
            if (cmd != null) Execute(cmd);
        }

        private void Execute(ICommand cmd)
        {
            var model = SceneModel.Instance;
            if (model != null) model.History.Execute(cmd);
            else cmd.Do();
        }
    }
}
