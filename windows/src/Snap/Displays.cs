using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Zones
{
    /// <summary>One display, as snapping sees it.</summary>
    public sealed class Display
    {
        /// <summary>
        /// The GDI device name (\\.\DISPLAY1): which display this is for as
        /// long as nothing changes. Windows hands the names out afresh when
        /// displays come and go, after sleep or a driver update, so the
        /// ultrawide that was DISPLAY1 this morning can be DISPLAY2 now.
        /// Layouts are kept by <see cref="Key"/> instead.
        /// </summary>
        public string Device;

        string _monitor;
        bool _monitorAsked;

        /// <summary>
        /// The monitor plugged in, by the device path Windows gives it, cut
        /// to the part that names it: DISPLAY#SAM72F2#5&amp;92adb24&amp;0&amp;UID4352,
        /// its make and model, then the connector it is on. It stays with the
        /// screen however Windows numbers its displays. Null when Windows does
        /// not say, as over Remote Desktop. Asked for only when wanted.
        /// </summary>
        public string Monitor
        {
            get
            {
                if (!_monitorAsked)
                {
                    _monitor = Displays.MonitorOf(Device);
                    _monitorAsked = true;
                }
                return _monitor;
            }
        }

        /// <summary>What the display's layouts are kept by: the monitor, or failing that the device name.</summary>
        public string Key
        {
            get
            {
                string monitor = Monitor;
                return !string.IsNullOrEmpty(monitor) ? monitor : Device;
            }
        }

        /// <summary>The number in the device name, which is what Windows numbers it by too.</summary>
        public int Number;

        public Rectangle Bounds;

        /// <summary>Everything but the taskbar, in device pixels: where zones go.</summary>
        public Rectangle Work;

        public bool Primary;

        /// <summary>How big a logical pixel is on this display.</summary>
        public float Scale;
    }

    /// <summary>
    /// The displays attached right now. Asked of Windows each time rather than
    /// through Screen.AllScreens, which caches and can go on describing a
    /// monitor for a while after it has been unplugged.
    /// </summary>
    public static class Displays
    {
        /// <summary>Every display, left to right.</summary>
        public static List<Display> All()
        {
            List<Display> list = new List<Display>();
            MonitorEnumProc proc = delegate(IntPtr mon, IntPtr hdc, ref RECT r, IntPtr data)
            {
                Display d = Describe(mon);
                if (d != null) list.Add(d);
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
            GC.KeepAlive(proc);

            list.Sort(delegate(Display a, Display b)
            {
                int c = a.Bounds.Left.CompareTo(b.Bounds.Left);
                return c != 0 ? c : a.Bounds.Top.CompareTo(b.Bounds.Top);
            });
            return list;
        }

        /// <summary>The display at a screen point, or the nearest one to it.</summary>
        public static Display At(Point p)
        {
            POINT pt;
            pt.X = p.X;
            pt.Y = p.Y;
            return Describe(MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST));
        }

        /// <summary>The display by device name, or null if it is not attached.</summary>
        public static Display Find(string device)
        {
            foreach (Display d in All())
            {
                if (string.Equals(d.Device, device, StringComparison.OrdinalIgnoreCase)) return d;
            }
            return null;
        }

        static Display Describe(IntPtr mon)
        {
            if (mon == IntPtr.Zero) return null;

            MONITORINFOEX mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            if (!GetMonitorInfo(mon, ref mi)) return null;

            Display d = new Display();
            d.Device = mi.szDevice ?? "";
            d.Number = NumberOf(d.Device);
            d.Bounds = Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
            d.Work = Rectangle.FromLTRB(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom);
            d.Primary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
            d.Scale = ScaleOf(mon);
            return d;
        }

        // Which monitor is on which device name, asked of Windows at most
        // every few seconds: a drag looks its display up every frame, and the
        // answer only changes when displays are plugged in, taken away or
        // numbered afresh.
        static readonly Dictionary<string, string> _monitors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static int _monitorsAt;
        const int MonitorsMs = 3000;

        /// <summary>The monitor on a display's output (see <see cref="Display.Monitor"/>), or null.</summary>
        public static string MonitorOf(string device)
        {
            if (string.IsNullOrEmpty(device)) return null;
            int now = Environment.TickCount;
            if (now - _monitorsAt > MonitorsMs || now < _monitorsAt)
            {
                _monitors.Clear();
                _monitorsAt = now;
            }

            string monitor;
            if (_monitors.TryGetValue(device, out monitor)) return monitor;
            monitor = AskMonitor(device);
            _monitors[device] = monitor;
            return monitor;
        }

        /// <summary>
        /// The active monitor on an adapter output, as Windows' device path for
        /// it, \\?\DISPLAY#SAM72F2#5&amp;92adb24&amp;0&amp;UID4352#{e6f07b5f-...}, without
        /// the prefix and the interface's GUID, which are the same for every
        /// monitor.
        /// </summary>
        static string AskMonitor(string device)
        {
            try
            {
                string first = null;
                for (uint i = 0; ; i++)
                {
                    DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
                    dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                    if (!EnumDisplayDevices(device, i, ref dd, EDD_GET_DEVICE_INTERFACE_NAME)) break;
                    if (string.IsNullOrEmpty(dd.DeviceID)) continue;
                    if ((dd.StateFlags & DISPLAY_DEVICE_ACTIVE) != 0) return Shorten(dd.DeviceID);
                    if (first == null) first = dd.DeviceID;
                }
                return first != null ? Shorten(first) : null;
            }
            catch
            {
                return null;
            }
        }

        static string Shorten(string path)
        {
            string s = path;
            if (s.StartsWith(@"\\?\", StringComparison.Ordinal)) s = s.Substring(4);
            int guid = s.LastIndexOf("#{", StringComparison.Ordinal);
            if (guid > 0) s = s.Substring(0, guid);
            return s;
        }

        static int NumberOf(string device)
        {
            int end = device.Length, start = end;
            while (start > 0 && char.IsDigit(device[start - 1])) start--;
            int n;
            return start < end && int.TryParse(device.Substring(start), out n) ? n : 0;
        }

        static float ScaleOf(IntPtr mon)
        {
            try
            {
                uint dx, dy;
                if (GetDpiForMonitor(mon, 0 /* MDT_EFFECTIVE_DPI */, out dx, out dy) == 0 && dx > 0)
                    return dx / 96f;
            }
            catch { }
            return 1f;
        }

        const int MONITOR_DEFAULTTONEAREST = 2;
        const int MONITORINFOF_PRIMARY = 1;
        const uint EDD_GET_DEVICE_INTERFACE_NAME = 1;
        const int DISPLAY_DEVICE_ACTIVE = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevices(string device, uint index, ref DISPLAY_DEVICE dd, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        delegate bool MonitorEnumProc(IntPtr mon, IntPtr hdc, ref RECT r, IntPtr data);

        [DllImport("user32.dll")]
        static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFOEX mi);

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromPoint(POINT pt, int flags);

        [DllImport("shcore.dll")]
        static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dx, out uint dy);
    }
}
