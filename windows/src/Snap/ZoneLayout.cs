using System;
using System.Collections.Generic;
using System.Drawing;

namespace Zones
{
    /// <summary>
    /// One piece of a snap layout: either a zone a window can be dropped into
    /// (no children), a part of the display left empty (no children,
    /// <see cref="Blank"/>), or a run of pieces laid side by side
    /// (<see cref="Columns"/>) or stacked top to bottom.
    ///
    /// A tree rather than a flat list of rectangles, because that is what makes
    /// editing behave: dragging the line between two columns resizes those two
    /// and nothing else, and removing a zone hands its space to a neighbour
    /// instead of leaving a hole nothing can fill. A hole on purpose is an
    /// empty piece, which keeps its lines draggable like any other.
    /// </summary>
    public sealed class ZoneNode
    {
        /// <summary>Share of the parent along the parent's axis. Siblings sum to 1.</summary>
        public double Size;

        /// <summary>For a run: children side by side (true) or stacked (false).</summary>
        public bool Columns;

        /// <summary>Null, or empty, for a zone or an empty piece.</summary>
        public List<ZoneNode> Children;

        /// <summary>A leaf left empty: it takes its share of the run but is not a zone.</summary>
        public bool Blank;

        public ZoneNode(double size)
        {
            Size = size;
        }

        /// <summary>No children: a zone, or an empty piece.</summary>
        public bool IsLeaf
        {
            get { return Children == null || Children.Count == 0; }
        }

        /// <summary>A leaf a window can be dropped into: one not left empty.</summary>
        public bool IsZone
        {
            get { return IsLeaf && !Blank; }
        }

        /// <summary>A run of equal shares, each child a zone unless given.</summary>
        public static ZoneNode Run(double size, bool columns, params ZoneNode[] children)
        {
            ZoneNode n = new ZoneNode(size);
            n.Columns = columns;
            n.Children = new List<ZoneNode>(children);
            return n;
        }

        /// <summary>A run of <paramref name="count"/> equal zones.</summary>
        public static ZoneNode Even(double size, bool columns, int count)
        {
            ZoneNode[] kids = new ZoneNode[count];
            for (int i = 0; i < count; i++) kids[i] = new ZoneNode(1.0 / count);
            return Run(size, columns, kids);
        }

        public ZoneNode Clone()
        {
            ZoneNode n = new ZoneNode(Size);
            n.Columns = Columns;
            n.Blank = Blank;
            if (Children != null)
            {
                n.Children = new List<ZoneNode>(Children.Count);
                foreach (ZoneNode c in Children) n.Children.Add(c.Clone());
            }
            return n;
        }
    }

    /// <summary>
    /// The line between two neighbouring pieces of a run, in the pixel space of
    /// the rectangle the layout was laid out in. The editor drags these.
    /// </summary>
    public sealed class ZoneDivider
    {
        /// <summary>The run whose children the line separates.</summary>
        public ZoneNode Run;

        /// <summary>The line sits between Run.Children[Index] and [Index + 1].</summary>
        public int Index;

        /// <summary>The run's own rectangle. Moving the line never changes it.</summary>
        public Rectangle Region;

        /// <summary>Where the line is: an x for a vertical line, a y for a horizontal one.</summary>
        public int Position;

        /// <summary>A vertical line, between two columns.</summary>
        public bool Vertical
        {
            get { return Run.Columns; }
        }

        /// <summary>The line itself, as a zero-width (or zero-height) rectangle.</summary>
        public Rectangle Line
        {
            get
            {
                return Vertical
                    ? new Rectangle(Position, Region.Top, 0, Region.Height)
                    : new Rectangle(Region.Left, Position, Region.Width, 0);
            }
        }
    }

    /// <summary>
    /// How one display is carved up into zones. Shares, not pixels, so the same
    /// layout fits whatever resolution and scale the display has, and survives
    /// either changing.
    ///
    /// Zones are numbered in reading order of the tree: a run of columns left to
    /// right, a stack top to bottom, depth first. A piece left empty holds its
    /// place in that order but takes no number, so the numbers skip it;
    /// outside the editor it is only room no window is snapped into. Every
    /// operation leaves the tree normalised: no run of one, no run inside a run
    /// going the same way, no two empty pieces side by side, and every set of
    /// siblings summing to 1.
    /// </summary>
    public sealed class ZoneLayout
    {
        public ZoneNode Root;

