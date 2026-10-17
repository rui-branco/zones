using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Zones
{
    /// <summary>
    /// A number between a minus and a plus button: the compact Windows 11 way
    /// to set a small count, where a slider would be too fiddly to land on an
    /// exact value and a text box would invite typing in nonsense.
    ///
    /// Holding a button repeats, after a pause, like a scroll bar arrow; the
    /// mouse wheel and the arrow keys step it too. A button at its limit draws
    /// itself disabled, so the end of the range is visible before it is hit.
    /// </summary>
    public class Stepper : Control
    {
        float _s = 1f;
        bool _onCard;
        int _min;
        int _max = 100;
        int _increment = 1;
        int _value;
        string _suffix = "";

        int _hot;       // -1 the minus button, +1 the plus button, 0 neither
        int _down;      // the button being held, same values
        readonly Timer _repeat = new Timer();

        // A fade of each kind for each button, minus first: going from one
        // button straight to the other, one fades out as the other fades in.
        readonly Tween[] _hoverMotion = new Tween[2];
        readonly Tween[] _pressMotion = new Tween[2];

        public event EventHandler ValueChanged;

        public Stepper()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = true;

            for (int i = 0; i < 2; i++)
            {
                _hoverMotion[i] = new Tween(this, 0, Motion.Fast);
                _pressMotion[i] = new Tween(this, 0, Motion.Fast);
            }

            _repeat.Tick += delegate
            {
                // The first tick waits out the pause; after that, quick steps,
                // and only while the cursor is still on the button it pressed.
                _repeat.Interval = 80;
                if (_down != 0 && _hot == _down) Step(_down);
            };
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

        public int Minimum
        {
            get { return _min; }
            set
            {
                _min = value;
                Value = _value;
                Invalidate();
            }
        }

        public int Maximum
        {
            get { return _max; }
            set
            {
                _max = value;
                Value = _value;
                Invalidate();
            }
        }

        public int Increment
        {
            get { return _increment; }
            set { _increment = value; }
        }

        /// <summary>Kept between Minimum and Maximum.</summary>
        public int Value
        {
            get { return _value; }
            set
            {
                int v = Math.Max(_min, Math.Min(_max, value));
                if (v == _value) return;
                _value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>Written after the number, as " px".</summary>
        public string Suffix
        {
            get { return _suffix; }
            set
            {
                _suffix = value ?? "";
                Invalidate();
            }
        }

        int R(float px)
        {
            return (int)Math.Round(px * _s);
        }

        Rectangle MinusButton()
        {
            int b = R(32);
            return new Rectangle(0, (Height - b) / 2, b, b);
        }

        Rectangle PlusButton()
        {
            int b = R(32);
            return new Rectangle(Width - b, (Height - b) / 2, b, b);
        }

        int HitButton(Point p)
        {
            if (MinusButton().Contains(p)) return -1;
            if (PlusButton().Contains(p)) return 1;
            return 0;
        }

        bool AtLimit(int button)
        {
            return button < 0 ? _value <= _min : _value >= _max;
        }

        void Step(int direction)
        {
            Value = _value + direction * _increment;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(_onCard ? Theme.CardBack : Theme.WindowBack))
                g.FillRectangle(b, ClientRectangle);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Rectangle minus = MinusButton();
            Rectangle plus = PlusButton();
            DrawButton(g, minus, -1, 0xE738);
            DrawButton(g, plus, 1, 0xE710);

            using (SolidBrush b = new SolidBrush(Enabled ? Theme.Text : Theme.TextFaint))
            using (StringFormat f = new StringFormat())
            {
                f.Alignment = StringAlignment.Center;
                f.LineAlignment = StringAlignment.Center;
                f.FormatFlags = StringFormatFlags.NoWrap;
                RectangleF between = RectangleF.FromLTRB(minus.Right, 0, plus.Left, Height);
                g.DrawString(_value + _suffix, Theme.Font(14 * _s), b, between, f);
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

        void DrawButton(Graphics g, Rectangle r, int which, int glyph)
        {
            bool live = Enabled && !AtLimit(which);
            int i = which < 0 ? 0 : 1;
            float hover = live ? _hoverMotion[i].Value : 0;
            float pressed = live ? _pressMotion[i].Value : 0;
            Color fill = Motion.Blend(Motion.Blend(Theme.ControlBack, Theme.ControlHover, hover), Theme.ControlPressed, pressed);

            RectangleF edge = r;
            edge.Inflate(-0.5f, -0.5f);
            using (GraphicsPath p = Theme.Rounded(edge, 4 * _s))
            {
                using (SolidBrush b = new SolidBrush(fill))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.ControlStroke, 1f))
                    g.DrawPath(pen, p);
            }
            Theme.DrawGlyph(g, Theme.Glyph(glyph), 12 * _s, live ? Theme.Text : Theme.TextFaint, r);
        }

        /// <summary>
        /// Points each button's fades at where the mouse has left things.
        /// Pressed only shows while the cursor is still on the button held,
        /// which is also when it repeats.
        /// </summary>
        void Animate()
        {
            for (int i = 0; i < 2; i++)
            {
                int which = i == 0 ? -1 : 1;
                _hoverMotion[i].Target = _hot == which ? 1 : 0;
                _pressMotion[i].Target = _hot == which && _down == which ? 1 : 0;
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hot = HitButton(PointToClient(Cursor.Position));
            Animate();
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hot = 0;
            Animate();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hot = HitButton(e.Location);
            if (hot == _hot) return;
            _hot = hot;
            Animate();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled || e.Button != MouseButtons.Left) return;
            Focus();

            int button = HitButton(e.Location);
            if (button == 0 || AtLimit(button)) return;

            _down = button;
            Animate();
            Step(button);
            _repeat.Interval = 400;
            _repeat.Start();
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            StopRepeat();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            // Capture taken away mid-press (a window popping up) never sends
            // the mouse up: without this the button would go on repeating.
            StopRepeat();
        }

        void StopRepeat()
        {
            _repeat.Stop();
            if (_down == 0) return;
            _down = 0;
            Animate();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!Enabled || e.Delta == 0) return;
            Step(Math.Sign(e.Delta));
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Up || keyData == Keys.Down) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled) return;
            if (e.KeyCode == Keys.Up)
            {
                Step(1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Down)
            {
                Step(-1);
                e.Handled = true;
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _hot = 0;
            StopRepeat();
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

        protected override void Dispose(bool disposing)
        {
            if (disposing) _repeat.Dispose();
            base.Dispose(disposing);
        }
    }
}
