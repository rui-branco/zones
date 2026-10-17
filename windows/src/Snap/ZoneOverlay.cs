using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Zones
{
    /// <summary>
    /// The zones drawn over a display while a window is dragged: every zone as
    /// a frosted plate with its number, the ones the window would land in
    /// filled with the accent, and when there is something to say about the
    /// layout itself - which of its sizes this is - a label at the top.
    ///
    /// A layered window that the mouse goes straight through and that never
    /// activates, so the drag underneath it carries on as if it were not there.
    /// It covers a whole display, so its buffer is only kept while it shows.
    ///
    /// It fades in as it appears and out as it goes. The picture is drawn
    /// once and only shown again at another opacity for each step of a fade:
    /// drawing a whole 4K display over every frame would make the fade lag.
    /// </summary>
    sealed class ZoneOverlay : Form
    {
        const int WS_EX_LAYERED = 0x80000;
        const int WS_EX_TRANSPARENT = 0x20;
        const int WS_EX_TOOLWINDOW = 0x80;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TOPMOST = 0x8;

        const int FadeIn = 120;
        const int FadeOut = 100;

        LayeredSurface _surface;
        bool _showing;      // asked to show: false as soon as it is asked to hide, fading out or not
        bool _onScreen;     // the window is up, at whatever opacity, until a fade out ends
        Point _at;          // where the picture goes: the work area's corner
        Timer _flashTimer;

        /// <summary>The window's opacity, 0 to 255.</summary>
        readonly Tween _fade;

        public ZoneOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;

            _fade = new Tween(delegate { Faded(); }, 0, FadeIn);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        /// <summary>
        /// Asked to show and not since asked to hide. False the moment
        /// <see cref="HideZones"/> is called, while it is still fading out:
        /// the drag engine draws it again whenever it is not showing.
        /// </summary>
        public bool Showing
        {
            get { return _showing; }
        }

        /// <summary>
        /// Draws <paramref name="zones"/> (screen pixels) over <paramref name="work"/>,
        /// those listed in <paramref name="selected"/> highlighted, and shows the
        /// overlay if it is not up already.
        /// </summary>
        public void ShowZones(Rectangle work, List<Rectangle> zones, ICollection<int> selected, float scale)
        {
            ShowZones(work, zones, selected, scale, null);
        }

        /// <summary>
        /// As above, with <paramref name="label"/>, if not null, in a pill at
        /// the top of the work area, over the zones.
        /// </summary>
        public void ShowZones(Rectangle work, List<Rectangle> zones, ICollection<int> selected, float scale, string label)
        {
            StopFlash();
            Render(work, zones, selected, scale, label);
            _at = work.Location;

            // Up already, or on its way up: the new picture, at whatever
            // opacity the fade has reached.
            if (_showing)
            {
                _surface.Present(Handle, _at, Alpha());
                return;
            }
            _showing = true;

            // Caught on its way out, as when a drag ends and the size it
            // dropped into flashes up: straight back to full, no dip.
            if (_onScreen)
            {
                _fade.Snap(255);
                return;
            }

            // Put in place at nothing before it shows, so the last frame of
            // the fade before never flickers up; then faded in.
            _surface.Present(Handle, _at, 0);
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            _onScreen = true;
            _fade.Duration = FadeIn;
            _fade.Target = 255;
        }

        /// <summary>Shows the zones, none selected, for <paramref name="ms"/> milliseconds.</summary>
        public void Flash(Rectangle work, List<Rectangle> zones, float scale, int ms)
        {
            Flash(work, zones, scale, ms, null);
        }

        /// <summary>As above, with <paramref name="label"/>, if not null, at the top.</summary>
        public void Flash(Rectangle work, List<Rectangle> zones, float scale, int ms, string label)
        {
            ShowZones(work, zones, null, scale, label);

            if (_flashTimer == null)
            {
                _flashTimer = new Timer();
                _flashTimer.Tick += delegate { HideZones(); };
            }
            _flashTimer.Interval = Math.Max(1, ms);
            _flashTimer.Start();
        }

        /// <summary>Fades the overlay out; at the end of the fade it is hidden and its buffer let go.</summary>
        public void HideZones()
        {
            StopFlash();
            if (!_showing) return;
            _showing = false;
            _fade.Duration = FadeOut;
            _fade.Target = 0;
        }

        void StopFlash()
        {
            if (_flashTimer != null) _flashTimer.Stop();
        }

        byte Alpha()
        {
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(_fade.Value)));
        }

        /// <summary>
        /// A step of the fade: the picture shown again at the new opacity. At
        /// the end of a fade out, the window is hidden and the buffer freed:
        /// a 4K display is a 33 MB buffer, not something to keep between drags.
        /// </summary>
        void Faded()
        {
            if (_surface == null || !IsHandleCreated) return;
            if (_showing || _fade.Running)
            {
                _surface.Present(Handle, _at, Alpha());
                return;
            }

            ShowWindow(Handle, SW_HIDE);
            _onScreen = false;
            _surface.Dispose();
            _surface = null;
        }

        void Render(Rectangle work, List<Rectangle> zones, ICollection<int> selected, float s, string label)
        {
            if (_surface == null) _surface = new LayeredSurface();

            using (Graphics g = _surface.Begin(work.Width, work.Height))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // Grayscale: ClearType's coloured fringes need an opaque
                // background to blend against, and this one is see-through.
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                for (int i = 0; i < zones.Count; i++)
                {
                    Rectangle z = zones[i];
                    z.Offset(-work.Left, -work.Top);
                    DrawZone(g, z, i + 1, selected != null && selected.Contains(i), s);
                }

                // Last, so no zone is ever drawn over it.
                if (!string.IsNullOrEmpty(label)) DrawLabel(g, work.Width, label, s);
            }
        }

        static void DrawZone(Graphics g, Rectangle zone, int number, bool hot, float s)
        {
            RectangleF r = zone;
            float inset = Math.Max(1f, 3 * s);
            r.Inflate(-inset, -inset);
            if (r.Width < 4 || r.Height < 4) return;

            Color fill = hot
                ? Color.FromArgb(150, Theme.Accent)
                : (Theme.Light ? Color.FromArgb(150, 243, 243, 243) : Color.FromArgb(150, 32, 32, 32));
            Color edge = hot
                ? Theme.Accent
                : (Theme.Light ? Color.FromArgb(90, 0, 0, 0) : Color.FromArgb(110, 255, 255, 255));

            using (GraphicsPath p = Theme.Rounded(r, 8 * s))
            {
                using (SolidBrush b = new SolidBrush(fill))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(edge, (hot ? 2f : 1.5f) * s))
                    g.DrawPath(pen, p);
            }

            // The number, as big as the zone comfortably takes, and the zone's
            // size under it when there is room for a second line.
            float px = Math.Min(40 * s, Math.Min(r.Width, r.Height) * 0.4f);
            if (px < 8) return;

            Color ink = hot ? Theme.OnAccent : Theme.Text;
            bool sizeLine = r.Height > 120 * s && r.Width > 120 * s;
            Font big = Theme.Strong(px);
            Font small = Theme.Font(13 * s);

            float lineH = big.GetHeight(g);
            float totalH = lineH + (sizeLine ? small.GetHeight(g) : 0);
            float top = r.Top + (r.Height - totalH) / 2;

            using (StringFormat f = new StringFormat())
            {
                f.Alignment = StringAlignment.Center;
                f.LineAlignment = StringAlignment.Near;
                f.FormatFlags = StringFormatFlags.NoWrap;

                using (SolidBrush b = new SolidBrush(ink))
                    g.DrawString(number.ToString(), big, b, new RectangleF(r.Left, top, r.Width, lineH), f);

                if (sizeLine)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(170, ink)))
                    {
                        string size = zone.Width + " × " + zone.Height;
                        g.DrawString(size, small, b, new RectangleF(r.Left, top + lineH, r.Width, small.GetHeight(g)), f);
                    }
                }
            }
        }

        /// <summary>
        /// A line of text in a pill, centred across the top of a work area
        /// <paramref name="width"/> pixels wide: the menus' own plate and
        /// edge, so it reads as Zones talking rather than as one more zone.
        /// </summary>
        static void DrawLabel(Graphics g, int width, string text, float s)
        {
            Font font = Theme.Strong(14 * s);

            // Without GDI+'s padding, so the pill's own is what shows.
            using (StringFormat f = new StringFormat(StringFormat.GenericTypographic))
            {
                f.Alignment = StringAlignment.Center;
                f.LineAlignment = StringAlignment.Center;
                f.FormatFlags |= StringFormatFlags.NoWrap;

                SizeF size = g.MeasureString(text, font, PointF.Empty, f);
                float w = (float)Math.Ceiling(size.Width + 2 * 14 * s);
                float h = (float)Math.Ceiling(size.Height + 2 * 7 * s);
                RectangleF pill = new RectangleF((float)Math.Round((width - w) / 2), (float)Math.Round(16 * s), w, h);

                using (GraphicsPath p = Theme.Rounded(pill, h / 2))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(235, Theme.MenuBack)))
                        g.FillPath(b, p);
                    using (Pen pen = new Pen(Theme.MenuBorder, 1f))
                        g.DrawPath(pen, p);
                }

                using (SolidBrush b = new SolidBrush(Theme.Text))
                    g.DrawString(text, font, b, pill, f);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_flashTimer != null) _flashTimer.Dispose();
                if (_surface != null) _surface.Dispose();
                _flashTimer = null;
                _surface = null;
                // Off the clock mid-fade; with no surface, the step does nothing.
                _fade.Snap(0);
            }
            base.Dispose(disposing);
        }

        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const int SW_HIDE = 0;
        const int SW_SHOWNOACTIVATE = 4;
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hwnd, int cmd);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                        int X, int Y, int cx, int cy, uint uFlags);
    }
}
