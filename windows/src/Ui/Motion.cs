using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Zones
{
    /// <summary>
    /// A number that eases from where it is to where it is told to go: how
    /// far a toggle's knob has slid, how lit a hovered button is. Controls
    /// keep their state as before and point a Tween at it; paint reads
    /// <see cref="Value"/>, so a state change is drawn over a few frames
    /// instead of all at once.
    ///
    /// Told to go somewhere else halfway, it sets off from where it has got
    /// to, so a quick in-and-out of the mouse reverses instead of jumping.
    /// The value is worked out from the clock when it is read, not counted
    /// in ticks, so a frame painted late shows the right moment anyway.
    /// </summary>
    public sealed class Tween
    {
        readonly Action _invalidate;
        int _duration;
        float _from;
        float _target;
        long _start;
        bool _running;

        /// <summary>From linear time, 0 to 1, to how far along the value is. Windows' fast out, slow in by default.</summary>
        public Func<float, float> Easing = Motion.EaseOut;

        /// <summary>Repaints <paramref name="owner"/> on every step.</summary>
        public Tween(Control owner, float value, int durationMs)
            : this(delegate { if (!owner.IsDisposed) owner.Invalidate(); }, value, durationMs)
        {
        }

        /// <summary>Calls <paramref name="invalidate"/> on every step: for an owner that is not a control, or only part of one.</summary>
        public Tween(Action invalidate, float value, int durationMs)
        {
            _invalidate = invalidate;
            _from = value;
            _target = value;
            _duration = durationMs;
        }

        /// <summary>How long a move to a new target takes, in milliseconds.</summary>
        public int Duration
        {
            get { return _duration; }
            set { _duration = value; }
        }

        /// <summary>Where the value is this moment, eased.</summary>
        public float Value
        {
            get
            {
                if (!_running) return _target;
                float t = (Motion.Now - _start) / (float)_duration;
                if (t >= 1) return _target;
                return _from + (_target - _from) * Easing(Math.Max(0f, t));
            }
        }

        /// <summary>
        /// Where the value is going. Set to somewhere new, it sets off from
        /// where it is now; with Windows' animation effects off, it is there
        /// at once.
        /// </summary>
        public float Target
        {
            get { return _target; }
            set
            {
                if (value == _target) return;
                if (!Motion.Enabled || _duration <= 0)
                {
                    Snap(value);
                    return;
                }

                _from = Value;
                _target = value;
                _start = Motion.Now;
                if (_running) return;
                _running = true;
                Motion.Add(this);
            }
        }

        /// <summary>Moving towards the target, not there yet.</summary>
        public bool Running
        {
            get { return _running; }
        }

        /// <summary>Puts the value at <paramref name="value"/> straight away, without moving there.</summary>
        public void Snap(float value)
        {
            bool changed = _running || value != _target;
            _from = value;
            _target = value;
            if (_running)
            {
                _running = false;
                Motion.Remove(this);
            }
            if (changed) _invalidate();
        }

        /// <summary>A tick of the shared clock: a repaint, and off the clock once there.</summary>
        internal void Step(long now)
        {
            if (!_running) return;
            if (now - _start >= _duration)
            {
                _running = false;
                Motion.Remove(this);
            }
            _invalidate();
        }
    }

    /// <summary>
    /// The one clock every <see cref="Tween"/> runs on: a single UI timer for
    /// the whole app, running only while something is on the move, so an
    /// idle window costs no CPU at all.
    ///
    /// Follows Windows' own "Animation effects" setting: with it off, every
    /// Tween goes straight to its target, as Windows' own controls do.
    /// </summary>
    public static class Motion
    {
        /// <summary>Hover and press fades.</summary>
        public const int Fast = 83;

        /// <summary>Toggles and selection.</summary>
        public const int Normal = 167;

        /// <summary>Layouts changing shape, indicators sliding.</summary>
        public const int Slow = 250;

        const int SPI_GETCLIENTAREAANIMATION = 0x1042;

        static readonly Stopwatch _clock = Stopwatch.StartNew();
        static readonly List<Tween> _running = new List<Tween>();
        static readonly List<Tween> _frame = new List<Tween>();   // the running ones as a tick found them, reused
        static Timer _timer;
        static bool _enabled;

        static Motion()
        {
            _enabled = ReadEnabled();
            SystemEvents.UserPreferenceChanged += delegate { _enabled = ReadEnabled(); };
        }

        /// <summary>Windows' animation effects are on: false, and every Tween snaps.</summary>
        public static bool Enabled
        {
            get { return _enabled; }
        }

        /// <summary>Milliseconds on a clock that only goes forward.</summary>
        internal static long Now
        {
            get { return _clock.ElapsedMilliseconds; }
        }

        /// <summary>Cubic ease-out: quick to leave, gentle to arrive, the curve Windows moves things on.</summary>
        public static float EaseOut(float t)
        {
            float u = 1 - t;
            return 1 - u * u * u;
        }

        /// <summary>From <paramref name="a"/> to <paramref name="b"/>, alpha included; <paramref name="t"/> is held to 0..1.</summary>
        public static Color Blend(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)Math.Round(a.A + (b.A - a.A) * t),
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        public static RectangleF Lerp(RectangleF a, RectangleF b, float t)
        {
            return new RectangleF(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t), Lerp(a.Width, b.Width, t), Lerp(a.Height, b.Height, t));
        }

        internal static void Add(Tween t)
        {
            _running.Add(t);
            if (_timer == null)
            {
                _timer = new Timer();
                _timer.Interval = 15;
                _timer.Tick += delegate { Tick(); };
            }
            if (!_timer.Enabled) _timer.Start();
        }

        internal static void Remove(Tween t)
        {
            _running.Remove(t);
            if (_running.Count == 0 && _timer != null) _timer.Stop();
        }

        // Stepped from a copy: a step can finish its tween, and a repaint it
        // asks for can start or stop others, either of which changes the list.
        // Copied by hand: AddRange makes a new array every time.
        static void Tick()
        {
            long now = Now;
            for (int i = 0; i < _running.Count; i++) _frame.Add(_running[i]);
            try
            {
                foreach (Tween t in _frame) t.Step(now);
            }
            finally
            {
                _frame.Clear();
            }
        }

        static bool ReadEnabled()
        {
            try
            {
                bool on = true;
                if (!SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, ref on, 0)) return true;
                return on;
            }
            catch { return true; }
        }

        [DllImport("user32.dll")]
        static extern bool SystemParametersInfo(int action, int param, ref bool value, int winIni);
    }
}