        public ZoneLayout(ZoneNode root)
        {
            Root = root;
            Normalize();
        }

        public ZoneLayout Clone()
        {
            return new ZoneLayout(Root.Clone());
        }

        public int ZoneCount
        {
            get { return Zones().Count; }
        }

        /// <summary>The zone nodes, in zone-number order: the pieces without the empty ones.</summary>
        public List<ZoneNode> Zones()
        {
            List<ZoneNode> list = new List<ZoneNode>();
            foreach (ZoneNode n in Pieces())
            {
                if (n.IsZone) list.Add(n);
            }
            return list;
        }

        public int PieceCount
        {
            get { return Pieces().Count; }
        }

        /// <summary>Every leaf, zone or empty, in reading order: what the editor numbers its pieces by.</summary>
        public List<ZoneNode> Pieces()
        {
            List<ZoneNode> list = new List<ZoneNode>();
            CollectPieces(Root, list);
            return list;
        }

        static void CollectPieces(ZoneNode n, List<ZoneNode> list)
        {
            if (n.IsLeaf)
            {
                list.Add(n);
                return;
            }
            foreach (ZoneNode c in n.Children) CollectPieces(c, list);
        }

        /// <summary>Whether piece <paramref name="piece"/> is left empty; false for one there is not.</summary>
        public bool IsBlank(int piece)
        {
            ZoneNode parent;
            ZoneNode node = Find(piece, out parent);
            return node != null && node.Blank;
        }

        // ---------- geometry ----------

        /// <summary>
        /// Every zone's rectangle inside <paramref name="area"/>, in zone-number
        /// order, with <paramref name="spacing"/> pixels between neighbouring
        /// zones and around the outside: <see cref="PiecesIn"/> without the
        /// empty pieces. A zone's edge against an empty piece is an inner edge
        /// all the same, so emptying a neighbour does not move a zone.
        /// </summary>
        public List<Rectangle> ZonesIn(Rectangle area, int spacing)
        {
            List<ZoneNode> pieces = Pieces();
            List<Rectangle> all = PiecesIn(area, spacing);
            List<Rectangle> zones = new List<Rectangle>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                if (pieces[i].IsZone) zones.Add(all[i]);
            }
            return zones;
        }

        /// <summary>
        /// Every piece's rectangle inside <paramref name="area"/>, zones and
        /// empty ones alike, in reading order, with <paramref name="spacing"/>
        /// pixels between neighbouring pieces and around the outside.
        ///
        /// Laid out in whole pixels as it goes, so two neighbours share the very
        /// same edge before the spacing is taken off: rounding each piece on its
        /// own left one-pixel slivers and overlaps between them.
        /// </summary>
        public List<Rectangle> PiecesIn(Rectangle area, int spacing)
        {
            List<Rectangle> raw = new List<Rectangle>();
            Lay(Root, area, raw, null);

            if (spacing <= 0) return raw;

            // The outer edge gets the whole gap; an inner edge gets half, so two
            // halves meet as one gap. The odd pixel goes to the left/top side.
            int inner1 = spacing - spacing / 2;
            int inner2 = spacing / 2;

            List<Rectangle> pieces = new List<Rectangle>(raw.Count);
            foreach (Rectangle r in raw)
            {
                int l = r.Left + (r.Left <= area.Left ? spacing : inner2);
                int t = r.Top + (r.Top <= area.Top ? spacing : inner2);
                int rr = r.Right - (r.Right >= area.Right ? spacing : inner1);
                int b = r.Bottom - (r.Bottom >= area.Bottom ? spacing : inner1);
                pieces.Add(Rectangle.FromLTRB(l, t, Math.Max(l + 1, rr), Math.Max(t + 1, b)));
            }
            return pieces;
        }

