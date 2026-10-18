using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Zones
{
    /// <summary>
    /// Window snapping: while a window is dragged by its title bar, the zones
    /// of the display under the cursor show over everything, the one under the
    /// cursor lit, and on the drop the window is fitted to it.
    ///
    /// Windows says when a move starts and ends through a WinEvent hook, out
    /// of context, so nothing is loaded into anyone else's process and the
    /// events arrive on the UI thread's own message loop. Everything between
    /// the two is polled on a short timer instead of hooked: the cursor, Shift,
    /// Esc and the window's size are one cheap call each, and a low-level mouse
    /// hook would have put Zones in the path of every mouse move on the machine.
    /// The wheel is the one thing there is no polling for, so a mouse hook is
    /// in for exactly as long as it has a use - zones showing on a display
    /// with more than one size to turn through - and out the moment it has not.
    ///
    /// The same two events also bracket a resize from an edge, which must never
    /// snap. Those are told apart by what is under the cursor when the drag
    /// starts, and by how the window's size behaves along the way.
    ///
    /// The same zones are a keyboard shortcut away too, with no drag at all:
    /// each press moves the active window on to the next zone of its display's
    /// layout, round to the first again, and with Shift back the other way. A
    /// window not in a zone yet goes into the one it is over first.
    ///
    /// A display's layout can come in several sizes - the middle zone at 50%,
    /// 60%, 70%, say - one of them active. A second shortcut flips between
    /// them, as does the wheel during a drag, and the windows already in that
    /// display's zones move into the same zones of the new size.
    /// </summary>
    public static class Snapper
    {
        const int TickMs = 15;
        const int RecheckMs = 120;

        /// <summary>
        /// How far past its edge a zone still counts as under the cursor, in
        /// logical pixels, on top of half the gap. Enough that near the line
        /// between two zones both are lit, and the window spans the pair.
        /// </summary>
        const int Reach = 12;

        /// <summary>
        /// How far off a zone's edges a window can be, in logical pixels, and
        /// the zone shortcut still take it as in that zone (see Cycle).
        /// </summary>
        const int NearlyInZone = 16;

        static AppConfig _config;
        static WinEventDelegate _proc;      // held so the delegate outlives the hook
        static Timer _timer;
        static ZoneOverlay _overlay;
        static uint _pid;                   // this process, whose windows only the zone shortcuts move

        // The drag being followed. _hwnd is zero between drags.
        static IntPtr _hwnd;
        static uint _thread;
        static Rectangle _start;
        static Size _size;
        static float _scale;
        static int _sizeChanges;
        static bool _resizing;
        static bool _cancelled;

        // What the overlay shows, and where the window would go if dropped now.
        static List<int> _selected = new List<int>();
        static Display _display;
        static Rectangle _target;
        static int _shownSize;

        /// <summary>
        /// Which size of the aimed-at display's layout the zones show: the
        /// display's active one, at the start of every drag and on every other
        /// display, until the wheel turns it. <see cref="_sizeCount"/> is how
        /// many sizes that display has, for the wheel to go round, and
        /// <see cref="_wheel"/> the turn not yet come to a whole notch.
        /// </summary>
        static int _previewSize;
        static int _sizeCount;
        static int _wheel;

        static IntPtr _mouseHook;           // zero but while the wheel picks the size
        static MouseProc _mouseProc;        // held so the delegate outlives the hook

        /// <summary>
        /// Where Zones last put each window, and how big it was before. Dragged
        /// back out of a zone, a window is given its own size again, as Windows
        /// does for the windows it snaps itself. The shortcut moves it on from
        /// the zone it is in, which for a window with a minimum size bigger
        /// than the zone its frame alone would never say. And when its display
        /// flips to another size of the layout, it follows into the same zones.
        /// </summary>
        static readonly Dictionary<IntPtr, SnapRecord> _snapped = new Dictionary<IntPtr, SnapRecord>();

        sealed class SnapRecord
        {
            /// <summary>The window's size before it went into a zone.</summary>
            public Size Original;

            /// <summary>The size the zone gave it. Any other size means something has resized it since.</summary>
            public Size Snapped;

            /// <summary>The display whose layout the zones belong to.</summary>
            public string Device;

            /// <summary>
            /// The zones it fills, in zone order: more than one when it spans
            /// several. Null once a drag has taken it out of them.
            /// </summary>
            public List<int> Indices;

            /// <summary>Which of the display's layout sizes those zones are of.</summary>
            public int SizeIndex;

            /// <summary>Where the window was left. Anywhere else means it has been moved since.</summary>
            public Rectangle Rect;
        }

        /// <summary>
        /// Starts watching for windows being dragged, and for the shortcuts.
        /// Once, on the UI thread: an out-of-context hook delivers through the
        /// message loop of the thread that installed it, and the shortcuts
        /// belong to a window made on this thread.
        /// </summary>
        public static void Start(AppConfig config)
        {
            _config = config;

            using (System.Diagnostics.Process self = System.Diagnostics.Process.GetCurrentProcess())
                _pid = (uint)self.Id;

            _timer = new Timer();
            _timer.Interval = TickMs;
            _timer.Tick += delegate { Tick(); };

            _proc = new WinEventDelegate(OnMoveSize);
            SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND,
                IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

            Hotkey.Pressed += delegate(int slot, int step)
            {
                if (slot == Hotkey.SlotZone) Cycle(1);
                else if (slot == Hotkey.SlotZoneBack) Cycle(-1);
                else if (slot == Hotkey.SlotSize) FlipSize(1);
                else if (slot == Hotkey.SlotSizeBack) FlipSize(-1);
            };
            ApplyHotkey();
        }

        /// <summary>
        /// Registers the shortcuts as the settings have them, or none while
        /// snapping is off. False when another program has any of them; see
        /// Hotkey.IsFailed for which.
        /// </summary>
        public static bool ApplyHotkey()
        {
            if (_config == null) return true;
            bool on = _config.SnapEnabled;
            bool zone = Hotkey.Set(Hotkey.SlotZone, on ? _config.SnapCycleMods : 0, on ? _config.SnapCycleKey : 0);
            bool size = Hotkey.Set(Hotkey.SlotSize, on ? _config.SnapSizeMods : 0, on ? _config.SnapSizeKey : 0);
            bool zoneBack = Hotkey.Set(Hotkey.SlotZoneBack, on ? _config.SnapCyclePrevMods : 0, on ? _config.SnapCyclePrevKey : 0);
            bool sizeBack = Hotkey.Set(Hotkey.SlotSizeBack, on ? _config.SnapSizePrevMods : 0, on ? _config.SnapSizePrevKey : 0);
            return zone && size && zoneBack && sizeBack;
        }

        /// <summary>A window is being dragged, or zones are on screen: no moment for an update to restart Zones.</summary>
        public static bool Busy
        {
            get { return _hwnd != IntPtr.Zero || (_overlay != null && _overlay.Showing); }
        }

        /// <summary>
        /// Makes size <paramref name="index"/> the active one on
        /// <paramref name="display"/>, moves the windows snapped there into the
        /// matching zones of that size, saves, and flashes the zones with a
        /// "Size n of m" label. On the UI thread.
        /// </summary>
        public static void ActivateSize(Display display, int index)
        {
            ActivateSize(display, index, true);
        }

        /// <summary>
        /// As above, and without <paramref name="flash"/> for a drop: the zones
        /// were on screen a moment ago, and the dropped window is about to show
        /// where they are.
        /// </summary>
        static void ActivateSize(Display display, int index, bool flash)
        {
            int spacing = (int)Math.Round(_config.SnapSpacing * display.Scale);
            DisplayLayouts layouts = _config.LayoutsFor(display);
            List<Rectangle> oldZones = layouts.Current.ZonesIn(display.Work, spacing);

            layouts.Active = index;
            _config.Keep(display, layouts);
            _config.Save();
            List<Rectangle> newZones = layouts.Current.ZonesIn(display.Work, spacing);

            Reflow(display, oldZones, newZones, layouts.Active);

            if (flash)
            {
                if (_overlay == null) _overlay = new ZoneOverlay();
                _overlay.Flash(display.Work, newZones, display.Scale, 700,
                    "Size " + (layouts.Active + 1) + " of " + layouts.Sizes.Count);
            }
        }

        /// <summary>
        /// Moves the windows in <paramref name="display"/>'s zones, laid out as
        /// <paramref name="oldZones"/>, into the same zones of
        /// <paramref name="newZones"/>, which are of layout size
        /// <paramref name="sizeIndex"/>.
        ///
        /// First the windows Zones put there itself, by what it remembers of
        /// them, while nothing has moved them since: that is what knows a
        /// window spanning several zones, or one too big to fill its zone.
        /// Then any other window whose frame fills a zone, snapped before Zones
        /// was started or fitted by hand: to the eye it is just as much in it.
        /// A window in a zone the new size does not have is left where it is.
        /// </summary>
        static void Reflow(Display display, List<Rectangle> oldZones, List<Rectangle> newZones, int sizeIndex)
        {
            // Over a copy: Place writes to _snapped as it goes. A window being
            // dragged is the drag's to place.
            Dictionary<IntPtr, bool> handled = new Dictionary<IntPtr, bool>();
            foreach (KeyValuePair<IntPtr, SnapRecord> kv in new List<KeyValuePair<IntPtr, SnapRecord>>(_snapped))
            {
                IntPtr h = kv.Key;
                SnapRecord r = kv.Value;
                if (r.Indices == null || r.Indices.Count == 0) continue;
                if (!string.Equals(r.Device, display.Device, StringComparison.OrdinalIgnoreCase)) continue;
                if (h == _hwnd || !IsWindow(h) || IsIconic(h) || WindowRect(h) != r.Rect) continue;
                handled[h] = true;

                bool fits = true;
                foreach (int i in r.Indices)
                {
                    if (i < 0 || i >= newZones.Count) fits = false;
                }
                if (!fits) continue;

                Rectangle target = Rectangle.Empty;
                foreach (int i in r.Indices)
                    target = target.IsEmpty ? newZones[i] : Rectangle.Union(target, newZones[i]);
                Place(h, target, display.Device, r.Indices, sizeIndex);
            }

            // Gathered first and looked at after, so nothing is moved while
            // Windows is still going through its list.
            List<IntPtr> windows = new List<IntPtr>();
            EnumWindowsProc collect = delegate(IntPtr h, IntPtr data)
            {
                // Called by Windows: whatever goes wrong must stay in here.
                try
                {
                    windows.Add(h);
                }
                catch (Exception ex)
                {
                    Program.LogError(ex);
                }
                return true;
            };
            EnumWindows(collect, IntPtr.Zero);
            GC.KeepAlive(collect);

            foreach (IntPtr h in windows)
            {
                if (handled.ContainsKey(h) || h == _hwnd) continue;
                // Maximized fills the work area, which with no spacing is
                // exactly a one-zone layout's zone; it is not in that zone and
                // must not be un-maximized into the next size.
                if (IsIconic(h) || IsZoomed(h) || !Snappable(h)) continue;

                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid == _pid) continue;

                // In a zone to a pixel or two of rounding, as the shortcut
                // tells one; with that zone's like in the new size to go to.
                Rectangle frame = Frame(h);
                int zone = -1;
                for (int i = 0; zone < 0 && i < oldZones.Count; i++)
                {
                    Rectangle z = oldZones[i];
                    if (Math.Abs(frame.Left - z.Left) <= 2 && Math.Abs(frame.Top - z.Top) <= 2
                        && Math.Abs(frame.Right - z.Right) <= 2 && Math.Abs(frame.Bottom - z.Bottom) <= 2)
                        zone = i;
                }
                if (zone < 0 || zone >= newZones.Count) continue;

                Display on = Displays.At(new Point(frame.Left + frame.Width / 2, frame.Top + frame.Height / 2));
                if (on == null || !string.Equals(on.Device, display.Device, StringComparison.OrdinalIgnoreCase)) continue;

                List<int> indices = new List<int>();
                indices.Add(zone);
                Place(h, newZones[zone], display.Device, indices, sizeIndex);
            }
        }

        static void OnMoveSize(IntPtr hWinEventHook, uint eventType,
            IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // Called by Windows: whatever goes wrong must stay in here.
            try
            {
                if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF) return;

                if (eventType == EVENT_SYSTEM_MOVESIZESTART) BeginDrag(hwnd);
                else if (eventType == EVENT_SYSTEM_MOVESIZEEND && hwnd != IntPtr.Zero && hwnd == _hwnd) EndDrag(true);
            }
            catch (Exception ex)
            {
                Program.LogError(ex);
            }
        }

        // ----------------------------------------------------------
        // The drag
        // ----------------------------------------------------------

        static void BeginDrag(IntPtr hwnd)
        {
            if (!_config.SnapEnabled) return;
            if (_hwnd != IntPtr.Zero) EndDrag(false);
            if (!Snappable(hwnd)) return;

            uint pid;
            _hwnd = hwnd;
            _thread = GetWindowThreadProcessId(hwnd, out pid);
            _start = WindowRect(hwnd);
            _size = _start.Size;
            _scale = Dpi.ScaleForWindow(hwnd);
            _sizeChanges = 0;
            _resizing = false;
            _cancelled = false;
            _selected.Clear();
            _display = null;            // so the first Aim starts from the display's own size
            _target = Rectangle.Empty;
            _wheel = 0;

            // A resize from an edge starts with the same event. The window
            // knows what is under the cursor: one of its sizing borders means a
            // resize. The coordinates are masked so a display left of or above
            // the main one, at negative coordinates, still packs into two words.
            // A maximized window has no edge to pull, so it can only be moving.
            if (!IsZoomed(hwnd))
            {
                POINT pt;
                IntPtr hit;
                if (GetCursorPos(out pt)
                    && SendMessageTimeout(hwnd, WM_NCHITTEST, IntPtr.Zero,
                           new IntPtr(((pt.Y & 0xFFFF) << 16) | (pt.X & 0xFFFF)),
                           SMTO_ABORTIFHUNG, 50, out hit) != IntPtr.Zero)
                {
                    long h = hit.ToInt64();
                    if (h >= HTLEFT && h <= HTBOTTOMRIGHT) _resizing = true;
                }
            }

            _timer.Start();
        }

        /// <summary>
        /// Only a top-level window that can be resized goes in a zone: a
        /// fixed-size dialog cannot be made to fit one, and tool windows -
        /// palettes, the shell's own popups - are not the user's windows to
        /// arrange.
        /// </summary>
        static bool Snappable(IntPtr hwnd)
        {
            if (GetAncestor(hwnd, GA_ROOT) != hwnd || !IsWindowVisible(hwnd)) return false;
            int style = GetWindowLong(hwnd, GWL_STYLE);
            if ((style & WS_THICKFRAME) == 0 || (style & WS_CHILD) != 0) return false;
            return (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) == 0;
        }

        /// <summary>
        /// Every few milliseconds while a drag is on: whether it still is,
        /// whether it is a move, whether zones are wanted, and which of them
        /// are under the cursor.
        /// </summary>
        static void Tick()
        {
            try
            {
                if (_hwnd == IntPtr.Zero)
                {
                    _timer.Stop();
                    return;
                }
                if (!IsWindow(_hwnd))
                {
                    EndDrag(false);
                    return;
                }

                // Esc has Windows put the window back where it started: it
                // must not then be snapped somewhere else. Looked at before
                // anything can end the drag below.
                if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0) _cancelled = true;

                // The end event is what to go by, but it can be lost, or still
                // on its way: once the window's thread has left its move loop
                // the drag is over regardless. The event coming after finds
                // nothing left to end.
                GUITHREADINFO gti = new GUITHREADINFO();
                gti.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
                if (GetGUIThreadInfo(_thread, ref gti) && (gti.flags & GUI_INMOVESIZE) == 0)
                {
                    EndDrag(true);
                    return;
                }

                if (_cancelled)
                {
                    HideOverlay();
                    return;
                }

                // A move changes the window's size once at most, when a
                // maximized or snapped window is restored as the drag begins.
                // A resize goes on changing it. A window that rescales itself
                // on crossing to a display of another scale changes size too,
                // and that does not count: it is still only being moved.
                Size size = WindowRect(_hwnd).Size;
                if (size != _size)
                {
                    float scale = Dpi.ScaleForWindow(_hwnd);
                    if (scale != _scale) _scale = scale;
                    else _sizeChanges++;
                    _size = size;
                    if (_sizeChanges >= 2) _resizing = true;
                }
                if (_resizing)
                {
                    HideOverlay();
                    return;
                }

                // Read afresh every time: the settings window changes them live.
                bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                bool wanted = _config.SnapEnabled && (_config.SnapHoldShift ? shift : !shift);
                if (!wanted)
                {
                    HideOverlay();
                    return;
                }

                Aim(true);
            }
            catch (Exception ex)
            {
                Program.LogError(ex);
            }
        }

        /// <summary>
        /// Works out which zones are under the cursor right now and where the
        /// window would land, and with <paramref name="draw"/> brings the
        /// overlay up to date. False when there is no display to aim at.
        /// </summary>
        static bool Aim(bool draw)
        {
            POINT pt;
            GetCursorPos(out pt);
            Point cursor = new Point(pt.X, pt.Y);
            Display d = Displays.At(cursor);
            if (d == null)
            {
                HideOverlay();
                return false;
            }

            // A size picked with the wheel is a size of that display's layout:
            // on another display the zones start again from its own.
            DisplayLayouts layouts = _config.LayoutsFor(d);
            bool moved = _display == null || _display.Device != d.Device;
            _sizeCount = layouts.Sizes.Count;
            if (moved || _previewSize < 0 || _previewSize >= _sizeCount)
            {
                _previewSize = layouts.Active;
                _wheel = 0;
            }

            int spacing = (int)Math.Round(_config.SnapSpacing * d.Scale);
            List<Rectangle> zones = layouts.Sizes[_previewSize].ZonesIn(d.Work, spacing);
            List<Rectangle> blanks = layouts.Sizes[_previewSize].BlanksIn(d.Work);
            List<int> selected = ZonesUnder(cursor, zones, blanks, spacing / 2 + (int)Math.Round(Reach * d.Scale));

            Rectangle target = Rectangle.Empty;
            foreach (int i in selected)
                target = target.IsEmpty ? zones[i] : Rectangle.Union(target, zones[i]);

            // Drawn again only when what it shows has changed: a 4K overlay
            // every tick is a lot of work for the same picture. A turn of the
            // wheel changes the size, and with it the zones and the label.
            bool changed = _overlay == null || !_overlay.Showing
                || moved || _previewSize != _shownSize || selected.Count != _selected.Count;
            for (int i = 0; !changed && i < selected.Count; i++)
                changed = selected[i] != _selected[i];

            _target = target;
            _selected = selected;
            _display = d;
            _shownSize = _previewSize;

            if (draw)
            {
                if (changed)
                {
                    // Which size it is, and that the wheel turns it, only where
                    // there is more than one to turn through.
                    string label = _sizeCount > 1
                        ? "Size " + (_previewSize + 1) + " of " + _sizeCount + "  ·  scroll to change"
                        : null;
                    if (_overlay == null) _overlay = new ZoneOverlay();
                    _overlay.ShowZones(d.Work, zones, _selected, d.Scale, label);
                }

                if (_sizeCount > 1) HookWheel();
                else UnhookWheel();
            }
            return true;
        }

        /// <summary>
        /// The zones under <paramref name="cursor"/>, in zone order: every one
        /// within <paramref name="reach"/> of it, so near the line between two
        /// the window spans both; none with the cursor out in a part of the
        /// layout left empty (<paramref name="blanks"/>), where windows are
        /// meant not to go; or, with the cursor past the edge of them all (in
        /// the margin, over the taskbar), the nearest one.
        /// </summary>
        static List<int> ZonesUnder(Point cursor, List<Rectangle> zones, List<Rectangle> blanks, int reach)
        {
            List<int> selected = new List<int>();
            for (int i = 0; i < zones.Count; i++)
            {
                if (Rectangle.Inflate(zones[i], reach, reach).Contains(cursor)) selected.Add(i);
            }
            if (selected.Count > 0) return selected;

            // Nothing lit, so a window let go there is not snapped anywhere.
            foreach (Rectangle b in blanks)
            {
                if (b.Contains(cursor)) return selected;
            }

            int nearest = Nearest(cursor, zones);
            if (nearest >= 0) selected.Add(nearest);
            return selected;
        }

        /// <summary>
        /// The zone nearest to <paramref name="p"/>, measured to its edge, not
        /// its middle, so a big zone is not passed over for a small one beside
        /// it. -1 when there are none.
        /// </summary>
        static int Nearest(Point p, List<Rectangle> zones)
        {
            int nearest = -1;
            long best = long.MaxValue;
            for (int i = 0; i < zones.Count; i++)
            {
                Rectangle z = zones[i];
                long dx = Math.Max(0, Math.Max(z.Left - p.X, p.X - z.Right));
                long dy = Math.Max(0, Math.Max(z.Top - p.Y, p.Y - z.Bottom));
                long dist = dx * dx + dy * dy;
                if (dist < best)
                {
                    best = dist;
                    nearest = i;
                }
            }
            return nearest;
        }

        /// <summary>
        /// Ends the drag: a move that was showing zones fits the window to the
        /// lit ones, a move that was not gives a window taken out of a zone its
        /// own size back. Ending the same drag twice does nothing the second time.
        /// </summary>
        static void EndDrag(bool commit)
        {
            _timer.Stop();
            IntPtr hwnd = _hwnd;
            _hwnd = IntPtr.Zero;

            // Esc can come and go between ticks, and the event can beat the next
            // tick here; a window back exactly where it started is the surer
            // sign. A cancelled move puts it there, and so does a plain click on
            // a title bar, which runs the same move loop without moving anything.
            // Neither should snap the window, or shrink one back out of a zone.
            if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0) _cancelled = true;
            if (hwnd != IntPtr.Zero && IsWindow(hwnd) && WindowRect(hwnd) == _start) _cancelled = true;

            // Moved by hand, it is in no zone any more: the shortcut starts its
            // round afresh, and a flip of the display's size leaves it be. Only
            // once it has really moved: a click on the title bar of a window too
            // big for the zone the shortcut put it in must not forget the zone.
            // The size it had before the zone stays, for RestoreSize below.
            SnapRecord record;
            if (hwnd != IntPtr.Zero && !_cancelled && _snapped.TryGetValue(hwnd, out record)) record.Indices = null;

            bool move = hwnd != IntPtr.Zero && commit && !_cancelled && !_resizing;
            bool snap = move && _overlay != null && _overlay.Showing;

            // Aimed again at where the cursor is now, not where the last tick
            // saw it: a quick flick that lets go within the same few
            // milliseconds would otherwise land one zone short. What it aimed
            // at is taken before the overlay goes, which empties the selection.
            if (snap) snap = Aim(false) && _selected.Count > 0;
            List<int> indices = new List<int>(_selected);
            Display display = _display;
            HideOverlay();

            if (snap)
            {
                // A size picked with the wheel becomes the display's own, and
                // the windows already in its zones go with it, before this one
                // goes into its zones of that size.
                if (_previewSize != _config.LayoutsFor(display).Active) ActivateSize(display, _previewSize, false);
                Place(hwnd, _target, display.Device, indices, _previewSize);
            }
            else if (move) RestoreSize(hwnd);
        }

        static void HideOverlay()
        {
            UnhookWheel();
            if (_overlay != null && _overlay.Showing) _overlay.HideZones();
            _selected.Clear();
        }

        // ----------------------------------------------------------
        // The wheel
        // ----------------------------------------------------------

        /// <summary>
        /// Puts the wheel hook in, unless it is in already. Every mouse event
        /// on the machine waits on it, this one's moves included, so only for
        /// as long as zones with sizes to turn through are on screen.
        /// </summary>
        static void HookWheel()
        {
            if (_mouseHook != IntPtr.Zero) return;
            if (_mouseProc == null) _mouseProc = new MouseProc(OnMouse);
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
        }

        static void UnhookWheel()
        {
            if (_mouseHook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        /// <summary>
        /// Every mouse event while the hook is in. A turn of the wheel steps
        /// the size the zones show, a notch at a time, towards you for the next
        /// and away for the one before, and goes no further: the window under
        /// the cursor must not scroll along with it. The next tick sees the new
        /// size and draws it. Anything else goes straight on. No more than
        /// that in here: the whole machine's mouse is waiting.
        /// </summary>
        static IntPtr OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Called by Windows: whatever goes wrong must stay in here.
            try
            {
                if (nCode >= 0 && wParam.ToInt64() == WM_MOUSEWHEEL && _hwnd != IntPtr.Zero && _sizeCount > 1)
                {
                    MSLLHOOKSTRUCT info = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    _wheel += (short)(info.mouseData >> 16);

                    // Whole notches, towards zero, so what is left keeps its
                    // sign. Away from you is a positive turn: back a size.
                    int notches = _wheel / WHEEL_DELTA;
                    _wheel -= notches * WHEEL_DELTA;
                    if (notches != 0) _previewSize = ((_previewSize - notches) % _sizeCount + _sizeCount) % _sizeCount;
                    return new IntPtr(1);
                }
            }
            catch (Exception ex)
            {
                Program.LogError(ex);
            }
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        // ----------------------------------------------------------
        // The shortcuts
        // ----------------------------------------------------------

        /// <summary>
        /// Moves the active window <paramref name="step"/> zones on, +1 or -1,
        /// round its display's layout. One not in a zone yet is put in the one
        /// it is over: the first press snaps it where it is, every press after
        /// moves it on.
        /// </summary>
        static void Cycle(int step)
        {
            try
            {
                if (!_config.SnapEnabled) return;

                // A window being dragged is the drag's to place. A minimized
                // one can still be the foreground window, but it is out of
                // the way on purpose: the shortcut must not bring it back.
                // Zones' own settings window included: it is the window in
                // front, and a shortcut that did nothing to it read as one
                // that did not work. While a new shortcut is being pressed
                // into it, they are let go, so that never moves it.
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero || hwnd == _hwnd || IsIconic(hwnd) || !Snappable(hwnd)) return;

                // A maximized window's frame covers the whole work area, which
                // is no one zone: it goes into the one under its middle, and
                // Place restores it on the way.
                Rectangle frame = Frame(hwnd);
                Point centre = new Point(frame.Left + frame.Width / 2, frame.Top + frame.Height / 2);
                Display d = Displays.At(centre);
                if (d == null) return;

                int spacing = (int)Math.Round(_config.SnapSpacing * d.Scale);
                DisplayLayouts layouts = _config.LayoutsFor(d);
                List<Rectangle> zones = layouts.Current.ZonesIn(d.Work, spacing);
                int n = zones.Count;
                if (n == 0) return;

                // Which zone it is in now. The one Zones last put it in, while
                // it is still exactly where it was left there - the only way to
                // tell for a window too big to fill its zone. Otherwise the one
                // its frame is on, give or take a few pixels: put there across
                // several by a drag, moved away and back, or placed by hand or
                // by the app just about there. Snapping it into a zone it is
                // already that close to would barely move it, and the press
                // would look as if it did nothing; it goes on to the next.
                int current = -1;
                SnapRecord record;
                if (_snapped.TryGetValue(hwnd, out record)
                    && record.Indices != null && record.Indices.Count == 1
                    && string.Equals(record.Device, d.Device, StringComparison.OrdinalIgnoreCase)
                    && record.SizeIndex == layouts.Active
                    && record.Indices[0] < n
                    && WindowRect(hwnd) == record.Rect)
                    current = record.Indices[0];

                int near = Math.Max(2, (int)Math.Round(NearlyInZone * d.Scale));
                for (int i = 0; current < 0 && i < n; i++)
                {
                    Rectangle z = zones[i];
                    if (Math.Abs(frame.Left - z.Left) <= near && Math.Abs(frame.Top - z.Top) <= near
                        && Math.Abs(frame.Right - z.Right) <= near && Math.Abs(frame.Bottom - z.Bottom) <= near)
                        current = i;
                }

                // In none of them, it goes into the one its middle is over, or
                // with that in a gap or the margin, the nearest.
                int next = -1;
                if (current >= 0)
                {
                    next = ((current + step) % n + n) % n;
                }
                else
                {
                    for (int i = 0; next < 0 && i < n; i++)
                    {
                        if (zones[i].Contains(centre)) next = i;
                    }
                    if (next < 0) next = Nearest(centre, zones);
                }

                List<int> indices = new List<int>();
                indices.Add(next);
                Place(hwnd, zones[next], d.Device, indices, layouts.Active);
            }
            catch (Exception ex)
            {
                Program.LogError(ex);
            }
        }

        /// <summary>
        /// Flips a display <paramref name="step"/> sizes on, +1 or -1, round
        /// its layout's sizes: the active window's display, or with no window
        /// of the user's active, the one under the cursor. A display with only
        /// the one size says so, rather than the shortcut seeming to do nothing.
        /// </summary>
        static void FlipSize(int step)
        {
            try
            {
                if (!_config.SnapEnabled) return;

                // The display the active window is on, judged as the zone
                // shortcut judges it. Zones' own windows, a minimized one, the
                // desktop and the taskbar say nothing about where the user is
                // working: then it is wherever the cursor is.
                Display d = null;
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd != IntPtr.Zero && !IsIconic(hwnd) && Snappable(hwnd))
                {
                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    if (pid != _pid)
                    {
                        Rectangle frame = Frame(hwnd);
                        d = Displays.At(new Point(frame.Left + frame.Width / 2, frame.Top + frame.Height / 2));
                    }
                }
                if (d == null) d = Displays.At(Cursor.Position);
                if (d == null) return;

                DisplayLayouts layouts = _config.LayoutsFor(d);
                int n = layouts.Sizes.Count;
                if (n < 2)
                {
                    int spacing = (int)Math.Round(_config.SnapSpacing * d.Scale);
                    if (_overlay == null) _overlay = new ZoneOverlay();
                    _overlay.Flash(d.Work, layouts.Current.ZonesIn(d.Work, spacing), d.Scale, 1200,
                        "One size · add more in Zones settings");
                    return;
                }

                ActivateSize(d, ((layouts.Active + step) % n + n) % n);
            }
            catch (Exception ex)
            {
                Program.LogError(ex);
            }
        }

        // ----------------------------------------------------------
        // Placing
        // ----------------------------------------------------------

        /// <summary>
        /// Fits a window to <paramref name="target"/>, the frame it should show
        /// in screen pixels: zones <paramref name="indices"/> of layout size
        /// <paramref name="sizeIndex"/> on display <paramref name="device"/>.
        /// Remembers those, and the size it had, so that dragging it out again
        /// can give that back. A window that will not be moved - an elevated
        /// one, say - is simply left where it is.
        /// </summary>
        static void Place(IntPtr hwnd, Rectangle target, string device, List<int> indices, int sizeIndex)
        {
            if (IsZoomed(hwnd) || IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);

            // Moving from one zone to another, the size to give back is still
            // the one it had before the first zone, not the first zone's.
            Size current = WindowRect(hwnd).Size;
            SnapRecord record;
            SnapRecord placed = new SnapRecord();
            placed.Original = _snapped.TryGetValue(hwnd, out record) && record.Snapped == current
                ? record.Original
                : current;

            Fit(hwnd, target);

            Rectangle set = WindowRect(hwnd);
            placed.Snapped = set.Size;
            placed.Device = device;
            placed.Indices = new List<int>(indices);
            placed.SizeIndex = sizeIndex;
            placed.Rect = set;
            _snapped[hwnd] = placed;

            List<IntPtr> gone = new List<IntPtr>();
            foreach (IntPtr h in _snapped.Keys)
            {
                if (!IsWindow(h)) gone.Add(h);
            }
            foreach (IntPtr h in gone) _snapped.Remove(h);

            // Aero Snap can still apply a placement of its own just after the
            // move loop has ended. A moment later, once, the window is put back.
            // Only if this is still the last word on where it goes: each Place
            // leaves its own record, so one replaced by a quicker second press,
            // or dropped by a drag back out of the zone, has been overtaken,
            // and putting the window back here would undo the newer move.
            Timer recheck = new Timer();
            recheck.Interval = RecheckMs;
            recheck.Tick += delegate
            {
                recheck.Stop();
                recheck.Dispose();
                try
                {
                    SnapRecord latest;
                    if (!_snapped.TryGetValue(hwnd, out latest) || latest != placed) return;
                    if (hwnd == _hwnd || !IsWindow(hwnd)) return;
                    if (WindowRect(hwnd) != set) Fit(hwnd, target);
                }
                catch (Exception ex)
                {
                    Program.LogError(ex);
                }
            };
            recheck.Start();
        }

        /// <summary>
        /// Moves a window so its visible frame covers <paramref name="target"/>.
        /// A window's rectangle takes in the invisible borders Windows keeps
        /// around it for grabbing its edges; DWM knows where the frame that
        /// shows is, and the difference goes back on. Twice, because a window
        /// arriving on a display of another scale rescales itself, borders and
        /// all, and the first pass measured the borders it had before.
        /// </summary>
        static void Fit(IntPtr hwnd, Rectangle target)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                Rectangle wr = WindowRect(hwnd);
                Rectangle fr = Frame(hwnd);

                // Clamped, so one odd answer cannot fling the window off.
                int l = Math.Max(0, Math.Min(64, fr.Left - wr.Left));
                int t = Math.Max(0, Math.Min(64, fr.Top - wr.Top));
                int r = Math.Max(0, Math.Min(64, wr.Right - fr.Right));
                int b = Math.Max(0, Math.Min(64, wr.Bottom - fr.Bottom));

                SetWindowPos(hwnd, IntPtr.Zero, target.Left - l, target.Top - t,
                             target.Width + l + r, target.Height + t + b,
                             SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            }

            // A window that will not go smaller than it is stays bigger than
            // the zone, from the zone's top-left corner. In a zone at the
            // display's right or bottom edge that hangs it off the display, so
            // it is moved back on, as far into the zone as it can still be.
            Rectangle got = Frame(hwnd);
            if (got.Width <= target.Width && got.Height <= target.Height) return;
            Display d = Displays.At(new Point(target.Left + target.Width / 2, target.Top + target.Height / 2));
            if (d == null) return;

            int dx = Math.Max(d.Work.Left - got.Left, Math.Min(0, d.Work.Right - got.Right));
            int dy = Math.Max(d.Work.Top - got.Top, Math.Min(0, d.Work.Bottom - got.Bottom));
            if (dx == 0 && dy == 0) return;
            Rectangle now = WindowRect(hwnd);
            SetWindowPos(hwnd, IntPtr.Zero, now.Left + dx, now.Top + dy, 0, 0,
                         SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        /// <summary>
        /// Gives a window dragged out of a zone, and not into another, the size
        /// it had before it went in. The cursor keeps its place across the
        /// window, so the title bar does not jump out from under it.
        /// </summary>
        static void RestoreSize(IntPtr hwnd)
        {
            SnapRecord record;
            if (!_snapped.TryGetValue(hwnd, out record)) return;

            // The size is only ours while it is exactly the zone's. Anything
            // else - Aero Snap, a resize by hand - has taken it over since.
            _snapped.Remove(hwnd);
            Rectangle cur = WindowRect(hwnd);
            if (cur.Size != record.Snapped) return;

            POINT pt;
            GetCursorPos(out pt);
            Size size = record.Original;
            double fx = (pt.X - cur.Left) / (double)Math.Max(1, cur.Width);
            int left = pt.X - (int)Math.Round(fx * size.Width);
            int top = cur.Top;

            // Kept on the display it was dropped on, where it fits there.
            Display d = Displays.At(new Point(pt.X, pt.Y));
            if (d != null)
            {
                Rectangle work = d.Work;
                if (size.Width <= work.Width) left = Math.Max(work.Left, Math.Min(left, work.Right - size.Width));
                if (size.Height <= work.Height) top = Math.Max(work.Top, Math.Min(top, work.Bottom - size.Height));
            }

            SetWindowPos(hwnd, IntPtr.Zero, left, top, size.Width, size.Height,
                         SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        static Rectangle WindowRect(IntPtr hwnd)
        {
            RECT r;
            GetWindowRect(hwnd, out r);
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }

        /// <summary>
        /// The frame a window shows, in screen pixels: its rectangle less the
        /// invisible borders around it for grabbing its edges. DWM knows where
        /// that is; without DWM's answer, the whole rectangle.
        /// </summary>
        static Rectangle Frame(IntPtr hwnd)
        {
            RECT fr;
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out fr, Marshal.SizeOf(typeof(RECT))) != 0)
                return WindowRect(hwnd);
            return Rectangle.FromLTRB(fr.Left, fr.Top, fr.Right, fr.Bottom);
        }

        // ----------------------------------------------------------
        // Interop
        // ----------------------------------------------------------

        const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
        const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
        const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
        const int OBJID_WINDOW = 0;
        const int CHILDID_SELF = 0;
        const uint GA_ROOT = 2;
        const int GWL_STYLE = -16;
        const int GWL_EXSTYLE = -20;
        const int WS_THICKFRAME = 0x00040000;
        const int WS_CHILD = 0x40000000;
        const int WS_EX_TOOLWINDOW = 0x80;
        const uint WM_NCHITTEST = 0x0084;
        const uint SMTO_ABORTIFHUNG = 0x2;
        const int HTLEFT = 10;
        const int HTBOTTOMRIGHT = 17;
        const int GUI_INMOVESIZE = 0x2;
        const int VK_SHIFT = 0x10;
        const int VK_ESCAPE = 0x1B;
        const int SW_RESTORE = 9;
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_NOOWNERZORDER = 0x0200;
        const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        const int WH_MOUSE_LL = 14;
        const int WM_MOUSEWHEEL = 0x020A;
        const int WHEEL_DELTA = 120;

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
            IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        delegate IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
            IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc,
            uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, MouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vk);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("user32.dll")]
        static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
                                                uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hwnd, int cmd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                        int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
    }
}
