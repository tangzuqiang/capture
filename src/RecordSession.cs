using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Jietu
{
    static class RecordSession
    {
        static ScreenRecorder _recorder;
        static RecordBar _bar;
        static RegionFrame _frame;
        static NativeMethods.HookProc _hookProc;
        static IntPtr _hook;
        static Rectangle _region;
        static bool _busy;

        public static bool Active
        {
            get { return _recorder != null || _busy; }
        }

        public static async void Start(MainForm owner, Rectangle region)
        {
            if (Active)
            {
                return;
            }

            _busy = true;
            _region = ScreenRecorder.Fit(region);
            string path = Path.Combine(AppSettings.VideoDirectory(), "录屏_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4");
            try
            {
                _frame = new RegionFrame(_region);
                _frame.Show();
                _bar = new RecordBar();
                _bar.StopClicked += (s, e) => Stop(owner);
                _bar.Place(_region);
                _bar.Show();
                await Task.Delay(150);
                _recorder = await ScreenRecorder.StartAsync(_region, path, AppSettings.RecordSystemAudio, AppSettings.RecordMicrophone);
                _bar.Begin(_recorder.FrameSize);
                Hook();
                if (_recorder.AudioMissing != null)
                {
                    owner.Tip(_recorder.AudioMissing + "不可用，本次录屏不含该声音");
                }
            }
            catch (Exception ex)
            {
                _recorder = null;
                CloseUi();
                MessageBox.Show("录屏启动失败：" + ex.Message, "截长图", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _busy = false;
            }
        }

        public static async void Stop(MainForm owner)
        {
            if (_recorder == null || _busy)
            {
                return;
            }

            _busy = true;
            Unhook();
            ScreenRecorder recorder = _recorder;
            _bar.Saving();
            try
            {
                await recorder.StopAsync();
                if (owner != null && !owner.IsDisposed)
                {
                    owner.TipFile("录屏已保存，点此打开所在文件夹", recorder.Path);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("录屏保存失败：" + ex.Message, "截长图", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _recorder = null;
                _busy = false;
                CloseUi();
            }
        }

        static void CloseUi()
        {
            Unhook();
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

        static void Hook()
        {
            if (_hook != IntPtr.Zero)
            {
                return;
            }

            _hookProc = OnMouse;
            _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _hookProc, NativeMethods.GetModuleHandle(null), 0);
        }

        static void Unhook()
        {
            if (_hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }

        static IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                int message = wParam.ToInt32();
                if (message == NativeMethods.WM_LBUTTONDOWN || message == NativeMethods.WM_RBUTTONDOWN)
                {
                    Point point = Marshal.PtrToStructure<NativeMethods.MouseHookInfo>(lParam).Point;
                    bool right = message == NativeMethods.WM_RBUTTONDOWN;
                    RecordBar bar = _bar;
                    if (bar != null && _region.Contains(point) && !bar.Bounds.Contains(point))
                    {
                        bar.BeginInvoke(new Action(() => ClickMarker.Pop(point, right)));
                    }
                }
            }

            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }
    }

    sealed class RecordBar : Form
    {
        readonly Label _status;
        readonly Button _stop;
        readonly Timer _timer;
        readonly Stopwatch _clock = new Stopwatch();
        string _sizeText = "";
        bool _blink;
        public event EventHandler StopClicked;

        public RecordBar()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Surface;
            Size = new Size(300, 46);
            Padding = new Padding(0, 3, 0, 0);

            _status = new Label();
            _status.AutoSize = false;
            _status.Location = new Point(34, 3);
            _status.Size = new Size(176, 43);
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.ForeColor = Theme.Text;
            _status.BackColor = Theme.Surface;
            _status.Font = Theme.UiFont();
            _status.Text = "正在准备录屏…";
            _status.MouseDown += (s, e) => Drag(e);

            _stop = Theme.Button("停止", true);
            _stop.AutoSize = false;
            _stop.Size = new Size(76, 32);
            _stop.Location = new Point(Width - _stop.Width - 8, 9);
            _stop.Enabled = false;
            _stop.Click += (s, e) =>
            {
                if (StopClicked != null)
                {
                    StopClicked(this, EventArgs.Empty);
                }
            };

            Controls.Add(_status);
            Controls.Add(_stop);

            _timer = new Timer();
            _timer.Interval = 500;
            _timer.Tick += (s, e) =>
            {
                _blink = !_blink;
                ShowElapsed();
                Invalidate(new Rectangle(0, 0, 34, Height));
            };
        }

        public void Begin(Size frame)
        {
            _sizeText = frame.Width + "×" + frame.Height;
            _clock.Restart();
            _timer.Start();
            _stop.Enabled = true;
            ShowElapsed();
        }

        public void Saving()
        {
            _timer.Stop();
            _clock.Stop();
            _blink = false;
            _stop.Enabled = false;
            _status.Text = "正在保存视频…";
            Invalidate();
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

        void ShowElapsed()
        {
            TimeSpan span = _clock.Elapsed;
            _status.Text = ((int)span.TotalHours).ToString("00") + ":" + span.Minutes.ToString("00") + ":" + span.Seconds.ToString("00") + "    " + _sizeText;
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
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            base.OnFormClosed(e);
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

            Color dot = _clock.IsRunning ? Color.FromArgb(_blink ? 120 : 255, 255, 72, 72) : Theme.SubText;
            using (var brush = new SolidBrush(dot))
            {
                g.FillEllipse(brush, 13, 18, 12, 12);
            }
        }
    }

    sealed class RegionFrame : Form
    {
        const int Gap = 3;
        readonly Rectangle _bounds;

        public RegionFrame(Rectangle region)
        {
            _bounds = Rectangle.Inflate(region, Gap, Gap);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            Bounds = _bounds;
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
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.ExcludeFromCapture(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, 0x0010);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.Magenta);
            using (var pen = new Pen(Color.FromArgb(255, 72, 72), 2f))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
            }
        }
    }
}
