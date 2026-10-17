using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Zones
{
    static class Program
    {
        static Mutex _mutex;
        static EventWaitHandle _showSettings;   // a second launch's way of asking for the settings window
        static RegisteredWaitHandle _showSettingsWait;  // held so the wait on it lasts as long as Zones
        static AppConfig _config;
        static Form _owner;          // hidden window that owns cross-thread invokes
        static System.Windows.Forms.Timer _updateTimer;   // the quiet update checks

        // The first quiet check waits for startup to settle; after that, every
        // few hours, because Zones stays running for weeks between restarts.
        const int FirstUpdateCheckMs = 10 * 1000;
        const int UpdateCheckEveryMs = 6 * 60 * 60 * 1000;

        [STAThread]
        static void Main()
        {
            try
            {
                Run();
            }
            catch (Exception ex)
            {
                LogError(ex);
                throw;
            }
        }

        /// <summary>
        /// The one error log, at %APPDATA%\Zones\error.log. Reachable from the rest of the
        /// app so that a click which quietly did nothing leaves a trace somewhere.
        /// </summary>
        public static void LogError(Exception ex)
        {
            try
            {
                if (!System.IO.Directory.Exists(AppConfig.Dir))
                    System.IO.Directory.CreateDirectory(AppConfig.Dir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppConfig.Dir, "error.log"),
                    DateTime.Now.ToString("s") + "  " + ex.ToString() + Environment.NewLine);
            }
            catch { }
        }

        static void Run()
        {
            // Opened before the mutex is tried, by every launch: whichever one
            // ends up owning the mutex has then always made the event before
            // any other launch can find the mutex taken and signal it.
            _showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, "Zones_ShowSettings");

            // One Zones at a time. Launched again - from the Start menu, with
            // it already in the notification area - is someone looking for it,
            // so the running one opens its settings and this one quits. It is
            // this launch Windows lets take the foreground; it hands that on,
            // or the settings window could open behind whatever is in front.
            // An update relaunches us while the build it replaced is still
            // shutting down, so that one start is allowed to wait for the
            // mutex instead of giving up on the spot.
            bool afterUpdate = HasArg("--after-update");
            if (!ClaimSingleInstance(afterUpdate ? 10000 : 0))
            {
                AllowSetForegroundWindow(ASFW_ANY);
                _showSettings.Set();
                _showSettings.Close();
                return;
            }

            // Before the first window exists, or it does not take.
            Dpi.MakeProcessAware();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // A stray exception in a click handler should cost that click, not
            // put up .NET's crash dialog or end the app.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { LogError(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                if (ex != null) LogError(ex);
            };

            // An update leaves the previous build renamed beside this one.
            Updater.CleanupOldBuild();

            // Asked before loading, which reads the file but never writes it.
            bool firstRun = !AppConfig.Exists;
            _config = AppConfig.Load();

            // A hidden window owning the UI thread, for the work that arrives
            // elsewhere: a second launch asking for the settings. It never
            // needs to be shown, only to have a handle.
            _owner = new Form();
            IntPtr ownerHandle = _owner.Handle;

            ListenForSecondLaunch();

            Tray.Start(_config);

            // Drag any window onto a zone, or press the shortcut, to snap it.
            // The zones are set up in SnapForm, from the notification area.
            Snapper.Start(_config);

            // Nothing on screen says Zones has started, so the very first time
            // the settings open by themselves, and are saved so it is only
            // the first time once.
            if (firstRun)
            {
                SnapForm.ShowFor(_config);
                _config.Save();
            }
            else if (HasArg("--show-settings"))
            {
                // An update installed while the settings were open: back as they were.
                SnapForm.ShowFor(_config);
            }

            // Quietly look for a newer release. Nothing pops up: with automatic
            // updates on, it installs itself when Zones is idle; otherwise the
            // answer only shows up as the wording of the menu's update item,
            // and as a button in the settings window.
            Updater.Init(_owner, _config);
            StartUpdateChecks();

            // Started: what that took is garbage, and Zones waits in the tray.
            Memory.TrimAfter(3000);

            Application.Run();

            // Cleanup: no ghost icon left in the notification area.
            Tray.Dispose();
            ReleaseSingleInstance();
        }

        /// <summary>
        /// Waits for another launch to ask for the settings window, and opens
        /// it on the UI thread each time. A wait registered with the thread
        /// pool rather than a thread of its own: the pool's wait thread
        /// watches the event among others, so no thread sits parked on it for
        /// weeks, and none keeps the process alive past a Quit.
        /// </summary>
        static void ListenForSecondLaunch()
        {
            _showSettingsWait = ThreadPool.RegisterWaitForSingleObject(_showSettings, delegate(object state, bool timedOut)
            {
                try
                {
                    _owner.BeginInvoke((MethodInvoker)delegate { SnapForm.ShowFor(_config); });
                }
                catch (Exception ex)
                {
                    LogError(ex);
                }
            }, null, -1, false);
        }

        /// <summary>
        /// A quiet check a little after starting, then one every few hours. A
        /// Forms timer, so each check starts on the UI thread like one from the menu.
        /// </summary>
        static void StartUpdateChecks()
        {
            _updateTimer = new System.Windows.Forms.Timer();
            _updateTimer.Interval = FirstUpdateCheckMs;
            _updateTimer.Tick += delegate
            {
                _updateTimer.Interval = UpdateCheckEveryMs;
                Updater.CheckForUpdates(false);
            };
            _updateTimer.Start();
        }

        static bool HasArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        static bool ClaimSingleInstance(int waitMs)
        {
            _mutex = new Mutex(false, "Zones_SingleInstance_Mutex");
            try
            {
                if (_mutex.WaitOne(waitMs, false)) return true;
            }
            catch (AbandonedMutexException)
            {
                // The previous run died without releasing it. It is ours now.
                return true;
            }

            _mutex.Close();
            _mutex = null;
            return false;
        }

        /// <summary>
        /// Lets go of the single-instance mutex. An update calls this before it
        /// launches the new build, so the two never fight over the name.
        /// </summary>
        public static void ReleaseSingleInstance()
        {
            if (_mutex == null) return;
            try { _mutex.ReleaseMutex(); }
            catch { }
            try { _mutex.Close(); }
            catch { }
            _mutex = null;
        }

        const int ASFW_ANY = -1;

        [DllImport("user32.dll")]
        static extern bool AllowSetForegroundWindow(int processId);
    }
}
