using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Zones
{
    /// <summary>
    /// Gives memory back to Windows when Zones goes back to waiting in the
    /// tray: after starting, and after the settings window closes. Zones runs
    /// for weeks and is busy for seconds of them, so the pages startup and
    /// the window touched are better on Windows' standby list than held here;
    /// the few a drag needs fault back in from there in no time.
    ///
    /// Only at those moments, never on a schedule: trimming a process that is
    /// working only makes it fault its pages straight back.
    /// </summary>
    public static class Memory
    {
        /// <summary>
        /// Collects what is garbage now, finalizers and all, then empties the
        /// working set. Best effort: nothing here is worth an error.
        /// </summary>
        public static void Trim()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
            }
            catch { }
        }

        /// <summary>
        /// Trims once, <paramref name="ms"/> from now, on the UI thread. Not
        /// under a drag, when the collection's pause would stall the zones on
        /// screen: then a little later, as many times as it takes.
        /// </summary>
        public static void TrimAfter(int ms)
        {
            Timer timer = new Timer();
            timer.Interval = ms;
            timer.Tick += delegate
            {
                if (Snapper.Busy) return;
                timer.Stop();
                timer.Dispose();
                Trim();
            };
            timer.Start();
        }

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimumWorkingSetSize, IntPtr maximumWorkingSetSize);
    }
}
