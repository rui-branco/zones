using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Zones
{
    /// <summary>
    /// The displays across the top of the Layouts page, as tabs: one per
    /// display, the chosen one lit and underlined with an accent pill, which
    /// slides across to another when it is chosen, as the selector bars of
    /// Windows 11 do.
    ///
    /// Each tab gives the display's Windows number and resolution, the two
    /// things that tell otherwise identical monitors apart in Display settings
    /// as well, so the same screen goes by the same name in both places.
    /// Tabs are as wide as their words; short of room, they share what there
    /// is, and the resolution is cut short before the name is.
    /// </summary>
    public class DisplayPicker : Control
    {
        // In logical pixels: a tab's height, and the gap between two.
        const int TabH = 56;
        const int Gap = 8;

        float _s = 1f;
        List<Display> _displays = new List<Display>();
        int _selected = -1;
        int _hover = -1;
        int _down = -1;

        // A fade of each kind per tab, so moving between tabs, or choosing
        // another, cross-fades. Made again when the number of displays changes.
        Tween[] _hoverMotion;
        Tween[] _pressMotion;
        Tween[] _selectMotion;

        /// <summary>Where the pill is, in tabs: 1.5 is halfway from the second tab to the third.</summary>
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

        /// <summary>Once there is a window, the pill slides to the new tab; before, it is simply there.</summary>
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

        static string NameOf(Display d)
        {
            return d.Number > 0 ? "Display " + d.Number : "Display";
        }

        static string DetailOf(Display d)
        {
            return d.Bounds.Width + " × " + d.Bounds.Height + (d.Primary ? "  ·  Primary" : "");
        }

        /// <summary>The tabs' height, in device pixels.</summary>
        public int PreferredHeight()
        {
            return R(TabH);
        }

        /// <summary>Wide enough for every tab at its own width, in device pixels.</summary>
        public int PreferredWidth()
        {
            int[] widths = Widths();
            int total = 0;
            foreach (int w in widths) total += w;
            return total + Math.Max(0, widths.Length - 1) * R(Gap);
        }

        /// <summary>
        /// Each tab as wide as its words: the icon, then the name or the
        /// resolution under it, whichever is longer. Measured as they are
        /// drawn, by GDI+, which pads them a little more than GDI would.
        /// </summary>
        int[] Widths()
        {
            int[] widths = new int[_displays.Count];
            using (Bitmap b = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(b))
            {
                Font name = Theme.Strong(14 * _s);
                Font detail = Theme.Font(12 * _s);
                for (int i = 0; i < _displays.Count; i++)
                {
                    float words = Math.Max(g.MeasureString(NameOf(_displays[i]), name).Width,
                                           g.MeasureString(DetailOf(_displays[i]), detail).Width);
                    widths[i] = R(14 + 20 + 12) + (int)Math.Ceiling(words) + R(14);
                }
            }
            return widths;
        }

        /// <summary>
        /// Where each tab is. At their own widths if they fit; if not, each
        /// gets an equal share of the room there is.
        /// </summary>
        Rectangle[] Tabs()
        {
            int n = _displays.Count;
            Rectangle[] tabs = new Rectangle[n];
            if (n == 0) return tabs;

            int[] widths = Widths();
            bool fit = PreferredWidth() <= Width;
            int share = (Width - (n - 1) * R(Gap)) / n;
            int x = 0;
            for (int i = 0; i < n; i++)
            {
                int w = fit ? widths[i] : Math.Max(R(48), share);
                tabs[i] = new Rectangle(x, 0, w, R(TabH));
                x += w + R(Gap);
            }
            return tabs;
        }

        int TabAt(Point p)
        {
            Rectangle[] tabs = Tabs();
            for (int i = 0; i < tabs.Length; i++)
            {
                if (tabs[i].Contains(p)) return i;
            }
            return -1;
        }

        /// <summary>A fresh set of fades, one per tab, all at rest: a new list of displays starts from nothing lit.</summary>
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
        /// Moves the marks of the selection to the selected tab: the pill
        /// slides there and the tabs' fills cross-fade, or, without
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
            using (SolidBrush b = new SolidBrush(Theme.WindowBack))
                g.FillRectangle(b, ClientRectangle);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            bool enabled = Enabled;
            Font name = Theme.Strong(14 * _s);
            Font detail = Theme.Font(12 * _s);
            float nameH = name.GetHeight(g);
            float detailH = detail.GetHeight(g);
            Rectangle[] tabs = Tabs();

            for (int i = 0; i < tabs.Length; i++)
            {
                Display d = _displays[i];
                Rectangle tab = tabs[i];

                // Selected and hovered are the same fill, not one on the other;
                // pressed is a shade off it.
                float lit = Math.Max(_selectMotion[i].Value, enabled ? _hoverMotion[i].Value : 0);
                Color fill = Motion.Blend(Color.FromArgb(0, Theme.Hover), Theme.Hover, lit);
                fill = Motion.Blend(fill, Theme.Pressed, enabled ? _pressMotion[i].Value : 0);
                if (fill.A > 0) Theme.FillRounded(g, fill, tab, 4 * _s);

                // Squeezed narrow, the tab gives the icon's room to the words.
                float x = tab.Left + R(14);
                if (tab.Width >= R(120))
                {
                    // In the tab's whole height: the icon font's line is taller
                    // than the glyph, and a box only the glyph's height shows nothing.
                    Theme.DrawGlyph(g, Theme.Glyph(0xE7F4), 20 * _s, enabled ? Theme.Text : Theme.TextFaint,
                                    new RectangleF(x, tab.Top, R(20), tab.Height));
                    x += R(20 + 12);
                }

                float top = tab.Top + (tab.Height - nameH - detailH) / 2;
                float width = Math.Max(0, tab.Right - R(8) - x);
                using (StringFormat f = new StringFormat())
                {
                    f.FormatFlags = StringFormatFlags.NoWrap;
                    f.Trimming = StringTrimming.EllipsisCharacter;
                    using (SolidBrush b = new SolidBrush(enabled ? Theme.Text : Theme.TextFaint))
                        g.DrawString(NameOf(d), name, b, new RectangleF(x, top, width, nameH), f);
                    using (SolidBrush b = new SolidBrush(enabled ? Theme.TextDim : Theme.TextFaint))
                        g.DrawString(DetailOf(d), detail, b, new RectangleF(x, top + nameH, width, detailH), f);
                }
            }

            // The pill, after every tab, as on its way between two it belongs
            // to neither: centred under the tab, or wherever the slide has got
            // to between two tabs' centres.
            if (_selected >= 0 && tabs.Length > 0)
            {
                float at = _pill.Value;
                int from = Math.Max(0, Math.Min(tabs.Length - 1, (int)Math.Floor(at)));
                int to = Math.Min(tabs.Length - 1, from + 1);
                float centre = Motion.Lerp(tabs[from].Left + tabs[from].Width / 2f,
                                           tabs[to].Left + tabs[to].Width / 2f, at - from);
                float pillW = 16 * _s;
                float pillH = 3 * _s;
                Theme.FillRounded(g, enabled ? Theme.Accent : Theme.TextFaint,
                                  new RectangleF(centre - pillW / 2, R(TabH) - pillH, pillW, pillH), 1.5f * _s);
            }

            if (Focused && ShowFocusCues && tabs.Length > 0)
            {
                RectangleF ring = tabs[Math.Max(0, _selected)];
                ring.Inflate(-_s, -_s);
                using (GraphicsPath p = Theme.Rounded(ring, 4 * _s))
                using (Pen pen = new Pen(Theme.Text, 2 * _s))
                    g.DrawPath(pen, p);
            }
        }

        /// <summary>
        /// Points every tab's hover and press at where the mouse has left
        /// things. Pressed only shows on the tab pressed, while the cursor is
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
            _hover = TabAt(PointToClient(Cursor.Position));
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
            int hover = TabAt(e.Location);
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
            _down = TabAt(e.Location);
            Animate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int down = _down;
            _down = -1;
            Animate();
            if (!Enabled || e.Button != MouseButtons.Left || down < 0) return;
            if (TabAt(e.Location) == down) SelectedIndex = down;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled || _displays.Count == 0) return;
            if (e.KeyCode == Keys.Left)
            {
                SelectedIndex = Math.Max(0, _selected - 1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Right)
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
