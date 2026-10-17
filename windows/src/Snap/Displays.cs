using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Zones
{
    /// <summary>One display, as snapping sees it.</summary>
    public sealed class Display
    {
        /// <summary>The GDI device name (\\.\DISPLAY1). Layouts are kept by it.</summary>
        public string Device;

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
