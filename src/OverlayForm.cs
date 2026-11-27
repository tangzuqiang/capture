using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Shiying
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
            SE,
            Draw
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
        readonly List<Mark> _marks = new List<Mark>();
        AnnotateTool _tool = AnnotateTool.None;
        Color _ink = Markup.Red;
        Mark _draft;
        int _editIndex = -1;
        bool _styleLock;
        Form _ocrForm;
        public ScrollCapture.Outcome Outcome;
        public Rectangle RecordRegion;
        public Rectangle ScrollRegion;
        bool _recordMode;
        bool _longRunning;
        bool _longStop;

        public static bool TryStopLong()
        {
            OverlayForm form = _active;
            if (form == null || form.IsDisposed || !form._longRunning)
            {
                return false;
            }

            form._longStop = true;
            return true;
        }

        public static bool TryStartRecord()
        {
            OverlayForm form = _active;
            if (form == null || form.IsDisposed || !form.Visible || form._busy)
            {
                return false;
            }

            if (form._hasSel)
            {
                form.StartRecord();
            }
            else
            {
                form._recordMode = true;
                form.Invalidate();
            }

            return true;
        }

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

            form.ShowNote("已复制 " + text, 1200);
            return true;
        }

        public static void Run(MainForm owner, bool recordMode = false)
        {
            if (_running || ScrollSession.Active)
            {
                return;
            }

            _running = true;
            ScrollCapture.Outcome outcome = null;
            Rectangle record = Rectangle.Empty;
            Rectangle scroll = Rectangle.Empty;
            try
            {
                if (owner != null && owner.Visible && !owner.IsDisposed)
                {
                    owner.Hide();
                }

                Application.DoEvents();
                System.Threading.Thread.Sleep(80);

                using (var overlay = new OverlayForm(owner))
                {
                    overlay._recordMode = recordMode;
                    overlay.ShowDialog();
                    outcome = overlay.Outcome;
                    record = overlay.RecordRegion;
                    scroll = overlay.ScrollRegion;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "拾影", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _running = false;
            }

            if (!record.IsEmpty)
            {
                RecordSession.Start(owner, record);
                return;
            }

            if (!scroll.IsEmpty)
            {
                ScrollSession.Start(owner, scroll);
                return;
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
            _toolbar.ScrollButton.Click += (s, e) => StartManualLong();
            _toolbar.RecordButton.Click += (s, e) => StartRecord();
            _toolbar.DismissButton.Click += (s, e) => Cancel();
            _toolbar.RectButton.Click += (s, e) => SetTool(AnnotateTool.Rect);
            _toolbar.EllipseButton.Click += (s, e) => SetTool(AnnotateTool.Ellipse);
            _toolbar.ArrowButton.Click += (s, e) => SetTool(AnnotateTool.Arrow);
            _toolbar.PenButton.Click += (s, e) => SetTool(AnnotateTool.Pen);
            _toolbar.DoodleButton.Click += (s, e) => SetTool(AnnotateTool.Doodle);
            _toolbar.TextButton.Click += (s, e) => SetTool(AnnotateTool.Text);
            _toolbar.PinButton.Click += (s, e) => PinStill();
            _toolbar.OcrButton.Click += (s, e) => RecognizeText(false);
            _toolbar.TranslateButton.Click += (s, e) => RecognizeText(true);
            _toolbar.UndoButton.Click += (s, e) => Undo();
            _toolbar.InkChanged += (s, e) => SyncEditedText();
            _toolbar.PhraseChanged += (s, e) => SyncEditedText();
            _ink = _toolbar.Ink;
            _toolbar.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    if (_toolbar.PhraseFocused)
                    {
                        Focus();
                        e.Handled = true;
                        return;
                    }

                    Cancel();
                    e.Handled = true;
                }
                else if (e.Control && e.KeyCode == Keys.Z)
                {
                    if (_toolbar.PhraseFocused)
                    {
                        return;
                    }

                    Undo();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    if (_toolbar.PhraseFocused)
                    {
                        e.Handled = true;
                        return;
                    }

                    CopyStill();
                    e.Handled = true;
                }
                else if (e.Control && e.KeyCode == Keys.S)
                {
                    SaveStill();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Down)
                {
                    StartLong(true);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Right)
                {
                    StartLong(false);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.M && e.Modifiers == Keys.None && !_toolbar.PhraseFocused)
                {
                    StartManualLong();
                    e.Handled = true;
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

            if (_ocrForm != null && !_ocrForm.IsDisposed)
            {
                _ocrForm.FormClosed -= ResultFormClosed;
                _ocrForm.Close();
                _ocrForm = null;
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
            if (_hasSel && _tool != AnnotateTool.None)
            {
                if (Inside(p))
                {
                    if (_tool == AnnotateTool.Text)
                    {
                        PlaceText(p);
                    }
                    else if (_tool == AnnotateTool.Pin)
                    {
                        PlacePin(p);
                    }
                    else
                    {
                        _drag = DragMode.Draw;
                        _down = p;
                        _draft = new Mark();
                        _draft.Kind = KindFor(_tool);
                        _draft.Color = InkNow();
                        _draft.Thickness = ThickFor(_tool);
                        _draft.Start = p;
                        _draft.End = p;
                        _draft.FontPx = Markup.FontPx;
                        if (_draft.Kind == MarkKind.Pen || _draft.Kind == MarkKind.Doodle)
                        {
                            _draft.Points = new List<Point>();
                            _draft.Points.Add(p);
                        }
                    }
                }

                Invalidate();
                return;
            }

            if (_hasSel)
            {
                DragMode hit = HitTest(p);
                if (hit == DragMode.None)
                {
                    _hasSel = false;
                    _marks.Clear();
                    _draft = null;
                    _editIndex = -1;
                    _tool = AnnotateTool.None;
                    _toolbar.SetActive(AnnotateTool.None);
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
                if (_tool == AnnotateTool.Text)
                {
                    Cursor = Cursors.IBeam;
                }
                else if (_tool != AnnotateTool.None)
                {
                    Cursor = Cursors.Cross;
                }
                else
                {
                    Cursor = _hasSel ? CursorFor(HitTest(_mouse)) : Cursors.Cross;
                }

                if (_hasSel)
                {
                    return;
                }

                Invalidate();
                return;
            }

            Point p = ToImage(e.Location);
            if (_drag == DragMode.Draw)
            {
                if (_draft != null)
                {
                    Point clamped = ClampToSel(p);
                    _draft.End = clamped;
                    if (_draft.Points != null && _draft.Points.Count > 0)
                    {
                        Point last = _draft.Points[_draft.Points.Count - 1];
                        int dx = clamped.X - last.X;
                        int dy = clamped.Y - last.Y;
                        if (dx * dx + dy * dy >= 4)
                        {
                            _draft.Points.Add(clamped);
                        }
                    }
                }

                Invalidate();
                return;
            }

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

            if (_drag == DragMode.Draw)
            {
                _drag = DragMode.None;
                if (_draft != null && KeepDraft(_draft))
                {
                    _marks.Add(_draft);
                }

                _draft = null;
                Invalidate();
                return;
            }

            bool fresh = _drag == DragMode.New;
            _drag = DragMode.None;
            if (_recordMode && fresh)
            {
                if (_sel.Width < 8 || _sel.Height < 8)
                {
                    _sel = new Rectangle(0, 0, _shot.Width, _shot.Height);
                }

                _hasSel = true;
                StartRecord();
                return;
            }

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
                if (_toolbar.PhraseFocused)
                {
                    Focus();
                    return true;
                }

                Cancel();
                return true;
            }

            if (keyData == (Keys.Control | Keys.Z) && !_toolbar.PhraseFocused)
            {
                Undo();
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

            if (keyData == Keys.M && !_toolbar.PhraseFocused)
            {
                StartManualLong();
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
                DrawFrame(g, dest);
                DrawSizeTag(g, dest);
                DrawMarks(g);
                if (_hasSel && _tool == AnnotateTool.None)
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
                if (_tool == AnnotateTool.Rect)
                {
                    text = "拖拽画方框    再点图标退出    Ctrl+Z 撤回";
                }
                else if (_tool == AnnotateTool.Ellipse)
                {
                    text = "拖拽画圆圈    再点图标退出    Ctrl+Z 撤回";
                }
                else if (_tool == AnnotateTool.Arrow)
                {
                    text = "拖拽画箭头    再点图标退出    Ctrl+Z 撤回";
                }
                else if (_tool == AnnotateTool.Pen)
                {
                    text = "按住拖动画笔    再点图标退出    Ctrl+Z 撤回";
                }
                else if (_tool == AnnotateTool.Doodle)
                {
                    text = "按住拖动涂鸦    再点图标退出    Ctrl+Z 撤回";
                }
                else if (_tool == AnnotateTool.Text)
                {
                    text = "在工具栏输入最多 16 个字，再点到画面上";
                }
                else if (_hasSel)
                {
                    text = "悬停图标可看名称    钉图后可拖动    Enter 复制    ↓→ 长图    Esc 取消";
                }
                else if (_recordMode)
                {
                    text = "录屏：拖拽框选区域后立即开始，单击录制整个屏幕，Esc 取消";
                }
                else
                {
                    text = "拖拽选择区域，Esc 取消";
                }
            }
            using (var font = Theme.UiFont())
            {
                Size size = TextRenderer.MeasureText(text, font);
                var box = new Rectangle((ClientSize.Width - size.Width - 36) / 2, 18, size.Width + 36, size.Height + 16);
                SmoothingMode smoothing = g.SmoothingMode;
                PixelOffsetMode offset = g.PixelOffsetMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (GraphicsPath pill = Theme.Round(box, box.Height / 2f))
                using (var brush = new SolidBrush(Color.FromArgb(225, Theme.Surface)))
                using (LinearGradientBrush ring = Theme.AccentBrush(box))
                using (var pen = new Pen(ring, 1.2f))
                {
                    g.FillPath(brush, pill);
                    g.DrawPath(pen, pill);
                }

                g.SmoothingMode = smoothing;
                g.PixelOffsetMode = offset;
                TextRenderer.DrawText(g, text, font, box, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        void DrawFrame(Graphics g, Rectangle dest)
        {
            SmoothingMode smoothing = g.SmoothingMode;
            PixelOffsetMode offset = g.PixelOffsetMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.None;
            var frame = new RectangleF(dest.X, dest.Y, Math.Max(1, dest.Width - 1), Math.Max(1, dest.Height - 1));
            for (int i = 3; i >= 1; i--)
            {
                using (LinearGradientBrush halo = Theme.AccentBrush(frame, 26 * (4 - i)))
                using (var pen = new Pen(halo, 2f + i * 2.5f))
                {
                    g.DrawRectangle(pen, frame.X - i, frame.Y - i, frame.Width + i * 2, frame.Height + i * 2);
                }
            }

            using (LinearGradientBrush edge = Theme.AccentBrush(frame))
            using (var pen = new Pen(edge, 2f))
            {
                g.DrawRectangle(pen, frame.X, frame.Y, frame.Width, frame.Height);
            }

            g.SmoothingMode = smoothing;
            g.PixelOffsetMode = offset;
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

                var box = new Rectangle(x, y, size.Width + 16, size.Height + 6);
                SmoothingMode smoothing = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath pill = Theme.Round(box, box.Height / 2f))
                using (LinearGradientBrush brush = Theme.AccentBrush(box))
                {
                    g.FillPath(brush, pill);
                }

                g.SmoothingMode = smoothing;
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

            SmoothingMode smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var fill = new SolidBrush(Color.White))
            using (var glow = new SolidBrush(Color.FromArgb(70, Theme.Accent)))
            {
                for (int i = 0; i < points.Length; i++)
                {
                    var r = new Rectangle(points[i].X - 5, points[i].Y - 5, 10, 10);
                    g.FillEllipse(glow, r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6);
                    using (LinearGradientBrush ring = Theme.AccentBrush(r))
                    using (var pen = new Pen(ring, 2f))
                    {
                        g.FillEllipse(fill, r);
                        g.DrawEllipse(pen, r);
                    }
                }
            }

            g.SmoothingMode = smoothing;
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
            using (LinearGradientBrush edge = Theme.AccentBrush(dest))
            using (var pen = new Pen(edge, 2f))
            using (var blue = new Pen(Color.FromArgb(200, Theme.Accent), 1f))
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
                using (var brush = new SolidBrush(Color.FromArgb(235, Theme.Surface)))
                {
                    g.FillRectangle(brush, info);
                }

                TextRenderer.DrawText(g, text, font, info, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
                using (Bitmap crop = Markup.Compose(_shot, _sel, _marks))
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
                MessageBox.Show(this, ex.Message, "拾影", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void PinStill()
        {
            if (_busy || !_hasSel)
            {
                return;
            }

            Bitmap crop = null;
            PinnedImageForm pinned = null;
            try
            {
                crop = Markup.Compose(_shot, _sel, _marks);
                pinned = new PinnedImageForm(
                    crop,
                    new Point(_screenBounds.X + _sel.X, _screenBounds.Y + _sel.Y));
                pinned.Show();
                crop = null;
                pinned = null;
                Close();
            }
            catch (Exception ex)
            {
                if (pinned != null)
                {
                    pinned.Dispose();
                    crop = null;
                }
                else if (crop != null)
                {
                    crop.Dispose();
                }

                MessageBox.Show(this, ex.Message, "拾影", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                dialog.FileName = "拾影_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                dialog.InitialDirectory = AppSettings.SaveDirectory();
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    TopMost = true;
                    _toolbar.TopMost = true;
                    return;
                }

                try
                {
                    using (Bitmap crop = Markup.Compose(_shot, _sel, _marks))
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
                    MessageBox.Show(this, ex.Message, "拾影", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            _longStop = false;
            _longRunning = true;
            try
            {
                PlaceHint(hint, live);
                hint.SetText((vertical ? "纵向" : "横向") + "长图 · 准备滚动…  Esc 或 " + AppSettings.FormatGesture(AppSettings.Gesture) + " 结束");
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
                        if (_longStop)
                        {
                            return true;
                        }

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
                    MessageBox.Show(outcome.Message ?? "长图拼接失败", "拾影", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    Outcome = outcome;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "拾影", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _longRunning = false;
                hint.Close();
                hint.Dispose();
                Close();
            }
        }

        void StartManualLong()
        {
            if (_busy || !_hasSel)
            {
                return;
            }

            ScrollRegion = new Rectangle(_screenBounds.X + _sel.X, _screenBounds.Y + _sel.Y, _sel.Width, _sel.Height);
            Outcome = null;
            Close();
        }

        void StartRecord()
        {
            if (_busy || !_hasSel)
            {
                return;
            }

            RecordRegion = new Rectangle(_screenBounds.X + _sel.X, _screenBounds.Y + _sel.Y, _sel.Width, _sel.Height);
            Outcome = null;
            Close();
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

        void ShowNote(string text, int milliseconds)
        {
            _copiedNote = text;
            if (_noteTimer == null)
            {
                _noteTimer = new Timer();
                _noteTimer.Tick += (s, e) =>
                {
                    _copiedNote = null;
                    _noteTimer.Stop();
                    Invalidate();
                };
            }

            _noteTimer.Stop();
            if (!string.IsNullOrEmpty(text) && milliseconds > 0)
            {
                _noteTimer.Interval = milliseconds;
                _noteTimer.Start();
            }

            Invalidate();
        }

        void SetTool(AnnotateTool tool)
        {
            if (_busy || !_hasSel)
            {
                return;
            }

            _editIndex = -1;
            _tool = _tool == tool ? AnnotateTool.None : tool;
            _toolbar.SetActive(_tool);
            if (_toolbar.Visible)
            {
                PlaceToolbar();
            }

            if (_tool == AnnotateTool.Text)
            {
                _toolbar.FocusPhrase();
            }

            Invalidate();
        }

        void Undo()
        {
            if (_busy || _marks.Count == 0)
            {
                return;
            }

            _marks.RemoveAt(_marks.Count - 1);
            if (_editIndex >= _marks.Count)
            {
                _editIndex = -1;
            }

            Invalidate();
        }

        void RecognizeText(bool translate)
        {
            if (_busy || !_hasSel)
            {
                return;
            }

            Bitmap crop;
            try
            {
                crop = ScreenGrab.Crop(_shot, _sel);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "识别文字", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _busy = true;
            ShowNote("正在识别文字…", 0);
            Task<string> task = Task.Run(new Func<string>(() =>
            {
                try
                {
                    return OcrText.Recognize(crop);
                }
                finally
                {
                    crop.Dispose();
                }
            }));
            task.ContinueWith(done =>
            {
                if (!IsHandleCreated || IsDisposed)
                {
                    return;
                }

                try
                {
                    BeginInvoke(new Action(() => FinishRecognize(done, translate)));
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        void FinishRecognize(Task<string> task, bool translate)
        {
            _busy = false;
            if (IsDisposed)
            {
                return;
            }

            if (task.IsFaulted || task.IsCanceled)
            {
                ShowNote(null, 0);
                Exception ex = task.Exception == null ? null : task.Exception.GetBaseException();
                TopMost = false;
                MessageBox.Show(this, ex == null ? "文字识别失败" : ex.Message, "识别文字", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (!IsDisposed)
                {
                    TopMost = true;
                }

                return;
            }

            string text = task.Result == null ? "" : task.Result.Trim();
            if (text.Length == 0)
            {
                ShowNote("没有识别到文字", 1800);
                return;
            }

            if (translate)
            {
                ShowNote(null, 0);
                ShowOcr(text, true);
                return;
            }

            try
            {
                ScreenGrab.CopyText(text);
                ShowNote("已复制识别文字", 1800);
            }
            catch (Exception ex)
            {
                ShowNote("识别完成", 1800);
                TopMost = false;
                MessageBox.Show(this, ex.Message, "识别文字", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (!IsDisposed)
                {
                    TopMost = true;
                }
            }

            ShowOcr(text, false);
        }

        void ShowOcr(string text, bool translate)
        {
            if (IsDisposed)
            {
                return;
            }

            if (_ocrForm != null && !_ocrForm.IsDisposed)
            {
                _ocrForm.FormClosed -= ResultFormClosed;
                _ocrForm.Close();
            }

            _ocrForm = translate ? (Form)new TranslateForm(text) : new OcrForm(text);
            _ocrForm.FormClosed += ResultFormClosed;
            _ocrForm.Show(this);
            Rectangle monitor = Screen.FromControl(this).WorkingArea;
            int x = monitor.Left + Math.Max(0, (monitor.Width - _ocrForm.Width) / 2);
            int y = monitor.Top + Math.Max(0, (monitor.Height - _ocrForm.Height) / 2);
            _ocrForm.Location = new Point(x, y);
        }

        void ResultFormClosed(object sender, FormClosedEventArgs e)
        {
            Form form = sender as Form;
            if (form != null)
            {
                form.FormClosed -= ResultFormClosed;
            }

            if (_ocrForm == form)
            {
                _ocrForm = null;
            }

            if (!IsDisposed && !Disposing)
            {
                Outcome = null;
                Close();
            }
        }

        void PlaceText(Point imagePoint)
        {
            int index = HitText(imagePoint);
            if (index >= 0)
            {
                _styleLock = true;
                try
                {
                    _editIndex = index;
                    _toolbar.LoadPhrase(_marks[index].Text, _marks[index].Color);
                    _ink = Color.FromArgb(255, _marks[index].Color);
                }
                finally
                {
                    _styleLock = false;
                }

                _toolbar.FocusPhrase();
                Invalidate();
                return;
            }

            string text = (_toolbar.Phrase ?? "").Trim();
            if (text.Length == 0)
            {
                _editIndex = -1;
                _toolbar.FocusPhrase();
                return;
            }

            if (text.Length > 16)
            {
                text = text.Substring(0, 16);
            }

            var mark = new Mark();
            mark.Kind = MarkKind.Text;
            mark.Color = InkNow();
            mark.Thickness = Markup.Thickness;
            mark.Start = imagePoint;
            mark.End = imagePoint;
            mark.Text = text;
            mark.FontPx = Markup.FontPx;
            mark.WrapWidth = 0;
            _marks.Add(mark);
            _editIndex = _marks.Count - 1;
            Invalidate();
        }

        void PlacePin(Point imagePoint)
        {
            var mark = new Mark();
            mark.Kind = MarkKind.Pin;
            mark.Color = InkNow();
            mark.Thickness = Markup.Thickness;
            mark.Start = imagePoint;
            mark.End = imagePoint;
            _marks.Add(mark);
            Invalidate();
        }

        void SyncEditedText()
        {
            _ink = _toolbar.Ink;
            if (_styleLock || _editIndex < 0 || _editIndex >= _marks.Count)
            {
                return;
            }

            Mark mark = _marks[_editIndex];
            if (mark.Kind != MarkKind.Text)
            {
                return;
            }

            string text = (_toolbar.Phrase ?? "").Trim();
            if (text.Length == 0)
            {
                _marks.RemoveAt(_editIndex);
                _editIndex = -1;
                Invalidate();
                return;
            }

            if (text.Length > 16)
            {
                text = text.Substring(0, 16);
            }

            mark.Text = text;
            mark.Color = InkNow();
            Invalidate();
        }

        Color InkNow()
        {
            int alpha = _toolbar.InkOpacity;
            if (alpha < 40) alpha = 40;
            if (alpha > 255) alpha = 255;
            return Color.FromArgb(alpha, _ink.R, _ink.G, _ink.B);
        }

        static MarkKind KindFor(AnnotateTool tool)
        {
            if (tool == AnnotateTool.Ellipse) return MarkKind.Ellipse;
            if (tool == AnnotateTool.Arrow) return MarkKind.Arrow;
            if (tool == AnnotateTool.Pen) return MarkKind.Pen;
            if (tool == AnnotateTool.Doodle) return MarkKind.Doodle;
            return MarkKind.Rect;
        }

        static int ThickFor(AnnotateTool tool)
        {
            if (tool == AnnotateTool.Doodle) return 16;
            if (tool == AnnotateTool.Pen) return 3;
            return Markup.Thickness;
        }

        int HitText(Point point)
        {
            for (int i = _marks.Count - 1; i >= 0; i--)
            {
                if (Markup.TextBounds(_marks[i]).Contains(point))
                {
                    return i;
                }
            }

            return -1;
        }

        void DrawMarks(Graphics g)
        {
            if (_marks.Count == 0 && _draft == null)
            {
                return;
            }

            GraphicsState state = g.Save();
            float sx = ClientSize.Width / (float)Math.Max(1, _shot.Width);
            float sy = ClientSize.Height / (float)Math.Max(1, _shot.Height);
            g.ScaleTransform(sx, sy);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            for (int i = 0; i < _marks.Count; i++)
            {
                Markup.Draw(g, _marks[i], 1f, 0f, 0f);
            }

            if (_draft != null)
            {
                Markup.Draw(g, _draft, 1f, 0f, 0f);
            }

            g.Restore(state);
        }

        bool Inside(Point point)
        {
            return _hasSel && point.X >= _sel.Left && point.Y >= _sel.Top && point.X <= _sel.Right && point.Y <= _sel.Bottom;
        }

        Point ClampToSel(Point point)
        {
            int x = point.X;
            int y = point.Y;
            if (x < _sel.Left) x = _sel.Left;
            if (y < _sel.Top) y = _sel.Top;
            if (x > _sel.Right) x = _sel.Right;
            if (y > _sel.Bottom) y = _sel.Bottom;
            return new Point(x, y);
        }

        static bool KeepDraft(Mark mark)
        {
            if (mark.Kind == MarkKind.Pen || mark.Kind == MarkKind.Doodle)
            {
                return mark.Points != null && mark.Points.Count >= 2;
            }

            int dx = mark.End.X - mark.Start.X;
            int dy = mark.End.Y - mark.Start.Y;
            if (mark.Kind == MarkKind.Arrow)
            {
                return dx * dx + dy * dy >= 36;
            }

            return Math.Abs(dx) >= 4 && Math.Abs(dy) >= 4;
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
