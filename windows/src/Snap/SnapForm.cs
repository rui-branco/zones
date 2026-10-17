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
    /// The settings window: a menu down the left, and beside it the page
    /// chosen from it. Layouts has the displays as tabs and the layout of
    /// whichever is picked, chosen from the presets or drawn by hand, with
    /// its sizes; Snapping, Shortcuts and General are cards of settings.
    ///
    /// A real window with a taskbar button, though Zones otherwise keeps to
    /// the notification area, because this is somewhere people stay a while
    /// and come back to, and it is laid out and coloured like a Windows 11
    /// Settings page so it reads as part of the system it changes. The title
    /// bar is its own: the window's colour with no line under it, so the
    /// header is flat with the page instead of a system caption stacked on
    /// top. Every edit is saved as it is made; there is no OK button to forget.
    ///
    /// All the static parts (the header, titles, cards and their words) are
    /// painted here; the parts that take input are child controls, and only
    /// the open page's are shown. Both are placed from one scale and the
    /// window's size, so moving to a monitor with another DPI, dragging an
    /// edge or opening another page only needs <see cref="Arrange"/> again.
    /// The menu keeps its width; the page takes the rest.
    /// </summary>
    public class SnapForm : Form
    {
        const int WS_MINIMIZEBOX = 0x00020000;
        const int WS_THICKFRAME = 0x00040000;
        const int WS_SYSMENU = 0x00080000;
        const int WM_SETTINGCHANGE = 0x001A;
        const int WM_GETMINMAXINFO = 0x0024;
        const int WM_DISPLAYCHANGE = 0x007E;
        const int WM_NCCALCSIZE = 0x0083;
        const int WM_NCHITTEST = 0x0084;
        const int WM_NCACTIVATE = 0x0086;
        const int WM_NCLBUTTONDBLCLK = 0x00A3;
        const int WM_NCUAHDRAWCAPTION = 0x00AE;
        const int WM_NCUAHDRAWFRAME = 0x00AF;
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
        const int SM_CXSIZEFRAME = 32;
        const int SM_CYSIZEFRAME = 33;
        const int SM_CXPADDEDBORDER = 92;

        const string Hint = "Click to split  ·  Drag a line to resize  ·  Right-click for more";
        const string CopiedText = "Copied to every display";

        // In logical pixels: the size the window opens at, room permitting,
        // and the smallest it can be made, below which the layout editor
        // gets too small to draw in.
        const int OpenW = 1200;
        const int OpenH = 680;
        const int MinW = 1000;
        const int MinH = 640;

        // In logical pixels: the header band the window is dragged by; the
        // menu's column, and where the page beside it starts.
        const int HeaderH = 40;
        const int RailW = 232;
        const int PageX = RailW + 24;

        // In logical pixels: where a settings page's first card starts, under
        // its title; a card's row, tall enough for a title and one line of
        // description beside the control; and the widest a card gets, past
        // which its rows would only grow emptier.
        const int CardsTop = 104;
        const int RowH = 64;
        const int MaxCardW = 1000;

        /// <summary>
        /// The widest a shortcut box gets, in logical pixels: room for four
        /// keys, and to grow into while one is being pressed in.
        /// </summary>
        const int ShortcutMaxW = 240;

        // The pages, in the menu's order.
        const int PageLayouts = 0;
        const int PageSnapping = 1;
        const int PageShortcuts = 2;
        const int PageGeneral = 3;
        static readonly string[] PageNames = { "Layouts", "Snapping", "Shortcuts", "General" };
        static readonly int[] PageGlyphs = { 0xF246, 0xE7C2, 0xE765, 0xE713 };

        /// <summary>The Hotkey slot each shortcut box sets, in the order of the boxes.</summary>
        static readonly int[] BoxSlots = { Hotkey.SlotZone, Hotkey.SlotZoneBack, Hotkey.SlotSize, Hotkey.SlotSizeBack };
        static readonly string[] BoxTitles = { "Next zone", "Previous zone", "Next size", "Previous size" };
        static readonly string[] BoxDescriptions =
        {
            "Moves the active window to its next zone",
            "Moves it back to the zone before",
            "Switches the display to its next size, its windows with it",
            "Switches back to the size before"
        };

        static SnapForm _open;

        readonly AppConfig _config;
        float _s = 1f;
        int _page = PageLayouts;
        List<Display> _displays = new List<Display>();
        DisplayLayouts _layouts;    // the selected display's sizes, as the config has them
        ZoneLayout _layout;         // a copy of the active one, which the editor changes
        ZoneOverlay _overlay;   // made on the first flash: most visits never need it
        Timer _applied;         // takes "Copied to every display" away again
        bool _copied;           // the Layouts page says the layout was copied, for a moment

        readonly NavList _nav = new NavList();
        readonly FlatButton _railUpdateButton = new FlatButton();
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
        readonly FlatButton _updateButton = new FlatButton();
        readonly FlatButton _showButton = new FlatButton();
        readonly FlatButton _moreButton = new FlatButton();
        readonly ContextMenuStrip _moreMenu = new ContextMenuStrip();
        readonly ToolStripMenuItem _allItem;
        readonly ZoneEditor _editor = new ZoneEditor();
        readonly ToggleSwitch _autoUpdateToggle = new ToggleSwitch();
        readonly ToggleSwitch _startupToggle = new ToggleSwitch();
        readonly CaptionButton _minimizeButton = new CaptionButton();
        readonly CaptionButton _closeButton = new CaptionButton();

        /// <summary>One row of a settings card: what it is, what it does, and the control that sets it.</summary>
        sealed class Row
        {
            public readonly string Title;
            public readonly string Description;
            public readonly Control Control;
            /// <summary>Greyed out: the setting means nothing while snapping is off.</summary>
            public readonly bool Faint;

            public Row(string title, string description, Control control, bool faint)
            {
                Title = title;
                Description = description;
                Control = control;
                Faint = faint;
            }
        }

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
            // Size, not ClientSize: the window is all client (see
            // WM_NCCALCSIZE), but WinForms works a client size out from the
            // style, and would add the sizing frame nobody sees to it.
            Size = new Size(Math.Min(R(OpenW), (int)(area.Width * 0.92)), Math.Min(R(OpenH), (int)(area.Height * 0.92)));
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);

            for (int i = 0; i < PageNames.Length; i++) _nav.Add(PageNames[i], PageGlyphs[i]);
            _nav.SelectedChanged += delegate { ShowPage(_nav.SelectedIndex); };

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

            // On the General page: a check asked for, which says what it
            // found, or once a newer Zones is known, the accent button that
            // installs it. The tray menu's item does the same, and so does
            // the one at the foot of the menu, there on every page once an
            // update is found.
            _updateButton.OnCard = true;
            _updateButton.Click += delegate
            {
                if (Updater.Available != null) Updater.StartUpdate();
                else Updater.CheckForUpdates(true);
            };
            _railUpdateButton.Accent = true;
            _railUpdateButton.Glyph = 0xE896;
            _railUpdateButton.Click += delegate { Updater.StartUpdate(); };
            Updater.UpdateStateChanged += OnUpdateStateChanged;

            // The two settings the tray menu also has, which go through it so
            // the menu's checkmarks and these switches never disagree. Set
            // from the menu, a switch is only following: already done.
            _autoUpdateToggle.OnCard = true;
            _autoUpdateToggle.ShowLabel = false;
            _autoUpdateToggle.Checked = _config.AutoUpdate;
            _autoUpdateToggle.CheckedChanged += delegate
            {
                if (_autoUpdateToggle.Checked != _config.AutoUpdate) Tray.SetAutoUpdate(_autoUpdateToggle.Checked);
            };

            _startupToggle.OnCard = true;
            _startupToggle.ShowLabel = false;
            _startupToggle.Checked = _config.StartWithWindows;
            _startupToggle.CheckedChanged += delegate
            {
                if (_startupToggle.Checked != _config.StartWithWindows) Tray.SetStartWithWindows(_startupToggle.Checked);
            };
            Tray.SettingsChanged += OnTraySettingsChanged;

            _showButton.Text = "Show on screen";
            _showButton.Glyph = 0xE7F4;
            _showButton.Click += delegate { Flash(1500); };

            // The actions wanted now and then, behind one button so the
            // Layouts page leads with the displays and the one it is mostly
            // used for.
            Theme.StyleMenu(_moreMenu);
            _allItem = Theme.MenuItem("Use on all displays", 0xE8A9);
            _allItem.Click += delegate { UseOnAll(); };
            ToolStripMenuItem resetItem = Theme.MenuItem("Reset this display", 0xE72C);
            resetItem.Click += delegate
            {
                Display d = _displayPicker.Selected;
                if (d == null) return;

                // Its sizes may be an afternoon's work, and one stray click
                // in the menu must not take them.
                int sizes = _config.LayoutsFor(d).Sizes.Count;
                string name = d.Number > 0 ? "Display " + d.Number : "this display";
                string lost = sizes == 1 ? "Its layout goes" : "Its layout and all " + sizes + " sizes go";
                if (MessageBox.Show(this, "Reset " + name + "?\n\n" + lost + " back to the default. This can't be undone.",
                        "Zones", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;

                _config.Forget(d);
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

            // In tab order, the menu, then the pages in its order; the
            // caption buttons are not tab stops.
            Controls.Add(_nav);
            Controls.Add(_displayPicker);
            Controls.Add(_showButton);
            Controls.Add(_moreButton);
            Controls.Add(_layoutPicker);
            Controls.Add(_sizeStrip);
            Controls.Add(_editor);
            Controls.Add(_snapToggle);
            Controls.Add(_shiftToggle);
            Controls.Add(_spacing);
            Controls.Add(_zoneBox);
            Controls.Add(_zoneBackBox);
            Controls.Add(_sizeBox);
            Controls.Add(_sizeBackBox);
            Controls.Add(_startupToggle);
            Controls.Add(_autoUpdateToggle);
            Controls.Add(_updateButton);
            Controls.Add(_railUpdateButton);
            Controls.Add(_minimizeButton);
            Controls.Add(_closeButton);

            ApplyScale();
            UpdateEnabled();
            ShowUpdate();
            LoadDisplay();
            ShowPage(PageLayouts);
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

        // ---------- pages ----------

        /// <summary>
        /// A settings page's cards, top to bottom, each a list of rows. The
        /// Layouts page has none: it is the editor's.
        /// </summary>
        List<Row[]> Cards(int page)
        {
            List<Row[]> cards = new List<Row[]>();
            bool on = _config.SnapEnabled;
            if (page == PageSnapping)
            {
                cards.Add(new Row[]
                {
                    new Row("Snap windows", "Drop a dragged window into a zone", _snapToggle, false),
                    new Row("Hold Shift to snap", "Zones show only while Shift is held. Off, every drag shows them", _shiftToggle, !on),
                    new Row("Spacing", "The gap around each zone", _spacing, !on)
                });
            }
            else if (page == PageShortcuts)
            {
                Row[] rows = new Row[_boxes.Length];
                for (int i = 0; i < _boxes.Length; i++) rows[i] = new Row(BoxTitles[i], BoxDescriptions[i], _boxes[i], !on);
                cards.Add(rows);
            }
            else if (page == PageGeneral)
            {
                cards.Add(new Row[]
                {
                    new Row("Start with Windows", "Opens Zones in the notification area when you sign in", _startupToggle, false),
                    new Row("Auto-update", "Installs new versions by itself while Zones is idle", _autoUpdateToggle, false)
                });
                cards.Add(new Row[] { new Row("Version " + Updater.CurrentText, UpdateStatus(), _updateButton, false) });
            }
            return cards;
        }

        /// <summary>The child controls that belong to a page, shown only while it is open.</summary>
        List<Control> ControlsOf(int page)
        {
            List<Control> controls = new List<Control>();
            if (page == PageLayouts)
            {
                controls.AddRange(new Control[] { _displayPicker, _showButton, _moreButton, _layoutPicker, _sizeStrip, _editor });
                return controls;
            }
            foreach (Row[] card in Cards(page))
            {
                foreach (Row row in card) controls.Add(row.Control);
            }
            return controls;
        }

        /// <summary>Opens a page: its controls appear, every other page's go, and it is laid out and painted.</summary>
        void ShowPage(int page)
        {
            _page = page;
            for (int p = 0; p < PageNames.Length; p++)
            {
                foreach (Control c in ControlsOf(p)) c.Visible = p == page;
            }
            if (_nav.SelectedIndex != page) _nav.SelectedIndex = page;
            Arrange();
            Invalidate();
        }

        /// <summary>What the updater is doing, or found, in words: the version row's description.</summary>
        static string UpdateStatus()
        {
            ReleaseInfo update = Updater.Available;
            if (Updater.Updating && update != null) return "Installing Zones " + Updater.VersionText(update.Version) + "…";
            if (Updater.Checking) return "Checking for updates…";
            if (update != null) return "Zones " + Updater.VersionText(update.Version) + " is available";
            return "Zones checks for updates every few hours";
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
            // so the page says it worked, for a moment.
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

        /// <summary>
        /// The update buttons follow the updater, as the tray menu's item
        /// does. On the General page: a check to ask for, the check under
        /// way, the newer Zones it found in the accent, and that being
        /// installed. At the foot of the menu, only the last two.
        /// </summary>
        void ShowUpdate()
        {
            ReleaseInfo update = Updater.Available;
            bool found = update != null && !Updater.Checking;
            _updateButton.Enabled = !Updater.Checking && !Updater.Updating;
            _updateButton.Accent = found;
            _updateButton.Glyph = found ? 0xE896 : 0xE895;
            if (Updater.Updating) _updateButton.Text = "Updating…";
            else if (Updater.Checking) _updateButton.Text = "Checking…";
            else if (update != null) _updateButton.Text = "Update to " + Updater.VersionText(update.Version);
            else _updateButton.Text = "Check for updates";

            _railUpdateButton.Visible = found || Updater.Updating;
            _railUpdateButton.Enabled = !Updater.Updating;
            _railUpdateButton.Text = Updater.Updating ? "Updating…"
                : update != null ? "Update to " + Updater.VersionText(update.Version) : "";

            Arrange();      // the buttons are as wide as their words,
            Invalidate();   // and the version row says what is going on
        }

        void OnUpdateStateChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) ShowUpdate();
        }

        /// <summary>A setting changed from the tray menu: the switch for it follows.</summary>
        void OnTraySettingsChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            _autoUpdateToggle.Checked = _config.AutoUpdate;
            _startupToggle.Checked = _config.StartWithWindows;
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
            _nav.Scale = _s;
            _railUpdateButton.Scale = _s;
            _snapToggle.Scale = _s;
            _shiftToggle.Scale = _s;
            _spacing.Scale = _s;
            foreach (ShortcutBox box in _boxes) box.Scale = _s;
            _displayPicker.Scale = _s;
            _layoutPicker.Scale = _s;
            _sizeStrip.Scale = _s;
            _updateButton.Scale = _s;
            _showButton.Scale = _s;
            _moreButton.Scale = _s;
            _editor.Scale = _s;
            _autoUpdateToggle.Scale = _s;
            _startupToggle.Scale = _s;
            _minimizeButton.Scale = _s;
            _closeButton.Scale = _s;
        }

        /// <summary>The width of a settings card, in logical pixels: the page's, up to the widest a card gets.</summary>
        float CardWidth()
        {
            return Math.Min(ClientSize.Width / _s - 24 - PageX, MaxCardW);
        }

        /// <summary>
        /// Places every child control, in logical pixels, at the current scale
        /// and for the window's current size: the menu is fixed, the page
        /// takes whatever width and height are left. Every page is laid out,
        /// not only the open one, so opening another only needs showing it.
        /// </summary>
        void Arrange()
        {
            float width = ClientSize.Width / _s;
            float right = width - 24;                       // the page's right edge
            float bottom = ClientSize.Height / _s - 24;     // the bottom margin
            float column = right - PageX;                   // the page's width

            // The caption buttons in the window's top-right corner, a pixel
            // down so the edge above them still resizes the window.
            _minimizeButton.Bounds = Logical(width - 92, 1, 46, HeaderH - 1);
            _closeButton.Bounds = Logical(width - 46, 1, 46, HeaderH - 1);

            // The menu under the title bar, and at its foot, over the version
            // (see PaintRail), the update once one is found.
            Rectangle nav = Logical(12, 52, RailW - 24, 0);
            nav.Height = _nav.PreferredHeight();
            _nav.Bounds = nav;
            int update = Math.Min(_railUpdateButton.PreferredWidth(), R(RailW - 48));
            _railUpdateButton.Bounds = new Rectangle(R(24), R(bottom - 16 - 12 - 32), update, R(32));

            // ----- Layouts -----

            // The page's buttons, level with the display tabs, right to left
            // from its edge: "More" square with only its glyph, then "Show on
            // screen" 8 to its left. The tabs have the rest of the line.
            _moreButton.Bounds = Logical(right - 32, 60, 32, 32);
            int show = _showButton.PreferredWidth();
            _showButton.Bounds = new Rectangle(_moreButton.Left - R(8) - show, R(60), show, R(32));
            Rectangle tabs = Logical(PageX, 48, 0, 0);
            tabs.Width = Math.Max(0, Math.Min(_displayPicker.PreferredWidth(), _showButton.Left - R(24) - tabs.Left));
            tabs.Height = _displayPicker.PreferredHeight();
            _displayPicker.Bounds = tabs;

            // The tiles grow with the page, up to 120 each; past that the
            // row stops widening and keeps to the page's left edge.
            int n = ZoneLayout.PresetNames.Length;
            _layoutPicker.Bounds = Logical(PageX, 156, Math.Min(column, n * 120 + (n - 1) * 8), 88);

            // The display's sizes, on the "Zones" heading's line, out to the
            // page's edge: as wide as however many sizes there are.
            int strip = _sizeStrip.PreferredWidth();
            _sizeStrip.Bounds = Rectangle.FromLTRB(R(right) - strip, R(256), R(right), R(256 + 32));

            // Inside the card the form paints for it, from 300 to the bottom
            // margin: 12 in from its sides and top, the hint in the 26 left
            // under it.
            _editor.Bounds = Logical(PageX + 12, 312, column - 24, bottom - 26 - 312);

            // ----- the settings pages -----

            // Each row's control right-aligned 16 in from its card's edge and
            // centred in the row; the cards 16 apart.
            float cardRight = PageX + CardWidth() - 16;
            for (int page = PageSnapping; page < PageNames.Length; page++)
            {
                float top = CardsTop;
                foreach (Row[] card in Cards(page))
                {
                    for (int i = 0; i < card.Length; i++)
                    {
                        Control c = card[i].Control;
                        float rowTop = top + i * RowH;
                        Rectangle r = Logical(cardRight, rowTop + (RowH - 32) / 2f, 0, 32);
                        r.Width = ControlWidth(c);
                        r.X -= r.Width;
                        c.Bounds = r;
                    }
                    top += card.Length * RowH + 16;
                }
            }
        }

        /// <summary>How wide a row's control is, in device pixels.</summary>
        int ControlWidth(Control c)
        {
            if (c == _spacing) return R(112);
            ShortcutBox box = c as ShortcutBox;
            if (box != null)
            {
                // As wide as its keys, up to a maximum that also holds the
                // box while it sizes itself to the keys held as one is
                // recorded.
                box.MaximumSize = new Size(R(ShortcutMaxW), 0);
                return Math.Min(box.PreferredWidth(), R(ShortcutMaxW));
            }
            FlatButton button = c as FlatButton;
            if (button != null) return button.PreferredWidth();
            return R(40);       // a switch
        }

        // ---------- painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            PaintRail(g);
            if (_page == PageLayouts) PaintLayouts(g);
            else PaintSettings(g);
        }

        /// <summary>
        /// The menu's column: the Zones mark and the window's name in the
        /// title bar, on the window's own colour with no line under it, and
        /// at the foot the version running. The menu itself, the update
        /// button over the version and the caption buttons are controls.
        /// </summary>
        void PaintRail(Graphics g)
        {
            Logo.Draw(g, new RectangleF(R(14), R(12), R(16), R(16)));
            using (SolidBrush b = new SolidBrush(Theme.Text))
            using (StringFormat f = new StringFormat())
            {
                f.LineAlignment = StringAlignment.Center;
                f.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(Text, Theme.Font(12 * _s), b, new RectangleF(R(40), 0, R(RailW) - R(40), R(HeaderH)), f);
            }

            float bottom = ClientSize.Height / _s - 24;
            DrawLabel(g, "Version " + Updater.CurrentText, Theme.Font(12 * _s), Theme.TextDim, R(24), R(bottom - 16));
        }

        /// <summary>
        /// The Layouts page: the headings over the presets and the sizes, the
        /// zone editor's card with the hint under it, and for a moment after
        /// "Use on all displays", word that it was done. Which display is
        /// picked, and which layout and size it has, are the lit tab, tile
        /// and size already.
        /// </summary>
        void PaintLayouts(Graphics g)
        {
            Font heading = Theme.Strong(14 * _s);
            float right = ClientSize.Width / _s - 24;
            float bottom = ClientSize.Height / _s - 24;

            DrawLabel(g, "Presets", heading, Theme.Text, R(PageX), R(128));
            DrawLabel(g, "Zones", heading, Theme.Text, R(PageX), R(264));

            if (_copied)
            {
                using (SolidBrush b = new SolidBrush(Theme.Accent))
                using (StringFormat f = new StringFormat())
                {
                    f.Alignment = StringAlignment.Far;
                    f.FormatFlags = StringFormatFlags.NoWrap;
                    g.DrawString(CopiedText, Theme.Font(13 * _s), b, RectangleF.FromLTRB(R(PageX), R(129), R(right), R(152)), f);
                }
            }

            // The zone editor's card, the page's width and down to the
            // bottom margin: the editor itself sits inside it, and the hint
            // under the editor, where the clicks it describes happen.
            Rectangle card = Logical(PageX, 300, right - PageX, bottom - 300);
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
        /// A settings page: its title, its cards with the lines between rows
        /// and each row's words beside its control, and under the last card
        /// whatever the page has to add.
        /// </summary>
        void PaintSettings(Graphics g)
        {
            // GDI+ pads text by a fraction of its size, so at 24px the title
            // would start visibly right of the cards under it. Pulled back
            // by the difference, the two share a left edge.
            DrawLabel(g, PageNames[_page], Theme.Strong(24 * _s), Theme.Text, R(PageX) - 3 * _s, R(50));

            Font title = Theme.Font(14 * _s);
            Font description = Theme.Font(12 * _s);
            float titleH = title.GetHeight(g);
            float descriptionH = description.GetHeight(g);
            float cardW = CardWidth();

            float top = CardsTop;
            Rectangle last = Rectangle.Empty;
            foreach (Row[] rows in Cards(_page))
            {
                Rectangle card = Logical(PageX, top, cardW, rows.Length * RowH);
                DrawCard(g, card);
                for (int i = 0; i < rows.Length; i++)
                {
                    Row row = rows[i];
                    float rowTop = top + i * RowH;
                    if (i > 0)
                    {
                        using (SolidBrush b = new SolidBrush(Theme.Divider))
                            g.FillRectangle(b, R(PageX + 16), R(rowTop), R(PageX + cardW - 16) - R(PageX + 16), 1);
                    }

                    // The title and its description as one block, centred in
                    // the row, each one line, cut short rather than wrapped
                    // under the control.
                    float x = card.Left + R(16);
                    float width = Math.Max(0, row.Control.Left - R(16) - x);
                    float y = R(rowTop) + (R(RowH) - titleH - descriptionH) / 2;
                    using (StringFormat f = new StringFormat())
                    {
                        f.FormatFlags = StringFormatFlags.NoWrap;
                        f.Trimming = StringTrimming.EllipsisCharacter;
                        using (SolidBrush b = new SolidBrush(row.Faint ? Theme.TextFaint : Theme.Text))
                            g.DrawString(row.Title, title, b, new RectangleF(x, y, width, titleH), f);
                        using (SolidBrush b = new SolidBrush(row.Faint ? Theme.TextFaint : Theme.TextDim))
                            g.DrawString(row.Description, description, b, new RectangleF(x, y + titleH, width, descriptionH), f);
                    }
                }
                last = card;
                top += rows.Length * RowH + 16;
            }

            Color color;
            string note = Footnote(out color);
            if (note == null || last.IsEmpty) return;
            using (SolidBrush b = new SolidBrush(color))
            using (StringFormat f = new StringFormat())
            {
                f.FormatFlags = StringFormatFlags.NoWrap;
                f.Trimming = StringTrimming.EllipsisCharacter;
                g.DrawString(note, description, b, new RectangleF(last.Left, last.Bottom + R(12), last.Width, descriptionH), f);
            }
        }

        /// <summary>
        /// The line under the open page's cards, if it has one. On Shortcuts,
        /// first that one is taken: Windows turned it down because another
        /// program has it, so it does nothing until it is changed. Not while a
        /// new one is being pressed in, when the old one is let go anyway.
        /// </summary>
        string Footnote(out Color color)
        {
            color = Theme.TextDim;
            if (_page == PageSnapping) return "While zones are showing, scroll the mouse wheel to switch between the display's sizes.";
            if (_page != PageShortcuts) return null;

            string taken = null;
            int count = 0;
            for (int i = 0; i < _boxes.Length; i++)
            {
                if (_boxes[i].Recording || !Hotkey.IsFailed(BoxSlots[i])) continue;
                taken = BoxTitles[i];
                count++;
            }
            if (count > 0)
            {
                color = Theme.Danger;
                return count == 1 ? taken + " shortcut is already in use" : count + " shortcuts are already in use";
            }
            if (!_config.SnapEnabled) return "Snapping is off: these do nothing until it is on again.";
            return "Click a shortcut, then press the keys for it. Backspace clears it.";
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
                // minimize it. And a sizing frame: Windows starts a resize
                // from the edges the hit test reports only on a window that
                // has one. WM_NCCALCSIZE keeps it from being seen, and DWM
                // gives a framed window its shadow, so the class needs none.
                cp.Style |= WS_MINIMIZEBOX | WS_SYSMENU | WS_THICKFRAME;
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
            if (m.Msg == WM_NCUAHDRAWCAPTION || m.Msg == WM_NCUAHDRAWFRAME)
            {
                // Undocumented, and sent now and then to have DefWindowProc
                // draw the standard caption and frame. This window draws its
                // own, and theirs would land on top of it.
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WM_NCACTIVATE)
            {
                // Going to the background or coming back, DefWindowProc
                // repaints the caption to show which. With none of its own to
                // repaint, the window got the old Windows caption painted
                // across its header instead, a "?" button and all, until
                // something made it paint again. An lParam of -1 keeps the
                // change of state and leaves out the painting.
                m.LParam = new IntPtr(-1);
            }
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                // The sizing frame is there for Windows to resize by, not to
                // be seen: the whole window stays client area. Maximized all
                // the same (Windows snapping it to the top of a display),
                // the frame hangs off the display's edges, so the client
                // area is pulled in by as much or its edges would be cut off.
                if (IsZoomed(Handle))
                {
                    RECT r = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                    int padded = GetSystemMetrics(SM_CXPADDEDBORDER);
                    int x = GetSystemMetrics(SM_CXSIZEFRAME) + padded;
                    int y = GetSystemMetrics(SM_CYSIZEFRAME) + padded;
                    r.Left += x;
                    r.Top += y;
                    r.Right -= x;
                    r.Bottom -= y;
                    Marshal.StructureToPtr(r, m.LParam, false);
                }
                m.Result = IntPtr.Zero;
                return;
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
            Updater.UpdateStateChanged -= OnUpdateStateChanged;
            Tray.SettingsChanged -= OnTraySettingsChanged;
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

            // The window's controls, fonts and bitmaps are garbage now, and
            // Zones goes back to sitting in the tray: a moment later, once
            // the window is gone, their memory goes back to Windows.
            Memory.TrimAfter(2000);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct MINMAXINFO { public POINT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }

        [DllImport("user32.dll")]
        static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int index);
    }
}
