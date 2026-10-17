using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Zones
{
    /// <summary>
    /// The Windows 11 toggle switch: a pill-shaped track with a knob that sits
    /// left for off and right for on, and the state written out beside it, the
    /// way Settings shows every on/off setting.
    ///
    /// The whole control is the click target, label included, as it is in
    /// Settings: a track 40 pixels wide is a small thing to aim at. Where
    /// room is short and the row's title already says what is switched, the
    /// label can go (<see cref="ShowLabel"/>) and the track alone remains.
    /// Colours come from Theme on every paint, so a light/dark switch only
    /// needs a repaint.
    ///
    /// It moves as Windows 11's does: the knob slides across while the track
    /// fills with the accent, grows under the mouse, and stretches while
    /// pressed, away from the side it is about to leave.
    /// </summary>
    public class ToggleSwitch : Control
    {
        float _s = 1f;
        bool _onCard;
        bool _showLabel = true;
        bool _checked;
        bool _hover;
        bool _pressed;

        readonly Tween _onMotion;       // 0 off, 1 on: where the knob is, how full the track
        readonly Tween _hoverMotion;    // the knob grown under the mouse
        readonly Tween _pressMotion;    // the knob stretched while pressed

        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = true;

            _onMotion = new Tween(this, 0, Motion.Normal);
            _hoverMotion = new Tween(this, 0, Motion.Fast);
            _pressMotion = new Tween(this, 0, Motion.Fast);
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

        /// <summary>Writes "On" or "Off" before the track. Without it, the track sits alone at the right edge.</summary>
        public bool ShowLabel
        {
            get { return _showLabel; }
            set
            {
                _showLabel = value;
                Invalidate();
            }
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                // Set before there is a window, as the owner fills in its
                // settings, it is simply in place; after that, it slides.
                if (IsHandleCreated) _onMotion.Target = value ? 1 : 0;
                else _onMotion.Snap(value ? 1 : 0);
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        int R(float px)
        {
            return (int)Math.Round(px * _s);
        }

        Rectangle Track()
        {
            int w = R(40), h = R(20);
            return new Rectangle(Width - w, (Height - h) / 2, w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color back = _onCard ? Theme.CardBack : Theme.WindowBack;
            using (SolidBrush b = new SolidBrush(back))
                g.FillRectangle(b, ClientRectangle);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            bool enabled = Enabled;
            Rectangle track = Track();

            if (_showLabel)
            {
                using (SolidBrush b = new SolidBrush(enabled ? Theme.Text : Theme.TextFaint))
                using (StringFormat f = new StringFormat())
                {
                    f.Alignment = StringAlignment.Far;
                    f.LineAlignment = StringAlignment.Center;
                    f.FormatFlags = StringFormatFlags.NoWrap;
                    g.DrawString(_checked ? "On" : "Off", Theme.Font(14 * _s), b,
                                 new RectangleF(0, 0, track.Left - R(12), Height), f);
                }
            }

            // The knob sits in the middle of the track's round end, and grows
            // around that centre under the mouse rather than off its edge.
            float half = track.Height / 2f;
            float cy = track.Top + half;

            if (!enabled)
            {
                float d = 12 * _s;
                float cx = _checked ? track.Right - half : track.Left + half;
                RectangleF knob = new RectangleF(cx - d / 2, cy - d / 2, d, d);
                if (_checked)
                {
                    Theme.FillRounded(g, Theme.TextFaint, track, half);
                    // Disabled, the knob is cut out of the faint track, or it would
                    // vanish into a fill of its own colour.
                    using (SolidBrush b = new SolidBrush(back))
                        g.FillEllipse(b, knob);
                }
                else
                {
                    RectangleF edge = track;
                    edge.Inflate(-0.5f, -0.5f);
                    using (GraphicsPath p = Theme.Rounded(edge, edge.Height / 2))
                    using (Pen pen = new Pen(Theme.TextFaint, 1f))
                        g.DrawPath(pen, p);
                    using (SolidBrush b = new SolidBrush(Theme.TextFaint))
                        g.FillEllipse(b, knob);
                }
            }
            else
            {
                float on = _onMotion.Value;
                float grow = _hoverMotion.Value;
                float press = _pressMotion.Value;

                // Off, a dim outline; on, a fill of the accent. The outline
                // turns the accent as the fill comes up under it, so once on
                // there is nothing of it left to see.
                Color fill = Motion.Blend(Color.FromArgb(0, Theme.Accent), Theme.Accent, on);
                if (fill.A > 0) Theme.FillRounded(g, fill, track, half);
                if (on < 1)
                {
                    RectangleF edge = track;
                    edge.Inflate(-0.5f, -0.5f);
                    using (GraphicsPath p = Theme.Rounded(edge, edge.Height / 2))
                    using (Pen pen = new Pen(Motion.Blend(Theme.TextDim, Theme.Accent, on), 1f))
                        g.DrawPath(pen, p);
                }

                // 12 across, 14 under the mouse, and pressed 17 wide by 14
                // tall. The extra width goes the way the knob will move: all
                // to the right when off, all to the left when on, and handed
                // over as it slides, so letting go never makes it jump.
                float d = Motion.Lerp(12, 14, grow) * _s;
                float stretch = 3 * press * _s;
                float cx = Motion.Lerp(track.Left + half, track.Right - half, on);
                RectangleF knob = new RectangleF(cx - d / 2 - stretch * on, cy - d / 2, d + stretch, d);
                using (SolidBrush b = new SolidBrush(Motion.Blend(Theme.TextDim, Theme.OnAccent, on)))
                {
                    if (stretch <= 0)
                    {
                        g.FillEllipse(b, knob);
                    }
                    else
                    {
                        using (GraphicsPath p = Theme.Rounded(knob, d / 2))
                            g.FillPath(b, p);
                    }
                }
            }

            if (Focused && ShowFocusCues)
            {
                RectangleF ring = ClientRectangle;
                ring.Inflate(-_s, -_s);
                using (GraphicsPath p = Theme.Rounded(ring, 4 * _s))
                using (Pen pen = new Pen(Theme.Text, 2 * _s))
                    g.DrawPath(pen, p);
            }
        }

        /// <summary>Points the knob's grow and stretch at where the mouse has left things.</summary>
        void Animate()
        {
            _hoverMotion.Target = _hover ? 1 : 0;
            _pressMotion.Target = _hover && _pressed ? 1 : 0;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Animate();
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Animate();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = ClientRectangle.Contains(e.Location);
            if (hover == _hover) return;
            _hover = hover;
            Animate();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled || e.Button != MouseButtons.Left) return;
            _pressed = true;
            Animate();
            Focus();
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool pressed = _pressed;
            _pressed = false;
            Animate();
            Invalidate();
            if (pressed && Enabled && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
                Checked = !Checked;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space) return true;
            return base.IsInputKey(keyData);
        }

        // On the key's release, as Windows' own buttons do: on the press, a
        // held Space auto-repeats and flips the switch back and forth.
        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode != Keys.Space || !Enabled) return;
            Checked = !Checked;
            e.Handled = true;
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _hover = false;
            _pressed = false;
            Animate();
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnChangeUICues(UICuesEventArgs e)
        {
            base.OnChangeUICues(e);
            Invalidate();
        }
    }
}
