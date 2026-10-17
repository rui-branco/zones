using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Zones
{
    /// <summary>
    /// The list of displays down the side of the settings window, drawn
    /// like the navigation list in Windows 11 Settings: one row per display,
    /// the chosen one marked with an accent pill at its edge.
    ///
    /// Each row gives the display's Windows number and resolution, the two
    /// things that tell otherwise identical monitors apart in Display settings
    /// as well, so the same screen goes by the same name in both places.
    /// Choosing another slides the pill across to it, as Settings does.
    /// </summary>
    public class DisplayPicker : Control
    {
        float _s = 1f;
        bool _onCard;
        List<Display> _displays = new List<Display>();
        int _selected = -1;
        int _hover = -1;
        int _down = -1;

        // A fade of each kind per row, so moving between rows, or choosing
        // another, cross-fades. Made again when the number of displays changes.
        Tween[] _hoverMotion;
        Tween[] _pressMotion;
        Tween[] _selectMotion;

        /// <summary>Where the pill is, in rows: 1.5 is halfway from the second row to the third.</summary>
        readonly Tween _pill;

        public event EventHandler SelectedChanged;

        public DisplayPicker()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = true;

            _pill = new Tween(this, 0, Motion.Slow);
            Rebuild();
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

        /// <summary>
        /// The displays to list. A fresh list keeps the same display selected,
        /// by device name, if it is still attached; otherwise the first. This
        /// does not raise <see cref="SelectedChanged"/>: whoever hands over a
        /// new list reloads from <see cref="Selected"/> itself. Nothing was
        /// chosen, so the pill does not slide either: it is simply there.
        /// </summary>
        public List<Display> Displays
        {
            get { return _displays; }
            set
            {
                string keep = Selected != null ? Selected.Device : null;
                _displays = value ?? new List<Display>();
                _selected = _displays.Count > 0 ? 0 : -1;
                if (keep != null)
                {
                    for (int i = 0; i < _displays.Count; i++)
                    {
                        if (string.Equals(_displays[i].Device, keep, StringComparison.OrdinalIgnoreCase))
                        {
                            _selected = i;
                            break;
                        }
                    }
                }
                _hover = -1;
                _down = -1;
                if (_hoverMotion.Length != _displays.Count) Rebuild();
                MarkSelected(false);
                Animate();
                Invalidate();
            }
        }

        /// <summary>Once there is a window, the pill slides to the new row; before, it is simply there.</summary>
        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int i = Math.Max(-1, Math.Min(_displays.Count - 1, value));
                if (i == _selected) return;
                _selected = i;
                MarkSelected(IsHandleCreated);
                Invalidate();
                if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>The selected display, or null if there is none.</summary>
        public Display Selected
        {
            get { return _selected >= 0 && _selected < _displays.Count ? _displays[_selected] : null; }
        }

        int R(float px)
        {
            return (int)Math.Round(px * _s);
        }

        /// <summary>Tall enough for every row, in device pixels.</summary>
        public int PreferredHeight()
        {
            int n = _displays.Count;
            return n == 0 ? 0 : R(n * 56 + (n - 1) * 4);
        }

        Rectangle Row(int i)
        {
            return new Rectangle(0, R(i * 60), Width, R(56));
        }

        int RowAt(Point p)
        {
            for (int i = 0; i < _displays.Count; i++)
            {
                if (Row(i).Contains(p)) return i;
            }
            return -1;
        }

        /// <summary>A fresh set of fades, one per row, all at rest: a new list of displays starts from nothing lit.</summary>
        void Rebuild()
        {
            int n = _displays.Count;
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

        /// <summary>
        /// Moves the marks of the selection to the selected row: the pill
        /// slides there and the rows' fills cross-fade, or, without
        /// <paramref name="animate"/>, both are simply there.
        /// </summary>
        void MarkSelected(bool animate)
        {
            for (int i = 0; i < _selectMotion.Length; i++)
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
            using (SolidBrush b = new SolidBrush(_onCard ? Theme.CardBack : Theme.WindowBack))
                g.FillRectangle(b, ClientRectangle);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            bool enabled = Enabled;
            Font name = Theme.Strong(14 * _s);
            Font detail = Theme.Font(12 * _s);
            float nameH = name.GetHeight(g);
            float detailH = detail.GetHeight(g);

            for (int i = 0; i < _displays.Count; i++)
            {
                Display d = _displays[i];
                Rectangle row = Row(i);

                // Selected and hovered are the same fill, not one on the other;
                // pressed is a shade off it.
                float lit = Math.Max(_selectMotion[i].Value, enabled ? _hoverMotion[i].Value : 0);
                Color fill = Motion.Blend(Color.FromArgb(0, Theme.Hover), Theme.Hover, lit);
                fill = Motion.Blend(fill, Theme.Pressed, enabled ? _pressMotion[i].Value : 0);
                if (fill.A > 0) Theme.FillRounded(g, fill, row, 4 * _s);

                // Centred in the row's whole height rather than a 20-high box:
                // DrawGlyph lays out whole lines only, and the icon font's line
                // is taller than 20 at 20px, so in a 20-high box nothing shows.
                Theme.DrawGlyph(g, Theme.Glyph(0xE7F4), 20 * _s, enabled ? Theme.Text : Theme.TextFaint,
                                new RectangleF(row.Left + R(16), row.Top, R(20), row.Height));

                string title = d.Number > 0 ? "Display " + d.Number : "Display";
                string size = d.Bounds.Width + " × " + d.Bounds.Height + (d.Primary ? "  ·  Primary" : "");

                float x = row.Left + R(52);
                float top = row.Top + (row.Height - nameH - detailH) / 2;
                using (StringFormat f = new StringFormat())
                {
                    f.FormatFlags = StringFormatFlags.NoWrap;
                    f.Trimming = StringTrimming.EllipsisCharacter;
                    using (SolidBrush b = new SolidBrush(enabled ? Theme.Text : Theme.TextFaint))
                        g.DrawString(title, name, b, new RectangleF(x, top, row.Right - x, nameH), f);
                    using (SolidBrush b = new SolidBrush(enabled ? Theme.TextDim : Theme.TextFaint))
                        g.DrawString(size, detail, b, new RectangleF(x, top + nameH, row.Right - x, detailH), f);
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
                Theme.FillRounded(g, enabled ? Theme.Accent : Theme.TextFaint,
                                  new RectangleF(0, rowTop + (R(56) - pillH) / 2, 3 * _s, pillH), 1.5f * _s);
            }

            if (Focused && ShowFocusCues && _displays.Count > 0)
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
            for (int i = 0; i < _hoverMotion.Length; i++)
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
            int hover = RowAt(e.Location);
            if (hover == _hover) return;
            _hover = hover;
            Animate();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled || e.Button != MouseButtons.Left) return;
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
            if (!Enabled || e.Button != MouseButtons.Left || down < 0) return;
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
            if (!Enabled || _displays.Count == 0) return;
            if (e.KeyCode == Keys.Up)
            {
                SelectedIndex = Math.Max(0, _selected - 1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Down)
            {
                SelectedIndex = Math.Min(_displays.Count - 1, _selected + 1);
                e.Handled = true;
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _hover = -1;
            _down = -1;
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