        /// <summary>
        /// The empty pieces' rectangles inside <paramref name="area"/>, without
        /// any spacing taken off: the parts of it where a dragged window lights
        /// no zone.
        /// </summary>
        public List<Rectangle> BlanksIn(Rectangle area)
        {
            List<ZoneNode> pieces = Pieces();
            List<Rectangle> all = PiecesIn(area, 0);
            List<Rectangle> blanks = new List<Rectangle>();
            for (int i = 0; i < all.Count; i++)
            {
                if (pieces[i].Blank) blanks.Add(all[i]);
            }
            return blanks;
        }

        /// <summary>Every line between neighbouring pieces, laid out in <paramref name="area"/>.</summary>
        public List<ZoneDivider> Dividers(Rectangle area)
        {
            List<ZoneDivider> list = new List<ZoneDivider>();
            Lay(Root, area, null, list);
            return list;
        }

        static void Lay(ZoneNode n, Rectangle r, List<Rectangle> pieces, List<ZoneDivider> dividers)
        {
            if (n.IsLeaf)
            {
                if (pieces != null) pieces.Add(r);
                return;
            }

            int start = n.Columns ? r.Left : r.Top;
            int extent = n.Columns ? r.Width : r.Height;
            double cum = 0;
            int from = start;

            for (int i = 0; i < n.Children.Count; i++)
            {
                cum += n.Children[i].Size;
                // The last child always ends exactly on the run's edge, whatever
                // the sum of the shares has drifted to.
                int to = i == n.Children.Count - 1
                    ? start + extent
                    : start + (int)Math.Round(cum * extent);

                Rectangle child = n.Columns
                    ? Rectangle.FromLTRB(from, r.Top, to, r.Bottom)
                    : Rectangle.FromLTRB(r.Left, from, r.Right, to);
                Lay(n.Children[i], child, pieces, dividers);

                if (dividers != null && i < n.Children.Count - 1)
                {
                    ZoneDivider d = new ZoneDivider();
                    d.Run = n;
                    d.Index = i;
                    d.Region = r;
                    d.Position = to;
                    dividers.Add(d);
                }
                from = to;
            }
        }

        // ---------- editing ----------

        /// <summary>
        /// Cuts piece <paramref name="piece"/> in two: with a vertical line when
        /// <paramref name="columns"/>, into a left and a right part, otherwise
        /// into a top and a bottom one. <paramref name="at"/> is where the cut
        /// falls inside the piece, 0 to 1. Size limits are the caller's to check.
        /// Only a zone is cut: two halves of an empty piece would be put
        /// straight back together as one.
        /// </summary>
        public bool Split(int piece, bool columns, double at)
        {
            if (at <= 0 || at >= 1) return false;

            ZoneNode parent;
            ZoneNode node = Find(piece, out parent);
            if (node == null || node.Blank) return false;

            if (parent != null && parent.Columns == columns)
            {
                // Same direction as the run it is in: one more sibling, so the
                // line between them stays draggable on its own.
                int i = parent.Children.IndexOf(node);
                double whole = node.Size;
                node.Size = whole * at;
                parent.Children.Insert(i + 1, new ZoneNode(whole * (1 - at)));
            }
            else
            {
                // Across it: the zone becomes a run of two.
                node.Columns = columns;
                node.Children = new List<ZoneNode>();
                node.Children.Add(new ZoneNode(at));
                node.Children.Add(new ZoneNode(1 - at));
            }

            Normalize();
            return true;
        }

        /// <summary>
        /// Takes piece <paramref name="piece"/> out, its space going to the
        /// neighbour before it (after it, for the first). The last piece stays,
        /// and so does the last zone, however many empty pieces are around it.
        /// </summary>
        public bool Remove(int piece)
        {
            ZoneNode parent;
            ZoneNode node = Find(piece, out parent);
            if (node == null || parent == null) return false;
            if (node.IsZone && ZoneCount == 1) return false;

            int i = parent.Children.IndexOf(node);
            int heir = i > 0 ? i - 1 : i + 1;
            parent.Children[heir].Size += node.Size;
            parent.Children.RemoveAt(i);

            Normalize();
            return true;
        }

