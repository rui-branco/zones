using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Zones
{
    public class AppConfig
    {
        /// <summary>Whether to launch at Windows startup.</summary>
        public bool StartWithWindows { get; set; }

        /// <summary>Whether dragging a window offers snap zones at all.</summary>
        public bool SnapEnabled { get; set; }
        /// <summary>
        /// Zones show only while Shift is held (true), or on every drag with
        /// Shift holding them back (false).
        /// </summary>
        public bool SnapHoldShift { get; set; }
        /// <summary>The gap around each zone, in logical pixels: scaled to each display.</summary>
        public int SnapSpacing { get; set; }
        /// <summary>
        /// Each display's own layouts, by device name (\\.\DISPLAY1): one or
        /// more sizes, one active. A display with none here gets the default
        /// for its shape; see LayoutsFor.
        /// </summary>
        public Dictionary<string, DisplayLayouts> SnapLayouts { get; private set; }
        /// <summary>
        /// The shortcut that moves the active window on to its next zone:
        /// Hotkey.MOD_* flags and a virtual-key code. A key of 0 means none.
        /// </summary>
        public int SnapCycleMods { get; set; }
        public int SnapCycleKey { get; set; }
        /// <summary>
        /// The shortcut that flips the active window's display to its next
        /// layout size. Same encoding as the cycle shortcut.
        /// </summary>
        public int SnapSizeMods { get; set; }
        public int SnapSizeKey { get; set; }
        /// <summary>
        /// The shortcuts that go the other way: the previous zone, the
        /// previous size. They start as the forward ones with Shift added.
        /// </summary>
        public int SnapCyclePrevMods { get; set; }
        public int SnapCyclePrevKey { get; set; }
        public int SnapSizePrevMods { get; set; }
        public int SnapSizePrevKey { get; set; }

        public AppConfig()
        {
            StartWithWindows = false;
            SnapEnabled = true;
            SnapHoldShift = true;
            SnapSpacing = 8;
            SnapLayouts = new Dictionary<string, DisplayLayouts>(StringComparer.OrdinalIgnoreCase);
            SnapCycleMods = Hotkey.MOD_CONTROL | Hotkey.MOD_ALT;
            SnapCycleKey = 0x5A;    // Z
            SnapSizeMods = Hotkey.MOD_CONTROL | Hotkey.MOD_ALT;
            SnapSizeKey = 0x58;     // X
            SnapCyclePrevMods = Hotkey.MOD_CONTROL | Hotkey.MOD_ALT | Hotkey.MOD_SHIFT;
            SnapCyclePrevKey = 0x5A;
            SnapSizePrevMods = Hotkey.MOD_CONTROL | Hotkey.MOD_ALT | Hotkey.MOD_SHIFT;
            SnapSizePrevKey = 0x58;
        }

        /// <summary>
        /// A display's layouts: the ones set up for it, or a single default for
        /// its shape. The default is not stored, so a display keeps following
        /// it until someone actually chooses something; <see cref="Keep"/>
        /// stores what was returned.
        /// </summary>
        public DisplayLayouts LayoutsFor(Display display)
        {
            DisplayLayouts layouts;
            if (display.Device != null && SnapLayouts.TryGetValue(display.Device, out layouts) && layouts != null)
                return layouts;
            return new DisplayLayouts(ZoneLayout.Default(display.Work.Size));
        }

        /// <summary>The layout a display is using now: its active size.</summary>
        public ZoneLayout LayoutFor(Display display)
        {
            return LayoutsFor(display).Current;
        }

        /// <summary>Stores a display's layouts (for instance a default that has just been edited).</summary>
        public void Keep(Display display, DisplayLayouts layouts)
        {
            if (display.Device != null) SnapLayouts[display.Device] = layouts;
        }

        /// <summary>The widest gap the settings window offers, in logical pixels.</summary>
        public const int MaxSnapSpacing = 48;

        public static string Dir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Zones");
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(Dir, "config.json"); }
        }

        /// <summary>
        /// Whether settings have ever been saved. Nothing is written until
        /// something is, so no file yet means Zones has never run here: the
        /// first run, when the settings window opens by itself.
        /// </summary>
        public static bool Exists
        {
            get { return File.Exists(FilePath); }
        }

        /// <summary>The previous save, kept by every save as it replaces the file.</summary>
        public static string BackupPath
        {
            get { return FilePath + ".bak"; }
        }

        /// <summary>
        /// Reads the settings, falling back to the previous save when the file
        /// is unreadable. The layouts are hours of someone's fiddling: a file
        /// cut short by a crash or a full disk must not quietly turn into the
        /// defaults, which the next change would then save over it for good.
        /// So the bad file is moved aside, not overwritten, and the last good
        /// save is used instead.
        /// </summary>
        public static AppConfig Load()
        {
            if (!File.Exists(FilePath)) return new AppConfig();

            AppConfig cfg = Read(FilePath);
            if (cfg != null) return cfg;

            try
            {
                File.Move(FilePath, Path.Combine(Dir,
                    "config.unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"));
            }
            catch { }

            cfg = File.Exists(BackupPath) ? Read(BackupPath) : null;
            return cfg ?? new AppConfig();
        }

        /// <summary>One settings file, or null if it cannot be read as one.</summary>
        static AppConfig Read(string path)
        {
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                Dictionary<string, object> root = Json.Obj(Json.Parse(json));
                if (root == null) return null;

                AppConfig cfg = new AppConfig();

                cfg.StartWithWindows = Json.Bool(root, "StartWithWindows", false);
                cfg.SnapEnabled = Json.Bool(root, "SnapEnabled", true);
                cfg.SnapHoldShift = Json.Bool(root, "SnapHoldShift", true);
                cfg.SnapSpacing = Json.Int(root, "SnapSpacing", 8);
                cfg.SnapCycleMods = Json.Int(root, "SnapCycleMods", cfg.SnapCycleMods) & Hotkey.MOD_ALL;
                cfg.SnapCycleKey = Json.Int(root, "SnapCycleKey", cfg.SnapCycleKey) & 0xFF;
                cfg.SnapSizeMods = Json.Int(root, "SnapSizeMods", cfg.SnapSizeMods) & Hotkey.MOD_ALL;
                cfg.SnapSizeKey = Json.Int(root, "SnapSizeKey", cfg.SnapSizeKey) & 0xFF;
                cfg.SnapCyclePrevMods = Json.Int(root, "SnapCyclePrevMods", cfg.SnapCyclePrevMods) & Hotkey.MOD_ALL;
                cfg.SnapCyclePrevKey = Json.Int(root, "SnapCyclePrevKey", cfg.SnapCyclePrevKey) & 0xFF;
                cfg.SnapSizePrevMods = Json.Int(root, "SnapSizePrevMods", cfg.SnapSizePrevMods) & Hotkey.MOD_ALL;
                cfg.SnapSizePrevKey = Json.Int(root, "SnapSizePrevKey", cfg.SnapSizePrevKey) & 0xFF;

                object layouts;
                if (root.TryGetValue("SnapLayouts", out layouts) && Json.Obj(layouts) != null)
                {
                    foreach (KeyValuePair<string, object> kv in Json.Obj(layouts))
                    {
                        DisplayLayouts display = DisplayLayouts.FromJson(kv.Value);
                        if (display != null) cfg.SnapLayouts[kv.Key] = display;
                    }
                }

                // Sanity clamps
                if (cfg.SnapSpacing < 0) cfg.SnapSpacing = 0;
                if (cfg.SnapSpacing > MaxSnapSpacing) cfg.SnapSpacing = MaxSnapSpacing;

                return cfg;
            }
            catch
            {
                // A corrupt config must never stop the app running.
                return null;
            }
        }

        public bool Save()
        {
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);

                Dictionary<string, object> root = new Dictionary<string, object>();
                root["StartWithWindows"] = StartWithWindows;
                root["SnapEnabled"] = SnapEnabled;
                root["SnapHoldShift"] = SnapHoldShift;
                root["SnapSpacing"] = SnapSpacing;
                root["SnapCycleMods"] = SnapCycleMods;
                root["SnapCycleKey"] = SnapCycleKey;
                root["SnapSizeMods"] = SnapSizeMods;
                root["SnapSizeKey"] = SnapSizeKey;
                root["SnapCyclePrevMods"] = SnapCyclePrevMods;
                root["SnapCyclePrevKey"] = SnapCyclePrevKey;
                root["SnapSizePrevMods"] = SnapSizePrevMods;
                root["SnapSizePrevKey"] = SnapSizePrevKey;

                Dictionary<string, object> layouts = new Dictionary<string, object>();
                foreach (KeyValuePair<string, DisplayLayouts> kv in SnapLayouts)
                    layouts[kv.Key] = kv.Value.ToJson();
                root["SnapLayouts"] = layouts;

                // Written beside the real file and swapped in whole, the old one
                // kept as the backup: a save cut short leaves the last good file
                // where it was, never half of a new one.
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Json.Write(root), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, BackupPath, true);
                else File.Move(tmp, FilePath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
