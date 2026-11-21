using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Shiying
{
    /// <summary>
    /// 手动长图：用户自己滚动，后台不断截选区，和上一张采纳的画面比对位移，结束时拼接。
    /// 始终和最后采纳的一屏比较，滚过头往回滚一点就能接上。
    /// </summary>
    static class ScrollSession
    {
        const int SampleMilliseconds = 60;

        static ScrollSessionBar _bar;
        static RegionFrame _frame;
        static Thread _worker;
        static volatile bool _stop;
        static bool _finishing;
        static Rectangle _region;
        static MainForm _owner;
        static List<Bitmap> _frames;
        static List<int> _offsets;
        static int _axis;
        static string _endNote;

        public static bool Active
        {
            get { return _bar != null; }
        }

        public static void Start(MainForm owner, Rectangle region)
        {
            if (Active)
            {
                return;
            }

            if (region.Width < 8 || region.Height < 8)
            {
                MessageBox.Show("选区太小。", "拾影", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _owner = owner;
            _region = region;
            _stop = false;
            _finishing = false;
            _axis = 0;
            _endNote = null;
            _frames = new List<Bitmap>();
            _offsets = new List<int>();

            Point center = new Point(region.Left + region.Width / 2, region.Top + region.Height / 2);
            NativeMethods.ForceForeground(NativeMethods.WindowFromPoint(center));

            _frame = new RegionFrame(region);
            _frame.Show();
            _bar = new ScrollSessionBar();
            _bar.DoneClicked += (s, e) => Finish();
            _bar.Place(region);
            _bar.Show();
            _bar.SetText("在框内滚动内容 · " + StopKeys());

            _worker = new Thread(Work);
            _worker.IsBackground = true;
            _worker.Name = "ScrollSession";
            _worker.Start();
        }

        public static async void Finish()
        {
            if (!Active || _finishing)
            {
                return;
            }

            _finishing = true;
            _stop = true;
            _bar.Saving();
            ScrollCapture.Outcome outcome = null;
            try
            {
                Thread worker = _worker;
                await Task.Run(() => worker.Join());
                outcome = await Task.Run(() => Build());
            }
            catch (Exception ex)
            {
                outcome = new ScrollCapture.Outcome { Message = ex.Message };
            }
            finally
            {
                CloseUi();
                _worker = null;
                _finishing = false;
            }

            if (outcome.Ok && outcome.Image != null)
            {
                var result = new ResultForm(outcome);
                result.Show();
                result.TopMost = true;
                result.Activate();
                result.TopMost = false;
            }
            else
            {
                MessageBox.Show(outcome.Message ?? "长图拼接失败", "拾影", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        static string StopKeys()
        {
            return "Esc 或 " + AppSettings.FormatGesture(AppSettings.Gesture) + " 完成";
        }

        static void CloseUi()
        {
            if (_bar != null)
            {
                _bar.Close();
                _bar = null;
            }

            if (_frame != null)
            {
                _frame.Close();
                _frame = null;
            }
        }

        static void Post(string text)
        {
            ScrollSessionBar bar = _bar;
            if (bar == null || bar.IsDisposed || !bar.IsHandleCreated)
            {
                return;
            }

            try
            {
                bar.BeginInvoke(new Action(() => bar.SetText(text)));
            }
            catch (InvalidOperationException)
            {
            }
        }

        static void RequestFinish()
        {
            ScrollSessionBar bar = _bar;
            if (bar == null || bar.IsDisposed || !bar.IsHandleCreated)
            {
                return;
            }

            try
            {
                bar.BeginInvoke(new Action(Finish));
            }
            catch (InvalidOperationException)
            {
            }
        }

        static void Work()
        {
            List<Bitmap> frames = _frames;
            List<int> offsets = _offsets;
            int maxFrames = ScrollCapture.MaxFrames(_region);
            int total = 0;
            bool limited = false;
            bool escWasDown = NativeMethods.IsEscapeDown();
            Bitmap previous = null;
            bool previousOwned = false;
            string shown = null;

            frames.Add(CaptureStable());
            offsets.Add(0);
            try
            {
                while (!_stop)
                {
                    Thread.Sleep(SampleMilliseconds);
                    if (_stop)
                    {
                        break;
                    }

                    if (_bar != null && !_bar.EscRegistered)
                    {
                        bool down = NativeMethods.IsEscapeDown();
                        if (down && !escWasDown)
                        {
                            RequestFinish();
                            break;
                        }

                        escWasDown = down;
                    }

                    Bitmap now = ScreenGrab.Capture(_region);
                    bool stable = previous != null && ScrollCapture.QuickSame(previous, now);
                    if (previousOwned)
                    {
                        previous.Dispose();
                    }

                    previous = now;
                    previousOwned = true;

                    Bitmap last = frames[frames.Count - 1];
                    if (ScrollCapture.QuickSame(last, now))
                    {
                        continue;
                    }

                    bool vertical;
                    ImageStitcher.ShiftMeasure forward = MeasureForward(last, now, out vertical);
                    int along = vertical ? _region.Height : _region.Width;
                    int minStep = Math.Max(16, along / 5);
                    bool good = forward.Moved && forward.Reliable;
                    if (good && forward.Offset >= minStep && (stable || forward.Offset >= along * 45 / 100))
                    {
                        if (_axis == 0)
                        {
                            _axis = vertical ? 1 : 2;
                        }

                        frames.Add(now);
                        offsets.Add(forward.Offset);
                        previousOwned = false;
                        total += forward.Offset;
                        shown = string.Format("{0}长图 · 第 {1} 屏 · {2}", vertical ? "纵向" : "横向", frames.Count, StopKeys());
                        Post(shown);

                        if (frames.Count >= maxFrames)
                        {
                            _endNote = "已达到屏数上限";
                            limited = true;
                        }
                        else if (along + total > 46000)
                        {
                            _endNote = "已达到长度上限";
                            limited = true;
                        }

                        if (limited)
                        {
                            RequestFinish();
                            break;
                        }

                        continue;
                    }

                    if (good || !stable)
                    {
                        continue;
                    }

                    string warn = MovedBackward(last, now)
                        ? "已往回滚，继续往前滚即可接上 · " + StopKeys()
                        : "和上一屏对不上，滚太快了，请往回滚一点 · " + StopKeys();
                    if (warn != shown)
                    {
                        shown = warn;
                        Post(warn);
                    }
                }

                if (!limited)
                {
                    AppendTail(frames, offsets);
                }
            }
            catch (Exception ex)
            {
                _endNote = ex.Message;
            }
            finally
            {
                if (previousOwned && previous != null)
                {
                    previous.Dispose();
                }
            }
        }

        static void AppendTail(List<Bitmap> frames, List<int> offsets)
        {
            Bitmap now = CaptureStable();
            Bitmap last = frames[frames.Count - 1];
            bool vertical;
            ImageStitcher.ShiftMeasure forward = MeasureForward(last, now, out vertical);
            if (forward.Moved && forward.Reliable)
            {
                if (_axis == 0)
                {
                    _axis = vertical ? 1 : 2;
                }

                frames.Add(now);
                offsets.Add(forward.Offset);
                return;
            }

            now.Dispose();
        }

        static ImageStitcher.ShiftMeasure MeasureForward(Bitmap last, Bitmap now, out bool vertical)
        {
            if (_axis != 0)
            {
                vertical = _axis == 1;
                return ImageStitcher.Measure(last, now, vertical);
            }

            ImageStitcher.ShiftMeasure down = ImageStitcher.Measure(last, now, true);
            ImageStitcher.ShiftMeasure right = ImageStitcher.Measure(last, now, false);
            bool downOk = down.Moved && down.Reliable;
            bool rightOk = right.Moved && right.Reliable;
            if (rightOk && (!downOk || right.Score < down.Score))
            {
                vertical = false;
                return right;
            }

            vertical = true;
            return down;
        }

        static bool MovedBackward(Bitmap last, Bitmap now)
        {
            if (_axis != 0)
            {
                ImageStitcher.ShiftMeasure back = ImageStitcher.Measure(now, last, _axis == 1);
                return back.Moved && back.Reliable;
            }

            ImageStitcher.ShiftMeasure up = ImageStitcher.Measure(now, last, true);
            if (up.Moved && up.Reliable)
            {
                return true;
            }

            ImageStitcher.ShiftMeasure left = ImageStitcher.Measure(now, last, false);
            return left.Moved && left.Reliable;
        }

        static Bitmap CaptureStable()
        {
            Bitmap first = ScreenGrab.Capture(_region);
            for (int i = 0; i < 8; i++)
            {
                Thread.Sleep(50);
                Bitmap second = ScreenGrab.Capture(_region);
                bool same = ScrollCapture.QuickSame(first, second);
                first.Dispose();
                first = second;
                if (same)
                {
                    break;
                }
            }

            return first;
        }

        static ScrollCapture.Outcome Build()
        {
            var outcome = new ScrollCapture.Outcome();
            List<Bitmap> frames = _frames;
            List<int> offsets = _offsets;
            _frames = null;
            _offsets = null;
            try
            {
                outcome.Vertical = _axis != 2;
                if (frames == null || frames.Count < 2)
                {
                    outcome.Message = _endNote ?? "没有检测到滚动。请在框内滚动内容后再结束。";
                    return outcome;
                }

                ImageStitcher.StitchOutput stitched = ImageStitcher.Stitch(frames, offsets, outcome.Vertical);
                outcome.Frames = stitched.UsedFrames;
                if (stitched.Image == null || stitched.UsedFrames < 2)
                {
                    if (stitched.Image != null)
                    {
                        stitched.Image.Dispose();
                    }

                    outcome.Message = string.IsNullOrEmpty(stitched.Note) ? "拼接失败" : stitched.Note;
                    return outcome;
                }

                outcome.Ok = true;
                outcome.Image = stitched.Image;
                outcome.Message = _endNote ?? stitched.Note;
                return outcome;
            }
            finally
            {
                if (frames != null)
                {
                    for (int i = 0; i < frames.Count; i++)
                    {
                        frames[i].Dispose();
                    }
                }
            }
        }
    }

    sealed class ScrollSessionBar : Form
    {
        const int EscHotkeyId = 0x5301;

        readonly Label _status;
        readonly Button _done;
        public event EventHandler DoneClicked;

        public bool EscRegistered { get; private set; }

        public ScrollSessionBar()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Surface;
            Size = new Size(420, 46);
            Padding = new Padding(0, 3, 0, 0);

            _status = new Label();
            _status.AutoSize = false;
            _status.Location = new Point(14, 3);
            _status.Size = new Size(312, 43);
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.ForeColor = Theme.Text;
            _status.BackColor = Theme.Surface;
            _status.Font = Theme.UiFont();
            _status.MouseDown += (s, e) => Drag(e);

            _done = Theme.Button("完成", true);
            _done.AutoSize = false;
            _done.Size = new Size(76, 32);
            _done.Location = new Point(Width - _done.Width - 8, 9);
            _done.Click += (s, e) => RaiseDone();

            Controls.Add(_status);
            Controls.Add(_done);
        }

        public void SetText(string text)
        {
            _status.Text = text;
        }

        public void Saving()
        {
            _done.Enabled = false;
            _status.Text = "正在拼接长图…";
            ReleaseEsc();
        }

        public void Place(Rectangle region)
        {
            Rectangle monitor = Screen.FromRectangle(region).WorkingArea;
            int centerX = region.X + (region.Width - Width) / 2;
            var spots = new[]
            {
                new Point(centerX, region.Bottom + 10),
                new Point(centerX, region.Top - Height - 10),
                new Point(monitor.Right - Width - 12, monitor.Bottom - Height - 12),
                new Point(monitor.Right - Width - 12, monitor.Top + 12),
                new Point(monitor.Left + 12, monitor.Bottom - Height - 12),
                new Point(monitor.Left + 12, monitor.Top + 12)
            };

            for (int i = 0; i < spots.Length; i++)
            {
                var rect = new Rectangle(spots[i], Size);
                if (monitor.Contains(rect) && !rect.IntersectsWith(Rectangle.Inflate(region, 4, 4)))
                {
                    Location = spots[i];
                    return;
                }
            }

            Location = new Point(monitor.X + (monitor.Width - Width) / 2, monitor.Bottom - Height - 24);
        }

        void RaiseDone()
        {
            if (DoneClicked != null)
            {
                DoneClicked(this, EventArgs.Empty);
            }
        }

        void ReleaseEsc()
        {
            if (EscRegistered)
            {
                NativeMethods.UnregisterHotKey(Handle, EscHotkeyId);
                EscRegistered = false;
            }
        }

        void Drag(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Drag(e);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.RoundCorners(Handle);
            NativeMethods.ExcludeFromCapture(Handle);
            EscRegistered = NativeMethods.RegisterHotKey(Handle, EscHotkeyId, NativeMethods.MOD_NOREPEAT, (uint)NativeMethods.VK_ESCAPE);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            ReleaseEsc();
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == EscHotkeyId)
            {
                BeginInvoke(new Action(RaiseDone));
            }

            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var glow = Theme.AccentBrush(new RectangleF(0, 0, Width, 3)))
            {
                g.FillRectangle(glow, 0, 0, Width, 3);
            }
        }
    }
}