        /// <summary>
        /// Leaves piece <paramref name="piece"/> empty, or makes it a zone
        /// again. False when it already is what it is asked to be, or when it
        /// is the last zone: a layout with nowhere to put a window is no layout.
        /// </summary>
        public bool SetBlank(int piece, bool blank)
        {
            ZoneNode parent;
            ZoneNode node = Find(piece, out parent);
            if (node == null || node.Blank == blank) return false;
            if (blank && ZoneCount == 1) return false;

            node.Blank = blank;
            Normalize();
            return true;
        }

        /// <summary>
        /// Moves a line to <paramref name="position"/>, in the same pixel space
        /// it was laid out in, resizing only the two pieces either side of it and
        /// keeping each at least <paramref name="minPx"/> along the run.
        /// </summary>
        public void MoveDivider(ZoneDivider d, int position, int minPx)
        {
            List<ZoneNode> kids = d.Run.Children;
            if (kids == null || d.Index < 0 || d.Index + 1 >= kids.Count) return;

            int start = d.Vertical ? d.Region.Left : d.Region.Top;
            int extent = d.Vertical ? d.Region.Width : d.Region.Height;
            if (extent <= 0) return;

            double before = 0;
            for (int i = 0; i < d.Index; i++) before += kids[i].Size;

            double pair = kids[d.Index].Size + kids[d.Index + 1].Size;
            double min = (double)minPx / extent;
            double lo = min, hi = pair - min;
            if (lo > hi) return;

            double first = (double)(position - start) / extent - before;
            if (first < lo) first = lo;
            if (first > hi) first = hi;

            kids[d.Index].Size = first;
            kids[d.Index + 1].Size = pair - first;
            d.Position = start + (int)Math.Round((before + first) * extent);
        }

        /// <summary>Piece <paramref name="piece"/>, counting empty ones, and the run it is in.</summary>
        ZoneNode Find(int piece, out ZoneNode parent)
        {
            parent = null;
            int index = 0;
            return Find(Root, null, piece, ref index, out parent);
        }

