using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Shiying
{
    static class ScrollCapture
    {
        public sealed class Outcome
        {
            public Bitmap Image;
            public string Message;
            public bool Ok;
            public bool Vertical;
            public int Frames;
        }

        enum ScrollKind
        {
            Wheel,
            ShiftWheel,
            Message,
            Bar
        }

        sealed class Scroller
        {
            public ScrollKind Kind;
            public bool Horizontal;
            public int Sign;
            public IntPtr Hwnd;
            public Point CursorPoint;

            public string Name
            {
                get { return Kind + (Horizontal ? "H" : "V") + (Sign > 0 ? "+" : "-"); }
            }

            public void Step(int notches)
            {
                notches = Math.Max(1, notches);
                NativeMethods.SetCursorPos(CursorPoint.X, CursorPoint.Y);
                if (Kind == ScrollKind.Wheel)
                {
                    int delta = WheelDelta(notches);
                    uint flag = Horizontal ? NativeMethods.MOUSEEVENTF_HWHEEL : NativeMethods.MOUSEEVENTF_WHEEL;
                    NativeMethods.mouse_event(flag, 0, 0, delta, UIntPtr.Zero);
                    return;
                }

                if (Kind == ScrollKind.ShiftWheel)
                {
                    int delta = Sign > 0 ? -120 * notches : 120 * notches;
                    NativeMethods.keybd_event((byte)NativeMethods.VK_SHIFT, 0, 0, UIntPtr.Zero);
                    NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, delta, UIntPtr.Zero);
                    NativeMethods.keybd_event((byte)NativeMethods.VK_SHIFT, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
                    return;
                }

                if (Kind == ScrollKind.Message)
                {
                    int delta = WheelDelta(notches);
                    int msg = Horizontal ? NativeMethods.WM_MOUSEHWHEEL : NativeMethods.WM_MOUSEWHEEL;
                    NativeMethods.PostMessage(Hwnd, msg, NativeMethods.MakeWheelWParam(delta), NativeMethods.MakeLParam(CursorPoint));
                    return;
                }

                int code = ForwardCode();
                int bar = Horizontal ? NativeMethods.WM_HSCROLL : NativeMethods.WM_VSCROLL;
                int times = notches * 3;
                for (int i = 0; i < times; i++)
                {
                    NativeMethods.PostMessage(Hwnd, bar, (IntPtr)code, IntPtr.Zero);
                }
            }

            public Scroller Reverse()
            {
                return new Scroller
                {
                    Kind = Kind,
                    Horizontal = Horizontal,
                    Sign = -Sign,
                    Hwnd = Hwnd,
                    CursorPoint = CursorPoint
                };
            }

            int WheelDelta(int notches)
            {
                if (!Horizontal)
                {
                    return Sign > 0 ? -120 * notches : 120 * notches;
                }

                return Sign > 0 ? 120 * notches : -120 * notches;
            }

            int ForwardCode()
            {
                if (Sign > 0)
                {
                    return NativeMethods.SB_LINEDOWN;
                }

                return NativeMethods.SB_LINEUP;
            }
        }

        enum StepHit
        {
            None,
            Forward,
            Backward
        }

        public static Outcome Run(Rectangle region, bool vertical, Action<string> progress, Func<bool> cancelled)
        {
            var outcome = new Outcome();
            outcome.Vertical = vertical;
            if (region.Width < 8 || region.Height < 8)
            {
                outcome.Message = "选区太小。";
                return outcome;
            }

            Point origin = Cursor.Position;
            var frames = new List<Bitmap>();
            try
            {
                Point center = new Point(region.Left + region.Width / 2, region.Top + region.Height / 2);
                NativeMethods.SetCursorPos(center.X, center.Y);
                System.Threading.Thread.Sleep(20);
                IntPtr hwnd = NativeMethods.WindowFromPoint(center);
                NativeMethods.ForceForeground(hwnd);

                string stop;
                Scroller active = Probe(region, vertical, hwnd, center, frames, progress, cancelled, out stop);
                if (active == null || frames.Count < 2)
                {
                    outcome.Message = stop ?? "没有检测到滚动。请框选能用滚轮滚动的内容；如果已经在底部，先滚回起点再试。";
                    return outcome;
                }

                int notches = 2;
                double pxPerNotch = 0;
                int viewAlong = vertical ? region.Height : region.Width;
                int hint = 0;
                ImageStitcher.ShiftMeasure first = ImageStitcher.Measure(frames[0], frames[1], vertical, 0, 0, viewAlong);
                if (first.Moved)
                {
                    pxPerNotch = first.Offset / 2.0;
                    hint = first.Offset;
                }
                int maxFrames = MaxFrames(region);
                while (frames.Count < maxFrames)
                {
                    if (cancelled != null && cancelled())
                    {
                        stop = "已手动停止";
                        break;
                    }

                    if (pxPerNotch >= 1)
                    {
                        notches = ChooseNotches(pxPerNotch, viewAlong);
                    }

                    active.CursorPoint = center;
                    active.Step(notches);
                    if (SleepCancel(AppSettings.SettleMilliseconds, cancelled))
                    {
                        stop = "已手动停止";
                        break;
                    }

                    Bitmap frame = CaptureStable(region, cancelled);
                    Bitmap last = frames[frames.Count - 1];
                    ImageStitcher.ShiftMeasure measure = ImageStitcher.Measure(last, frame, vertical, hint, 0, viewAlong);
                    if (!measure.Moved || measure.Score > 7)
                    {
                        if (SleepCancel(measure.Moved ? 220 : 180, cancelled))
                        {
                            frame.Dispose();
                            stop = "已手动停止";
                            break;
                        }

                        Bitmap again = ScreenGrab.Capture(region);
                        ImageStitcher.ShiftMeasure againMeasure = ImageStitcher.Measure(last, again, vertical, hint, 0, viewAlong);
                        bool useAgain = againMeasure.Moved && (!measure.Moved || againMeasure.Score + 0.4 < measure.Score);
                        if (useAgain)
                        {
                            frame.Dispose();
                            frame = again;
                            measure = againMeasure;
                        }
                        else
                        {
                            again.Dispose();
                        }
                    }

                    if (!measure.Moved)
                    {
                        frame.Dispose();
                        stop = "已到达边缘";
                        break;
                    }

                    if (!measure.Reliable)
                    {
                        frame.Dispose();
                        stop = "画面变化较大，已保留已拼好的部分";
                        break;
                    }

                    frames.Add(frame);
                    hint = measure.Offset;
                    pxPerNotch = pxPerNotch <= 0
                        ? measure.Offset / (double)notches
                        : pxPerNotch * 0.45 + (measure.Offset / (double)notches) * 0.55;

                    if (progress != null)
                    {
                        progress(string.Format("{0}长图 · 第 {1} 屏 · Esc 结束", vertical ? "纵向" : "横向", frames.Count));
                    }

                    int estimated = viewAlong + (int)(pxPerNotch * notches * (frames.Count - 1));
                    if (estimated > 46000)
                    {
                        stop = "已达到长度上限";
                        break;
                    }
                }

                if (frames.Count >= maxFrames && string.IsNullOrEmpty(stop))
                {
                    stop = "已达到屏数上限";
                }

                ImageStitcher.StitchOutput stitched = ImageStitcher.Stitch(frames, vertical);
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
                outcome.Message = string.IsNullOrEmpty(stop) ? stitched.Note : stop;
                return outcome;
            }
            catch (Exception ex)
            {
                outcome.Message = ex.Message;
                return outcome;
            }
            finally
            {
                for (int i = 0; i < frames.Count; i++)
                {
                    frames[i].Dispose();
                }

                NativeMethods.SetCursorPos(origin.X, origin.Y);
            }
        }

        static Scroller Probe(
            Rectangle region,
            bool vertical,
            IntPtr hwnd,
            Point center,
            List<Bitmap> frames,
            Action<string> progress,
            Func<bool> cancelled,
            out string stop)
        {
            stop = null;
            IntPtr parent = NativeMethods.GetParent(hwnd);
            if (parent == hwnd)
            {
                parent = IntPtr.Zero;
            }

            var methods = new List<Scroller>();
            if (vertical)
            {
                methods.Add(Make(ScrollKind.Wheel, false, 1, hwnd, center));
                methods.Add(Make(ScrollKind.Message, false, 1, hwnd, center));
                if (parent != IntPtr.Zero)
                {
                    methods.Add(Make(ScrollKind.Message, false, 1, parent, center));
                }

                methods.Add(Make(ScrollKind.Bar, false, 1, hwnd, center));
                if (parent != IntPtr.Zero)
                {
                    methods.Add(Make(ScrollKind.Bar, false, 1, parent, center));
                }
            }
            else
            {
                methods.Add(Make(ScrollKind.Wheel, true, 1, hwnd, center));
                methods.Add(Make(ScrollKind.Wheel, true, -1, hwnd, center));
                methods.Add(Make(ScrollKind.ShiftWheel, true, 1, hwnd, center));
                methods.Add(Make(ScrollKind.ShiftWheel, true, -1, hwnd, center));
                methods.Add(Make(ScrollKind.Message, true, 1, hwnd, center));
                methods.Add(Make(ScrollKind.Message, true, -1, hwnd, center));
                methods.Add(Make(ScrollKind.Bar, true, 1, hwnd, center));
                methods.Add(Make(ScrollKind.Bar, true, -1, hwnd, center));
            }

            Bitmap baseline = CaptureStable(region, cancelled);
            const int probeNotches = 2;
            for (int i = 0; i < methods.Count; i++)
            {
                if (cancelled != null && cancelled())
                {
                    stop = "已取消";
                    baseline.Dispose();
                    return null;
                }

                if (progress != null)
                {
                    progress((vertical ? "纵向" : "横向") + "长图 · 正在滚动…");
                }

                Scroller method = methods[i];
                method.CursorPoint = center;
                Bitmap now;
                StepHit hit = TryStep(method, baseline, region, vertical, probeNotches, cancelled, out now);
                if (hit == StepHit.Forward)
                {
                    frames.Add(baseline);
                    frames.Add(now);
                    return method;
                }

                if (hit == StepHit.Backward)
                {
                    if (now != null)
                    {
                        now.Dispose();
                    }

                    Scroller reverse = method.Reverse();
                    reverse.CursorPoint = center;
                    reverse.Step(probeNotches);
                    SleepCancel(AppSettings.SettleMilliseconds, cancelled);
                    reverse.Step(probeNotches);
                    SleepCancel(AppSettings.SettleMilliseconds, cancelled);
                    now = CaptureStable(region, cancelled);
                    ImageStitcher.ShiftMeasure forward = ImageStitcher.Measure(baseline, now, vertical);
                    if (forward.Moved && forward.Reliable)
                    {
                        frames.Add(baseline);
                        frames.Add(now);
                        return reverse;
                    }

                    now.Dispose();
                }
                else if (now != null)
                {
                    now.Dispose();
                }

                Bitmap fresh = ScreenGrab.Capture(region);
                if (!QuickSame(baseline, fresh))
                {
                    baseline.Dispose();
                    baseline = fresh;
                }
                else
                {
                    fresh.Dispose();
                }
            }

            baseline.Dispose();
            stop = vertical
                ? "没有检测到向下滚动。请框选可滚动内容，并确认页面不在最底部。"
                : "没有检测到向右滚动。请框选可横向滚动的内容，例如宽表格。";
            return null;
        }

        static StepHit TryStep(Scroller method, Bitmap baseline, Rectangle region, bool vertical, int notches, Func<bool> cancelled, out Bitmap now)
        {
            now = null;
            method.Step(notches);
            if (SleepCancel(AppSettings.SettleMilliseconds, cancelled))
            {
                return StepHit.None;
            }

            now = CaptureStable(region, cancelled);
            ImageStitcher.ShiftMeasure forward = ImageStitcher.Measure(baseline, now, vertical);
            if (forward.Moved && forward.Reliable)
            {
                return StepHit.Forward;
            }

            ImageStitcher.ShiftMeasure backward = ImageStitcher.Measure(now, baseline, vertical);
            if (backward.Moved && backward.Reliable)
            {
                return StepHit.Backward;
            }

            return StepHit.None;
        }

        static Scroller Make(ScrollKind kind, bool horizontal, int sign, IntPtr hwnd, Point center)
        {
            return new Scroller
            {
                Kind = kind,
                Horizontal = horizontal,
                Sign = sign,
                Hwnd = hwnd,
                CursorPoint = center
            };
        }

        static int ChooseNotches(double pxPerNotch, int viewAlong)
        {
            int desired = (int)(viewAlong * AppSettings.TargetRatio);
            int notches = (int)Math.Round(desired / pxPerNotch);
            if (notches < 1)
            {
                notches = 1;
            }

            if (notches > 12)
            {
                notches = 12;
            }

            int limit = (int)(viewAlong * 0.58);
            while (notches > 1 && pxPerNotch * notches > limit)
            {
                notches--;
            }

            return notches;
        }

        static int MaxFrames(Rectangle region)
        {
            long pixels = (long)region.Width * region.Height;
            if (pixels < 1)
            {
                return 6;
            }

            long budget = 700L * 1024L * 1024L;
            int count = (int)(budget / (pixels * 3L));
            if (count < 6)
            {
                count = 6;
            }

            if (count > 80)
            {
                count = 80;
            }

            return count;
        }

        static Bitmap CaptureStable(Rectangle region, Func<bool> cancelled)
        {
            Bitmap first = ScreenGrab.Capture(region);
            int waited = 0;
            int budget = AppSettings.StableMilliseconds;
            while (waited < budget)
            {
                if (cancelled != null && cancelled())
                {
                    return first;
                }

                System.Threading.Thread.Sleep(50);
                waited += 50;
                Bitmap second = ScreenGrab.Capture(region);
                if (QuickSame(first, second))
                {
                    first.Dispose();
                    return second;
                }

                first.Dispose();
                first = second;
                Application.DoEvents();
            }

            return first;
        }

        static bool QuickSame(Bitmap a, Bitmap b)
        {
            if (a.Width != b.Width || a.Height != b.Height)
            {
                return false;
            }

            int hits = 0;
            int sum = 0;
            for (int i = 0; i < 48; i++)
            {
                int x = (a.Width * (i * 17 + 3)) / 800;
                int y = (a.Height * (i * 13 + 5)) / 640;
                if (x >= a.Width)
                {
                    x = a.Width - 1;
                }

                if (y >= a.Height)
                {
                    y = a.Height - 1;
                }

                Color ca = a.GetPixel(x, y);
                Color cb = b.GetPixel(x, y);
                sum += Math.Abs(ca.R - cb.R) + Math.Abs(ca.G - cb.G) + Math.Abs(ca.B - cb.B);
                hits++;
            }

            return hits > 0 && (sum / (double)(hits * 3)) < 3.0;
        }

        static bool SleepCancel(int milliseconds, Func<bool> cancelled)
        {
            int left = milliseconds;
            while (left > 0)
            {
                if (cancelled != null && cancelled())
                {
                    return true;
                }

                int step = left > 30 ? 30 : left;
                System.Threading.Thread.Sleep(step);
                left -= step;
                Application.DoEvents();
            }

            return cancelled != null && cancelled();
        }
    }
}
