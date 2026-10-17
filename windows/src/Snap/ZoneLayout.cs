using System;
using System.Collections.Generic;
using System.Drawing;

namespace Zones
{
    /// <summary>
    /// One piece of a snap layout: either a zone a window can be dropped into
    /// (no children), or a run of pieces laid side by side (<see cref="Columns"/>)
    /// or stacked top to bottom.
    ///
    /// A tree rather than a flat list of rectangles, because that is what makes
    /// editing behave: dragging the line between two columns resizes those two
    /// and nothing else, and removing a zone hands its space to a neighbour
    /// instead of leaving a hole nothing can fill.
    /// </summary>
    public sealed class ZoneNode
    {
        /// <summary>Share of the parent along the parent's axis. Siblings sum to 1.</summary>
        public double Size;

        /// <summary>For a run: children side by side (true) or stacked (false).</summary>
        public bool Columns;

        /// <summary>Null, or empty, for a zone.</summary>
        public List<ZoneNode> Children;

        public ZoneNode(double size)
        {
            Size = size;
        }

        public bool IsZone
        {
            get { return Children == null || Children.Count == 0; }
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
    /// right, a stack top to bottom, depth first. Every operation leaves the tree
    /// normalised: no run of one, no run inside a run going the same way, and
    /// every set of siblings summing to 1.
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

        /// <summary>The zone nodes, in zone-number order.</summary>
        public List<ZoneNode> Zones()
        {
            List<ZoneNode> list = new List<ZoneNode>();
            CollectZones(Root, list);
            return list;
        }

        static void CollectZones(ZoneNode n, List<ZoneNode> list)
        {
            if (n.IsZone)
            {
                list.Add(n);
                return;
            }
            foreach (ZoneNode c in n.Children) CollectZones(c, list);
        }

        // ---------- geometry ----------

        /// <summary>
        /// Every zone's rectangle inside <paramref name="area"/>, in zone-number
        /// order, with <paramref name="spacing"/> pixels between neighbouring
        /// zones and around the outside.
        ///
        /// Laid out in whole pixels as it goes, so two neighbours share the very
        /// same edge before the spacing is taken off: rounding each zone on its
        /// own left one-pixel slivers and overlaps between them.
        /// </summary>
        public List<Rectangle> ZonesIn(Rectangle area, int spacing)
        {
            List<Rectangle> raw = new List<Rectangle>();
            Lay(Root, area, raw, null);

            if (spacing <= 0) return raw;

            // The outer edge gets the whole gap; an inner edge gets half, so two
            // halves meet as one gap. The odd pixel goes to the left/top side.
            int inner1 = spacing - spacing / 2;
            int inner2 = spacing / 2;

            List<Rectangle> zones = new List<Rectangle>(raw.Count);
            foreach (Rectangle r in raw)
            {
                int l = r.Left + (r.Left <= area.Left ? spacing : inner2);
                int t = r.Top + (r.Top <= area.Top ? spacing : inner2);
                int rr = r.Right - (r.Right >= area.Right ? spacing : inner1);
                int b = r.Bottom - (r.Bottom >= area.Bottom ? spacing : inner1);
                zones.Add(Rectangle.FromLTRB(l, t, Math.Max(l + 1, rr), Math.Max(t + 1, b)));
            }
            return zones;
        }

        /// <summary>Every line between neighbouring pieces, laid out in <paramref name="area"/>.</summary>
        public List<ZoneDivider> Dividers(Rectangle area)
        {
            List<ZoneDivider> list = new List<ZoneDivider>();
            Lay(Root, area, null, list);
            return list;
        }

        static void Lay(ZoneNode n, Rectangle r, List<Rectangle> zones, List<ZoneDivider> dividers)
        {
            if (n.IsZone)
            {
                if (zones != null) zones.Add(r);
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
                Lay(n.Children[i], child, zones, dividers);

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
        /// Cuts zone <paramref name="zone"/> in two: with a vertical line when
        /// <paramref name="columns"/>, into a left and a right part, otherwise
        /// into a top and a bottom one. <paramref name="at"/> is where the cut
        /// falls inside the zone, 0 to 1. Size limits are the caller's to check.
        /// </summary>
        public bool Split(int zone, bool columns, double at)
        {
            if (at <= 0 || at >= 1) return false;

            ZoneNode parent;
            ZoneNode node = Find(zone, out parent);
            if (node == null) return false;

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
        /// Takes zone <paramref name="zone"/> out, its space going to the
        /// neighbour before it (after it, for the first). The last zone stays.
        /// </summary>
        public bool Remove(int zone)
        {
            ZoneNode parent;
            ZoneNode node = Find(zone, out parent);
            if (node == null || parent == null) return false;

            int i = parent.Children.IndexOf(node);
            int heir = i > 0 ? i - 1 : i + 1;
            parent.Children[heir].Size += node.Size;
            parent.Children.RemoveAt(i);

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

        ZoneNode Find(int zone, out ZoneNode parent)
        {
            parent = null;
            int index = 0;
            return Find(Root, null, zone, ref index, out parent);
        }

        static ZoneNode Find(ZoneNode n, ZoneNode up, int zone, ref int index, out ZoneNode parent)
        {
            parent = null;
            if (n.IsZone)
            {
                if (index++ != zone) return null;
                parent = up;
                return n;
            }
            foreach (ZoneNode c in n.Children)
            {
                ZoneNode hit = Find(c, n, zone, ref index, out parent);
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
            if (n.IsZone)
            {
                n.Children = null;
                return;
            }

            foreach (ZoneNode c in n.Children) Normalize(c);

            // A run inside a run going the same way is just more siblings.
            List<ZoneNode> flat = new List<ZoneNode>();
            foreach (ZoneNode c in n.Children)
            {
                if (!c.IsZone && c.Columns == n.Columns)
                {
                    foreach (ZoneNode g in c.Children)
                    {
                        g.Size *= c.Size;
                        flat.Add(g);
                    }
                }
                else flat.Add(c);
            }
            n.Children = flat;

            // A run of one is that one.
            if (n.Children.Count == 1)
            {
                ZoneNode only = n.Children[0];
                n.Columns = only.Columns;
                n.Children = only.Children;
                if (n.IsZone) n.Children = null;
                return;
            }

            double sum = 0;
            foreach (ZoneNode c in n.Children) sum += c.Size;
            foreach (ZoneNode c in n.Children)
                c.Size = sum > 0 ? c.Size / sum : 1.0 / n.Children.Count;
        }

        // ---------- comparing ----------

        /// <summary>The same zones in the same places, give or take a rounding.</summary>
        public bool SameAs(ZoneLayout other)
        {
            return other != null && Same(Root, other.Root);
        }

        static bool Same(ZoneNode a, ZoneNode b)
        {
            if (Math.Abs(a.Size - b.Size) > 0.002) return false;
            if (a.IsZone || b.IsZone) return a.IsZone && b.IsZone;
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
            if (!n.IsZone)
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
        /// </summary>
        public static ZoneLayout FromJson(object v)
        {
            try
            {
                ZoneNode root = NodeFromJson(v, 0);
                return root == null ? null : new ZoneLayout(root);
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
            if (kids == null || kids.Count == 0) return n;

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
