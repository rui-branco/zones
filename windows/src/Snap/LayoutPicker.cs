using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Zones
{
    /// <summary>
    /// The ready-made layouts as one row of tiles, each a miniature of the
    /// display carved up the way that layout would carve it, with its name
    /// underneath. Picking one is the quick route; the zone editor below it is
    /// for anything the presets do not cover.
    ///
    /// The miniatures take the selected display's own proportions, so "Thirds"
    /// on an ultrawide looks like what it will be on that ultrawide, not on
    /// some generic 16:9 screen.
    /// </summary>
    public class LayoutPicker : Control
    {
        float _s = 1f;
        bool _onCard;
        float _aspect = 16f / 9f;
        int _selected = -1;
        int _hover = -1;
        int _down = -1;
        int _picked = -1;

        /// <summary>Built once: the presets never change, and they are drawn on every paint.</summary>
        readonly List<ZoneLayout> _presets = new List<ZoneLayout>();

        // Every tile fades on its own, so moving from one to the next, or
        // picking another, cross-fades: the old one goes as the new one comes.
        readonly Tween[] _hoverMotion;
        readonly Tween[] _pressMotion;
        readonly Tween[] _selectMotion;

        /// <summary>A tile was clicked; <see cref="PickedIndex"/> says which.</summary>
        public event EventHandler PresetPicked;

        public LayoutPicker()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            // Mouse only: there is no keyboard way through the tiles, so a tab
            // stop here would be a stop that does nothing.
            TabStop = false;

            for (int i = 0; i < ZoneLayout.PresetNames.Length; i++) _presets.Add(ZoneLayout.Preset(i));

            int n = _presets.Count;
            _hoverMotion = new Tween[n];
            _pressMotion = new Tween[n];
            _selectMotion = new Tween[n];
            for (int i = 0; i < n; i++)
            {
                _hoverMotion[i] = new Tween(this, 0, Motion.Fast);
                _pressMotion[i] = new Tween(this, 0, Motion.Fast);
                _selectMotion[i] = new Tween(this, 0, Motion.Normal);
            }
        }

        /// <summary>How big a logical pixel is: the owner's DPI scale.</summary>
        public new float Scale
        {
            get { return _s; }
            set
            {
                _s = value;
                Invalidate();
            }
        }

        /// <summary>Sits on a settings card rather than on the window itself.</summary>
        public bool OnCard
        {
            get { return _onCard; }
            set
            {
                _onCard = value;
                Invalidate();
            }
        }

        /// <summary>The display's width over its height: the shape of every miniature.</summary>
        public float Aspect
        {
            get { return _aspect; }
            set
            {
                _aspect = value > 0 ? value : 16f / 9f;
                Invalidate();
            }
        }

        /// <summary>
        /// The preset shown as chosen, or -1. Setting it raises nothing. Once
        /// there is a window, the accent edge fades across to the new one;
        /// before, as the owner fills in its settings, it is simply there.
        /// </summary>
        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                _selected = value;
                for (int i = 0; i < _selectMotion.Length; i++)
                {
                    float on = i == value ? 1 : 0;
                    if (IsHandleCreated) _selectMotion[i].Target = on;
                    else _selectMotion[i].Snap(on);
                }
                Invalidate();
            }
        }

        /// <summary>The tile last clicked, for <see cref="PresetPicked"/>.</summary>
        public int PickedIndex
        {
            get { return _picked; }
        }

        int R(float px)
        {
            return (int)Math.Round(px * _s);
        }

        Rectangle Tile(int i)
        {
            int n = _presets.Count;
            int gap = R(8);
            int w = (Width - gap * (n - 1)) / n;
            return new Rectangle(i * (w + gap), 0, w, Height);
        }

        int TileAt(Point p)
        {
            for (int i = 0; i < _presets.Count; i++)
            {
                if (Tile(i).Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(_onCard ? Theme.CardBack : Theme.WindowBack))
                g.FillRectangle(b, ClientRectangle);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            for (int i = 0; i < _presets.Count; i++) DrawTile(g, i);
        }

        void DrawTile(Graphics g, int i)
        {
            Rectangle tile = Tile(i);
            bool enabled = Enabled;
            float selected = _selectMotion[i].Value;
            float hover = enabled ? _hoverMotion[i].Value : 0;
            float pressed = enabled ? _pressMotion[i].Value : 0;

            // A button-like plate. The chosen one keeps the resting fill and
            // takes an accent edge instead, inset so all of it stays inside.
            RectangleF edge = tile;
            float inset = Motion.Lerp(0.5f, _s, selected);
            edge.Inflate(-inset, -inset);
            using (GraphicsPath p = Theme.Rounded(edge, 6 * _s))
            {
                Color fill = Motion.Blend(Motion.Blend(Theme.ControlBack, Theme.ControlHover, hover * (1 - selected)), Theme.ControlPressed, pressed);
                using (SolidBrush b = new SolidBrush(fill))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(Motion.Blend(Theme.ControlStroke, Theme.Accent, selected), Motion.Lerp(1f, 2 * _s, selected)))
                    g.DrawPath(pen, p);
            }

            // The miniature: the display's shape, as large as fits the box
            // over the name, and centred in it. Just the zones, no screen
            // behind them: their gaps already draw the display's outline.
            RectangleF box = new RectangleF(tile.Left + R(12), tile.Top + R(12), tile.Width - R(24), tile.Height - R(12) - R(30));
            float w = Math.Min(box.Width, box.Height * _aspect);
            float h = w / _aspect;
            RectangleF mini = new RectangleF(box.Left + (box.Width - w) / 2, box.Top + (box.Height - h) / 2, w, h);

            Color rest = Theme.Light ? Color.FromArgb(0xC9, 0xC9, 0xC9) : Color.FromArgb(0x5A, 0x5A, 0x5A);
            Color zone = Motion.Blend(rest, Theme.Accent, selected);
            foreach (Rectangle z in _presets[i].ZonesIn(Rectangle.Round(mini), Math.Max(2, R(3))))
                Theme.FillRounded(g, zone, z, 2 * _s);

            using (SolidBrush b = new SolidBrush(!enabled ? Theme.TextFaint : Motion.Blend(Theme.TextDim, Theme.Text, selected)))
            using (StringFormat f = new StringFormat())
            {
                f.Alignment = StringAlignment.Center;
                f.LineAlignment = StringAlignment.Center;
                f.FormatFlags = StringFormatFlags.NoWrap;
                f.Trimming = StringTrimming.EllipsisCharacter;
                RectangleF label = RectangleF.FromLTRB(tile.Left, tile.Bottom - R(30), tile.Right, tile.Bottom);
                g.DrawString(ZoneLayout.PresetNames[i], Theme.Font(12 * _s), b, label, f);
            }
        }

        /// <summary>
        /// Points every tile's hover and press at where the mouse has left
        /// things. Pressed only shows on the tile pressed, while the cursor is
        /// still on it: let go anywhere else and nothing is picked.
        /// </summary>
        void Animate()
        {
            for (int i = 0; i < _presets.Count; i++)
            {
                _hoverMotion[i].Target = i == _hover ? 1 : 0;
                _pressMotion[i].Target = i == _hover && i == _down ? 1 : 0;
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = TileAt(PointToClient(Cursor.Position));
            Animate();
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Animate();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hover = TileAt(e.Location);
            if (hover == _hover) return;
            _hover = hover;
            Animate();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled || e.Button != MouseButtons.Left) return;
            _down = TileAt(e.Location);
            Animate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int down = _down;
            _down = -1;
            Animate();
            if (!Enabled || e.Button != MouseButtons.Left || down < 0 || TileAt(e.Location) != down) return;

            _picked = down;
            SelectedIndex = down;
            if (PresetPicked != null) PresetPicked(this, EventArgs.Empty);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _hover = -1;
            _down = -1;
            Animate();
            Invalidate();
        }
    }
}
