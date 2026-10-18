using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Zones
{
    /// <summary>
    /// The canvas where a display's zones are drawn by hand: a scale model of
    /// the display, its zones laid out on it as they will be on screen, gaps
    /// and all.
    ///
    /// Everything is direct manipulation, because a layout is easier to see
    /// than to describe: a click cuts the zone under the cursor along the guide
    /// that follows it, a drag moves a line between zones, a right-click offers
    /// the rest, which includes leaving part of the display empty: an empty
    /// part keeps its place and the lines beside it, but takes no number and
    /// no windows. Both the guide and a dragged line are pulled onto halves,
    /// thirds and quarters, which are what people almost always want and are
    /// hard to hit by hand; Alt lets a dragged line go anywhere.
    ///
    /// Each zone shows its real size in pixels on the display, which is the
    /// feedback while a line is dragged: the numbers change as it moves.
    /// The layout is edited in place; <see cref="LayoutChanged"/> fires once
    /// per finished edit, not on every mouse move, so whoever saves it saves
    /// it once.
    ///
    /// A new layout, a split or a removal morphs the zones from where they
    /// were to where they now are, so the eye can follow what changed. A
    /// dragged line does not: it already follows the mouse.
    /// </summary>
    public class ZoneEditor : Control
    {
        /// <summary>Where a dragged line catches, as shares of the whole display.</summary>
        static readonly double[] LineStops = { 1 / 4.0, 1 / 3.0, 1 / 2.0, 2 / 3.0, 3 / 4.0 };

        /// <summary>Where the split guide catches, as shares of its zone. Earlier wins.</summary>
        static readonly double[] GuideStops = { 1 / 2.0, 1 / 3.0, 2 / 3.0, 1 / 4.0, 3 / 4.0 };

        ZoneLayout _layout;
        Size _displaySize;      // the display's work area, in its pixels
        int _spacingPx;         // the real gap on the display, already scaled
        float _s = 1f;
        bool _onCard;

        int _hoverZone = -1;    // a piece: a zone or an empty one
        ZoneDivider _hoverDivider;
        ZoneDivider _drag;
        Point _mouse;
        bool _shift;            // the guide runs across, to cut top from bottom

        bool _down;             // the left button went down on a piece, not a line
        Point _downAt;
        int _downZone = -1;

        // All of the motion here is drawing only. Hit-testing always meets the
        // layout as it is now, wherever its pieces are drawn on their way there.
        Tween[] _glow = new Tween[0];   // each piece's highlight under the mouse, so moving between them cross-fades
        readonly Tween _lineMotion;     // the line under the cursor fading in
        readonly Tween _morph;          // 0 to 1, from the pieces as they were drawn to where they are now
        List<RectangleF> _from = new List<RectangleF>();    // the pieces as drawn when the morph began: canvas pixels, gapped
        List<Rectangle> _fromReal = new List<Rectangle>();  // and their real sizes, for the ones on their way out
        List<int> _fromNumbers = new List<int>();           // and their labels then, 0 for an empty one
        int[] _source = new int[0];     // for each piece now, the old one it grows out of, or -1 for one that fades in
        bool[] _kept = new bool[0];     // for each old piece, whether any piece now grows out of it; the rest fade out

        readonly ContextMenuStrip _menu = new ContextMenuStrip();
        readonly ToolStripMenuItem _columnsItem;
        readonly ToolStripMenuItem _rowsItem;
        readonly ToolStripMenuItem _zoneItem;
        readonly ToolStripMenuItem _blankItem;
        readonly ToolStripMenuItem _removeItem;
        readonly ToolStripMenuItem _removeBlankItem;
        int _menuZone = -1;     // the piece the menu was opened on

        /// <summary>A split, a removal, a part left empty or filled again, or a finished drag changed the layout.</summary>
        public event EventHandler LayoutChanged;

        public ZoneEditor()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            // Focus only so Shift reaches it while the cursor is over it; there
            // is nothing here to do from the keyboard.
            TabStop = false;

            _lineMotion = new Tween(this, 0, Motion.Fast);
            _morph = new Tween(this, 1, Motion.Slow);

            Theme.StyleMenu(_menu);

            _columnsItem = Theme.MenuItem("Split left and right", 0);
            _columnsItem.Click += delegate { Edited(delegate { return _layout.Split(_menuZone, true, 0.5); }); };

            _rowsItem = Theme.MenuItem("Split top and bottom", 0);
            _rowsItem.Click += delegate { Edited(delegate { return _layout.Split(_menuZone, false, 0.5); }); };

            _zoneItem = Theme.MenuItem("Make it a zone", 0);
            _zoneItem.Click += delegate { Edited(delegate { return _layout.SetBlank(_menuZone, false); }); };

            _blankItem = Theme.MenuItem("Leave empty", 0);
            _blankItem.Click += delegate { Edited(delegate { return _layout.SetBlank(_menuZone, true); }); };

            _removeItem = Theme.MenuItem("Remove zone", 0xE74D);
            _removeItem.Click += delegate { Edited(delegate { return _layout.Remove(_menuZone); }); };

            _removeBlankItem = Theme.MenuItem("Remove empty area", 0xE74D);
            _removeBlankItem.Click += delegate { Edited(delegate { return _layout.Remove(_menuZone); }); };

            // One menu for both kinds of piece; OpenMenu hides what does not
            // apply. A zone's items and an empty piece's fall either side of
            // the one separator, so it never doubles up or dangles.
            _menu.Items.Add(_columnsItem);
            _menu.Items.Add(_rowsItem);
            _menu.Items.Add(_zoneItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_blankItem);
            _menu.Items.Add(_removeItem);
            _menu.Items.Add(_removeBlankItem);
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

        /// <summary>The layout being edited: the very one handed to <see cref="SetLayout"/>.</summary>
        public new ZoneLayout Layout
        {
            get { return _layout; }
        }

        /// <summary>
        /// Edits <paramref name="layout"/> in place, modelled on a display whose
        /// work area is <paramref name="displaySize"/>, with zones
        /// <paramref name="spacingPx"/> real pixels apart. Zones that end up
        /// somewhere else morph there from where they were drawn.
        /// </summary>
        public void SetLayout(ZoneLayout layout, Size displaySize, int spacingPx)
        {
            List<Rectangle> fromReal;
            List<ZoneNode> fromNodes;
            List<int> fromNumbers;
            List<RectangleF> from = Drawn(out fromReal, out fromNodes, out fromNumbers);
            List<Rectangle> before = Placed();

            _layout = layout;
            _displaySize = displaySize;
            _spacingPx = spacingPx;
            _hoverZone = -1;
            _hoverDivider = null;
            _drag = null;
            _down = false;
            _downZone = -1;

            // The same zones handed over again, as on every activation of the
            // window, change nothing: a morph on its way carries on.
            if (!Same(before, Placed())) Morph(from, fromReal, fromNodes, fromNumbers);
            Highlight();
            Invalidate();
        }

        bool Ready
        {
            get { return _layout != null && _displaySize.Width > 0 && _displaySize.Height > 0; }
        }

        int R(float px)
        {
            return (int)Math.Round(px * _s);
        }

        /// <summary>The model display: as large as fits, in the display's own proportions.</summary>
        Rectangle Canvas()
        {
            Rectangle area = ClientRectangle;
            area.Inflate(-R(2), -R(2));
            if (!Ready || area.Width <= 0 || area.Height <= 0) return area;

            double k = Math.Min((double)area.Width / _displaySize.Width, (double)area.Height / _displaySize.Height);
            int w = (int)Math.Round(_displaySize.Width * k);
            int h = (int)Math.Round(_displaySize.Height * k);
            return new Rectangle(area.Left + (area.Width - w) / 2, area.Top + (area.Height - h) / 2, w, h);
        }

        /// <summary>The real gap at the model's scale, but never so thin the zones run together.</summary>
        int Gap(Rectangle canvas)
        {
            double k = canvas.Width / (double)_displaySize.Width;
            return Math.Max(R(4), (int)Math.Round(_spacingPx * k));
        }

        /// <summary>The line whose grab area holds <paramref name="p"/>; where two meet, the nearer.</summary>
        ZoneDivider DividerAt(Point p, Rectangle canvas)
        {
            ZoneDivider best = null;
            int bestDistance = int.MaxValue;
            int reach = R(5);
            foreach (ZoneDivider d in _layout.Dividers(canvas))
            {
                Rectangle hit = d.Line;
                if (d.Vertical) hit.Inflate(reach, 0);
                else hit.Inflate(0, reach);
                if (!hit.Contains(p)) continue;

                int distance = Math.Abs((d.Vertical ? p.X : p.Y) - d.Position);
                if (distance < bestDistance)
                {
                    best = d;
                    bestDistance = distance;
                }
            }
            return best;
        }

        static int PieceAt(Point p, List<Rectangle> pieces)
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i].Contains(p)) return i;
            }
            return -1;
        }

        /// <summary>
        /// Each piece's label: its zone number, counting zones only so it is
        /// the number the display itself shows, or 0 for an empty piece.
        /// </summary>
        List<int> Numbers()
        {
            List<int> numbers = new List<int>();
            int zone = 0;
            foreach (ZoneNode n in _layout.Pieces()) numbers.Add(n.Blank ? 0 : ++zone);
            return numbers;
        }

        /// <summary>
        /// Where a click at <paramref name="p"/> would cut <paramref name="zone"/>
        /// (its un-gapped rectangle): an x for a vertical cut, a y for a
        /// horizontal one, caught by the zone's halves, thirds and quarters.
        /// </summary>
        int GuideAt(Rectangle zone, Point p, bool across)
        {
            int start = across ? zone.Top : zone.Left;
            int extent = across ? zone.Height : zone.Width;
            int pos = across ? p.Y : p.X;
            foreach (double f in GuideStops)
            {
                int c = start + (int)Math.Round(extent * f);
                if (Math.Abs(pos - c) <= R(6)) return c;
            }
            return pos;
        }

        /// <summary>Works out what is under the cursor, and the cursor that goes with it.</summary>
        void Hover(Point p)
        {
            _mouse = p;
            if (!Ready) return;

            Rectangle canvas = Canvas();
            ZoneDivider was = _hoverDivider;
            _hoverDivider = DividerAt(p, canvas);
            _hoverZone = _hoverDivider == null ? PieceAt(p, _layout.PiecesIn(canvas, 0)) : -1;
            Cursor = _hoverDivider == null
                ? Cursors.Default
                : (_hoverDivider.Vertical ? Cursors.SizeWE : Cursors.SizeNS);
            if (_hoverDivider != null && !SameLine(was, _hoverDivider)) LineFadesIn();
            Highlight();
        }

        /// <summary>
        /// A split, an emptying or a removal from the menu: <paramref name="edit"/>
        /// makes it and says whether anything changed. If it did, the pieces
        /// morph to their new places and the owner is told.
        /// </summary>
        void Edited(Func<bool> edit)
        {
            List<Rectangle> fromReal;
            List<ZoneNode> fromNodes;
            List<int> fromNumbers;
            List<RectangleF> from = Drawn(out fromReal, out fromNodes, out fromNumbers);
            if (!edit()) return;

            Morph(from, fromReal, fromNodes, fromNumbers);
            _hoverZone = -1;
            _hoverDivider = null;
            Highlight();
            Invalidate();
            if (LayoutChanged != null) LayoutChanged(this, EventArgs.Empty);
        }

        // ---------- motion ----------

        /// <summary>The pieces where they belong now, in canvas pixels, gapped; null with nothing to draw them on.</summary>
        List<Rectangle> Placed()
        {
            if (!Ready) return null;
            Rectangle canvas = Canvas();
            if (canvas.Width <= 0 || canvas.Height <= 0) return null;
            return _layout.PiecesIn(canvas, Gap(canvas));
        }

        /// <summary>
        /// The pieces as drawn this moment, in canvas pixels: mid-morph, wherever
        /// they have got to. Their real sizes come back in <paramref name="real"/>,
        /// the layout's own leaf nodes in <paramref name="nodes"/>, which an
        /// edit in place keeps for every piece it leaves be, and their labels in
        /// <paramref name="numbers"/>: taken now, because emptying a piece
        /// changes its node in place. Null before there is a window, when
        /// nothing is on screen to move from, as while the settings window is
        /// being put together.
        /// </summary>
        List<RectangleF> Drawn(out List<Rectangle> real, out List<ZoneNode> nodes, out List<int> numbers)
        {
            real = null;
            nodes = null;
            numbers = null;
            List<Rectangle> zones = Placed();
            if (zones == null || !IsHandleCreated) return null;

            real = _layout.PiecesIn(new Rectangle(Point.Empty, _displaySize), _spacingPx);
            nodes = _layout.Pieces();
            numbers = Numbers();
            float m = _morph.Value;
            List<RectangleF> drawn = new List<RectangleF>(zones.Count);
            for (int i = 0; i < zones.Count; i++) drawn.Add(Morphed(i, zones[i], m));
            return drawn;
        }

        /// <summary>
        /// Starts the zones on their way from <paramref name="from"/> to where
        /// they are now. Each zone grows out of the old zone it overlaps most,
        /// not the one that had its number: numbers shift when a zone is split
        /// or removed, places do not. So both halves of a split zone come out
        /// of it, and the neighbour that takes a removed zone's room spreads
        /// over it. A zone that overlaps none grows in where it is; an old one
        /// that nothing grows out of fades out where it was. With nothing drawn
        /// to start from, they are simply there.
        ///
        /// A zone that is still the same node of the layout, as after a split
        /// or a removal in place, comes from its own old place whatever the
        /// overlaps say. A neighbour that takes over a removed zone of the same
        /// size overlaps it as much as itself, give or take the gaps, and the
        /// gaps would pick the wrong one.
        /// </summary>
        void Morph(List<RectangleF> from, List<Rectangle> fromReal, List<ZoneNode> fromNodes, List<int> fromNumbers)
        {
            List<Rectangle> to = Placed();
            if (from == null || to == null)
            {
                _morph.Snap(1);
                return;
            }

            List<ZoneNode> nodes = _layout.Pieces();
            int[] source = new int[to.Count];
            bool[] kept = new bool[from.Count];
            for (int i = 0; i < to.Count; i++)
            {
                source[i] = fromNodes.IndexOf(nodes[i]);
                if (source[i] >= 0)
                {
                    kept[source[i]] = true;
                    continue;
                }

                float best = 0;
                for (int j = 0; j < from.Count; j++)
                {
                    RectangleF overlap = RectangleF.Intersect(from[j], to[i]);
                    float area = overlap.Width * overlap.Height;
                    if (area > best)
                    {
                        best = area;
                        source[i] = j;
                    }
                }
                if (source[i] >= 0) kept[source[i]] = true;
            }

            _from = from;
            _fromReal = fromReal;
            _fromNumbers = fromNumbers;
            _source = source;
            _kept = kept;
            _morph.Snap(0);
            _morph.Target = 1;
        }

        /// <summary>
        /// Zone <paramref name="i"/>, whose place is now <paramref name="to"/>,
        /// as far along the morph as <paramref name="m"/>: on its way from the
        /// old zone it grows out of, or, overlapping none, grown from 92% of
        /// its size about its centre.
        /// </summary>
        RectangleF Morphed(int i, Rectangle to, float m)
        {
            if (m >= 1) return to;
            int j = i < _source.Length ? _source[i] : -1;
            if (j >= 0) return Motion.Lerp(_from[j], to, m);

            float k = Motion.Lerp(0.92f, 1, m);
            float w = to.Width * k;
            float h = to.Height * k;
            return new RectangleF(to.X + (to.Width - w) / 2, to.Y + (to.Height - h) / 2, w, h);
        }

        static bool Same(List<Rectangle> a, List<Rectangle> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        /// <summary>The same line of the same layout: lines are worked out afresh on every move, so not the same object.</summary>
        static bool SameLine(ZoneDivider a, ZoneDivider b)
        {
            return a != null && b != null && a.Run == b.Run && a.Index == b.Index;
        }

        /// <summary>A line newly under the cursor: its accent fades in.</summary>
        void LineFadesIn()
        {
            _lineMotion.Snap(0);
            _lineMotion.Target = 1;
        }

        /// <summary>
        /// Points every piece's highlight at the one under the cursor now, none
        /// while a line is hovered or dragged. Made again when the number of
        /// pieces changes.
        /// </summary>
        void Highlight()
        {
            int n = _layout != null ? _layout.PieceCount : 0;
            if (_glow.Length != n)
            {
                _glow = new Tween[n];
                for (int i = 0; i < n; i++) _glow[i] = new Tween(this, 0, Motion.Fast);
            }
            int hot = _drag == null && _hoverDivider == null ? _hoverZone : -1;
            for (int i = 0; i < n; i++) _glow[i].Target = i == hot ? 1 : 0;
        }

        // ---------- painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(_onCard ? Theme.CardBack : Theme.WindowBack))
                g.FillRectangle(b, ClientRectangle);
            if (!Ready) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Rectangle canvas = Canvas();
            if (canvas.Width <= 0 || canvas.Height <= 0) return;
            int gap = Gap(canvas);

            // The screen: a shade off the card it sits on, sunk into it.
            Theme.FillRounded(g, Theme.Light ? Color.FromArgb(0xEC, 0xEC, 0xEC) : Color.FromArgb(0x1F, 0x1F, 0x1F), canvas, 8 * _s);

            List<Rectangle> pieces = _layout.PiecesIn(canvas, gap);
            List<Rectangle> real = _layout.PiecesIn(new Rectangle(Point.Empty, _displaySize), _spacingPx);
            List<int> numbers = Numbers();
            ZoneDivider line = _drag ?? _hoverDivider;
            int hot = line == null && _hoverZone < pieces.Count ? _hoverZone : -1;

            // Mid-morph, the old pieces nothing grows out of fade where they
            // were, under the rest, labelled as they were then; the ones that
            // overlap no old piece fade in as they grow.
            float m = _morph.Value;
            if (m < 1)
            {
                for (int j = 0; j < _from.Count; j++)
                {
                    if (!_kept[j]) DrawZone(g, _from[j], _fromNumbers[j], _fromReal[j], 0, 1 - m);
                }
            }
            for (int i = 0; i < pieces.Count; i++)
            {
                float glow = i < _glow.Length ? _glow[i].Value : 0;
                float alpha = m < 1 && (i >= _source.Length || _source[i] < 0) ? m : 1;
                DrawZone(g, Morphed(i, pieces[i], m), numbers[i], real[i], glow, alpha);
            }

            // The split guide only over a zone: an empty piece is not cut.
            if (line != null)
            {
                DrawDivider(g, line, canvas, gap);
            }
            else if (hot >= 0 && numbers[hot] > 0)
            {
                List<Rectangle> raw = _layout.PiecesIn(canvas, 0);
                DrawGuide(g, raw[hot], pieces[hot]);
            }
        }

        /// <summary>
        /// One piece at <paramref name="zone"/>, lit by the mouse as far as
        /// <paramref name="glow"/> has got and as opaque as <paramref name="alpha"/>:
        /// a piece fading in or out of a morph is see-through, label and all.
        /// A <paramref name="number"/> of 0 is an empty piece, drawn as next to
        /// nothing: a broken outline and a faint word, no fill.
        /// </summary>
        void DrawZone(Graphics g, RectangleF zone, int number, Rectangle real, float glow, float alpha)
        {
            float stroke = Motion.Lerp(1f, 1.5f * _s, glow);
            RectangleF r = zone;
            r.Inflate(-stroke / 2, -stroke / 2);
            using (GraphicsPath p = Theme.Rounded(r, 6 * _s))
            {
                if (number > 0)
                {
                    // Raised off the screen: lighter than it in either theme.
                    using (SolidBrush b = new SolidBrush(Faded(Theme.Light ? Color.White : Color.FromArgb(0x36, 0x36, 0x36), alpha)))
                        g.FillPath(b, p);
                    if (glow > 0)
                    {
                        using (SolidBrush b = new SolidBrush(Faded(Color.FromArgb(Theme.Light ? 28 : 40, Theme.Accent), glow * alpha)))
                            g.FillPath(b, p);
                    }
                }
                Color edge = Theme.Light ? Color.FromArgb(0xDD, 0xDD, 0xDD) : Color.FromArgb(0x42, 0x42, 0x42);
                using (Pen pen = new Pen(Faded(Motion.Blend(edge, Theme.Accent, glow), alpha), stroke))
                {
                    // Opened up from the stock dash, which at a hairline
                    // reads as nearly solid.
                    if (number == 0) pen.DashPattern = new float[] { 4, 3 };
                    g.DrawPath(pen, p);
                }
            }

            if (number == 0)
            {
                DrawEmptyLabel(g, zone, real, alpha);
                return;
            }

            // The number, as big as the zone comfortably takes, and the real
            // size under it when there is room for a second line. Too small
            // for a legible number, a sliver of a zone shows neither.
            float px = Math.Min(26 * _s, zone.Height * 0.35f);
            if (px < 8) return;

            Font big = Theme.Strong(px);
            Font small = Theme.Font(12 * _s);
            string size = real.Width + " × " + real.Height;
            bool sizeLine = zone.Height >= R(60) && g.MeasureString(size, small).Width <= zone.Width - R(8);

            float bigH = big.GetHeight(g);
            float smallH = small.GetHeight(g);
            float top = zone.Top + (zone.Height - bigH - (sizeLine ? smallH : 0)) / 2;

            using (StringFormat f = new StringFormat())
            {
                f.Alignment = StringAlignment.Center;
                f.LineAlignment = StringAlignment.Near;
                f.FormatFlags = StringFormatFlags.NoWrap;

                using (SolidBrush b = new SolidBrush(Faded(Theme.TextDim, alpha)))
                    g.DrawString(number.ToString(), big, b, new RectangleF(zone.Left, top, zone.Width, bigH), f);
                if (sizeLine)
                {
                    using (SolidBrush b = new SolidBrush(Faded(Theme.TextFaint, alpha)))
                        g.DrawString(size, small, b, new RectangleF(zone.Left, top + bigH, zone.Width, smallH), f);
                }
            }
        }

        /// <summary>
        /// An empty piece's label: a faint "Empty" where it fits, and the real
        /// size under it by the same rule as a zone's. No number: the zones
        /// around it are numbered as if it were not there, as on the display.
        /// </summary>
        void DrawEmptyLabel(Graphics g, RectangleF piece, Rectangle real, float alpha)
        {
            const string word = "Empty";
            Font small = Theme.Font(12 * _s);
            float smallH = small.GetHeight(g);
            if (piece.Height < smallH + R(8) || g.MeasureString(word, small).Width > piece.Width - R(8)) return;

            string size = real.Width + " × " + real.Height;
            bool sizeLine = piece.Height >= R(60) && g.MeasureString(size, small).Width <= piece.Width - R(8);
            float top = piece.Top + (piece.Height - smallH - (sizeLine ? smallH : 0)) / 2;

            using (StringFormat f = new StringFormat())
            using (SolidBrush b = new SolidBrush(Faded(Theme.TextFaint, alpha)))
            {
                f.Alignment = StringAlignment.Center;
                f.LineAlignment = StringAlignment.Near;
                f.FormatFlags = StringFormatFlags.NoWrap;

                g.DrawString(word, small, b, new RectangleF(piece.Left, top, piece.Width, smallH), f);
                if (sizeLine) g.DrawString(size, small, b, new RectangleF(piece.Left, top + smallH, piece.Width, smallH), f);
            }
        }

        /// <summary><paramref name="c"/> with its own opacity scaled by <paramref name="alpha"/>.</summary>
        static Color Faded(Color c, float alpha)
        {
            if (alpha >= 1) return c;
            return Color.FromArgb((int)Math.Round(c.A * Math.Max(0f, alpha)), c);
        }

        /// <summary>
        /// The line under the cursor, or being dragged, in the middle of the gap
        /// it sits in, from where the zones beside it start to where they end.
        /// </summary>
        void DrawDivider(Graphics g, ZoneDivider d, Rectangle canvas, int gap)
        {
            // PiecesIn gives an inner edge half the gap each side, the odd pixel
            // to the left or top, and an edge on the canvas's own edge all of it.
            int inner1 = gap - gap / 2;
            int inner2 = gap / 2;
            float at = d.Position - inner1 + gap / 2f;

            Rectangle r = d.Region;
            float from, to;
            if (d.Vertical)
            {
                from = r.Top + (r.Top <= canvas.Top ? gap : inner2);
                to = r.Bottom - (r.Bottom >= canvas.Bottom ? gap : inner1);
            }
            else
            {
                from = r.Left + (r.Left <= canvas.Left ? gap : inner2);
                to = r.Right - (r.Right >= canvas.Right ? gap : inner1);
            }

            using (Pen pen = new Pen(Faded(Theme.Accent, _lineMotion.Value), 3 * _s))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (d.Vertical) g.DrawLine(pen, at, from, at, to);
                else g.DrawLine(pen, from, at, to, at);
            }
        }

        /// <summary>Where a click would cut the hovered zone: down it, or across it with Shift.</summary>
        void DrawGuide(Graphics g, Rectangle raw, Rectangle zone)
        {
            int at = GuideAt(raw, _mouse, _shift);
            using (Pen pen = new Pen(Theme.Accent, 1.5f * _s))
            {
                pen.DashStyle = DashStyle.Dash;
                if (_shift) g.DrawLine(pen, zone.Left + R(6), at, zone.Right - R(6), at);
                else g.DrawLine(pen, at, zone.Top + R(6), at, zone.Bottom - R(6));
            }
        }

        // ---------- mouse ----------

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            // So Shift, pressed or let go, turns the guide without a mouse move.
            if (Form.ActiveForm == FindForm()) Focus();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverZone = -1;
            _hoverDivider = null;
            Highlight();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _shift = (Control.ModifierKeys & Keys.Shift) != 0;
            if (!Ready) return;

            if (_drag == null)
            {
                Hover(e.Location);
                Invalidate();
                return;
            }

            _mouse = e.Location;
            Rectangle canvas = Canvas();
            int start = _drag.Vertical ? canvas.Left : canvas.Top;
            int extent = _drag.Vertical ? canvas.Width : canvas.Height;
            int pos = _drag.Vertical ? e.X : e.Y;

            if ((Control.ModifierKeys & Keys.Alt) == 0)
            {
                foreach (double f in LineStops)
                {
                    int c = start + (int)Math.Round(extent * f);
                    if (Math.Abs(pos - c) <= R(6))
                    {
                        pos = c;
                        break;
                    }
                }
            }

            _layout.MoveDivider(_drag, pos, (int)Math.Round(extent * 0.05));
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Ready || e.Button != MouseButtons.Left) return;

            Rectangle canvas = Canvas();
            ZoneDivider d = DividerAt(e.Location, canvas);
            if (d != null)
            {
                // A dragged line follows the mouse itself: no morph under it.
                _morph.Snap(1);
                if (!SameLine(_hoverDivider, d)) LineFadesIn();
                _drag = d;
                _hoverDivider = d;
                _hoverZone = -1;
                Highlight();
                Invalidate();
                return;
            }

            _down = true;
            _downAt = e.Location;
            _downZone = PieceAt(e.Location, _layout.PiecesIn(canvas, 0));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!Ready) return;

            if (e.Button == MouseButtons.Right)
            {
                OpenMenu(e.Location);
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            if (_drag != null)
            {
                _drag = null;
                Hover(e.Location);
                Invalidate();
                if (LayoutChanged != null) LayoutChanged(this, EventArgs.Empty);
                return;
            }

            if (!_down) return;
            _down = false;

            // A click, not a drag that wandered: barely moved, and let go in
            // the zone it was pressed in.
            Size slop = SystemInformation.DragSize;
            if (Math.Abs(e.X - _downAt.X) >= slop.Width || Math.Abs(e.Y - _downAt.Y) >= slop.Height) return;

            // An empty piece is not cut: it is made a zone first, from the menu.
            Rectangle canvas = Canvas();
            List<Rectangle> raw = _layout.PiecesIn(canvas, 0);
            int zone = PieceAt(e.Location, raw);
            if (zone < 0 || zone != _downZone || _layout.IsBlank(zone)) return;

            bool across = (Control.ModifierKeys & Keys.Shift) != 0;
            Rectangle z = raw[zone];
            int start = across ? z.Top : z.Left;
            int extent = across ? z.Height : z.Width;
            int cut = GuideAt(z, e.Location, across);

            // Both halves have to be worth having: no slivers.
            double min = (across ? canvas.Height : canvas.Width) * 0.05;
            if (extent <= 0 || cut - start < min || start + extent - cut < min) return;

            List<Rectangle> fromReal;
            List<ZoneNode> fromNodes;
            List<int> fromNumbers;
            List<RectangleF> from = Drawn(out fromReal, out fromNodes, out fromNumbers);
            if (_layout.Split(zone, !across, (double)(cut - start) / extent))
            {
                Morph(from, fromReal, fromNodes, fromNumbers);
                Hover(e.Location);
                Invalidate();
                if (LayoutChanged != null) LayoutChanged(this, EventArgs.Empty);
            }
        }

        void OpenMenu(Point p)
        {
            Rectangle canvas = Canvas();
            List<Rectangle> raw = _layout.PiecesIn(canvas, 0);
            int piece = PieceAt(p, raw);
            if (piece < 0) return;

            // A zone is split, emptied or removed; an empty piece made a zone
            // again or removed. What does not apply is not shown at all.
            bool blank = _layout.IsBlank(piece);
            _menuZone = piece;
            _columnsItem.Visible = !blank;
            _rowsItem.Visible = !blank;
            _blankItem.Visible = !blank;
            _removeItem.Visible = !blank;
            _zoneItem.Visible = blank;
            _removeBlankItem.Visible = blank;

            // The same floor as a click: halves under 5% of the display are
            // slivers. The last zone can be neither emptied nor removed.
            _columnsItem.Enabled = raw[piece].Width / 2.0 >= canvas.Width * 0.05;
            _rowsItem.Enabled = raw[piece].Height / 2.0 >= canvas.Height * 0.05;
            _blankItem.Enabled = _layout.ZoneCount > 1;
            _removeItem.Enabled = _layout.ZoneCount > 1;
            _removeBlankItem.Enabled = _layout.PieceCount > 1;
            _menu.Show(this, p);
        }

        // ---------- keyboard ----------

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            ShiftChanged(e.Shift);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            ShiftChanged(e.Shift);
        }

        void ShiftChanged(bool shift)
        {
            if (shift == _shift) return;
            _shift = shift;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _menu.Dispose();
            base.Dispose(disposing);
        }
    }
}
