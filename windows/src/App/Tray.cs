using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Zones
{
    /// <summary>
    /// Zones' icon in the notification area, and its menu: the one thing on
    /// screen that says Zones is running, between drags. A click opens the
    /// settings, as the bold first item of the menu says; the rest is what
    /// every resident app needs, starting with Windows, updating and quitting.
    /// </summary>
    public static class Tray
    {
        static AppConfig _config;
        static NotifyIcon _icon;
        static ContextMenuStrip _menu;
        static ToolStripMenuItem _updateItem;
        static ToolStripMenuItem _startupItem;
        static ToolStripMenuItem _autoUpdateItem;

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>
        /// "Start with Windows" or automatic updates was turned on or off, by
        /// the menu or the settings window: the settings window's switches
        /// follow, as the menu's checkmarks do.
        /// </summary>
        public static event EventHandler SettingsChanged;

        public static void Start(AppConfig config)
        {
            _config = config;
            _menu = BuildMenu();

            // Starting with Windows, but the Run key no longer says this exe:
            // Zones was moved or reinstalled somewhere else, or a cleanup tool
            // took the value. It is told again, so the setting keeps working.
            if (_config.StartWithWindows && !StartupIsHere()) SetStartup(true);

            Updater.UpdateStateChanged += delegate { RefreshUpdateItem(); };

            _icon = new NotifyIcon();
            _icon.Text = "Zones";
            _icon.Icon = AppIcon();
            _icon.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) SnapForm.ShowFor(_config);
                else if (e.Button == MouseButtons.Right) ShowMenu();
            };
            _icon.Visible = true;
        }

        /// <summary>
        /// Takes the icon out of the notification area. Called on the way out:
        /// an icon left behind stays there, dead, until the mouse passes over it.
        /// </summary>
        public static void Dispose()
        {
            if (_icon == null) return;
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }

        /// <summary>The exe's own icon, at the size the notification area shows.</summary>
        static Icon AppIcon()
        {
            try
            {
                using (Icon exe = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                    return new Icon(exe, SystemInformation.SmallIconSize);
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        static ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            Theme.StyleMenu(menu);

            ToolStripMenuItem snap = Theme.MenuItem("Snap layouts...", 0xE80A);
            snap.Click += delegate { SnapForm.ShowFor(_config); };

            _startupItem = Theme.MenuItem("Start with Windows", 0);
            _startupItem.Checked = _config.StartWithWindows;
            _startupItem.Click += delegate { SetStartWithWindows(!_config.StartWithWindows); };

            _updateItem = Theme.MenuItem("Check for updates", 0xE895);
            _updateItem.Click += delegate
            {
                if (Updater.Available != null) Updater.StartUpdate();
                else Updater.CheckForUpdates(true);
            };

            _autoUpdateItem = Theme.MenuItem("Install updates automatically", 0);
            _autoUpdateItem.Checked = _config.AutoUpdate;
            _autoUpdateItem.Click += delegate { SetAutoUpdate(!_config.AutoUpdate); };

            ToolStripMenuItem version = Theme.MenuItem("Zones " + Updater.CurrentText, 0xE946);
            version.Enabled = false;

            ToolStripMenuItem quit = Theme.MenuItem("Quit", 0xE7E8);
            quit.Click += delegate { Application.Exit(); };

            menu.Items.Add(snap);
            menu.Items.Add(_startupItem);
            menu.Items.Add(_updateItem);
            menu.Items.Add(_autoUpdateItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(version);
            menu.Items.Add(quit);

            // Bold, as Windows marks the default item: what a plain click on
            // the icon does. Set once it is on the menu, so it is the menu's
            // own font made bold rather than the stock one.
            snap.Font = new Font(snap.Font, FontStyle.Bold);
            RefreshUpdateItem();
            return menu;
        }

        /// <summary>
        /// Starts Zones with Windows or stops it: the menu's item and the
        /// settings window's switch both come here, so they do the same.
        /// </summary>
        public static void SetStartWithWindows(bool on)
        {
            if (_config == null) return;
            _config.StartWithWindows = on;
            SetStartup(on);
            _config.Save();
            SettingChanged();
        }

        /// <summary>Turns automatic updates on or off, from the menu or the settings window.</summary>
        public static void SetAutoUpdate(bool on)
        {
            if (_config == null) return;
            _config.AutoUpdate = on;
            _config.Save();
            SettingChanged();
            // Turned on, it takes up a release already found rather than
            // waiting hours for the next quiet check.
            if (on) Updater.CheckForUpdates(false);
        }

        /// <summary>The checkmarks show the settings as they are now, and the settings window hears of it.</summary>
        static void SettingChanged()
        {
            if (_startupItem != null) _startupItem.Checked = _config.StartWithWindows;
            if (_autoUpdateItem != null) _autoUpdateItem.Checked = _config.AutoUpdate;
            EventHandler handler = SettingsChanged;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        /// <summary>The update item says where the updater is: checking, installing, or what it found.</summary>
        static void RefreshUpdateItem()
        {
            if (_updateItem == null) return;

            ReleaseInfo update = Updater.Available;
            if (Updater.Updating) _updateItem.Text = "Updating...";
            else if (Updater.Checking) _updateItem.Text = "Checking for updates...";
            else if (update != null) _updateItem.Text = "Update to Zones " + Updater.VersionText(update.Version);
            else _updateItem.Text = "Check for updates";

            _updateItem.Enabled = !Updater.Checking && !Updater.Updating;
        }

        static void ShowMenu()
        {
            _menu.Show(Cursor.Position);

            // A menu that was never activated never hears about a click
            // somewhere else, so it would sit there until something was
            // picked. This is the trick tray menus have always needed.
            SetForegroundWindow(_menu.Handle);
        }

        /// <summary>The Run key's value for this exe: its path, quoted.</summary>
        static string StartupCommand()
        {
            return "\"" + Application.ExecutablePath + "\"";
        }

        static void SetStartup(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;

                    if (enable) key.SetValue("Zones", StartupCommand());
                    else key.DeleteValue("Zones", false);
                }
            }
            catch { }
        }

        /// <summary>Whether the Run key starts this exe. Unreadable, it is taken to: nothing to repair then.</summary>
        static bool StartupIsHere()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (key == null) return false;
                    return string.Equals(key.GetValue("Zones") as string, StartupCommand(), StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return true;
            }
        }

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
