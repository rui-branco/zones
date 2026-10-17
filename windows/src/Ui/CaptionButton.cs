using System;
using System.Drawing;
using System.Windows.Forms;

namespace Zones
{
    /// <summary>
    /// A minimize or close button for a window that draws its own title bar:
    /// the Windows 11 caption button, a bare glyph on the window's colour that
    /// lights up grey under the mouse, or red for close.
    ///
    /// Square and edge to edge, as the system's are. In the window's corner,
    /// Windows' own rounding of the window clips the close button's red, the
    /// same as on any other window. The grey and the red fade in and out, as
    /// the system's do.
    /// </summary>
    public class CaptionButton : Control
    {
        float _s = 1f;
        int _glyph;
        bool _isClose;
        bool _hover;
        bool _pressed;

        readonly Tween _hoverMotion;
        readonly Tween _pressMotion;

        public CaptionButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            // Click is raised here, on a release over the button after a press
            // on it; and quick clicks are each a click, not a double-click.
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
            // Mouse only, like the system's: the keyboard has Escape and Alt+F4.
            TabStop = false;

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

        /// <summary>The icon-font code point to draw.</summary>
        public int Glyph
        {
            get { return _glyph; }
            set
            {
                _glyph = value;
                Invalidate();
            }
        }

        /// <summary>The close button: red under the mouse, with a white glyph.</summary>
        public bool IsClose
        {
            get { return _isClose; }
            set
            {
                _isClose = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Theme.WindowBack))
                g.FillRectangle(b, ClientRectangle);

            bool enabled = Enabled;
            float hot = enabled ? _hoverMotion.Value : 0;
            float pressed = enabled ? _pressMotion.Value : 0;

            // The hover colour faded in from nothing over the window's own,
            // then on towards the pressed one.
            Color hover = _isClose ? Color.FromArgb(0xC4, 0x2B, 0x1C) : Theme.Hover;
            Color down = _isClose ? Color.FromArgb(0xB2, 0x27, 0x19) : Theme.Pressed;
            Color fill = Motion.Blend(Motion.Blend(Color.FromArgb(0, hover), hover, hot), down, pressed);
            if (fill.A > 0)
            {
                using (SolidBrush b = new SolidBrush(fill))
                    g.FillRectangle(b, ClientRectangle);
            }

            if (_glyph != 0)
                Theme.DrawGlyph(g, Theme.Glyph(_glyph), 10 * _s, _isClose ? Motion.Blend(Theme.Text, Color.White, hot) : Theme.Text, ClientRectangle);
        }

        /// <summary>Points the fades at where the mouse has left things; pressed only while still over the button.</summary>
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

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _hover = false;
            _pressed = false;
            Animate();
            Invalidate();
        }
    }
}
