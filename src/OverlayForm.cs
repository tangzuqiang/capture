using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Jietu
{
    sealed class OverlayForm : Form
    {
        enum DragMode
        {
            None,
            New,
            Move,
            N,
            S,
            W,
            E,
            NW,
            NE,
            SW,
            SE
        }

        static bool _running;
        static OverlayForm _active;

        readonly MainForm _owner;
        readonly Rectangle _screenBounds;
        readonly Bitmap _shot;
        readonly Bitmap _dim;
        readonly ToolbarForm _toolbar;
        Rectangle _sel;
        bool _hasSel;
        DragMode _drag = DragMode.None;
        Point _down;
        Rectangle _startSel;
        Point _mouse = new Point(-1, -1);
        bool _busy;
        string _copiedNote;
        Timer _noteTimer;
        public ScrollCapture.Outcome Outcome;

        public static bool TryGetCursorColor(out Color color)
        {
            color = Color.Empty;
            OverlayForm form = _active;
            if (form == null || form.IsDisposed || !form.Visible || form._shot == null)
            {
                return false;
            }

            Rectangle bounds = form._screenBounds;
            if (!bounds.Contains(Cursor.Position))
            {
                return false;
            }

            Point p = form.ToImage(form.PointToClient(Cursor.Position));
            color = form._shot.GetPixel(p.X, p.Y);
            return true;
        }

        public static bool NotifyCopied(string text)
        {
            OverlayForm form = _active;
            if (form == null || form.IsDisposed || !form.Visible || string.IsNullOrEmpty(text))
            {
                return false;
            }

            form._copiedNote = "已复制 " + text;
            if (form._noteTimer == null)
            {
                form._noteTimer = new Timer();
                form._noteTimer.Interval = 1200;
                form._noteTimer.Tick += (s, e) =>
                {
                    form._copiedNote = null;
                    form._noteTimer.Stop();
                    form.Invalidate();
                };
            }

            form._noteTimer.Stop();
            form._noteTimer.Start();
            form.Invalidate();
            return true;
        }

        public static void Run(MainForm owner)
        {
            if (_running)
            {
                return;
            }

            _running = true;
            bool restore = owner != null && owner.Visible && !owner.IsDisposed;
            ScrollCapture.Outcome outcome = null;
            try
            {
                if (restore)
                {
                    owner.Hide();
                }

                Application.DoEvents();
                System.Threading.Thread.Sleep(80);

                using (var overlay = new OverlayForm(owner))
                {
                    overlay.ShowDialog();
                    outcome = overlay.Outcome;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "截长图", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _running = false;
                if (restore && owner != null && !owner.IsDisposed)
                {
                    owner.Show();
                }
            }

            if (outcome != null && outcome.Ok && outcome.Image != null)
            {
                var result = new ResultForm(outcome);
                result.Show();
                result.TopMost = true;
                result.Activate();
                result.TopMost = false;
            }
        }

        OverlayForm(MainForm owner)
        {
            _owner = owner;
            _screenBounds = Screen.FromPoint(Cursor.Position).Bounds;
            _shot = ScreenGrab.Capture(_screenBounds);
            _dim = ScreenGrab.Darken(_shot, 110);
            _toolbar = new ToolbarForm();
            _active = this;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            Bounds = _screenBounds;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();

            _toolbar.CopyButton.Click += (s, e) => CopyStill();
            _toolbar.SaveButton.Click += (s, e) => SaveStill();
            _toolbar.VerticalButton.Click += (s, e) => StartLong(true);
            _toolbar.HorizontalButton.Click += (s, e) => StartLong(false);
            _toolbar.DismissButton.Click += (s, e) => Cancel();
            _toolbar.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Cancel();
                }
            };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, _screenBounds.X, _screenBounds.Y, _screenBounds.Width, _screenBounds.Height, 0);
            Activate();
            _mouse = ToImage(PointToClient(Cursor.Position));
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_active == this)
            {
                _active = null;
            }

            if (_noteTimer != null)
            {
                _noteTimer.Stop();
                _noteTimer.Dispose();
                _noteTimer = null;
            }

            if (_toolbar != null && !_toolbar.IsDisposed)
            {
                _toolbar.Close();
            }

            if (_shot != null)
            {
                _shot.Dispose();
            }

            if (_dim != null)
            {
                _dim.Dispose();
            }

            base.OnFormClosed(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_busy)
            {
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                Cancel();
                return;
            }

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            Point p = ToImage(e.Location);
            if (_hasSel)
            {
                DragMode hit = HitTest(p);
                if (hit == DragMode.None)
                {
                    _hasSel = false;
                    HideToolbar();
                    _drag = DragMode.New;
                    _down = p;
                    _sel = new Rectangle(p.X, p.Y, 0, 0);
                }
                else
                {
                    _drag = hit;
                    _down = p;
                    _startSel = _sel;
                }
            }
            else
            {
                _drag = DragMode.New;
                _down = p;
                _sel = new Rectangle(p.X, p.Y, 0, 0);
            }

            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _mouse = ToImage(e.Location);
            if (_drag == DragMode.None)
            {
                Cursor = _hasSel ? CursorFor(HitTest(_mouse)) : Cursors.Cross;
                if (_hasSel)
                {
                    return;
                }

                Invalidate();
                return;
            }

            Point p = ToImage(e.Location);
            if (_drag == DragMode.New)
            {
                _sel = Normalize(_down, p);
            }
            else if (_drag == DragMode.Move)
            {
                int dx = p.X - _down.X;
                int dy = p.Y - _down.Y;
                int nx = _startSel.X + dx;
                int ny = _startSel.Y + dy;
                if (nx < 0) nx = 0;
                if (ny < 0) ny = 0;
                if (nx + _startSel.Width > _shot.Width) nx = _shot.Width - _startSel.Width;
                if (ny + _startSel.Height > _shot.Height) ny = _shot.Height - _startSel.Height;
                _sel = new Rectangle(nx, ny, _startSel.Width, _startSel.Height);
            }
            else
            {
                ResizeSelection(p);
            }

            if (_hasSel && _toolbar.Visible)
            {
                PlaceToolbar();
            }

            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_drag == DragMode.None)
            {
                return;
            }

            _drag = DragMode.None;
            if (_sel.Width >= 8 && _sel.Height >= 8)
            {
                _hasSel = true;
                ShowToolbar();
            }
            else
            {
                _hasSel = false;
                _sel = Rectangle.Empty;
                HideToolbar();
            }

            Cursor = _hasSel ? Cursors.Default : Cursors.Cross;
            Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Cancel();
                return true;
            }

            if (!_hasSel || _busy)
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }

            if (keyData == Keys.Enter)
            {
                CopyStill();
                return true;
            }

            if (keyData == Keys.Down)
            {
                StartLong(true);
                return true;
            }

            if (keyData == Keys.Right)
            {
                StartLong(false);
                return true;
            }

            if (keyData == (Keys.Control | Keys.S))
            {
                SaveStill();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_dim, ClientRectangle);

            if (_sel.Width > 0 && _sel.Height > 0)
            {
                Rectangle dest = ToClient(_sel);
                g.DrawImage(_shot, dest, _sel, GraphicsUnit.Pixel);
                using (var white = new Pen(Color.White, 1f))
                using (var blue = new Pen(Theme.Accent, 2f))
                {
                    if (dest.Width > 3 && dest.Height > 3)
                    {
                        g.DrawRectangle(white, dest.X + 1, dest.Y + 1, dest.Width - 3, dest.Height - 3);
                    }

                    g.DrawRectangle(blue, dest.X, dest.Y, Math.Max(1, dest.Width - 1), Math.Max(1, dest.Height - 1));
                }

                DrawSizeTag(g, dest);
                if (_hasSel)
                {
                    DrawHandles(g, dest);
                }
            }
            else
            {
                DrawLoupe(g);
            }

            DrawBanner(g);
        }

        void DrawBanner(Graphics g)
        {
            string text = _copiedNote;
            if (string.IsNullOrEmpty(text))
            {
                text = _hasSel
                    ? "Enter 复制      ↓ 纵向长图      → 横向长图      Esc 取消"
                    : "拖拽选择区域，Esc 取消";
            }
            using (var font = Theme.UiFont())
            {
                Size size = TextRenderer.MeasureText(text, font);
                var box = new Rectangle((ClientSize.Width - size.Width - 28) / 2, 18, size.Width + 28, size.Height + 14);
                using (var brush = new SolidBrush(Color.FromArgb(180, 20, 24, 28)))
                {
                    g.FillRectangle(brush, box);
                }

                TextRenderer.DrawText(g, text, font, box, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        void DrawSizeTag(Graphics g, Rectangle dest)
        {
            string text = _sel.Width + " × " + _sel.Height;
            using (var font = Theme.UiFont())
            {
                Size size = TextRenderer.MeasureText(text, font);
                int x = dest.X;
                int y = dest.Y - size.Height - 10;
                if (y < 8)
                {
                    y = dest.Y + 6;
                }

                if (x + size.Width + 12 > ClientSize.Width)
                {
                    x = Math.Max(4, ClientSize.Width - size.Width - 16);
                }

                var box = new Rectangle(x, y, size.Width + 10, size.Height + 4);
                using (var brush = new SolidBrush(Theme.Accent))
                {
                    g.FillRectangle(brush, box);
                }

                TextRenderer.DrawText(g, text, font, box, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        void DrawHandles(Graphics g, Rectangle dest)
        {
            Point[] points = new[]
            {
                new Point(dest.Left, dest.Top),
                new Point(dest.Left + dest.Width / 2, dest.Top),
                new Point(dest.Right, dest.Top),
                new Point(dest.Right, dest.Top + dest.Height / 2),
                new Point(dest.Right, dest.Bottom),
                new Point(dest.Left + dest.Width / 2, dest.Bottom),
                new Point(dest.Left, dest.Bottom),
                new Point(dest.Left, dest.Top + dest.Height / 2)
            };

            using (var fill = new SolidBrush(Color.White))
            using (var pen = new Pen(Theme.Accent, 1.6f))
            {
                for (int i = 0; i < points.Length; i++)
                {
                    var r = new Rectangle(points[i].X - 4, points[i].Y - 4, 8, 8);
                    g.FillRectangle(fill, r);
                    g.DrawRectangle(pen, r);
                }
            }
        }

        void DrawLoupe(Graphics g)
        {
            if (_mouse.X < 0 || _mouse.Y < 0 || _mouse.X >= _shot.Width || _mouse.Y >= _shot.Height)
            {
                return;
            }

            const int zoom = 4;
            const int sample = 34;
            int x0 = _mouse.X - sample / 2;
            int y0 = _mouse.Y - sample / 2;
            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x0 + sample > _shot.Width) x0 = Math.Max(0, _shot.Width - sample);
            if (y0 + sample > _shot.Height) y0 = Math.Max(0, _shot.Height - sample);
            int sw = Math.Min(sample, _shot.Width - x0);
            int sh = Math.Min(sample, _shot.Height - y0);
            int dw = sw * zoom;
            int dh = sh * zoom;
            const int infoBlock = 62;

            Point client = ToClientPoint(_mouse);
            int lx = client.X + 24;
            int ly = client.Y + 24;
            if (lx + dw + 8 > ClientSize.Width) lx = client.X - dw - 24;
            if (ly + dh + infoBlock > ClientSize.Height) ly = client.Y - dh - infoBlock - 16;
            if (lx < 4) lx = 4;
            if (ly < 4) ly = 4;

            var dest = new Rectangle(lx, ly, dw, dh);
            g.DrawImage(_shot, dest, new Rectangle(x0, y0, sw, sh), GraphicsUnit.Pixel);
            using (var pen = new Pen(Color.White, 2f))
            using (var blue = new Pen(Theme.Accent, 1f))
            {
                g.DrawRectangle(pen, dest);
                g.DrawLine(blue, dest.Left, dest.Top + dest.Height / 2, dest.Right, dest.Top + dest.Height / 2);
                g.DrawLine(blue, dest.Left + dest.Width / 2, dest.Top, dest.Left + dest.Width / 2, dest.Bottom);
            }

            Color color = _shot.GetPixel(_mouse.X, _mouse.Y);
            string text = (_screenBounds.X + _mouse.X) + ", " + (_screenBounds.Y + _mouse.Y) + "\r\n" + ColorText.Rgb(color) + "\r\n" + ColorText.Hex(color);
            using (var font = Theme.UiFont())
            {
                Size textSize = TextRenderer.MeasureText(text, font);
                int infoW = Math.Max(dest.Width, textSize.Width + 16);
                int infoH = textSize.Height + 6;
                int infoX = dest.X;
                if (infoX + infoW > ClientSize.Width - 4)
                {
                    infoX = Math.Max(4, ClientSize.Width - infoW - 4);
                }

                var info = new Rectangle(infoX, dest.Bottom, infoW, infoH);
                using (var brush = new SolidBrush(Color.FromArgb(230, 20, 24, 28)))
                {
                    g.FillRectangle(brush, info);
                }

                TextRenderer.DrawText(g, text, font, info, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        void ResizeSelection(Point p)
        {
            int left = _startSel.Left;
            int top = _startSel.Top;
            int right = _startSel.Right;
            int bottom = _startSel.Bottom;
            int dx = p.X - _down.X;
            int dy = p.Y - _down.Y;
            if (_drag == DragMode.W || _drag == DragMode.NW || _drag == DragMode.SW) left += dx;
            if (_drag == DragMode.E || _drag == DragMode.NE || _drag == DragMode.SE) right += dx;
            if (_drag == DragMode.N || _drag == DragMode.NW || _drag == DragMode.NE) top += dy;
            if (_drag == DragMode.S || _drag == DragMode.SW || _drag == DragMode.SE) bottom += dy;
            if (left < 0) left = 0;
            if (top < 0) top = 0;
            if (right > _shot.Width) right = _shot.Width;
            if (bottom > _shot.Height) bottom = _shot.Height;
            _sel = Normalize(new Point(left, top), new Point(right, bottom));
        }

        DragMode HitTest(Point p)
        {
            if (!_hasSel || _sel.Width < 1)
            {
                return DragMode.None;
            }

            const int grab = 8;
            bool nearL = Math.Abs(p.X - _sel.Left) <= grab;
            bool nearR = Math.Abs(p.X - _sel.Right) <= grab;
            bool nearT = Math.Abs(p.Y - _sel.Top) <= grab;
            bool nearB = Math.Abs(p.Y - _sel.Bottom) <= grab;
            bool inX = p.X >= _sel.Left - grab && p.X <= _sel.Right + grab;
            bool inY = p.Y >= _sel.Top - grab && p.Y <= _sel.Bottom + grab;
            if (nearL && nearT) return DragMode.NW;
            if (nearR && nearT) return DragMode.NE;
            if (nearL && nearB) return DragMode.SW;
            if (nearR && nearB) return DragMode.SE;
            if (nearT && inX) return DragMode.N;
            if (nearB && inX) return DragMode.S;
            if (nearL && inY) return DragMode.W;
            if (nearR && inY) return DragMode.E;
            if (_sel.Contains(p)) return DragMode.Move;
            return DragMode.None;
        }

        static Cursor CursorFor(DragMode mode)
        {
            switch (mode)
            {
                case DragMode.N:
                case DragMode.S:
                    return Cursors.SizeNS;
                case DragMode.W:
                case DragMode.E:
                    return Cursors.SizeWE;
                case DragMode.NW:
                case DragMode.SE:
                    return Cursors.SizeNWSE;
                case DragMode.NE:
                case DragMode.SW:
                    return Cursors.SizeNESW;
                case DragMode.Move:
                    return Cursors.SizeAll;
                default:
                    return Cursors.Cross;
            }
        }

        void ShowToolbar()
        {
            if (!_toolbar.Visible)
            {
                _toolbar.Show(this);
            }

            PlaceToolbar();
        }

        void HideToolbar()
        {
            if (_toolbar.Visible)
            {
                _toolbar.Hide();
            }
        }

        void PlaceToolbar()
        {
            Rectangle dest = ToClient(_sel);
            Point origin = PointToScreen(Point.Empty);
            int x = origin.X + dest.X;
            int y = origin.Y + dest.Bottom + 10;
            Rectangle monitor = Screen.FromPoint(new Point(x, origin.Y + dest.Y)).WorkingArea;
            if (y + _toolbar.Height > monitor.Bottom)
            {
                y = origin.Y + dest.Top - _toolbar.Height - 10;
            }

            if (x + _toolbar.Width > monitor.Right)
            {
                x = monitor.Right - _toolbar.Width - 4;
            }

            if (x < monitor.Left)
            {
                x = monitor.Left + 4;
            }

            if (y < monitor.Top)
            {
                y = monitor.Top + 4;
            }

            _toolbar.Location = new Point(x, y);
        }

        void CopyStill()
        {
            if (_busy || !_hasSel)
            {
                return;
            }

            try
            {
                using (Bitmap crop = _shot.Clone(_sel, PixelFormat.Format24bppRgb))
                {
                    ScreenGrab.CopyToClipboard(crop);
                }

                if (_owner != null)
                {
                    _owner.Tip("已复制到剪贴板");
                }

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "截长图", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void SaveStill()
        {
            if (_busy || !_hasSel)
            {
                return;
            }

            TopMost = false;
            _toolbar.TopMost = false;
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "保存截图";
                dialog.Filter = "PNG 图片|*.png|JPEG 图片|*.jpg";
                dialog.FileName = "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                dialog.InitialDirectory = AppSettings.SaveDirectory();
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    TopMost = true;
                    _toolbar.TopMost = true;
                    return;
                }

                try
                {
                    using (Bitmap crop = _shot.Clone(_sel, PixelFormat.Format24bppRgb))
                    {
                        ScreenGrab.Save(crop, dialog.FileName);
                    }

                    if (_owner != null)
                    {
                        _owner.Tip("已保存截图");
                    }

                    Close();
                }
                catch (Exception ex)
                {
                    TopMost = true;
                    MessageBox.Show(this, ex.Message, "截长图", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        void StartLong(bool vertical)
        {
            if (_busy || !_hasSel)
            {
                return;
            }

            _busy = true;
            HideToolbar();
            Hide();
            TopMost = false;
            Application.DoEvents();
            System.Threading.Thread.Sleep(140);

            Rectangle live = new Rectangle(_screenBounds.X + _sel.X, _screenBounds.Y + _sel.Y, _sel.Width, _sel.Height);
            bool escWasDown = NativeMethods.IsEscapeDown();
            HintForm hint = new HintForm();
            try
            {
                PlaceHint(hint, live);
                hint.SetText((vertical ? "纵向" : "横向") + "截长图 · 准备滚动…  Esc 结束");
                hint.Show();
                Application.DoEvents();

                ScrollCapture.Outcome outcome = ScrollCapture.Run(
                    live,
                    vertical,
                    text =>
                    {
                        hint.SetText(text);
                        Application.DoEvents();
                    },
                    () =>
                    {
                        bool down = NativeMethods.IsEscapeDown();
                        if (!down)
                        {
                            escWasDown = false;
                            return false;
                        }

                        return !escWasDown;
                    });

                if (!outcome.Ok || outcome.Image == null)
                {
                    MessageBox.Show(outcome.Message ?? "截长图失败", "截长图", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    Outcome = outcome;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "截长图", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                hint.Close();
                hint.Dispose();
                Close();
            }
        }

        static void PlaceHint(HintForm hint, Rectangle region)
        {
            Rectangle monitor = Screen.FromRectangle(region).WorkingArea;
            var spots = new[]
            {
                new Point(monitor.Left + 12, monitor.Top + 12),
                new Point(monitor.Right - hint.Width - 12, monitor.Top + 12),
                new Point(monitor.Left + 12, monitor.Bottom - hint.Height - 12),
                new Point(monitor.Right - hint.Width - 12, monitor.Bottom - hint.Height - 12)
            };

            for (int i = 0; i < spots.Length; i++)
            {
                var rect = new Rectangle(spots[i], hint.Size);
                if (!rect.IntersectsWith(region))
                {
                    hint.Location = spots[i];
                    return;
                }
            }

            hint.Location = spots[0];
        }

        void Cancel()
        {
            if (_busy)
            {
                return;
            }

            Outcome = null;
            Close();
        }

        Point ToImage(Point client)
        {
            int width = Math.Max(1, ClientSize.Width);
            int height = Math.Max(1, ClientSize.Height);
            int x = client.X * _shot.Width / width;
            int y = client.Y * _shot.Height / height;
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x >= _shot.Width) x = _shot.Width - 1;
            if (y >= _shot.Height) y = _shot.Height - 1;
            return new Point(x, y);
        }

        Point ToClientPoint(Point image)
        {
            int x = image.X * ClientSize.Width / Math.Max(1, _shot.Width);
            int y = image.Y * ClientSize.Height / Math.Max(1, _shot.Height);
            return new Point(x, y);
        }

        Rectangle ToClient(Rectangle image)
        {
            Point a = ToClientPoint(new Point(image.Left, image.Top));
            Point b = ToClientPoint(new Point(image.Right, image.Bottom));
            return Normalize(a, b);
        }

        static Rectangle Normalize(Point a, Point b)
        {
            int x = Math.Min(a.X, b.X);
            int y = Math.Min(a.Y, b.Y);
            int w = Math.Abs(a.X - b.X);
            int h = Math.Abs(a.Y - b.Y);
            return new Rectangle(x, y, w, h);
        }
    }
}
