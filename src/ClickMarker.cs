using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Jietu
{
    sealed class ClickMarker : Form
    {
        const int LifeMilliseconds = 650;
        public static readonly Color LeftInk = Color.FromArgb(0, 150, 255);
        public static readonly Color RightInk = Color.FromArgb(255, 136, 0);

        readonly Point _center;
        readonly bool _right;
        readonly float _scale;
        readonly int _size;
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly Timer _timer;

        public static void Pop(Point screen, bool right)
        {
            new ClickMarker(screen, right).Show();
        }

        ClickMarker(Point center, bool right)
        {
            _center = center;
            _right = right;
            _scale = NativeMethods.ScaleAt(center);
            _size = (int)Math.Ceiling(120 * _scale);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            _timer = new Timer();
            _timer.Interval = 15;
            _timer.Tick += (s, e) => Render();
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
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Render();
            _timer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            base.OnFormClosed(e);
        }

        void Render()
        {
            float t = _clock.ElapsedMilliseconds / (float)LifeMilliseconds;
            if (t >= 1f)
            {
                _timer.Stop();
                Close();
                return;
            }

            using (var bitmap = new Bitmap(_size, _size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);
                    Draw(g, new PointF(_size / 2f, _size / 2f), _scale, _right, t);
                }

                Push(bitmap);
            }
        }

        public static void Draw(Graphics g, PointF c, float s, bool right, float t)
        {
            Color ink = right ? RightInk : LeftInk;
            float fade = 1f - t;
            float hold = 1f - t * t;
            float ease = 1f - (1f - t) * (1f - t) * (1f - t);

            float outer = (10 + 34 * ease) * s;
            using (var pen = new Pen(Color.FromArgb((int)(255 * fade), ink), 3f * s))
            {
                g.DrawEllipse(pen, c.X - outer, c.Y - outer, outer * 2, outer * 2);
                if (right)
                {
                    float inner = (8 + 18 * ease) * s;
                    g.DrawEllipse(pen, c.X - inner, c.Y - inner, inner * 2, inner * 2);
                }
            }

            float core = (right ? 7f : 9f) * s;
            using (var fill = new SolidBrush(Color.FromArgb((int)(220 * hold), ink)))
            using (var edge = new Pen(Color.FromArgb((int)(240 * hold), Color.White), 1.5f * s))
            {
                g.FillEllipse(fill, c.X - core, c.Y - core, core * 2, core * 2);
                g.DrawEllipse(edge, c.X - core, c.Y - core, core * 2, core * 2);
            }

            int badgeAlpha = t < 0.6f ? 255 : (int)(255 * (1f - t) / 0.4f);
            DrawMouse(g, new RectangleF(c.X + 14 * s, c.Y - 44 * s, 19 * s, 28 * s), right, ink, badgeAlpha, s);
        }

        static void DrawMouse(Graphics g, RectangleF body, bool right, Color ink, int alpha, float s)
        {
            float split = body.Y + body.Height * 0.42f;
            using (GraphicsPath path = Theme.Round(body, body.Width / 2f))
            using (var fill = new SolidBrush(Color.FromArgb(alpha * 240 / 255, Color.White)))
            using (var press = new SolidBrush(Color.FromArgb(alpha, ink)))
            using (var line = new Pen(Color.FromArgb(alpha, 40, 44, 56), 1.4f * s))
            {
                g.FillPath(fill, path);
                Region clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                float half = body.Width / 2f;
                g.FillRectangle(press, right ? body.X + half : body.X, body.Y, half, split - body.Y);
                g.Clip = clip;
                g.DrawPath(line, path);
                g.DrawLine(line, body.X, split, body.Right, split);
                g.DrawLine(line, body.X + half, body.Y, body.X + half, split);
            }
        }

        void Push(Bitmap bitmap)
        {
            IntPtr screen = NativeMethods.GetDC(IntPtr.Zero);
            IntPtr memory = NativeMethods.CreateCompatibleDC(screen);
            IntPtr hbitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            IntPtr old = NativeMethods.SelectObject(memory, hbitmap);
            try
            {
                var dst = new Point(_center.X - _size / 2, _center.Y - _size / 2);
                var size = new Size(_size, _size);
                var src = Point.Empty;
                var blend = new NativeMethods.BlendFunction { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
                NativeMethods.UpdateLayeredWindow(Handle, screen, ref dst, ref size, memory, ref src, 0, ref blend, 2);
            }
            finally
            {
                NativeMethods.SelectObject(memory, old);
                NativeMethods.DeleteObject(hbitmap);
                NativeMethods.DeleteDC(memory);
                NativeMethods.ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
