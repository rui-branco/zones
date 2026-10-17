using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Zones
{
    /// <summary>
    /// The Zones mark: a screen split into three zones, the middle one lit,
    /// as a window lands in it. One drawing, used for the settings window's
    /// title bar and, through tools/IconGen.cs, for the exe icon and the
    /// README logo, so the three can never drift apart.
    ///
    /// Self-contained on purpose (System.Drawing only) so the icon generator can
    /// compile this file on its own without dragging WinForms along.
    ///
    /// Everything is a fraction of the box, and the shapes are deliberately
    /// chunky: the mark spends most of its life 16 pixels tall in the
    /// notification area and the title bar, where anything thinner than a
    /// pixel or so turns to mush.
    /// </summary>
    public static class Logo
    {
        static readonly Color ScreenTop = Color.FromArgb(0x8F, 0x80, 0xFF);
        static readonly Color ScreenBottom = Color.FromArgb(0x51, 0x40, 0xE8);
        static readonly Color LitTop = Color.FromArgb(0x6B, 0xE4, 0xFF);
        static readonly Color LitBottom = Color.FromArgb(0x21, 0x86, 0xFF);
        static readonly Color Zone = Color.FromArgb(150, 255, 255, 255);

        public static void Draw(Graphics g, RectangleF box)
        {
            float s = Math.Min(box.Width, box.Height);
            float x = box.X + (box.Width - s) / 2f;
            float y = box.Y + (box.Height - s) / 2f;

            SmoothingMode oldSmooth = g.SmoothingMode;
            PixelOffsetMode oldOffset = g.PixelOffsetMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            try
            {
                // 1. The screen: wider than tall, like the displays the zones
                // are laid out on.
                RectangleF screen = RectangleF.FromLTRB(x + 0.04f * s, y + 0.16f * s, x + 0.96f * s, y + 0.84f * s);
                using (GraphicsPath p = Rounded(screen, 0.12f * s))
                using (LinearGradientBrush b = Vertical(screen, ScreenTop, ScreenBottom))
                    g.FillPath(b, p);

                // 2. Three zones side by side, the middle one wider, as the
                // Focus layout has them. The screen shows through the gaps
                // between them, the way the desktop shows between snapped
                // windows.
                RectangleF inside = RectangleF.Inflate(screen, -0.08f * s, -0.08f * s);
                float gap = 0.05f * s;
                float unit = (inside.Width - 2 * gap) / 3.7f;
                RectangleF left = new RectangleF(inside.X, inside.Y, unit, inside.Height);
                RectangleF middle = new RectangleF(left.Right + gap, inside.Y, 1.7f * unit, inside.Height);
                RectangleF right = RectangleF.FromLTRB(middle.Right + gap, inside.Y, inside.Right, inside.Bottom);

                using (SolidBrush b = new SolidBrush(Zone))
                {
                    using (GraphicsPath p = Rounded(left, 0.05f * s))
                        g.FillPath(b, p);
                    using (GraphicsPath p = Rounded(right, 0.05f * s))
                        g.FillPath(b, p);
                }

                // 3. The middle zone lit, in the colours a window takes on as
                // it is dropped into one.
                using (GraphicsPath p = Rounded(middle, 0.05f * s))
                using (LinearGradientBrush b = Vertical(middle, LitTop, LitBottom))
                    g.FillPath(b, p);

                // Its top edge catches the light. Below 20px a line that thin
                // only muddies the edge.
                if (s >= 20)
                {
                    using (Pen hi = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
                        g.DrawLine(hi, middle.X + 0.05f * s, middle.Y + 0.5f,
                                       middle.Right - 0.05f * s, middle.Y + 0.5f);
                }
            }
            finally
            {
                g.SmoothingMode = oldSmooth;
                g.PixelOffsetMode = oldOffset;
            }
        }

        /// <summary>
        /// A vertical gradient that covers its rectangle exactly. Padded by a
        /// pixel each way: GDI+ tiles a gradient past its end points, so an
        /// anti-aliased edge that pokes out of the box picks up the opposite
        /// colour as a hard band along it.
        /// </summary>
        static LinearGradientBrush Vertical(RectangleF r, Color top, Color bottom)
        {
            LinearGradientBrush b = new LinearGradientBrush(
                new PointF(r.X, r.Y - 1f), new PointF(r.X, r.Bottom + 1f), top, bottom);
            b.WrapMode = WrapMode.TileFlipXY;
            return b;
        }

        static GraphicsPath Rounded(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            Corner(p, r.X, r.Y, radius, 180);
            Corner(p, r.Right - radius * 2, r.Y, radius, 270);
            Corner(p, r.Right - radius * 2, r.Bottom - radius * 2, radius, 0);
            Corner(p, r.X, r.Bottom - radius * 2, radius, 90);
            p.CloseFigure();
            return p;
        }

        static void Corner(GraphicsPath p, float x, float y, float radius, float start)
        {
            // With no radius the arc's box collapses onto the corner itself, so
            // (x, y) already is the corner point.
            if (radius > 0) p.AddArc(x, y, radius * 2, radius * 2, start, 90);
            else p.AddLine(x, y, x, y);
        }
    }
}
