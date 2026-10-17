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
    /// The settings window: the displays and the snapping options down the
    /// left, and on the right the layout of whichever display is picked,
    /// chosen from the presets or drawn by hand.
    ///
    /// A real window with a taskbar button, though Zones otherwise keeps to
    /// the notification area, because this is somewhere people stay a while
    /// and come back to, and it is laid out and coloured like a Windows 11
    /// Settings page so it reads as part of the system it changes. The title
    /// bar is its own: the window's colour with no line under it, so the
    /// header is flat with the page instead of a system caption stacked on
    /// top. Every edit is saved as it is made; there is no OK button to forget.
    ///
    /// All the static parts (the header, headings, cards and the hint) are
    /// painted here; the parts that take input are child controls. Both are
    /// placed from one scale and the window's size, so moving to a monitor
    /// with another DPI, or dragging an edge, only needs
    /// <see cref="Arrange"/> again. The left column keeps its width; the
    /// right one, where the layout is drawn, takes the rest.
    /// </summary>
    public class SnapForm : Form
    {
        const int WS_MINIMIZEBOX = 0x00020000;
        const int WS_SYSMENU = 0x00080000;
        const int CS_DROPSHADOW = 0x00020000;
        const int WM_SETTINGCHANGE = 0x001A;
        const int WM_GETMINMAXINFO = 0x0024;
        const int WM_DISPLAYCHANGE = 0x007E;
        const int WM_NCHITTEST = 0x0084;
        const int WM_NCLBUTTONDBLCLK = 0x00A3;
        const int WM_DPICHANGED = 0x02E0;
        const int WM_THEMECHANGED = 0x031A;
        const int HTCLIENT = 1;
        const int HTCAPTION = 2;
        const int HTLEFT = 10;
        const int HTRIGHT = 11;
        const int HTTOP = 12;
        const int HTTOPLEFT = 13;
        const int HTTOPRIGHT = 14;
        const int HTBOTTOM = 15;
        const int HTBOTTOMLEFT = 16;
        const int HTBOTTOMRIGHT = 17;

        const string Hint = "Click to split  ·  Drag a line to resize  ·  Right-click for more";
        const string CopiedText = "Copied to every display";

        // In logical pixels: the size the window opens at, room permitting,
        // and the smallest it can be made, below which the columns crowd (at
        // 640, the snapping card still fits under two displays).
        const int OpenW = 1200;
        const int OpenH = 680;
        const int MinW = 1000;
        const int MinH = 640;

        // In logical pixels: the header band the window is dragged by; a row
        // of the snapping card tall enough for a title and one line of
        // description beside the control, and one for a title alone.
        const int HeaderH = 40;
        const int RowH = 56;
        const int LineRowH = 40;

        // The widest a shortcut box gets, in logical pixels: beside it, the
        // row's title still needs room on the card.
        const int ShortcutMaxW = 172;

        /// <summary>
        /// The left column's width. Wide enough for "Previous zone" beside a
        /// four-key shortcut (Ctrl Alt Shift Z), the longest row the card has.
        /// </summary>
        const int LeftW = 304;
        /// <summary>Where the right column starts: the left one and a 24 gap either side.</summary>
        const int RightX = 24 + LeftW + 24;
        /// <summary>The card's inner right edge, where its controls end.</summary>
        const int CardRight = 24 + LeftW - 16;

        // The rows that need saying what they do have a description under the
        // title, and are taller for it; the rest are the title alone. The
        // last four are the shortcuts, in the order of the boxes.
        static readonly string[] RowTitles =
        {
            "Snap windows", "Hold Shift to snap", "Spacing",
            "Next zone", "Previous zone", "Next size", "Previous size"
        };
        static readonly string[] RowDescriptions =
        {
            "Drop dragged windows into zones",
            "Off: every drag shows zones"
        };
        const int FirstShortcutRow = 3;

        /// <summary>The Hotkey slot each shortcut box sets, in the order of the boxes.</summary>
        static readonly int[] BoxSlots = { Hotkey.SlotZone, Hotkey.SlotZoneBack, Hotkey.SlotSize, Hotkey.SlotSizeBack };

        static SnapForm _open;

        readonly AppConfig _config;
        float _s = 1f;
        List<Display> _displays = new List<Display>();
        DisplayLayouts _layouts;    // the selected display's sizes, as the config has them
        ZoneLayout _layout;         // a copy of the active one, which the editor changes
        ZoneOverlay _overlay;   // made on the first flash: most visits never need it
        Timer _applied;         // puts the subtitle back after "Copied to every display"
        bool _copied;           // the subtitle says the layout was copied, for a moment
        float _snappingY;       // the "Snapping" heading, in logical pixels: it sits under however many displays there are

        readonly ToggleSwitch _snapToggle = new ToggleSwitch();
        readonly ToggleSwitch _shiftToggle = new ToggleSwitch();
        readonly Stepper _spacing = new Stepper();
        readonly ShortcutBox _zoneBox = new ShortcutBox();
        readonly ShortcutBox _zoneBackBox = new ShortcutBox();
        readonly ShortcutBox _sizeBox = new ShortcutBox();
        readonly ShortcutBox _sizeBackBox = new ShortcutBox();
        readonly ShortcutBox[] _boxes;      // the four above, in row order
        readonly DisplayPicker _displayPicker = new DisplayPicker();
        readonly LayoutPicker _layoutPicker = new LayoutPicker();
        readonly SizeStrip _sizeStrip = new SizeStrip();
        readonly FlatButton _showButton = new FlatButton();
        readonly FlatButton _moreButton = new FlatButton();
        readonly ContextMenuStrip _moreMenu = new ContextMenuStrip();
        readonly ToolStripMenuItem _allItem;
        readonly ZoneEditor _editor = new ZoneEditor();
        readonly CaptionButton _minimizeButton = new CaptionButton();
        readonly CaptionButton _closeButton = new CaptionButton();

        /// <summary>Opens the window, or brings back the one already open.</summary>
        public static void ShowFor(AppConfig config)
        {
            if (_open != null && !_open.IsDisposed)
            {
                if (_open.WindowState == FormWindowState.Minimized) _open.WindowState = FormWindowState.Normal;
                _open.Activate();
                return;
            }

            _open = new SnapForm(config);
            _open.Show();
            _open.Activate();
        }

        SnapForm(AppConfig config)
        {
            _config = config;
            // First: sizing the window below lays it out, boxes and all.
            _boxes = new ShortcutBox[] { _zoneBox, _zoneBackBox, _sizeBox, _sizeBackBox };

            Text = "Zones";
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = Theme.WindowBack;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            // Opens where the user is looking: on the display under the
            // cursor, at that display's scale, and never so big it fills it.
            _s = Dpi.ScaleAt(Cursor.Position);
            Display here = Displays.At(Cursor.Position);
            Rectangle area = here != null ? here.Work : Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(R(OpenW), (int)(area.Width * 0.92)), Math.Min(R(OpenH), (int)(area.Height * 0.92)));
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);

            _snapToggle.OnCard = true;
            _snapToggle.ShowLabel = false;
            _snapToggle.Checked = _config.SnapEnabled;
            _snapToggle.CheckedChanged += delegate
            {
                _config.SnapEnabled = _snapToggle.Checked;
                _config.Save();
                Snapper.ApplyHotkey();      // with snapping off, the shortcut is let go
                UpdateEnabled();
            };

            _shiftToggle.OnCard = true;
            _shiftToggle.ShowLabel = false;
            _shiftToggle.Checked = _config.SnapHoldShift;
            _shiftToggle.CheckedChanged += delegate
            {
                _config.SnapHoldShift = _shiftToggle.Checked;
                _config.Save();
                UpdateEnabled();
            };

            _spacing.OnCard = true;
            _spacing.Minimum = 0;
            _spacing.Maximum = AppConfig.MaxSnapSpacing;
            _spacing.Increment = 2;
            _spacing.Suffix = " px";
            _spacing.Value = _config.SnapSpacing;
            _spacing.ValueChanged += delegate
            {
                _config.SnapSpacing = _spacing.Value;
                _config.Save();
                UpdateEnabled();
                Display d = _displayPicker.Selected;
                if (d != null) _editor.SetLayout(_layout, d.Work.Size, SpacingPx(d));
            };

            _zoneBox.OnCard = true;
            _zoneBox.Mods = _config.SnapCycleMods;
            _zoneBox.Vk = _config.SnapCycleKey;
            _zoneBox.ShortcutChanged += delegate
            {
                _config.SnapCycleMods = _zoneBox.Mods;
                _config.SnapCycleKey = _zoneBox.Vk;
                ShortcutSet();
            };

            _zoneBackBox.OnCard = true;
            _zoneBackBox.Mods = _config.SnapCyclePrevMods;
            _zoneBackBox.Vk = _config.SnapCyclePrevKey;
            _zoneBackBox.ShortcutChanged += delegate
            {
                _config.SnapCyclePrevMods = _zoneBackBox.Mods;
                _config.SnapCyclePrevKey = _zoneBackBox.Vk;
                ShortcutSet();
            };

            _sizeBox.OnCard = true;
            _sizeBox.Mods = _config.SnapSizeMods;
            _sizeBox.Vk = _config.SnapSizeKey;
            _sizeBox.ShortcutChanged += delegate
            {
                _config.SnapSizeMods = _sizeBox.Mods;
                _config.SnapSizeKey = _sizeBox.Vk;
                ShortcutSet();
            };

            _sizeBackBox.OnCard = true;
            _sizeBackBox.Mods = _config.SnapSizePrevMods;
            _sizeBackBox.Vk = _config.SnapSizePrevKey;
            _sizeBackBox.ShortcutChanged += delegate
            {
                _config.SnapSizePrevMods = _sizeBackBox.Mods;
                _config.SnapSizePrevKey = _sizeBackBox.Vk;
                ShortcutSet();
            };

            foreach (ShortcutBox box in _boxes)
            {
                box.RecordingChanged += delegate { ShortcutRecording(); };
                // While recording, a box sizes itself to the keys held, from
                // its left edge; laid out again, it keeps its right edge on
                // the card's instead of growing off it.
                box.SizeChanged += delegate
                {
                    Arrange();
                    Invalidate();
                };
            }

            // Selected before the handler is attached: opening the window
            // should not flash the zones at the user.
            _displays = Displays.All();
            _displayPicker.Displays = _displays;
            _displayPicker.SelectedIndex = StartIndex(here);
            _displayPicker.SelectedChanged += delegate
            {
                LoadDisplay();
                Flash(1200);
            };

            // A preset replaces the active size only: the display's other
            // sizes stay as they were.
            _layoutPicker.PresetPicked += delegate
            {
                Display d = _displayPicker.Selected;
                if (d == null) return;
                _layout = ZoneLayout.Preset(_layoutPicker.PickedIndex);
                _editor.SetLayout(_layout, d.Work.Size, SpacingPx(d));
                Commit();
            };

            // Switching, adding and removing sizes all go through Snapper,
            // which moves the windows snapped on the display to the size now
            // active, saves, and shows it on the display.
            _sizeStrip.SizePicked += delegate
            {
                Display d = _displayPicker.Selected;
                if (d == null) return;
                Snapper.ActivateSize(d, _sizeStrip.PickedIndex);
                LoadDisplay();
            };
            _sizeStrip.AddClicked += delegate
            {
                // A copy of the active size, made active: the same zones, so
                // nothing moves until its lines are dragged somewhere else.
                Display d = _displayPicker.Selected;
                if (d == null) return;
                DisplayLayouts layouts = _config.LayoutsFor(d);
                layouts.Sizes.Add(layouts.Current.Clone());
                _config.Keep(d, layouts);
                Snapper.ActivateSize(d, layouts.Sizes.Count - 1);
                LoadDisplay();
            };
            _sizeStrip.RemoveRequested += delegate
            {
                Display d = _displayPicker.Selected;
                if (d == null) return;
                DisplayLayouts layouts = _config.LayoutsFor(d);
                if (layouts.Sizes.Count < 2) return;

                // The active size removed, the one before it takes over (the
                // first, if it was the first); any other, and the same layout
                // stays active, one place earlier if it came after.
                int i = _sizeStrip.RemoveIndex;
                int active = layouts.Active;

                // The windows move first, while the size they are in still
                // exists to be moved out of: the engine finds the windows it
                // did not place itself by matching them against that size.
                if (i == active)
                {
                    int heir = i > 0 ? i - 1 : 1;
                    Snapper.ActivateSize(d, heir);
                    layouts = _config.LayoutsFor(d);
                    active = heir;
                }

                layouts.Sizes.RemoveAt(i);
                if (i < active) active--;
                layouts.Active = active;
                _config.Keep(d, layouts);
                _config.Save();
                LoadDisplay();
            };

            _showButton.Text = "Show on screen";
            _showButton.Glyph = 0xE7F4;
            _showButton.Click += delegate { Flash(1500); };

            // The actions wanted now and then, behind one button so the
            // header is the page's title and the one it is mostly used for.
            Theme.StyleMenu(_moreMenu);
            _allItem = Theme.MenuItem("Use on all displays", 0xE8A9);
            _allItem.Click += delegate { UseOnAll(); };
            ToolStripMenuItem resetItem = Theme.MenuItem("Reset this display", 0xE72C);
            resetItem.Click += delegate
            {
                Display d = _displayPicker.Selected;
                if (d == null) return;
                _config.SnapLayouts.Remove(d.Device);
                _config.Save();
                LoadDisplay();
            };
            _moreMenu.Items.Add(_allItem);
            _moreMenu.Items.Add(resetItem);

            _moreButton.Text = "";
            _moreButton.Glyph = 0xE712;
            _moreButton.Click += delegate
            {
                // "Use on all displays" only means something with more than
                // one. Under the button, out from its right edge, which is
                // the window's: the menu opens over the page, not past it.
                _allItem.Visible = _displays.Count > 1;
                _moreMenu.Show(_moreButton, new Point(_moreButton.Width, _moreButton.Height + R(4)), ToolStripDropDownDirection.BelowLeft);
            };

            _editor.OnCard = true;
            _editor.LayoutChanged += delegate { Commit(); };

            _minimizeButton.Glyph = 0xE921;
            _minimizeButton.Click += delegate { WindowState = FormWindowState.Minimized; };

            _closeButton.Glyph = 0xE8BB;
            _closeButton.IsClose = true;
            _closeButton.Click += delegate { Close(); };

            // The size shortcut flips sizes while this window is open behind
            // whatever has the keyboard; coming back to it, it shows the size
            // in use now rather than the one it last loaded.
            Activated += delegate { LoadDisplay(); };
            Hotkey.Pressed += OnHotkey;

            // In tab order; the caption buttons are not tab stops.
            Controls.Add(_displayPicker);
            Controls.Add(_snapToggle);
            Controls.Add(_shiftToggle);
            Controls.Add(_spacing);
            Controls.Add(_zoneBox);
            Controls.Add(_zoneBackBox);
            Controls.Add(_sizeBox);
            Controls.Add(_sizeBackBox);
            Controls.Add(_showButton);
            Controls.Add(_moreButton);
            Controls.Add(_layoutPicker);
            Controls.Add(_sizeStrip);
            Controls.Add(_editor);
            Controls.Add(_minimizeButton);
            Controls.Add(_closeButton);

            ApplyScale();
            UpdateEnabled();
            LoadDisplay();
            Arrange();
        }

        int R(float px)
        {
            return (int)Math.Round(px * _s);
        }

        /// <summary>A rectangle in logical pixels, each edge rounded on its own so neighbours meet.</summary>
        Rectangle Logical(float x, float y, float w, float h)
        {
            return Rectangle.FromLTRB(R(x), R(y), R(x + w), R(y + h));
        }

        /// <summary>The display under the cursor, else the primary, else the first.</summary>
        int StartIndex(Display here)
        {
            if (here != null)
            {
                for (int i = 0; i < _displays.Count; i++)
                {
                    if (string.Equals(_displays[i].Device, here.Device, StringComparison.OrdinalIgnoreCase)) return i;
                }
            }
            for (int i = 0; i < _displays.Count; i++)
            {
                if (_displays[i].Primary) return i;
            }
            return _displays.Count > 0 ? 0 : -1;
        }

        /// <summary>The gap in the display's own pixels: the setting is in logical ones.</summary>
        int SpacingPx(Display d)
        {
            return (int)Math.Round(_config.SnapSpacing * d.Scale);
        }

        /// <summary>How tall row <paramref name="i"/> of the snapping card is, in logical pixels.</summary>
        static int RowHeight(int i)
        {
            return i < RowDescriptions.Length ? RowH : LineRowH;
        }

        /// <summary>Where row <paramref name="i"/> starts, in logical pixels down from the card's top; past the last, the card's height.</summary>
        static int RowTop(int i)
        {
            int top = 0;
            for (int j = 0; j < i; j++) top += RowHeight(j);
            return top;
        }

        // ---------- behaviour ----------

        /// <summary>
        /// Puts the selected display's active size in the editor, and all its
        /// sizes in the strip. A copy in the editor: it changes the layout in
        /// place, and nothing should reach the config but a <see cref="Commit"/>.
        /// </summary>
        void LoadDisplay()
        {
            Display d = _displayPicker.Selected;
            if (d == null) return;

            _layouts = _config.LayoutsFor(d);
            _layout = _layouts.Current.Clone();
            _editor.SetLayout(_layout, d.Work.Size, SpacingPx(d));
            float aspect = d.Work.Width / (float)d.Work.Height;
            _layoutPicker.Aspect = aspect;
            _layoutPicker.SelectedIndex = _layout.PresetIndex();
            _sizeStrip.Sizes = _layouts.Sizes;
            _sizeStrip.Active = _layouts.Active;
            _sizeStrip.Aspect = aspect;
            Arrange();      // the strip is as wide as its sizes
            Invalidate();
        }

        /// <summary>Saves the layout being edited as the selected display's active size.</summary>
        void Commit()
        {
            Display d = _displayPicker.Selected;
            if (d == null || _layout == null) return;

            // Into the display's active size, keeping its other sizes. A display
            // still on its default has no stored sizes until now, so the strip
            // is handed the ones stored.
            DisplayLayouts layouts = _config.LayoutsFor(d);
            layouts.Sizes[layouts.Active] = _layout.Clone();
            _config.Keep(d, layouts);
            _config.Save();
            _layouts = layouts;
            _sizeStrip.Sizes = layouts.Sizes;
            _sizeStrip.Active = layouts.Active;
            _layoutPicker.SelectedIndex = _layout.PresetIndex();
            Invalidate();
        }

        /// <summary>
        /// Shows the layout on the display itself for a moment, which is what
        /// tells "Display 2" apart from "Display 3" on a desk of three.
        /// </summary>
        void Flash(int ms)
        {
            Display d = _displayPicker.Selected;
            if (d == null || _layout == null) return;

            if (_overlay == null) _overlay = new ZoneOverlay();
            _overlay.Flash(d.Work, _layout.ZonesIn(d.Work, SpacingPx(d)), d.Scale, ms);
        }

        /// <summary>Every display gets this one's sizes, all of them, with the same one active.</summary>
        void UseOnAll()
        {
            if (_layouts == null) return;

            foreach (Display d in _displays) _config.Keep(d, _layouts.Clone());
            _config.Save();

            // Nothing on screen changes and the menu it came from has closed,
            // so the subtitle says it worked, for a moment.
            _copied = true;
            Invalidate();

            if (_applied == null)
            {
                _applied = new Timer();
                _applied.Interval = 2000;
                _applied.Tick += delegate
                {
                    _applied.Stop();
                    _copied = false;
                    Invalidate();
                };
            }
            _applied.Stop();
            _applied.Start();
        }

        /// <summary>The settings under the first only mean anything while snapping is on.</summary>
        void UpdateEnabled()
        {
            _shiftToggle.Enabled = _config.SnapEnabled;
            _spacing.Enabled = _config.SnapEnabled;
            foreach (ShortcutBox box in _boxes) box.Enabled = _config.SnapEnabled;
            Invalidate();
        }

        /// <summary>Whether a shortcut is being pressed into any of the boxes.</summary>
        bool AnyRecording()
        {
            foreach (ShortcutBox box in _boxes)
            {
                if (box.Recording) return true;
            }
            return false;
        }

        /// <summary>
        /// A box started or stopped recording. While any is being pressed
        /// in, the old shortcuts must reach it instead of moving a window or
        /// flipping a size; afterwards they are whatever the settings say,
        /// which may now be taken or free.
        /// </summary>
        void ShortcutRecording()
        {
            if (AnyRecording()) Hotkey.Suspend();
            else Snapper.ApplyHotkey();
            Arrange();
            Invalidate();
        }

        /// <summary>A box has a new shortcut, already in the config: saved, then taken up.</summary>
        void ShortcutSet()
        {
            _config.Save();
            Snapper.ApplyHotkey();
            Arrange();
            Invalidate();
        }

        /// <summary>A display came or went: list them again, staying on the same one if it is still there.</summary>
        void ReloadDisplays()
        {
            _displays = Displays.All();
            _displayPicker.Displays = _displays;
            LoadDisplay();
            Arrange();
        }

        // ---------- layout ----------

        void ApplyScale()
        {
            _snapToggle.Scale = _s;
            _shiftToggle.Scale = _s;
            _spacing.Scale = _s;
            foreach (ShortcutBox box in _boxes) box.Scale = _s;
            _displayPicker.Scale = _s;
            _layoutPicker.Scale = _s;
            _sizeStrip.Scale = _s;
            _showButton.Scale = _s;
            _moreButton.Scale = _s;
            _editor.Scale = _s;
            _minimizeButton.Scale = _s;
            _closeButton.Scale = _s;
        }

        /// <summary>
        /// Places every child control, in logical pixels, at the current scale
        /// and for the window's current size: the left column is fixed, the
        /// right one takes whatever width and height are left.
        /// </summary>
        void Arrange()
        {
            float width = ClientSize.Width / _s;
            float right = width - 24;                       // the right column's edge
            float bottom = ClientSize.Height / _s - 24;     // the bottom margin
            float column = right - RightX;                     // the right column's width

            // The caption buttons in the window's top-right corner, a pixel
            // down so the edge above them still resizes the window.
            _minimizeButton.Bounds = Logical(width - 92, 1, 46, HeaderH - 1);
            _closeButton.Bounds = Logical(width - 46, 1, 46, HeaderH - 1);

            // The left column: the displays as tall as their rows, but never
            // so tall that the snapping card under them ends lower than 20
            // above the bottom margin, the room its footnote needs (two
            // headings of 28 above the card).
            Rectangle list = Logical(24, 84, LeftW, 0);
            list.Height = Math.Min(_displayPicker.PreferredHeight(), R(bottom - 20 - RowTop(RowTitles.Length) - 2 * 28) - list.Top);
            _displayPicker.Bounds = list;
            _snappingY = list.Bottom / _s + 28;

            // Each control right-aligned 16 in from the card's edge and
            // centred in its row.
            float cardTop = _snappingY + 28;
            _snapToggle.Bounds = Logical(CardRight - 40, cardTop + RowTop(0) + (RowHeight(0) - 32) / 2f, 40, 32);
            _shiftToggle.Bounds = Logical(CardRight - 40, cardTop + RowTop(1) + (RowHeight(1) - 32) / 2f, 40, 32);
            _spacing.Bounds = Logical(CardRight - 112, cardTop + RowTop(2) + (RowHeight(2) - 32) / 2f, 112, 32);

            // The shortcuts as wide as their keys, up to a maximum that also
            // holds the box while it sizes itself to the keys held as one is
            // recorded.
            for (int i = 0; i < _boxes.Length; i++)
            {
                int row = FirstShortcutRow + i;
                Rectangle box = Logical(CardRight, cardTop + RowTop(row) + (RowHeight(row) - 32) / 2f, 0, 32);
                box.Width = Math.Min(_boxes[i].PreferredWidth(), R(ShortcutMaxW));
                box.X -= box.Width;
                _boxes[i].MaximumSize = new Size(R(ShortcutMaxW), 0);
                _boxes[i].Bounds = box;
            }

            // The page's buttons, level with its title, right to left from the
            // column's edge: "More" square with only its glyph, then "Show on
            // screen" 8 to its left.
            _moreButton.Bounds = Logical(right - 32, 56, 32, 32);
            int show = _showButton.PreferredWidth();
            _showButton.Bounds = new Rectangle(_moreButton.Left - R(8) - show, R(56), show, R(88) - R(56));

            // The tiles grow with the column, up to 120 each; past that the
            // row stops widening and keeps to the column's left edge.
            int n = ZoneLayout.PresetNames.Length;
            _layoutPicker.Bounds = Logical(RightX, 156, Math.Min(column, n * 120 + (n - 1) * 8), 88);

            // The display's sizes, on the "Zones" heading's line, out to the
            // column's edge: as wide as however many sizes there are.
            int strip = _sizeStrip.PreferredWidth();
            _sizeStrip.Bounds = Rectangle.FromLTRB(R(right) - strip, R(256), R(right), R(256 + 32));

            // Inside the card the form paints for it, from 300 to the bottom
            // margin: 12 in from its sides and top, the hint in the 26 left
            // under it.
            _editor.Bounds = Logical(RightX + 12, 312, column - 24, bottom - 26 - 312);
        }

        // ---------- painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            PaintHeader(g);

            Font heading = Theme.Strong(14 * _s);

            DrawLabel(g, "Displays", heading, Theme.Text, R(24), R(56));
            DrawLabel(g, "Snapping", heading, Theme.Text, R(24), R(_snappingY));
            PaintOptions(g);

            PaintPageTitle(g);

            DrawLabel(g, "Layouts", heading, Theme.Text, R(RightX), R(128));

            DrawLabel(g, "Zones", heading, Theme.Text, R(RightX), R(264));

            // The zone editor's card, the right column's width and down to the
            // bottom margin: the editor itself sits inside it, and the hint
            // under the editor, where the clicks it describes happen.
            float right = ClientSize.Width / _s - 24;
            float bottom = ClientSize.Height / _s - 24;
            Rectangle card = Logical(RightX, 300, right - RightX, bottom - 300);
            DrawCard(g, card);
            using (SolidBrush b = new SolidBrush(Theme.TextDim))
            using (StringFormat f = new StringFormat())
            {
                f.Alignment = StringAlignment.Center;
                f.LineAlignment = StringAlignment.Center;
                f.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(Hint, Theme.Font(12 * _s), b, RectangleF.FromLTRB(card.Left, _editor.Bottom, card.Right, card.Bottom), f);
            }
        }

        /// <summary>
        /// The title bar: the Zones mark and the window's name on the window's
        /// own colour, with no line under it. The caption buttons are controls.
        /// </summary>
        void PaintHeader(Graphics g)
        {
            Logo.Draw(g, new RectangleF(R(14), R(12), R(16), R(16)));
            using (SolidBrush b = new SolidBrush(Theme.Text))
            using (StringFormat f = new StringFormat())
            {
                f.LineAlignment = StringAlignment.Center;
                f.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(Text, Theme.Font(12 * _s), b, new RectangleF(R(40), 0, _minimizeButton.Left - R(40), R(HeaderH)), f);
            }
        }

        /// <summary>
        /// The selected display's name, and under it its size and whether it
        /// is the primary: what the right column is about. Which layout and
        /// which size it has are the selected tile and the lit size already.
        /// For a moment after "Use on all displays", the line under the name
        /// says it was done instead.
        /// </summary>
        void PaintPageTitle(Graphics g)
        {
            Display d = _displayPicker.Selected;
            if (d == null) return;

            // GDI+ pads text by a fraction of its size, so at 24px the title
            // would start visibly right of the 13px line under it. Pulled back
            // by the difference, the two share a left edge.
            DrawLabel(g, d.Number > 0 ? "Display " + d.Number : "Display", Theme.Strong(24 * _s), Theme.Text, R(RightX) - 3 * _s, R(50));

            if (_copied)
            {
                DrawLabel(g, CopiedText, Theme.Font(13 * _s), Theme.Accent, R(RightX), R(86));
                return;
            }
            string detail = d.Bounds.Width + " × " + d.Bounds.Height + (d.Primary ? "  ·  Primary" : "");
            DrawLabel(g, detail, Theme.Font(13 * _s), Theme.TextDim, R(RightX), R(86));
        }

        /// <summary>
        /// The snapping card: its plate, the lines between rows, and each
        /// row's words; under it, only when there is something wrong to say,
        /// which shortcuts are taken.
        /// </summary>
        void PaintOptions(Graphics g)
        {
            float cardTop = _snappingY + 28;
            Rectangle card = Logical(24, cardTop, LeftW, RowTop(RowTitles.Length));
            DrawCard(g, card);

            Control[] controls = { _snapToggle, _shiftToggle, _spacing, _zoneBox, _zoneBackBox, _sizeBox, _sizeBackBox };
            Font title = Theme.Font(14 * _s);
            Font description = Theme.Font(12 * _s);
            float titleH = title.GetHeight(g);
            float descriptionH = description.GetHeight(g);

            for (int i = 0; i < controls.Length; i++)
            {
                float top = cardTop + RowTop(i);
                if (i > 0)
                {
                    using (SolidBrush b = new SolidBrush(Theme.Divider))
                        g.FillRectangle(b, R(24 + 16), R(top), R(CardRight) - R(24 + 16), 1);
                }

                // Under a switched-off "Snap windows", the rows that depend on it fade.
                bool faint = i > 0 && !_config.SnapEnabled;
                float x = card.Left + R(16);

                if (i >= RowDescriptions.Length)
                {
                    // The title alone, centred in its row.
                    using (SolidBrush b = new SolidBrush(faint ? Theme.TextFaint : Theme.Text))
                    using (StringFormat f = new StringFormat())
                    {
                        f.LineAlignment = StringAlignment.Center;
                        f.FormatFlags = StringFormatFlags.NoWrap;
                        g.DrawString(RowTitles[i], title, b, new RectangleF(x, R(top), controls[i].Left - x, R(top + RowHeight(i)) - R(top)), f);
                    }
                    continue;
                }

                // The title and its description as one block, centred in the row.
                float y = R(top) + (R(RowHeight(i)) - titleH - descriptionH) / 2;
                DrawLabel(g, RowTitles[i], title, faint ? Theme.TextFaint : Theme.Text, x, y);
                using (SolidBrush b = new SolidBrush(faint ? Theme.TextFaint : Theme.TextDim))
                using (StringFormat f = new StringFormat())
                {
                    // One line, cut short rather than wrapped under the control.
                    // The box stops only 4 short of it: DrawString pads the
                    // words inside their box, so they still end a good 12 clear,
                    // where a box 12 short cut "into zones" to an ellipsis.
                    f.FormatFlags = StringFormatFlags.NoWrap;
                    f.Trimming = StringTrimming.EllipsisCharacter;
                    float width = controls[i].Left - R(4) - x;
                    g.DrawString(RowDescriptions[i], description, b, new RectangleF(x, y + titleH, width, descriptionH), f);
                }
            }

            // Windows turned a shortcut down because another program has it,
            // so it does nothing until it is changed: the card says which,
            // under it. Not while a new one is being pressed in, when the old
            // one is let go anyway.
            string taken = null;
            int count = 0;
            for (int i = 0; i < _boxes.Length; i++)
            {
                if (_boxes[i].Recording || !Hotkey.IsFailed(BoxSlots[i])) continue;
                taken = RowTitles[FirstShortcutRow + i];
                count++;
            }
            if (count == 0) return;

            using (SolidBrush b = new SolidBrush(Theme.Danger))
            using (StringFormat f = new StringFormat())
            {
                f.FormatFlags = StringFormatFlags.NoWrap;
                f.Trimming = StringTrimming.EllipsisCharacter;
                string note = count == 1 ? taken + " shortcut is already in use" : count + " shortcuts are already in use";
                g.DrawString(note, description, b, new RectangleF(card.Left, card.Bottom + R(8), card.Width, descriptionH), f);
            }
        }

        /// <summary>A card raised off the window: rounded, filled, with a hairline edge.</summary>
        void DrawCard(Graphics g, RectangleF r)
        {
            r.Inflate(-0.5f, -0.5f);
            using (GraphicsPath p = Theme.Rounded(r, 8 * _s))
            {
                using (SolidBrush b = new SolidBrush(Theme.CardBack))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.CardStroke, 1f))
                    g.DrawPath(pen, p);
            }
        }

        static void DrawLabel(Graphics g, string text, Font font, Color color, float x, float y)
        {
            using (SolidBrush b = new SolidBrush(color))
                g.DrawString(text, font, b, x, y);
        }

        // ---------- window ----------

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // Borderless, but with a system menu and a minimize box all the
                // same: without them, clicking the taskbar button would not
                // minimize it.
                cp.Style |= WS_MINIMIZEBOX | WS_SYSMENU;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.RoundCorners(Handle);
        }

        /// <summary>The window was resized, by its edges or its new scale: everything follows its size.</summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Minimized, the client area is empty: nothing to lay out until it comes back.
            if (WindowState == FormWindowState.Minimized) return;
            Arrange();
            Invalidate();
        }

        /// <summary>
        /// The resize edge under <paramref name="p"/>, in client coordinates,
        /// as a hit-test code, or 0 inside them. Borderless, the window has no
        /// frame to grab, so a band 6 in along each side stands in for one,
        /// with a 12 square at each corner.
        /// </summary>
        int EdgeAt(Point p)
        {
            int band = R(6);
            int corner = R(12);
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (p.X < corner && p.Y < corner) return HTTOPLEFT;
            if (p.X >= w - corner && p.Y < corner) return HTTOPRIGHT;
            if (p.X < corner && p.Y >= h - corner) return HTBOTTOMLEFT;
            if (p.X >= w - corner && p.Y >= h - corner) return HTBOTTOMRIGHT;
            if (p.X < band) return HTLEFT;
            if (p.X >= w - band) return HTRIGHT;
            if (p.Y < band) return HTTOP;
            if (p.Y >= h - band) return HTBOTTOM;
            return 0;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DPICHANGED)
            {
                // Dragged onto a monitor with another scale: take the place
                // and size Windows suggests, then lay out at the new scale.
                // Borderless, the window is all client, so one rectangle does.
                _s = (m.WParam.ToInt32() & 0xFFFF) / 96f;
                RECT r = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                Bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                ApplyScale();
                Arrange();
                Invalidate(true);
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WM_NCLBUTTONDBLCLK)
            {
                return;     // the header is a title bar, but there is nothing to maximize to
            }

            base.WndProc(ref m);

            if (m.Msg == WM_NCHITTEST && m.Result.ToInt32() == HTCLIENT)
            {
                // The edges resize the window, and the header moves it. The
                // caption buttons are child controls, so they answer their own
                // hit tests and never get here; the pixel above them does.
                // As a 64-bit value: on a display above or left of the main
                // one the packed coordinates do not fit an int, and ToInt32
                // throws.
                long lp = m.LParam.ToInt64();
                Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                int edge = EdgeAt(p);
                if (edge != 0) m.Result = new IntPtr(edge);
                else if (p.Y < R(HeaderH)) m.Result = new IntPtr(HTCAPTION);
            }
            else if (m.Msg == WM_GETMINMAXINFO)
            {
                // No smaller than the page is laid out for, at the scale it
                // is at; the window is all client, so that is its size.
                MINMAXINFO info = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
                info.MinTrackSize.X = R(MinW);
                info.MinTrackSize.Y = R(MinH);
                Marshal.StructureToPtr(info, m.LParam, false);
            }
            else if (m.Msg == WM_SETTINGCHANGE || m.Msg == WM_THEMECHANGED)
            {
                Theme.Refresh();
                BackColor = Theme.WindowBack;
                Invalidate(true);
            }
            else if (m.Msg == WM_DISPLAYCHANGE)
            {
                ReloadDisplays();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// The size shortcuts can flip sizes with this window in front, when no
        /// Activated comes. The engine handled the press first (it subscribed
        /// first), so reloading now shows the size it switched to, and the
        /// editor never commits its copy of the old size into the new one.
        /// </summary>
        void OnHotkey(int slot, int step)
        {
            if ((slot == Hotkey.SlotSize || slot == Hotkey.SlotSizeBack) && !IsDisposed) LoadDisplay();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            Hotkey.Pressed -= OnHotkey;
            if (_open == this) _open = null;
            // Closed mid-recording, by Zones quitting say: the shortcuts were
            // let go for the recording, so they are taken back.
            if (AnyRecording()) Snapper.ApplyHotkey();
            if (_overlay != null)
            {
                _overlay.Dispose();
                _overlay = null;
            }
            if (_applied != null)
            {
                _applied.Dispose();
                _applied = null;
            }
            _moreMenu.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct MINMAXINFO { public POINT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    }
}
