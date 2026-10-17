using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Zones
{
    /// <summary>
    /// The Windows 11 standard button: a softly rounded plate with a hairline
    /// edge, an optional icon-font glyph before the label, or filled with the
    /// accent for the one action a page leads with.
    ///
    /// Owner-drawn rather than a themed Button, which keeps the Windows 10
    /// look and has no dark mode of its own. Sized by the caller from
    /// <see cref="PreferredWidth"/>, so a label that changes (an "Applied"
    /// confirmation) only needs the layout run again.
    /// </summary>
    public class FlatButton : Control
    {
        float _s = 1f;
        bool _onCard;
        int _glyph;
        bool _accent;
        bool _hover;
        bool _pressed;

        // Hover and press fade in and out rather than switching, as Windows
        // 11's own buttons do.
        readonly Tween _hoverMotion;
        readonly Tween _pressMotion;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            // Click is raised here, on a release over the button after a press
            // on it; and quick clicks are each a click, not a double-click.
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
            TabStop = true;

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

        /// <summary>An icon-font code point drawn before the label, or 0 for none.</summary>
        public int Glyph
        {
            get { return _glyph; }
            set
            {
                _glyph = value;
                Invalidate();
            }
        }

        /// <summary>Filled with the accent colour: the page's main action.</summary>
        public bool Accent
        {
            get { return _accent; }
            set
            {
                _accent = value;
                Invalidate();
            }
        }

        int R(float px)
        {
            return (int)Math.Round(px * _s);
        }

        /// <summary>
        /// Wide enough for the glyph and label with the standard padding, in
        /// device pixels. A glyph with no label has no gap after it.
        /// </summary>
        public int PreferredWidth()
        {
            bool label = !string.IsNullOrEmpty(Text);
            return R(12) + (_glyph != 0 ? R(16) + (label ? R(8) : 0) : 0)
                   + (label ? TextRenderer.MeasureText(Text, Theme.Font(14 * _s)).Width : 0) + R(12);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(_onCard ? Theme.CardBack : Theme.WindowBack))
                g.FillRectangle(b, ClientRectangle);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            bool enabled = Enabled;
            float hover = enabled ? _hoverMotion.Value : 0;
            float pressed = enabled ? _pressMotion.Value : 0;

            RectangleF plate = ClientRectangle;
            Color ink;
            if (_accent)
            {
                Color fill = Motion.Blend(Theme.Accent, Motion.Blend(Theme.Accent, Theme.WindowBack, 0.08f), hover);
                fill = Motion.Blend(fill, Motion.Blend(Theme.Accent, Theme.WindowBack, 0.16f), pressed);
                Theme.FillRounded(g, fill, plate, 4 * _s);
                ink = enabled ? Theme.OnAccent : Theme.TextFaint;
            }
            else
            {
                Color fill = Motion.Blend(Motion.Blend(Theme.ControlBack, Theme.ControlHover, hover), Theme.ControlPressed, pressed);
                plate.Inflate(-0.5f, -0.5f);
                using (GraphicsPath p = Theme.Rounded(plate, 4 * _s))
                {
                    using (SolidBrush b = new SolidBrush(fill))
                        g.FillPath(b, p);
                    using (Pen pen = new Pen(Theme.ControlStroke, 1f))
                        g.DrawPath(pen, p);
                }
                ink = enabled ? Theme.Text : Theme.TextFaint;
            }

            // Glyph and label as one block, centred: at the preferred width
            // that puts it exactly the padding in from the left. A glyph with
            // no label is centred on the whole button, to the half pixel.
            if (_glyph != 0 && string.IsNullOrEmpty(Text))
            {
                Theme.DrawGlyph(g, Theme.Glyph(_glyph), 14 * _s, ink, ClientRectangle);
            }
            else
            {
                int content = PreferredWidth() - R(24);
                int x = (Width - content) / 2;
                if (_glyph != 0)
                {
                    Theme.DrawGlyph(g, Theme.Glyph(_glyph), 14 * _s, ink, new RectangleF(x, 0, R(16), Height));
                    x += R(16) + R(8);
                }

                using (SolidBrush b = new SolidBrush(ink))
                using (StringFormat f = new StringFormat())
                {
                    f.LineAlignment = StringAlignment.Center;
                    f.FormatFlags = StringFormatFlags.NoWrap;
                    g.DrawString(Text, Theme.Font(14 * _s), b, new RectangleF(x, 0, Math.Max(0, Width - x), Height), f);
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

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        /// <summary>
        /// Points the fades at where the mouse has left things. Pressed only
        /// shows while the cursor is still on the button: let go elsewhere,
        /// and there is no click.
        /// </summary>
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
                OnClick(EventArgs.Empty);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Enter || keyData == Keys.Space) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled || (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Space)) return;
            OnClick(EventArgs.Empty);
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
