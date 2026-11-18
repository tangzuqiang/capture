using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Jietu
{
    static class UiTest
    {
        const int WM_MOUSEWHEEL = 0x020A;
        const int WM_MOUSEHWHEEL = 0x020E;
        const int WM_VSCROLL = 0x0115;
        const int WM_HSCROLL = 0x0114;
        const int MK_SHIFT = 0x0004;

        public static int Run()
        {
            int failed = 0;
            failed += RunCase("live vertical", TestVertical);
            failed += RunCase("live horizontal", TestHorizontal);
            if (failed == 0)
            {
                Console.WriteLine("UITEST OK");
                return 0;
            }

            Console.WriteLine("UITEST FAIL " + failed);
            return 1;
        }

        static int RunCase(string name, Func<string> test)
        {
            try
            {
                string error = test();
                if (error == null)
                {
                    Console.WriteLine("OK  " + name);
                    return 0;
                }

                Console.WriteLine("FAIL " + name + " " + error);
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL " + name + " " + ex);
                return 1;
            }
        }

        static string TestVertical()
        {
            return RunLive(true, 280, 1500, 240);
        }

        static string TestHorizontal()
        {
            return RunLive(false, 1500, 200, 220);
        }

        static string RunLive(bool vertical, int docWidth, int docHeight, int viewAlong)
        {
            AppSettings.Pace = ScrollPace.Fast;
            using (Bitmap document = MakeDocument(docWidth, docHeight))
            using (var form = new ScrollFixture(document))
            {
                form.ClientSize = vertical
                    ? new Size(docWidth, viewAlong)
                    : new Size(viewAlong, docHeight);
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                form.Location = new Point(area.Left + 80, area.Top + 80);
                form.Show();
                form.Activate();
                Application.DoEvents();
                System.Threading.Thread.Sleep(200);
                form.Refresh();

                Rectangle screen = Rectangle.Empty;
                Point center = Point.Empty;
                IntPtr under = IntPtr.Zero;
                bool onFixture = false;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    form.TopMost = true;
                    form.BringToFront();
                    form.Activate();
                    Application.DoEvents();
                    screen = form.RectangleToScreen(form.ClientRectangle);
                    center = new Point(screen.Left + screen.Width / 2, screen.Top + screen.Height / 2);
                    NativeMethods.SetCursorPos(center.X, center.Y);
                    System.Threading.Thread.Sleep(40);
                    Application.DoEvents();
                    under = NativeMethods.WindowFromPoint(center);
                    if (under == form.Handle)
                    {
                        onFixture = true;
                        break;
                    }
                }

                if (!onFixture)
                {
                    return "cursor is not on the fixture window hwnd=" + under + " form=" + form.Handle + " rect=" + screen;
                }

                using (Bitmap before = ScreenGrab.Capture(screen))
                {
                    ScrollCapture.Outcome outcome = ScrollCapture.Run(
                        screen,
                        vertical,
                        text => Console.WriteLine(text),
                        () => false);
                    try
                    {
                        if (!outcome.Ok || outcome.Image == null)
                        {
                            return outcome.Message ?? "no image";
                        }

                        string path = vertical ? "bin\\uitest-vertical.png" : "bin\\uitest-horizontal.png";
                        outcome.Image.Save(path, ImageFormat.Png);
                        int along = vertical ? outcome.Image.Height : outcome.Image.Width;
                        int captured = vertical ? screen.Height : screen.Width;
                        int expect = vertical ? docHeight : docWidth;
                        Console.WriteLine("size " + outcome.Image.Width + "x" + outcome.Image.Height + " frames " + outcome.Frames + " note " + outcome.Message);
                        if (along < captured + captured / 2)
                        {
                            return "too short " + along + " view " + captured + " " + outcome.Message;
                        }

                        if (Math.Abs(along - expect) > 90)
                        {
                            return "length " + along + " want about " + expect + " offset " + (vertical ? form.OffsetY : form.OffsetX);
                        }

                        if (!TopMatches(before, outcome.Image, vertical))
                        {
                            return "top of long image does not match the first viewport";
                        }

                        return null;
                    }
                    finally
                    {
                        if (outcome.Image != null)
                        {
                            outcome.Image.Dispose();
                        }
                    }
                }
            }
        }

        static bool TopMatches(Bitmap before, Bitmap stitched, bool vertical)
        {
            int samples = 0;
            int close = 0;
            int n = vertical ? Math.Min(before.Height, stitched.Height) : Math.Min(before.Width, stitched.Width);
            int cross = vertical ? Math.Min(before.Width, stitched.Width) : Math.Min(before.Height, stitched.Height);
            for (int i = 8; i < n - 2; i += 15)
            {
                int c = cross / 2;
                Color a = vertical ? before.GetPixel(c, i) : before.GetPixel(i, c);
                Color b = vertical ? stitched.GetPixel(Math.Min(c, stitched.Width - 1), i) : stitched.GetPixel(i, Math.Min(c, stitched.Height - 1));
                int diff = Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                samples++;
                if (diff < 24)
                {
                    close++;
                }
            }

            return samples > 0 && close * 100 / samples >= 75;
        }

        static Bitmap MakeDocument(int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int r = (y * 13 + x * 3) & 255;
                    int g = (x * 11 + y) & 255;
                    int b = ((y / 2) + x * 5 + 30) & 255;
                    bitmap.SetPixel(x, y, Color.FromArgb(r, g, b));
                }
            }

            return bitmap;
        }

        sealed class ScrollFixture : Form
        {
            readonly Bitmap _document;
            public int OffsetX;
            public int OffsetY;

            public ScrollFixture(Bitmap document)
            {
                _document = document;
                FormBorderStyle = FormBorderStyle.FixedToolWindow;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(180, 120);
                AutoScaleMode = AutoScaleMode.None;
                TopMost = true;
                ShowInTaskbar = false;
                Text = "uitest";
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                UpdateStyles();
                DoubleBuffered = true;
            }

            int MaxX
            {
                get { return Math.Max(0, _document.Width - ClientSize.Width); }
            }

            int MaxY
            {
                get { return Math.Max(0, _document.Height - ClientSize.Height); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (ClientSize.Width < 1 || ClientSize.Height < 1)
                {
                    return;
                }

                e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                e.Graphics.CompositingMode = CompositingMode.SourceCopy;
                var dest = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
                var source = new Rectangle(OffsetX, OffsetY, ClientSize.Width, ClientSize.Height);
                e.Graphics.DrawImage(_document, dest, source, GraphicsUnit.Pixel);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_MOUSEWHEEL || m.Msg == WM_MOUSEHWHEEL)
                {
                    int delta = unchecked((short)((long)m.WParam >> 16));
                    int keys = unchecked((int)(long)m.WParam) & 0xFFFF;
                    bool shift = (keys & MK_SHIFT) != 0 || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
                    bool horizontal = m.Msg == WM_MOUSEHWHEEL || shift;
                    ScrollBy(horizontal, delta);
                    m.Result = IntPtr.Zero;
                    return;
                }

                if (m.Msg == WM_VSCROLL || m.Msg == WM_HSCROLL)
                {
                    int code = unchecked((int)(long)m.WParam) & 0xFFFF;
                    int direction = code == 0 ? -1 : 1;
                    Nudge(m.Msg == WM_HSCROLL, direction * 18);
                    m.Result = IntPtr.Zero;
                    return;
                }

                base.WndProc(ref m);
            }

            void ScrollBy(bool horizontal, int delta)
            {
                int notches = delta / 120;
                if (notches == 0)
                {
                    notches = delta > 0 ? 1 : -1;
                }

                int pixels = notches * 36;
                if (horizontal)
                {
                    OffsetX = Clamp(OffsetX + pixels, 0, MaxX);
                }
                else
                {
                    OffsetY = Clamp(OffsetY - pixels, 0, MaxY);
                }

                Invalidate();
                Update();
            }

            void Nudge(bool horizontal, int pixels)
            {
                if (horizontal)
                {
                    OffsetX = Clamp(OffsetX + pixels, 0, MaxX);
                }
                else
                {
                    OffsetY = Clamp(OffsetY + pixels, 0, MaxY);
                }

                Invalidate();
                Update();
            }

            static int Clamp(int value, int min, int max)
            {
                if (value < min) return min;
                if (value > max) return max;
                return value;
            }
        }
    }
}
