using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Zones
{
    /// <summary>
    /// The menu down the left of the settings window, drawn like the
    /// navigation pane of Windows 11 Settings: one row per page, its icon and
    /// its name, the open one lit and marked with an accent pill at its edge.
    /// Choosing another slides the pill across to it, as Settings does.
    /// </summary>
    public class NavList : Control
    {
        // In logical pixels: a row, and the gap under it.
        const int ItemH = 36;
        const int Gap = 4;

        float _s = 1f;
        readonly List<string> _names = new List<string>();
        readonly List<int> _glyphs = new List<int>();
        int _selected = -1;
        int _hover = -1;
        int _down = -1;

        // A fade of each kind per row, so moving between rows, or choosing
        // another, cross-fades.
        readonly List<Tween> _hoverMotion = new List<Tween>();
        readonly List<Tween> _pressMotion = new List<Tween>();
        readonly List<Tween> _selectMotion = new List<Tween>();

        /// <summary>Where the pill is, in rows: 1.5 is halfway from the second row to the third.</summary>
        readonly Tween _pill;

        public event EventHandler SelectedChanged;

        public NavList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = true;

            _pill = new Tween(this, 0, Motion.Slow);
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

        /// <summary>Adds a page to the end of the menu. The first one added starts selected.</summary>
        public void Add(string name, int glyph)
        {
            _names.Add(name);
            _glyphs.Add(glyph);
            _hoverMotion.Add(new Tween(this, 0, Motion.Fast));
            _pressMotion.Add(new Tween(this, 0, Motion.Fast));
            _selectMotion.Add(new Tween(this, 0, Motion.Normal));
            if (_selected < 0)
            {
                _selected = 0;
                MarkSelected(false);
            }
            Invalidate();
        }

        /// <summary>Once there is a window, the pill slides to the new row; before, it is simply there.</summary>
        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int i = Math.Max(-1, Math.Min(_names.Count - 1, value));
                if (i == _selected) return;
                _selected = i;
                MarkSelected(IsHandleCreated);
                Invalidate();
                if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
            }
        }

        int R(float px)
        {
            return (int)Math.Round(px * _s);
        }

        /// <summary>Tall enough for every row, in device pixels.</summary>
        public int PreferredHeight()
        {
            int n = _names.Count;
            return n == 0 ? 0 : R(n * ItemH + (n - 1) * Gap);
        }

        Rectangle Row(int i)
        {
            return new Rectangle(0, R(i * (ItemH + Gap)), Width, R(ItemH));
        }

        int RowAt(Point p)
        {
            for (int i = 0; i < _names.Count; i++)
            {
                if (Row(i).Contains(p)) return i;
            }
            return -1;
        }

        /// <summary>
        /// Moves the marks of the selection to the selected row: the pill
        /// slides there and the rows' fills cross-fade, or, without
        /// <paramref name="animate"/>, both are simply there.
        /// </summary>
        void MarkSelected(bool animate)
        {
            for (int i = 0; i < _selectMotion.Count; i++)
            {
                float on = i == _selected ? 1 : 0;
                if (animate) _selectMotion[i].Target = on;
                else _selectMotion[i].Snap(on);
            }
            if (_selected < 0) return;
            if (animate) _pill.Target = _selected;
            else _pill.Snap(_selected);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Theme.WindowBack))
                g.FillRectangle(b, ClientRectangle);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Font font = Theme.Font(14 * _s);
            for (int i = 0; i < _names.Count; i++)
            {
                Rectangle row = Row(i);

                // Selected and hovered are the same fill, not one on the other;
                // pressed is a shade off it.
                float lit = Math.Max(_selectMotion[i].Value, _hoverMotion[i].Value);
                Color fill = Motion.Blend(Color.FromArgb(0, Theme.Hover), Theme.Hover, lit);
                fill = Motion.Blend(fill, Theme.Pressed, _pressMotion[i].Value);
                if (fill.A > 0) Theme.FillRounded(g, fill, row, 4 * _s);

                // In the row's whole height: the icon font's line is taller
                // than the glyph, and a box only the glyph's height shows nothing.
                Theme.DrawGlyph(g, Theme.Glyph(_glyphs[i]), 16 * _s, Theme.Text,
                                new RectangleF(row.Left + R(16), row.Top, R(16), row.Height));

                using (SolidBrush b = new SolidBrush(Theme.Text))
                using (StringFormat f = new StringFormat())
                {
                    f.LineAlignment = StringAlignment.Center;
                    f.FormatFlags = StringFormatFlags.NoWrap;
                    f.Trimming = StringTrimming.EllipsisCharacter;
                    float x = row.Left + R(48);
                    g.DrawString(_names[i], font, b, new RectangleF(x, row.Top, row.Right - x, row.Height), f);
                }
            }

            // The pill, after every row, as on its way between two it belongs
            // to neither: wherever the slide has got to, between their tops.
            if (_selected >= 0)
            {
                float at = _pill.Value;
                int from = (int)Math.Floor(at);
                float rowTop = Motion.Lerp(Row(from).Top, Row(from + 1).Top, at - from);
                float pillH = 16 * _s;
                Theme.FillRounded(g, Theme.Accent, new RectangleF(0, rowTop + (R(ItemH) - pillH) / 2, 3 * _s, pillH), 1.5f * _s);
            }

            if (Focused && ShowFocusCues && _names.Count > 0)
            {
                RectangleF ring = Row(Math.Max(0, _selected));
                ring.Inflate(-_s, -_s);
                using (GraphicsPath p = Theme.Rounded(ring, 4 * _s))
                using (Pen pen = new Pen(Theme.Text, 2 * _s))
                    g.DrawPath(pen, p);
            }
        }

        /// <summary>
        /// Points every row's hover and press at where the mouse has left
        /// things. Pressed only shows on the row pressed, while the cursor is
        /// still on it: let go anywhere else and nothing is chosen.
        /// </summary>
        void Animate()
        {
            for (int i = 0; i < _hoverMotion.Count; i++)
            {
                _hoverMotion[i].Target = i == _hover ? 1 : 0;
                _pressMotion[i].Target = i == _hover && i == _down ? 1 : 0;
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = RowAt(PointToClient(Cursor.Position));
            Animate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Animate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hover = RowAt(e.Location);
            if (hover == _hover) return;
            _hover = hover;
            Animate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            _down = RowAt(e.Location);
            Animate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int down = _down;
            _down = -1;
            Animate();
            if (e.Button != MouseButtons.Left || down < 0) return;
            if (RowAt(e.Location) == down) SelectedIndex = down;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Up || keyData == Keys.Down) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_names.Count == 0) return;
            if (e.KeyCode == Keys.Up)
            {
                SelectedIndex = Math.Max(0, _selected - 1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Down)
            {
                SelectedIndex = Math.Min(_names.Count - 1, _selected + 1);
                e.Handled = true;
            }
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