        static ZoneNode Find(ZoneNode n, ZoneNode up, int piece, ref int index, out ZoneNode parent)
        {
            parent = null;
            if (n.IsLeaf)
            {
                if (index++ != piece) return null;
                parent = up;
                return n;
            }
            foreach (ZoneNode c in n.Children)
            {
                ZoneNode hit = Find(c, n, piece, ref index, out parent);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>
        /// Folds the tree back into its one canonical shape, so a layout that
        /// looks like a preset also compares equal to it.
        /// </summary>
        void Normalize()
        {
            Root.Size = 1;
            Normalize(Root);
        }

        static void Normalize(ZoneNode n)
        {
            if (n.IsLeaf)
            {
                n.Children = null;
                return;
            }

            // Only a leaf is ever empty; a run's emptiness is its children's.
            n.Blank = false;

            foreach (ZoneNode c in n.Children) Normalize(c);

            // A run inside a run going the same way is just more siblings.
            List<ZoneNode> flat = new List<ZoneNode>();
            foreach (ZoneNode c in n.Children)
            {
                if (!c.IsLeaf && c.Columns == n.Columns)
                {
                    foreach (ZoneNode g in c.Children)
                    {
                        g.Size *= c.Size;
                        flat.Add(g);
                    }
                }
                else flat.Add(c);
            }

            // Two empty pieces side by side are one empty piece: the line
            // between them would move nothing anyone could see.
            List<ZoneNode> merged = new List<ZoneNode>(flat.Count);
            foreach (ZoneNode c in flat)
            {
                ZoneNode last = merged.Count > 0 ? merged[merged.Count - 1] : null;
                if (last != null && last.Blank && c.Blank) last.Size += c.Size;
                else merged.Add(c);
            }
            n.Children = merged;

            // A run of one is that one, so a run left all empty is one empty piece.
            if (n.Children.Count == 1)
            {
                ZoneNode only = n.Children[0];
                n.Columns = only.Columns;
                n.Blank = only.Blank;
                n.Children = only.Children;
                if (n.IsLeaf) n.Children = null;
                return;
            }

            double sum = 0;
            foreach (ZoneNode c in n.Children) sum += c.Size;
            foreach (ZoneNode c in n.Children)
                c.Size = sum > 0 ? c.Size / sum : 1.0 / n.Children.Count;
        }

        // ---------- comparing ----------

        /// <summary>The same zones and empty pieces in the same places, give or take a rounding.</summary>
        public bool SameAs(ZoneLayout other)
        {
            return other != null && Same(Root, other.Root);
        }

        static bool Same(ZoneNode a, ZoneNode b)
        {
            if (Math.Abs(a.Size - b.Size) > 0.002) return false;
            if (a.IsLeaf || b.IsLeaf) return a.IsLeaf && b.IsLeaf && a.Blank == b.Blank;
            if (a.Columns != b.Columns || a.Children.Count != b.Children.Count) return false;
            for (int i = 0; i < a.Children.Count; i++)
            {
                if (!Same(a.Children[i], b.Children[i])) return false;
            }
            return true;
        }

        // ---------- presets ----------

        /// <summary>Names of the ready-made layouts, in the order the settings window shows them.</summary>
        public static readonly string[] PresetNames =
        {
            "Halves", "Thirds", "Focus", "Two-thirds", "Main + 2", "Grid", "Six", "Rows"
        };

        /// <summary>A fresh copy of ready-made layout <paramref name="i"/> (see <see cref="PresetNames"/>).</summary>
        public static ZoneLayout Preset(int i)
        {
            switch (i)
            {
                case 0: return new ZoneLayout(ZoneNode.Even(1, true, 2));
                case 1: return new ZoneLayout(ZoneNode.Even(1, true, 3));
                case 2:
                    return new ZoneLayout(ZoneNode.Run(1, true,
                        new ZoneNode(0.25), new ZoneNode(0.5), new ZoneNode(0.25)));
                case 3:
                    return new ZoneLayout(ZoneNode.Run(1, true,
                        new ZoneNode(2.0 / 3), new ZoneNode(1.0 / 3)));
                case 4:
                    return new ZoneLayout(ZoneNode.Run(1, true,
                        new ZoneNode(0.6), ZoneNode.Even(0.4, false, 2)));
                case 5:
                    return new ZoneLayout(ZoneNode.Run(1, true,
                        ZoneNode.Even(0.5, false, 2), ZoneNode.Even(0.5, false, 2)));
                case 6:
                    return new ZoneLayout(ZoneNode.Run(1, true,
                        ZoneNode.Even(1.0 / 3, false, 2), ZoneNode.Even(1.0 / 3, false, 2),
                        ZoneNode.Even(1.0 / 3, false, 2)));
                default:
                    return new ZoneLayout(ZoneNode.Even(1, false, 2));
            }
        }

        /// <summary>The index of the preset this layout matches, or -1 for one of the user's own.</summary>
        public int PresetIndex()
        {
            for (int i = 0; i < PresetNames.Length; i++)
            {
                if (SameAs(Preset(i))) return i;
            }
            return -1;
        }

        /// <summary>
        /// The layout a display starts with, before anyone has picked one: two
        /// rows on a portrait screen, a wide centre on an ultrawide, halves on
        /// anything else.
        /// </summary>
        public static ZoneLayout Default(Size work)
        {
            if (work.Height > work.Width) return Preset(7);
            if (work.Height > 0 && (double)work.Width / work.Height >= 2.1) return Preset(2);
            return Preset(0);
        }

        // ---------- persistence ----------

        public object ToJson()
        {
            return NodeToJson(Root);
        }

        static Dictionary<string, object> NodeToJson(ZoneNode n)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["size"] = n.Size;
            if (n.IsLeaf)
            {
                // Only on an empty piece, so a layout without one is written
                // exactly as it was before there were any.
                if (n.Blank) d["blank"] = true;
            }
            else
            {
                d["columns"] = n.Columns;
                List<object> kids = new List<object>();
                foreach (ZoneNode c in n.Children) kids.Add(NodeToJson(c));
                d["children"] = kids;
            }
            return d;
        }

        /// <summary>
        /// Reads a layout back. Null for anything malformed rather than an
        /// exception: one bad layout should cost that display its layout, not
        /// every other setting in the file with it.
        ///
        /// Null too for one with every piece empty: everything that uses a
        /// layout counts on it having at least one zone.
        /// </summary>
        public static ZoneLayout FromJson(object v)
        {
            try
            {
                ZoneNode root = NodeFromJson(v, 0);
                if (root == null) return null;
                ZoneLayout layout = new ZoneLayout(root);
                return layout.ZoneCount > 0 ? layout : null;
            }
            catch
            {
                return null;
            }
        }

        static ZoneNode NodeFromJson(object v, int depth)
        {
            Dictionary<string, object> d = Json.Obj(v);
            if (d == null || depth > 12) return null;

            object sizeVal;
            double size = d.TryGetValue("size", out sizeVal) && sizeVal is double ? (double)sizeVal : 1;
            if (double.IsNaN(size) || double.IsInfinity(size) || size <= 0) return null;

            ZoneNode n = new ZoneNode(size);
            object kidsVal;
            List<object> kids = d.TryGetValue("children", out kidsVal) ? Json.Arr(kidsVal) : null;
            if (kids == null || kids.Count == 0)
            {
                n.Blank = Json.Bool(d, "blank", false);
                return n;
            }

            n.Columns = Json.Bool(d, "columns", true);
            n.Children = new List<ZoneNode>(kids.Count);
            foreach (object k in kids)
            {
                ZoneNode c = NodeFromJson(k, depth + 1);
                if (c == null) return null;
                n.Children.Add(c);
            }
            return n;
        }
    }

