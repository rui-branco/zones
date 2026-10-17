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

        public static void Start(AppConfig config)
        {
            _config = config;
            _menu = BuildMenu();
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

            ToolStripMenuItem startup = Theme.MenuItem("Start with Windows", 0);
            startup.Checked = _config.StartWithWindows;
            startup.Click += delegate
            {
                _config.StartWithWindows = !_config.StartWithWindows;
                startup.Checked = _config.StartWithWindows;
                SetStartup(_config.StartWithWindows);
                _config.Save();
            };

            _updateItem = Theme.MenuItem("Check for updates", 0xE895);
            _updateItem.Click += delegate
            {
                if (Updater.Available != null) Updater.StartUpdate();
                else Updater.CheckForUpdates(true);
            };

            ToolStripMenuItem autoUpdate = Theme.MenuItem("Install updates automatically", 0);
            autoUpdate.Checked = _config.AutoUpdate;
            autoUpdate.Click += delegate
            {
                _config.AutoUpdate = !_config.AutoUpdate;
                autoUpdate.Checked = _config.AutoUpdate;
                _config.Save();
                // Turned on, it takes up a release already found rather than
                // waiting hours for the next quiet check.
                if (_config.AutoUpdate) Updater.CheckForUpdates(false);
            };

            ToolStripMenuItem version = Theme.MenuItem("Zones " + Updater.CurrentText, 0xE946);
            version.Enabled = false;

            ToolStripMenuItem quit = Theme.MenuItem("Quit", 0xE7E8);
            quit.Click += delegate { Application.Exit(); };

            menu.Items.Add(snap);
            menu.Items.Add(startup);
            menu.Items.Add(_updateItem);
            menu.Items.Add(autoUpdate);
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

        static void SetStartup(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;

                    if (enable) key.SetValue("Zones", "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue("Zones", false);
                }
            }
            catch { }
        }

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
