using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Zones
{
    /// <summary>
    /// A keyboard shortcut, shown as the keys to press and set by pressing
    /// them: click it, or Enter or Space on it, and it records the next
    /// combination, as the shortcut boxes in Windows and PowerToys do. A
    /// text box would take Ctrl+Alt+Z for typing.
    ///
    /// While it records, every key stops here before the window sees it, so
    /// Esc and Alt+F4 cannot close the window under it. Esc on its own
    /// cancels, Backspace or Delete clears, and a shortcut needs Ctrl, Alt or
    /// Win: Shift and a letter is only a capital. Nor can it be one Windows or
    /// every app already answers to, Alt+F4 and the like. The modifiers show
    /// as they go down, and the box grows with them, up to its MaximumSize.
    /// </summary>
    public class ShortcutBox : Control
    {
        const int WM_KEYUP = 0x0101;
        const int WM_SYSKEYUP = 0x0105;
        const int VK_LWIN = 0x5B;
        const int VK_RWIN = 0x5C;

        const string NotSet = "Not set";
        const string Prompt = "Press a shortcut";

        float _s = 1f;
        bool _onCard;
        int _mods;
        int _vk;
        bool _recording;
        int _held;      // while recording, the modifiers down right now, as Hotkey.MOD_* flags
        bool _hover;
        bool _pressed;

        readonly Tween _hoverMotion;    // the keycaps lit under the mouse
        readonly Tween _pressMotion;    // and pressed
        readonly Tween _recordMotion;   // the accent edge of recording, faded in and out

        /// <summary>A new shortcut was recorded, or the old one cleared.</summary>
        public event EventHandler ShortcutChanged;

        /// <summary>Recording started, or ended for whatever reason.</summary>
        public event EventHandler RecordingChanged;

        public ShortcutBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = true;

            _hoverMotion = new Tween(this, 0, Motion.Fast);
            _pressMotion = new Tween(this, 0, Motion.Fast);
            _recordMotion = new Tween(this, 0, Motion.Fast);
        }

        /// <summary>How big a logical pixel is: the owner's DPI scale.</summary>
        public new float Scale
        {
            get { return _s; }
            set
            {
                _s = value;
                Invalidate();
            }
        }

        /// <summary>Sits on a settings card rather than on the window itself.</summary>
        public bool OnCard
        {
            get { return _onCard; }
            set
            {
                _onCard = value;
                Invalidate();
            }
        }

        /// <summary>The shortcut's modifiers, as Hotkey.MOD_* flags.</summary>
        public int Mods
        {
            get { return _mods; }
            set
            {
                _mods = value;
                Invalidate();
            }
        }

        /// <summary>The shortcut's key, a virtual-key code; 0 for none.</summary>
        public int Vk
        {
            get { return _vk; }
            set
            {
                _vk = value;
                Invalidate();
            }
        }

        /// <summary>Waiting for a shortcut to be pressed.</summary>
        public bool Recording
        {
            get { return _recording; }
        }

        int R(float px)
        {
            return (int)Math.Round(px * _s);
        }

        /// <summary>
        /// As wide as the keys as drawn or, while recording, the keys held and
        /// the prompt after them, in device pixels. It changes with the
        /// shortcut and with recording, so the owner lays out again on either.
        /// </summary>
        public int PreferredWidth()
        {
            if (_recording)
            {
                int w = R(10);
                List<string> held = ModifierNames(_held);
                foreach (string key in held) w += KeyWidth(key) + R(4);
                if (held.Count > 0) w += R(4);
                return w + TextWidth(Prompt, Theme.Font(12 * _s)) + R(10);
            }

            List<string> keys = Hotkey.Parts(_mods, _vk);
            if (keys.Count == 0) return KeyWidth(NotSet);
            int width = -R(4);
            foreach (string key in keys) width += KeyWidth(key) + R(4);
            return width;
        }

        int KeyWidth(string name)
        {
            return Math.Max(R(28), TextWidth(name, Theme.Strong(12 * _s)) + R(16));
        }

        static int TextWidth(string text, Font font)
        {
            return TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding).Width;
        }

        /// <summary>The names of the modifiers in <paramref name="mods"/>, in the order Hotkey.Parts writes them.</summary>
        static List<string> ModifierNames(int mods)
        {
            List<string> names = new List<string>();
            if ((mods & Hotkey.MOD_WIN) != 0) names.Add("Win");
            if ((mods & Hotkey.MOD_CONTROL) != 0) names.Add("Ctrl");
            if ((mods & Hotkey.MOD_ALT) != 0) names.Add("Alt");
            if ((mods & Hotkey.MOD_SHIFT) != 0) names.Add("Shift");
            return names;
        }

        /// <summary>
        /// The modifiers held, as Hotkey.MOD_* flags: Ctrl, Alt and Shift from
        /// a key's own flags, and Win from the keyboard, as WinForms has no
        /// flag for it.
        /// </summary>
        static int ModsOf(Keys keyData)
        {
            int mods = 0;
            if ((keyData & Keys.Control) != 0) mods |= Hotkey.MOD_CONTROL;
            if ((keyData & Keys.Alt) != 0) mods |= Hotkey.MOD_ALT;
            if ((keyData & Keys.Shift) != 0) mods |= Hotkey.MOD_SHIFT;
            if ((GetKeyState(VK_LWIN) & 0x8000) != 0 || (GetKeyState(VK_RWIN) & 0x8000) != 0) mods |= Hotkey.MOD_WIN;
            return mods;
        }

        // ---------- recording ----------

        void StartRecording()
        {
            if (_recording) return;
            _recording = true;
            _held = ModsOf(ModifierKeys);
            _recordMotion.Target = 1;
            Focus();
            Invalidate();
            if (RecordingChanged != null) RecordingChanged(this, EventArgs.Empty);
        }

        void StopRecording()
        {
            if (!_recording) return;
            _recording = false;
            _held = 0;
            _recordMotion.Target = 0;
            Invalidate();
            if (RecordingChanged != null) RecordingChanged(this, EventArgs.Empty);
        }

        /// <summary>
        /// Keeps the box as wide as the keys held while recording. Between
        /// recording starting and ending the owner does not lay out again, so
        /// the box sizes itself; MaximumSize keeps it inside the owner's room.
        /// </summary>
        void Fit()
        {
            Width = PreferredWidth();
        }

        /// <summary>A key went down while recording.</summary>
        void Press(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            int mods = ModsOf(keyData);
            if (mods != _held)
            {
                _held = mods;
                Fit();
                Invalidate();
            }

            // A modifier on its own is only the start of a shortcut.
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu
                || key == Keys.LWin || key == Keys.RWin) return;

            if (mods == 0 && key == Keys.Escape)
            {
                StopRecording();
                return;
            }
            if (mods == 0 && (key == Keys.Back || key == Keys.Delete))
            {
                Vk = 0;
                Mods = 0;
                if (ShortcutChanged != null) ShortcutChanged(this, EventArgs.Empty);
                StopRecording();
                return;
            }

            // Shift is not enough on its own: Shift and a letter is typing,
            // and a shortcut that is typing would fire in every text box.
            if ((mods & (Hotkey.MOD_CONTROL | Hotkey.MOD_ALT | Hotkey.MOD_WIN)) == 0) return;

            // Nor one that is already spoken for: it is ignored the same way,
            // and the box goes on waiting for a shortcut it can have.
            if (Reserved(mods, key)) return;

            Mods = mods;
            Vk = (int)key;
            StopRecording();
            if (ShortcutChanged != null) ShortcutChanged(this, EventArgs.Empty);
        }

        /// <summary>
        /// The combinations Windows itself, or every app, already answers to:
        /// closing a window, switching between them, the Start menu, locking
        /// the PC. Some never reach the box at all; the rest would reach Zones
        /// and stop doing what everyone expects of them everywhere else.
        /// </summary>
        static bool Reserved(int mods, Keys key)
        {
            if (mods == Hotkey.MOD_ALT) return key == Keys.F4 || key == Keys.Tab || key == Keys.Escape;
            if (mods == Hotkey.MOD_CONTROL) return key == Keys.Escape;
            if (mods == Hotkey.MOD_WIN) return key == Keys.L;
            if (mods == (Hotkey.MOD_CONTROL | Hotkey.MOD_ALT)) return key == Keys.Delete;
            return false;
        }

        // ---------- painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(_onCard ? Theme.CardBack : Theme.WindowBack))
                g.FillRectangle(b, ClientRectangle);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            if (_recording)
            {
                PaintRecording(g);
                return;
            }

            bool enabled = Enabled;
            List<string> keys = Hotkey.Parts(_mods, _vk);
            bool none = keys.Count == 0;
            if (none) keys.Add(NotSet);

            float hover = enabled ? _hoverMotion.Value : 0;
            float pressed = enabled ? _pressMotion.Value : 0;
            Color fill = Motion.Blend(Motion.Blend(Theme.ControlBack, Theme.ControlHover, hover), Theme.ControlPressed, pressed);
            Color ink = !enabled ? Theme.TextFaint : (none ? Theme.TextDim : Theme.Text);
            int x = 0;
            foreach (string key in keys) x = DrawKey(g, key, x, fill, ink) + R(4);

            // Just out of recording: its accent edge fading away round the keys.
            float recording = _recordMotion.Value;
            if (recording > 0)
            {
                using (GraphicsPath p = Theme.Rounded(RecordingBox(), 4 * _s))
                using (Pen pen = new Pen(Motion.Blend(Color.FromArgb(0, Theme.Accent), Theme.Accent, recording), 1.5f * _s))
                    g.DrawPath(pen, p);
            }

            if (Focused && ShowFocusCues)
            {
                RectangleF ring = ClientRectangle;
                ring.Inflate(-_s, -_s);
                using (GraphicsPath p = Theme.Rounded(ring, 4 * _s))
                using (Pen pen = new Pen(Theme.Text, 2 * _s))
                    g.DrawPath(pen, p);
            }
        }

        /// <summary>The outline of the recording box, inset so all of its accent edge stays inside.</summary>
        RectangleF RecordingBox()
        {
            float edge = 1.5f * _s;
            RectangleF box = new RectangleF(0, R(1), Width, Height - R(2));
            box.Inflate(-edge / 2, -edge / 2);
            return box;
        }

        /// <summary>
        /// Recording: a box edged in the accent, as a text box with the caret
        /// in it is, holding the modifiers down so far as keys and the prompt.
        /// The edge fades in as recording starts.
        /// </summary>
        void PaintRecording(Graphics g)
        {
            using (GraphicsPath p = Theme.Rounded(RecordingBox(), 4 * _s))
            {
                using (SolidBrush b = new SolidBrush(Theme.ControlBack))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(Motion.Blend(Color.FromArgb(0, Theme.Accent), Theme.Accent, _recordMotion.Value), 1.5f * _s))
                    g.DrawPath(pen, p);
            }

            int x = R(10);
            List<string> held = ModifierNames(_held);
            foreach (string key in held) x = DrawKey(g, key, x, Theme.ControlBack, Theme.Text) + R(4);
            if (held.Count > 0) x += R(4);

            // Without GDI+'s padding, so the words start exactly where the
            // keys end, and cut short if the box is held narrower than them.
            using (SolidBrush b = new SolidBrush(Theme.TextDim))
            using (StringFormat f = new StringFormat(StringFormat.GenericTypographic))
            {
                f.LineAlignment = StringAlignment.Center;
                f.FormatFlags |= StringFormatFlags.NoWrap;
                f.Trimming = StringTrimming.EllipsisCharacter;
                g.DrawString(Prompt, Theme.Font(12 * _s), b, new RectangleF(x, 0, Math.Max(0, Width - R(10) - x), Height), f);
            }
        }

        /// <summary>
        /// One key as a keycap, from <paramref name="x"/>: a rounded plate with
        /// a hairline edge and a darker lip along the inside of its bottom, the
        /// name in the middle. Returns its right edge.
        /// </summary>
        int DrawKey(Graphics g, string name, int x, Color fill, Color ink)
        {
            int h = R(26);
            Rectangle key = new Rectangle(x, (Height - h) / 2, KeyWidth(name), h);
            RectangleF plate = key;
            plate.Inflate(-0.5f, -0.5f);

            // The lip is the plate's own shape in the darker colour, with the
            // plate laid over it a pixel short: a line along the bottom that
            // follows the corners round, under the edge drawn last.
            Color lip = Theme.Light ? Color.FromArgb(0xCC, 0xCC, 0xCC) : Color.FromArgb(0x1E, 0x1E, 0x1E);
            Theme.FillRounded(g, lip, plate, 4 * _s);
            Theme.FillRounded(g, fill, new RectangleF(plate.X, plate.Y, plate.Width, plate.Height - 1.5f), 4 * _s);
            using (GraphicsPath p = Theme.Rounded(plate, 4 * _s))
            using (Pen pen = new Pen(Theme.ControlStroke, 1f))
                g.DrawPath(pen, p);

            using (SolidBrush b = new SolidBrush(ink))
            using (StringFormat f = new StringFormat())
            {
                f.Alignment = StringAlignment.Center;
                f.LineAlignment = StringAlignment.Center;
                f.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(name, Theme.Strong(12 * _s), b, key, f);
            }
            return key.Right;
        }

        // ---------- input ----------

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Animate();
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Animate();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = ClientRectangle.Contains(e.Location);
            if (hover == _hover) return;
            _hover = hover;
            Animate();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled || e.Button != MouseButtons.Left) return;
            _pressed = true;
            Animate();
            Focus();
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool pressed = _pressed;
            _pressed = false;
            Animate();
            Invalidate();
            if (pressed && Enabled && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
                StartRecording();
        }

        /// <summary>Points the keycaps' fades at where the mouse has left things; pressed only while still over the box.</summary>
        void Animate()
        {
            _hoverMotion.Target = _hover ? 1 : 0;
            _pressMotion.Target = _hover && _pressed ? 1 : 0;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (_recording) return true;
            if (keyData == Keys.Enter || keyData == Keys.Space) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_recording || !Enabled || (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Space)) return;
            StartRecording();
            e.Handled = true;
        }

        // While recording, every key ends here, before the window sees it:
        // Esc would close it, Alt+F4 too, and Tab would move away mid-shortcut.
        // The flags carry Ctrl, Alt and Shift as they were when the key went down.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!_recording) return base.ProcessCmdKey(ref msg, keyData);
            Press(keyData);
            return true;
        }

        // A key let go while recording: the keys shown drop to the modifiers
        // still held. Kept from the window as well, or letting go of Alt on its
        // own would wake the window's menu.
        protected override bool ProcessKeyMessage(ref Message m)
        {
            if (_recording && (m.Msg == WM_KEYUP || m.Msg == WM_SYSKEYUP))
            {
                int mods = ModsOf(ModifierKeys);
                if (mods != _held)
                {
                    _held = mods;
                    Fit();
                    Invalidate();
                }
                return true;
            }
            return base.ProcessKeyMessage(ref m);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _hover = false;
            _pressed = false;
            Animate();
            if (!Enabled) StopRecording();
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        // Clicked away, or another window came up: no new shortcut, so the
        // old one stays.
        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            StopRecording();
            Invalidate();
        }

        protected override void OnChangeUICues(UICuesEventArgs e)
        {
            base.OnChangeUICues(e);
            Invalidate();
        }

        [DllImport("user32.dll")]
        static extern short GetKeyState(int vk);
    }
}