    /// <summary>
    /// One display's layouts: one or more sizes of it (the middle zone at 50%,
    /// 60%, 70%, say), one of them active. A shortcut flips between them and
    /// the windows snapped on that display follow, which is how a layout gets
    /// resized without opening the settings window.
    /// </summary>
    public sealed class DisplayLayouts
    {
        /// <summary>Never empty.</summary>
        public readonly List<ZoneLayout> Sizes;
        int _active;

        public DisplayLayouts(ZoneLayout layout)
        {
            Sizes = new List<ZoneLayout>();
            Sizes.Add(layout);
        }

        public DisplayLayouts(List<ZoneLayout> sizes, int active)
        {
            Sizes = new List<ZoneLayout>(sizes);
            if (Sizes.Count == 0) Sizes.Add(ZoneLayout.Preset(0));
            _active = active;
        }

        /// <summary>The size in use. Clamped on every read, so it is always a valid index.</summary>
        public int Active
        {
            get { return Math.Max(0, Math.Min(_active, Sizes.Count - 1)); }
            set { _active = value; }
        }

        public ZoneLayout Current
        {
            get { return Sizes[Active]; }
        }

        public DisplayLayouts Clone()
        {
            List<ZoneLayout> copies = new List<ZoneLayout>(Sizes.Count);
            foreach (ZoneLayout l in Sizes) copies.Add(l.Clone());
            return new DisplayLayouts(copies, Active);
        }

        public object ToJson()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["active"] = Active;
            List<object> sizes = new List<object>(Sizes.Count);
            foreach (ZoneLayout l in Sizes) sizes.Add(l.ToJson());
            d["sizes"] = sizes;
            return d;
        }

        /// <summary>
        /// Reads a display's layouts back; also a bare layout, which is how a
        /// display was stored before it could have more than one size. Null for
        /// anything malformed: one bad size costs that display, not the file.
        /// </summary>
        public static DisplayLayouts FromJson(object v)
        {
            Dictionary<string, object> d = Json.Obj(v);
            if (d == null) return null;

            object sizesVal;
            if (!d.TryGetValue("sizes", out sizesVal))
            {
                ZoneLayout single = ZoneLayout.FromJson(v);
                return single == null ? null : new DisplayLayouts(single);
            }

            List<object> list = Json.Arr(sizesVal);
            if (list == null || list.Count == 0) return null;

            List<ZoneLayout> sizes = new List<ZoneLayout>(list.Count);
            foreach (object item in list)
            {
                ZoneLayout l = ZoneLayout.FromJson(item);
                if (l == null) return null;
                sizes.Add(l);
            }
            return new DisplayLayouts(sizes, Json.Int(d, "active", 0));
        }
    }
}
