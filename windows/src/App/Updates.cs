using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Zones
{
    public sealed class ReleaseInfo
    {
        public string Tag;
        public Version Version;
        public string ExeUrl;
        public string SumsUrl;
        public string PageUrl;
    }

    /// <summary>
    /// Self-update against the project's GitHub releases. The repository is pinned
    /// here on purpose: an updater that can be pointed somewhere else by a config
    /// file is a way to make Zones run someone else's code.
    ///
    /// The update flow lives here too, not in the tray menu: the menu and the
    /// settings window both offer it, and both show the one state it is in,
    /// told by <see cref="UpdateStateChanged"/>. With automatic updates on, a
    /// newer release the quiet checks find is downloaded and checked in the
    /// background and installed the first moment nobody is using Zones.
    /// </summary>
    public static class Updater
    {
        const string Owner = "rui-branco";
        const string Repo = "zones";
        const string LatestApi =
            "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";
        const string AssetName = "zones.exe";
        const string SumsName = "SHA256SUMS.txt";

        // GitHub rejects requests without one, and .NET 4 still defaults to TLS 1.0.
        const string Agent = "Zones-Updater";
        const SecurityProtocolType Tls12 = (SecurityProtocolType)3072;

        /// <summary>How often an automatic install waiting for Zones to be idle looks again.</summary>
        const int IdleRetryMs = 30 * 1000;

        static Control _owner;          // owns the UI thread for cross-thread callbacks
        static AppConfig _config;
        static ReleaseInfo _update;     // the newer release, once one is known
        static bool _checking;
        static bool _updating;          // downloading, for an install asked for or an automatic one
        static string _ready;           // downloaded and checked by the automatic path, waiting to install
        static Version _failed;         // failed to install this run: offered, never retried by itself
        static System.Windows.Forms.Timer _idleTimer;

        /// <summary>
        /// A check started or finished, or an update did: the menu item and the
        /// settings window's button change their words. On the UI thread.
        /// </summary>
        public static event EventHandler UpdateStateChanged;

        /// <summary>The newer release, once a check has found one; null until then.</summary>
        public static ReleaseInfo Available
        {
            get { return _update; }
        }

        public static bool Checking
        {
            get { return _checking; }
        }

        public static bool Updating
        {
            get { return _updating; }
        }

        public static void Init(Control owner, AppConfig config)
        {
            _owner = owner;
            _config = config;
        }

        public static Version Current
        {
            get
            {
                try { return Normalize(Assembly.GetExecutingAssembly().GetName().Version); }
                catch { return new Version(0, 0, 0, 0); }
            }
        }

        /// <summary>The running version as it is shown to a person: "1.0.0".</summary>
        public static string CurrentText
        {
            get { return VersionText(Current); }
        }

        /// <summary>A version as it is shown to a person: the three parts a tag has.</summary>
        public static string VersionText(Version v)
        {
            return v.Major + "." + v.Minor + "." + v.Build;
        }

        /// <summary>
        /// A tag reads "v1.2" while an assembly version is always four parts, and
        /// Version treats a missing part as lower than zero. Pad both out first.
        /// </summary>
        static Version Normalize(Version v)
        {
            if (v == null) return new Version(0, 0, 0, 0);
            return new Version(v.Major, v.Minor,
                v.Build < 0 ? 0 : v.Build, v.Revision < 0 ? 0 : v.Revision);
        }

        static Version ParseTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            string s = tag.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            try { return Normalize(new Version(s)); }
            catch { return null; }
        }

        static WebClient MakeClient()
        {
            try { ServicePointManager.SecurityProtocol |= Tls12; }
            catch { }

            WebClient c = new WebClient();
            c.Headers.Add("User-Agent", Agent);
            return c;
        }

        /// <summary>Looks up the newest release, or null when there is nothing newer.</summary>
        public static ReleaseInfo Check()
        {
            string json;
            using (WebClient c = MakeClient())
            {
                c.Headers.Add("Accept", "application/vnd.github+json");
                json = c.DownloadString(LatestApi);
            }
            return FromJson(json);
        }

        /// <summary>The release GitHub describes, if it is newer than this build and installable.</summary>
        static ReleaseInfo FromJson(string json)
        {
            Dictionary<string, object> root = Json.Obj(Json.Parse(json));
            if (root == null) return null;

            if (Json.Bool(root, "draft", false) || Json.Bool(root, "prerelease", false)) return null;

            Version v = ParseTag(Json.Str(root, "tag_name"));
            if (v == null || v <= Current) return null;

            // A release with no exe is nothing to install, and one with no
            // checksums is nothing to trust.
            string exe = AssetUrl(root, AssetName);
            string sums = AssetUrl(root, SumsName);
            if (exe == null || sums == null) return null;

            ReleaseInfo r = new ReleaseInfo();
            r.Tag = Json.Str(root, "tag_name");
            r.Version = v;
            r.ExeUrl = exe;
            r.SumsUrl = sums;
            r.PageUrl = Json.Str(root, "html_url");
            return r;
        }

        static string AssetUrl(Dictionary<string, object> root, string name)
        {
            object assetsObj;
            if (!root.TryGetValue("assets", out assetsObj)) return null;
            List<object> assets = Json.Arr(assetsObj);
            if (assets == null) return null;

            foreach (object o in assets)
            {
                Dictionary<string, object> a = Json.Obj(o);
                if (a == null) continue;
                if (!string.Equals(Json.Str(a, "name"), name, StringComparison.OrdinalIgnoreCase))
                    continue;

                string url = Json.Str(a, "browser_download_url");
                if (url != null && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return url;
            }
            return null;
        }

        /// <summary>
        /// Checks in the background. The callback gets the release, or null when
        /// there is nothing newer, and the error text when the lookup itself failed.
        /// </summary>
        public static void CheckAsync(Action<ReleaseInfo, string> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                ReleaseInfo r = null;
                string err = null;
                // Offline, rate-limited, no release yet: none of it is worth a crash.
                try { r = Check(); }
                catch (Exception ex) { err = ex.Message; }
                if (done != null) done(r, err);
            });
        }

        /// <summary>Downloads beside the installed exe and returns the temporary path.</summary>
        public static string Download(ReleaseInfo r)
        {
            if (r == null || string.IsNullOrEmpty(r.ExeUrl) || string.IsNullOrEmpty(r.SumsUrl))
                throw new InvalidOperationException("nothing to download");

            string exe = Application.ExecutablePath;
            string tmp = exe + ".new";
            if (File.Exists(tmp)) File.Delete(tmp);

            // The checksums first: without them the exe is not worth fetching.
            string sums;
            using (WebClient c = MakeClient())
                sums = c.DownloadString(r.SumsUrl);

            using (WebClient c = MakeClient())
                c.DownloadFile(r.ExeUrl, tmp);

            // Never hand a half-written, redirected-to-HTML or altered file to File.Move.
            string problem = Rejection(tmp, sums);
            if (problem != null)
            {
                try { File.Delete(tmp); }
                catch { }
                throw new InvalidOperationException(problem);
            }
            return tmp;
        }

        /// <summary>
        /// Why the downloaded file must not be installed, or null when it may.
        /// Size and the MZ header tell a program from an error page; the
        /// checksum the release script published beside it tells this build
        /// from one cut short or changed on the way.
        /// </summary>
        static string Rejection(string path, string sums)
        {
            FileInfo fi = new FileInfo(path);
            if (!fi.Exists || fi.Length < 20000) return "the download was not a program";

            using (FileStream fs = File.OpenRead(path))
            {
                if (fs.ReadByte() != 'M' || fs.ReadByte() != 'Z') return "the download was not a program";
            }

            string expected = SumFor(sums, AssetName);
            if (expected == null) return SumsName + " has no checksum for " + AssetName;

            if (!string.Equals(Sha256(path), expected, StringComparison.OrdinalIgnoreCase))
                return "the download does not match its checksum";
            return null;
        }

        /// <summary>
        /// The hash SHA256SUMS.txt gives <paramref name="name"/>: a line per
        /// asset, the hex digest then the file name, as sha256sum writes them.
        /// Null when it has no line for it.
        /// </summary>
        static string SumFor(string sums, string name)
        {
            if (sums == null) return null;

            foreach (string raw in sums.Split('\n'))
            {
                string line = raw.Trim();
                int gap = line.IndexOfAny(new char[] { ' ', '\t' });
                if (gap <= 0) continue;

                string hash = line.Substring(0, gap);
                string file = line.Substring(gap).Trim();
                if (hash.Length == 64 && string.Equals(file, name, StringComparison.OrdinalIgnoreCase))
                    return hash;
            }
            return null;
        }

        static string Sha256(string path)
        {
            byte[] hash;
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
                hash = sha.ComputeHash(fs);

            StringBuilder sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static void DownloadAsync(ReleaseInfo r, Action<string, string> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string path = null;
                string err = null;
                try { path = Download(r); }
                catch (Exception ex) { err = ex.Message; }
                if (done != null) done(path, err);
            });
        }

        /// <summary>
        /// Swaps the new build in and restarts. Windows will not let a running exe
        /// be overwritten, but it will let it be renamed out of the way - so the
        /// old build steps aside and is swept up on the next start.
        /// </summary>
        public static void Apply(string newExe)
        {
            string exe = Application.ExecutablePath;
            string old = exe + ".old";

            if (File.Exists(old))
            {
                try { File.Delete(old); }
                catch { }
            }

            File.Move(exe, old);
            try
            {
                File.Move(newExe, exe);
            }
            catch
            {
                // Put the working build back rather than leaving nothing to launch.
                try { File.Move(old, exe); }
                catch { }
                throw;
            }

            // Hand the single-instance mutex over before the new build claims it,
            // otherwise the restart sees this process still holding it and quits.
            // The shortcuts too: Windows gives a combination to one program at a
            // time, and the new build registers them as it starts, likely before
            // this one has finished exiting. Installed from the settings window,
            // the new build opens it again, so the restart reads as a refresh.
            string args = SettingsOpen() ? "--after-update --show-settings" : "--after-update";
            Program.ReleaseSingleInstance();
            Hotkey.Suspend();
            System.Diagnostics.Process.Start(exe, args);

            // Out of the notification area now, not as the message loop winds
            // down: the new build's icon is on its way, and two would show.
            Tray.Dispose();
            Application.Exit();
        }

        /// <summary>Clears the build an update stepped aside. Called at startup.</summary>
        public static void CleanupOldBuild()
        {
            try
            {
                string old = Application.ExecutablePath + ".old";
                if (File.Exists(old)) File.Delete(old);

                string tmp = Application.ExecutablePath + ".new";
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch { }
        }

        // ---------- the flow ----------

        /// <summary>
        /// Looks for a newer release. Quiet on the schedule, where the answer is
        /// installed by itself with automatic updates on, and otherwise only shows
        /// up as the wording of the menu item and a button in the settings; from
        /// the menu it reports what it found either way, because a check that
        /// answers nothing reads as a broken one.
        /// </summary>
        public static void CheckForUpdates(bool announce)
        {
            if (_checking || _updating) return;

            // One already known is installed rather than looked for again;
            // unless it failed, when a quiet check looks past it for a newer one.
            if (_update != null && (announce || _update.Version != _failed))
            {
                if (announce) StartUpdate();
                else AutoInstall();
                return;
            }

            _checking = true;
            Changed();

            CheckAsync(delegate(ReleaseInfo r, string err)
            {
                OnUi(delegate
                {
                    _checking = false;
                    // Nothing newer found past a failed one: it is still offered.
                    if (r != null) _update = r;
                    Changed();

                    if (!announce)
                    {
                        AutoInstall();
                        return;
                    }

                    if (r != null)
                    {
                        if (Ask("Zones " + VersionText(r.Version) + " is available.\n\nDownload and install it now?"))
                            StartUpdate();
                    }
                    else if (err != null)
                    {
                        Say("Could not check for updates:\n\n" + err, MessageBoxIcon.Warning);
                    }
                    else
                    {
                        Say("Zones " + CurrentText + " is the latest version.",
                            MessageBoxIcon.Information);
                    }
                });
            });
        }

        /// <summary>
        /// Downloads the newer release found and installs it, restarting into it,
        /// now: someone asked. Already downloaded and waiting for a quiet moment,
        /// it is installed as it is.
        /// </summary>
        public static void StartUpdate()
        {
            if (_update == null || _updating) return;

            if (_ready != null)
            {
                string ready = _ready;
                _ready = null;
                StopIdleTimer();
                FinishUpdate(ready, null);
                return;
            }

            _updating = true;
            Changed();

            ReleaseInfo r = _update;
            DownloadAsync(r, delegate(string path, string err)
            {
                OnUi(delegate { FinishUpdate(path, err); });
            });
        }

        static void FinishUpdate(string path, string err)
        {
            if (err == null && path != null)
            {
                // Apply restarts us, so nothing after this runs on success.
                try { Apply(path); return; }
                catch (Exception ex) { err = ex.Message; }
            }

            _updating = false;
            if (_update != null) _failed = _update.Version;
            Changed();
            Say("The update could not be installed:\n\n" + (err == null ? "no download" : err),
                MessageBoxIcon.Warning);
        }

        /// <summary>
        /// The automatic path: with automatic updates on, downloads and checks the
        /// release found in the background, then installs it once Zones is idle.
        /// Not for a version that already failed this run, which the menu still
        /// offers; quiet throughout, a failure leaving only a line in the log.
        /// </summary>
        static void AutoInstall()
        {
            if (_config == null || !_config.AutoUpdate || _update == null || _updating) return;

            if (_ready != null)
            {
                InstallWhenIdle();
                return;
            }
            if (_update.Version == _failed) return;

            _updating = true;
            Changed();

            ReleaseInfo r = _update;
            DownloadAsync(r, delegate(string path, string err)
            {
                OnUi(delegate
                {
                    _updating = false;
                    if (err != null || path == null)
                    {
                        _failed = r.Version;
                        Program.LogError(new InvalidOperationException(
                            "Automatic update to " + VersionText(r.Version) + " failed: " + err));
                        Changed();
                        return;
                    }

                    _ready = path;
                    Changed();
                    InstallWhenIdle();
                });
            });
        }

        /// <summary>
        /// Installs the downloaded build if nobody is using Zones this moment, and
        /// otherwise looks again in a while. Restarting under a drag would drop the
        /// window mid-air, and under the settings window would close it on someone.
        /// </summary>
        static void InstallWhenIdle()
        {
            // Installed by hand meanwhile, or automatic updates turned off: the
            // menu offers the build instead.
            if (_ready == null || _config == null || !_config.AutoUpdate)
            {
                StopIdleTimer();
                return;
            }

            if (!Idle())
            {
                if (_idleTimer == null)
                {
                    _idleTimer = new System.Windows.Forms.Timer();
                    _idleTimer.Interval = IdleRetryMs;
                    _idleTimer.Tick += delegate { InstallWhenIdle(); };
                }
                _idleTimer.Start();
                return;
            }

            StopIdleTimer();
            string path = _ready;
            _ready = null;
            try
            {
                // Restarts us, so nothing after this runs on success.
                Apply(path);
            }
            catch (Exception ex)
            {
                _failed = _update.Version;
                Program.LogError(ex);
                Changed();
            }
        }

        static void StopIdleTimer()
        {
            if (_idleTimer != null) _idleTimer.Stop();
        }

        /// <summary>
        /// Nobody is using Zones: no window being dragged or zones on screen, and
        /// the settings window closed, which also means no shortcut half pressed
        /// into it.
        /// </summary>
        static bool Idle()
        {
            return !Snapper.Busy && !SettingsOpen();
        }

        static bool SettingsOpen()
        {
            foreach (Form f in Application.OpenForms)
            {
                if (f is SnapForm) return true;
            }
            return false;
        }

        static void Changed()
        {
            EventHandler handler = UpdateStateChanged;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        /// <summary>Runs an action on the UI thread, wherever the caller came from.</summary>
        static void OnUi(MethodInvoker action)
        {
            try
            {
                if (_owner != null && _owner.IsHandleCreated && _owner.InvokeRequired)
                    _owner.BeginInvoke(action);
                else
                    action();
            }
            catch { }
        }

        /// <summary>
        /// Zones lives in the notification area and, between visits to its
        /// settings, has no window of its own, so a dialog it puts up can open
        /// behind everything. A throwaway topmost owner keeps it in front and
        /// off the taskbar.
        /// </summary>
        static DialogResult Dialog(string text, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            using (Form owner = new Form())
            {
                owner.ShowInTaskbar = false;
                owner.FormBorderStyle = FormBorderStyle.None;
                owner.StartPosition = FormStartPosition.Manual;
                owner.Size = new System.Drawing.Size(1, 1);
                owner.Location = new System.Drawing.Point(-32000, -32000);
                owner.TopMost = true;
                owner.Show();
                try
                {
                    return MessageBox.Show(owner, text, "Zones", buttons, icon);
                }
                finally
                {
                    owner.Hide();
                }
            }
        }

        static bool Ask(string text)
        {
            return Dialog(text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        static void Say(string text, MessageBoxIcon icon)
        {
            Dialog(text, MessageBoxButtons.OK, icon);
        }
    }
}
