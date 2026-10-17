using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Zones
{
    /// <summary>
    /// Zones' system-wide keyboard shortcuts, one per slot: next and previous
    /// zone, next and previous size. Going back has its own shortcut, set like
    /// any other, rather than being the forward one with Shift added: that
    /// left no way to put "back" on a key of its own.
    ///
    /// Registered with Windows (RegisterHotKey) rather than read from a
    /// keyboard hook: Windows only tells us about our own combinations, from
    /// whatever app has the keyboard, and refuses one outright when another
    /// program already owns it, which is what <see cref="IsFailed"/> reports.
    /// The registrations belong to a hidden window of ours, so they arrive on
    /// the UI thread like any other message.
    /// </summary>
    public static class Hotkey
    {
        public const int MOD_ALT = 0x1;
        public const int MOD_CONTROL = 0x2;
        public const int MOD_SHIFT = 0x4;
        public const int MOD_WIN = 0x8;
        public const int MOD_ALL = MOD_ALT | MOD_CONTROL | MOD_SHIFT | MOD_WIN;

        /// <summary>Moves the active window on to its next zone.</summary>
        public const int SlotZone = 0;
        /// <summary>Flips the active window's display to its next layout size.</summary>
        public const int SlotSize = 1;
        /// <summary>Moves the active window back to its previous zone.</summary>
        public const int SlotZoneBack = 2;
        /// <summary>Flips the active window's display back to its previous size.</summary>
        public const int SlotSizeBack = 3;
        const int SlotCount = 4;

        const int MOD_NOREPEAT = 0x4000;     // held down, it fires once
        const int WM_HOTKEY = 0x0312;

        static Sink _sink;
        static readonly int[] _mods = new int[SlotCount];
        static readonly int[] _vk = new int[SlotCount];
        static readonly bool[] _registered = new bool[SlotCount];
        static readonly bool[] _failed = new bool[SlotCount];

        /// <summary>
        /// A shortcut was pressed: its slot (the step is always +1; which way
        /// to go is the slot's). On the UI thread.
        /// </summary>
        public static event Action<int, int> Pressed;

        /// <summary>The last <see cref="Set"/> for this slot asked for a shortcut another program already has.</summary>
        public static bool IsFailed(int slot)
        {
            return slot >= 0 && slot < SlotCount && _failed[slot];
        }

        // One registration per slot.
        static int IdOf(int slot) { return slot + 1; }

        /// <summary>
        /// Makes <paramref name="mods"/> + <paramref name="vk"/> the shortcut
        /// for <paramref name="slot"/>, replacing any before it; a
        /// <paramref name="vk"/> of 0 means none. False when Windows refuses it
        /// because it is taken. Call on the UI thread.
        /// </summary>
        public static bool Set(int slot, int mods, int vk)
        {
            if (slot < 0 || slot >= SlotCount) return false;

            Unregister(slot);
            _mods[slot] = mods & MOD_ALL;
            _vk[slot] = vk & 0xFF;
            _failed[slot] = false;
            if (_vk[slot] == 0) return true;

            if (_sink == null) _sink = new Sink();

            if (!RegisterHotKey(_sink.Handle, IdOf(slot), _mods[slot] | MOD_NOREPEAT, _vk[slot]))
            {
                _failed[slot] = true;
                return false;
            }
            _registered[slot] = true;
            return true;
        }

        /// <summary>
        /// Lets go of every shortcut for a while, keeping what they were: while
        /// a new one is being typed in, the old ones must reach the window
        /// recording it.
        /// </summary>
        public static void Suspend()
        {
            for (int slot = 0; slot < SlotCount; slot++) Unregister(slot);
        }

        /// <summary>Takes the shortcuts back after <see cref="Suspend"/>. False if any is now taken.</summary>
        public static bool Resume()
        {
            bool ok = true;
            for (int slot = 0; slot < SlotCount; slot++)
            {
                if (!Set(slot, _mods[slot], _vk[slot])) ok = false;
            }
            return ok;
        }

        static void Unregister(int slot)
        {
            if (!_registered[slot] || _sink == null) return;
            UnregisterHotKey(_sink.Handle, IdOf(slot));
            _registered[slot] = false;
        }

        // ---------- names ----------

        /// <summary>
        /// The keys of a shortcut, one name each, in the order Windows writes
        /// them: Win, Ctrl, Alt, Shift, then the key. Empty for none.
        /// </summary>
        public static List<string> Parts(int mods, int vk)
        {
            List<string> parts = new List<string>();
            if (vk == 0) return parts;
            if ((mods & MOD_WIN) != 0) parts.Add("Win");
            if ((mods & MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((mods & MOD_ALT) != 0) parts.Add("Alt");
            if ((mods & MOD_SHIFT) != 0) parts.Add("Shift");
            parts.Add(KeyName(vk));
            return parts;
        }

        /// <summary>The shortcut as one line, "Ctrl + Alt + Z"; empty for none.</summary>
        public static string Text(int mods, int vk)
        {
            return string.Join(" + ", Parts(mods, vk).ToArray());
        }

        static string KeyName(int vk)
        {
            if ((vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A)) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            if (vk >= 0x60 && vk <= 0x69) return "Num " + (vk - 0x60);

            switch (vk)
            {
                case 0x08: return "Backspace";
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x13: return "Pause";
                case 0x1B: return "Esc";
                case 0x20: return "Space";
                case 0x21: return "Page Up";
                case 0x22: return "Page Down";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x25: return "Left";
                case 0x26: return "Up";
                case 0x27: return "Right";
                case 0x28: return "Down";
                case 0x2C: return "Print Screen";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
            }

            // Punctuation differs from one keyboard layout to the next: ask
            // the current layout what the key types. The top bit marks a dead
            // key, which still names its character.
            uint ch = MapVirtualKey((uint)vk, MAPVK_VK_TO_CHAR) & 0x7FFFFFFF;
            if (ch > 0x20) return char.ToUpperInvariant((char)ch).ToString();
            return ((Keys)vk).ToString();
        }

        /// <summary>The hidden window the shortcuts are registered to.</summary>
        sealed class Sink : NativeWindow
        {
            public Sink()
            {
                CreateHandle(new CreateParams());
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    // Out of Windows' message loop: whatever goes wrong stays here.
                    try
                    {
                        int id = m.WParam.ToInt32();
                        Action<int, int> handler = Pressed;
                        if (handler != null && id >= 1 && id <= SlotCount) handler(id - 1, 1);
                    }
                    catch (Exception ex)
                    {
                        Program.LogError(ex);
                    }
                    return;
                }
                base.WndProc(ref m);
            }
        }

        const uint MAPVK_VK_TO_CHAR = 2;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint uCode, uint uMapType);
    }
}
