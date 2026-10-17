using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Zones
{
    /// <summary>
    /// The sizes of one display's layout as a row of small chips, the active
    /// one edged in the accent, with a "+" after them. Each chip is a
    /// miniature of that size, because sizes differ only in where their lines
    /// sit: a middle zone at 50% and at 60% are told apart by looking, not by
    /// a number.
    ///
    /// Clicking a chip makes that size the active one; the owner moves the
    /// snapped windows and shows it in the editor. "+" asks for a copy of the
    /// active size, which changes nothing on screen until its lines are
    /// dragged. Removing is on a right-click, out of the way of the clicks
    /// that switch sizes, and never offered for the last one.
    /// </summary>
    public class SizeStrip : Control
    {
        /// <summary>
        /// The most sizes a display can have. The shortcut steps through them
        /// one at a time, so past a handful it stops being quicker than
        /// opening this window.
        /// </summary>
        public const int MaxSizes = 6;

        const string Label = "Sizes";

        float _s = 1f;
        bool _onCard;
        float _aspect = 16f / 9f;
        List<ZoneLayout> _sizes = new List<ZoneLayout>();
        int _active;
        int _hover = -1;    // a chip: a size, or Count for the "+"
        int _down = -1;
        int _picked = -1;
        int _remove = -1;

        // Every chip fades on its own, the "+" last, so moving along the row,
        // or switching sizes, cross-fades: the old one goes as the new one
        // comes. Made again whenever the number of sizes changes.
        Tween[] _hoverMotion;
        Tween[] _pressMotion;
        Tween[] _activeMotion;

        readonly ContextMenuStrip _menu = new ContextMenuStrip();
        readonly ToolStripMenuItem _removeItem;
        int _menuIndex = -1;

        /// <summary>A size was clicked; <see cref="PickedIndex"/> says which.</summary>
        public event EventHandler SizePicked;

        /// <summary>The "+" was clicked: a new size is wanted.</summary>
        public event EventHandler AddClicked;

        /// <summary>"Remove size" was chosen; <see cref="RemoveIndex"/> says which.</summary>
        public event EventHandler RemoveRequested;

        public SizeStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            // Mouse only, like the layout tiles: there is no keyboard way
            // through the chips, so a tab stop here would do nothing.
            TabStop = false;

            Theme.StyleMenu(_menu);
            _removeItem = Theme.MenuItem("Remove size", 0xE74D);
            _removeItem.Click += delegate
            {
                _remove = _menuIndex;
                if (RemoveRequested != null) RemoveRequested(this, EventArgs.Empty);
            };
            _menu.Items.Add(_removeItem);

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

        /// <summary>The display's width over its height: the shape of every miniature.</summary>
        public float Aspect
        {
            get { return _aspect; }
            set
            {
                _aspect = value > 0 ? value : 16f / 9f;
                Invalidate();
            }
        }

        /// <summary>
        /// The display's sizes, in order. Only read, never changed here: adding
        /// and removing are the owner's, through the events.
        /// </summary>
        public List<ZoneLayout> Sizes
        {
            get { return _sizes; }
            set
            {
                _sizes = value ?? new List<ZoneLayout>();
                _down = -1;
                if (_hoverMotion.Length != Count + 1) Rebuild();
                Animate();
                Invalidate();
            }
        }

        /// <summary>
        /// The size shown as in use. Setting it raises nothing. Once there is
        /// a window, the accent edge fades across to the new one; before, as
        /// the owner fills in its settings, it is simply there.
        /// </summary>
        public int Active
        {
            get { return _active; }
            set
            {
                _active = value;
                for (int i = 0; i < Count; i++)
                {
                    float on = i == value ? 1 : 0;
                    if (IsHandleCreated) _activeMotion[i].Target = on;
                    else _activeMotion[i].Snap(on);
                }
                Invalidate();
            }
        }

        /// <summary>The size last clicked, for <see cref="SizePicked"/>.</summary>
        public int PickedIndex
        {
            get { return _picked; }
        }

        /// <summary>The size to remove, for <see cref="RemoveRequested"/>.</summary>
        public int RemoveIndex
        {
            get { return _remove; }
        }

        int Count
        {
            get { return _sizes.Count; }
        }

        /// <summary>Another size can be added: the "+" is live.</summary>
        bool CanAdd
        {
            get { return Count < MaxSizes; }
        }

        int R(float px)
        {
            return (int)Math.Round(px * _s);
        }

        /// <summary>
        /// A fresh set of fades, one per chip and the "+", each starting where
        /// its chip is now: sizes come and go, and only what changes after
        /// that should move.
        /// </summary>
        void Rebuild()
        {
            int n = Count + 1;
            _hoverMotion = new Tween[n];
            _pressMotion = new Tween[n];
            _activeMotion = new Tween[n];
            for (int i = 0; i < n; i++)
            {
                _hoverMotion[i] = new Tween(this, i == _hover ? 1 : 0, Motion.Fast);
                _pressMotion[i] = new Tween(this, 0, Motion.Fast);
                _activeMotion[i] = new Tween(this, i < Count && i == _active ? 1 : 0, Motion.Normal);
            }
        }

        /// <summary>
        /// As wide as the label, every size and the "+", in device pixels. It
        /// changes with the number of sizes, so the owner lays out again
        /// whenever sizes come or go.
        /// </summary>
        public int PreferredWidth()
        {
            int label = TextRenderer.MeasureText(Label, Theme.Font(12 * _s), Size.Empty, TextFormatFlags.NoPadding).Width;
            return label + R(8) + Count * (R(52) + R(6)) + R(32);
        }

        /// <summary>
        /// Chip <paramref name="i"/>: a size, or the "+" at <see cref="Count"/>.
        /// Placed from the right edge, which is where the owner aligns the
        /// strip: whatever the label measures, the chips stay put, and the
        /// "+" stays under the cursor as sizes are added.
        /// </summary>
        Rectangle Chip(int i)
        {
            if (i >= Count) return new Rectangle(Width - R(32), 0, R(32), Height);
            int x = Width - R(32) - (Count - i) * (R(52) + R(6));
            return new Rectangle(x, 0, R(52), Height);
        }

        /// <summary>The chip under <paramref name="p"/>, or -1. The "+" only counts while it is live.</summary>
        int ChipAt(Point p)
        {
            int last = CanAdd ? Count : Count - 1;
            for (int i = 0; i <= last; i++)
            {
                if (Chip(i).Contains(p)) return i;
            }
            return -1;
        }

        // ---------- painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(_onCard ? Theme.CardBack : Theme.WindowBack))
                g.FillRectangle(b, ClientRectangle);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // The label ends the gap before the first chip, so a measurement a
            // pixel out only moves its start, which nothing lines up with.
            using (SolidBrush b = new SolidBrush(Theme.TextDim))
            using (StringFormat f = new StringFormat(StringFormat.GenericTypographic))
            {
                f.Alignment = StringAlignment.Far;
                f.LineAlignment = StringAlignment.Center;
                f.FormatFlags |= StringFormatFlags.NoWrap;
                g.DrawString(Label, Theme.Font(12 * _s), b, new RectangleF(0, 0, Math.Max(0, Chip(0).Left - R(8)), Height), f);
            }

            bool enabled = Enabled;
            for (int i = 0; i < Count; i++)
                DrawSize(g, i, enabled ? _hoverMotion[i].Value : 0, enabled ? _pressMotion[i].Value : 0);

            // The "+": the same plate, dimmed and inert once there are as many
            // sizes as there may be.
            Rectangle plus = Chip(Count);
            bool live = enabled && CanAdd;
            DrawPlate(g, plus, 0, live ? _hoverMotion[Count].Value : 0, live ? _pressMotion[Count].Value : 0);
            Theme.DrawGlyph(g, Theme.Glyph(0xE710), 12 * _s, live ? Theme.Text : Theme.TextFaint, plus);
        }

        /// <summary>
        /// One size: a button-like plate holding a miniature of the layout
        /// at that size, in the display's own proportions.
        /// </summary>
        void DrawSize(Graphics g, int i, float hover, float pressed)
        {
            Rectangle chip = Chip(i);
            float active = _activeMotion[i].Value;
            DrawPlate(g, chip, active, hover, pressed);

            RectangleF box = new RectangleF(chip.Left + R(6), chip.Top + R(5), chip.Width - R(12), chip.Height - R(10));
            float w = Math.Min(box.Width, box.Height * _aspect);
            float h = w / _aspect;
            RectangleF mini = new RectangleF(box.Left + (box.Width - w) / 2, box.Top + (box.Height - h) / 2, w, h);

            Color rest = Theme.Light ? Color.FromArgb(0xC9, 0xC9, 0xC9) : Color.FromArgb(0x5A, 0x5A, 0x5A);
            Color zone = Motion.Blend(rest, Theme.Accent, active);
            foreach (Rectangle z in _sizes[i].ZonesIn(Rectangle.Round(mini), Math.Max(1, R(2))))
                Theme.FillRounded(g, zone, z, 1.5f * _s);
        }

        /// <summary>
        /// A chip's plate. The active one takes an accent edge in place of the
        /// hairline, inset so all of it stays inside the chip.
        /// </summary>
        void DrawPlate(Graphics g, Rectangle chip, float active, float hover, float pressed)
        {
            float stroke = Motion.Lerp(1f, 1.5f * _s, active);
            RectangleF edge = chip;
            edge.Inflate(-stroke / 2, -stroke / 2);
            using (GraphicsPath p = Theme.Rounded(edge, 4 * _s))
            {
                Color fill = Motion.Blend(Motion.Blend(Theme.ControlBack, Theme.ControlHover, hover), Theme.ControlPressed, pressed);
                using (SolidBrush b = new SolidBrush(fill))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(Motion.Blend(Theme.ControlStroke, Theme.Accent, active), stroke))
                    g.DrawPath(pen, p);
            }
        }

        // ---------- input ----------

        /// <summary>
        /// Points every chip's hover and press at where the mouse has left
        /// things. Pressed only shows on the chip pressed, while the cursor is
        /// still on it: let go anywhere else and nothing happens.
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
            _hover = ChipAt(PointToClient(Cursor.Position));
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
            int hover = ChipAt(e.Location);
            if (hover == _hover) return;
            _hover = hover;
            Animate();
            Invalidate();
        }

        // The owner resizes the strip to its chips as sizes come and go, which
        // moves them under a cursor that has not moved. Not before there is a
        // window: asking where the cursor is would make one, and the form's
        // with it, in the middle of the form's constructor.
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!IsHandleCreated) return;
            Point p = PointToClient(Cursor.Position);
            _hover = ClientRectangle.Contains(p) ? ChipAt(p) : -1;
            Animate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled || e.Button != MouseButtons.Left) return;
            _down = ChipAt(e.Location);
            Animate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int down = _down;
            _down = -1;
            Animate();
            if (!Enabled) return;

            int at = ChipAt(e.Location);
            if (e.Button == MouseButtons.Right)
            {
                if (at >= 0 && at < Count) OpenMenu(at, e.Location);
                return;
            }
            if (e.Button != MouseButtons.Left || down < 0 || at != down) return;

            if (down == Count)
            {
                if (AddClicked != null) AddClicked(this, EventArgs.Empty);
                return;
            }
            _picked = down;
            if (SizePicked != null) SizePicked(this, EventArgs.Empty);
        }

        void OpenMenu(int i, Point p)
        {
            _menuIndex = i;
            _removeItem.Enabled = Count > 1;
            _menu.Show(this, p);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _hover = -1;
            _down = -1;
            Animate();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _menu.Dispose();
            base.Dispose(disposing);
        }
    }
}
