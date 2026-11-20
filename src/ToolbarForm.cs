using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Shiying
{
    enum ToolGlyph
    {
        Rect,
        Ellipse,
        Arrow,
        Pen,
        Doodle,
        Text,
        Pin,
        Undo,
        Ocr,
        Translate,
        Copy,
        Save,
        Down,
        Right,
        Record,
        Close
    }

    sealed class ToolbarForm : Form
    {
        readonly IconButton[] _tools;
        readonly SwatchDot[] _swatches;
        readonly Panel _options;
        readonly Panel _phraseHost;
        readonly TextBox _phrase;
        readonly Label _hint;
        readonly OpacityBar _slider;
        Color _ink;
        int _opacity = 255;

        public IconButton RectButton;
        public IconButton EllipseButton;
        public IconButton ArrowButton;
        public IconButton PenButton;
        public IconButton DoodleButton;
        public IconButton TextButton;
        public IconButton PinButton;
        public IconButton UndoButton;
        public IconButton OcrButton;
        public IconButton TranslateButton;
        public IconButton CopyButton;
        public IconButton SaveButton;
        public IconButton VerticalButton;
        public IconButton HorizontalButton;
        public IconButton RecordButton;
        public IconButton DismissButton;
        public event EventHandler InkChanged;
        public event EventHandler PhraseChanged;

        public Color Ink
        {
            get { return _ink; }
        }

        public int InkOpacity
        {
            get { return _opacity; }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Phrase
        {
            get { return _phrase.Text ?? ""; }
            set
            {
                _phrase.Text = value ?? "";
                RefreshHint();
            }
        }

        public int PhraseMaxLength
        {
            get { return _phrase.MaxLength; }
        }

        public bool PhraseFocused
        {
            get { return _phrase.ContainsFocus || _hint.ContainsFocus; }
        }

        public ToolbarForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Padding = new Padding(6, 5, 6, 5);
            KeyPreview = true;

            RectButton = MakeIcon(ToolGlyph.Rect, "矩形");
            EllipseButton = MakeIcon(ToolGlyph.Ellipse, "圆圈");
            ArrowButton = MakeIcon(ToolGlyph.Arrow, "箭头");
            PenButton = MakeIcon(ToolGlyph.Pen, "画笔");
            DoodleButton = MakeIcon(ToolGlyph.Doodle, "涂鸦");
            TextButton = MakeIcon(ToolGlyph.Text, "文字");
            PinButton = MakeIcon(ToolGlyph.Pin, "钉");
            UndoButton = MakeIcon(ToolGlyph.Undo, "撤回");
            OcrButton = MakeIcon(ToolGlyph.Ocr, "识别文字");
            TranslateButton = MakeIcon(ToolGlyph.Translate, "翻译");
            CopyButton = MakeIcon(ToolGlyph.Copy, "复制");
            SaveButton = MakeIcon(ToolGlyph.Save, "保存");
            VerticalButton = MakeIcon(ToolGlyph.Down, "纵向长图");
            HorizontalButton = MakeIcon(ToolGlyph.Right, "横向长图");
            RecordButton = MakeIcon(ToolGlyph.Record, "录屏");
            DismissButton = MakeIcon(ToolGlyph.Close, "取消");
            CopyButton.Accent = true;

            var tools = Row();
            tools.Controls.Add(RectButton);
            tools.Controls.Add(EllipseButton);
            tools.Controls.Add(ArrowButton);
            tools.Controls.Add(PenButton);
            tools.Controls.Add(DoodleButton);
            tools.Controls.Add(TextButton);
            tools.Controls.Add(PinButton);
            tools.Controls.Add(UndoButton);
            tools.Controls.Add(Sep(18));
            tools.Controls.Add(OcrButton);
            tools.Controls.Add(TranslateButton);
            tools.Controls.Add(CopyButton);
            tools.Controls.Add(SaveButton);
            tools.Controls.Add(VerticalButton);
            tools.Controls.Add(HorizontalButton);
            tools.Controls.Add(RecordButton);
            tools.Controls.Add(DismissButton);

            _phraseHost = new Panel();
            _phraseHost.Size = new Size(176, 28);
            _phraseHost.Margin = new Padding(2, 4, 4, 2);
            _phraseHost.BackColor = Theme.Page;
            _phraseHost.Paint += (s, e) =>
            {
                var frame = new RectangleF(0, 0, _phraseHost.Width - 1, _phraseHost.Height - 1);
                if (_phrase != null && _phrase.Focused)
                {
                    using (LinearGradientBrush ring = Theme.AccentBrush(frame))
                    using (var pen = new Pen(ring, 1.4f))
                    {
                        e.Graphics.DrawRectangle(pen, frame.X, frame.Y, frame.Width, frame.Height);
                    }
                }
                else
                {
                    using (var pen = new Pen(Theme.Line))
                    {
                        e.Graphics.DrawRectangle(pen, frame.X, frame.Y, frame.Width, frame.Height);
                    }
                }
            };

            _phrase = new TextBox();
            _phrase.BorderStyle = BorderStyle.None;
            _phrase.BackColor = Theme.Page;
            _phrase.ForeColor = Theme.Text;
            _phrase.Font = Theme.UiFont();
            _phrase.Location = new Point(8, 6);
            _phrase.Size = new Size(160, 18);
            _phrase.MaxLength = 16;
            _phrase.TextChanged += (s, e) =>
            {
                RefreshHint();
                if (PhraseChanged != null)
                {
                    PhraseChanged(this, EventArgs.Empty);
                }
            };
            _phrase.GotFocus += (s, e) =>
            {
                RefreshHint();
                _phraseHost.Invalidate();
            };
            _phrase.LostFocus += (s, e) =>
            {
                RefreshHint();
                _phraseHost.Invalidate();
            };

            _hint = new Label();
            _hint.Text = "最多输入16个字符";
            _hint.ForeColor = Theme.SubText;
            _hint.BackColor = Theme.Page;
            _hint.Font = Theme.UiFont();
            _hint.AutoSize = false;
            _hint.Location = new Point(6, 4);
            _hint.Size = new Size(162, 20);
            _hint.TextAlign = ContentAlignment.MiddleLeft;
            _hint.Cursor = Cursors.IBeam;
            _hint.Click += (s, e) => _phrase.Focus();
            _phraseHost.Controls.Add(_phrase);
            _phraseHost.Controls.Add(_hint);
            _hint.BringToFront();

            var opacityLabel = new Label();
            opacityLabel.Text = "透明度";
            opacityLabel.AutoSize = true;
            opacityLabel.ForeColor = Theme.SubText;
            opacityLabel.Font = Theme.UiFont();
            opacityLabel.Margin = new Padding(6, 8, 2, 0);

            _slider = new OpacityBar();
            _slider.Value = 255;
            _slider.Margin = new Padding(2, 4, 4, 0);
            _slider.ValueChanged += (s, e) =>
            {
                _opacity = _slider.Value;
                RaiseInk();
            };

            _options = Row();
            _options.Controls.Add(_phraseHost);
            _options.Controls.Add(Sep(16));
            _options.Controls.Add(opacityLabel);
            _options.Controls.Add(_slider);
            _options.Controls.Add(Sep(16));
            _swatches = new SwatchDot[Markup.Palette.Length];
            for (int i = 0; i < Markup.Palette.Length; i++)
            {
                _swatches[i] = new SwatchDot(Markup.Palette[i]);
                int index = i;
                _swatches[i].Click += (s, e) => Pick(_swatches[index]);
                _options.Controls.Add(_swatches[i]);
            }

            _ink = Markup.Red;
            _opacity = 255;
            PaintSwatches(_swatches[0]);
            _options.Visible = false;

            var stack = new FlowLayoutPanel();
            stack.AutoSize = true;
            stack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            stack.FlowDirection = FlowDirection.TopDown;
            stack.WrapContents = false;
            stack.BackColor = Theme.Surface;
            stack.Margin = new Padding(0);
            stack.Padding = new Padding(0);
            stack.Controls.Add(tools);
            stack.Controls.Add(_options);
            Controls.Add(stack);

            _tools = new IconButton[]
            {
                RectButton, EllipseButton, ArrowButton, PenButton, DoodleButton, TextButton, PinButton
            };

            var tip = new ToolTip();
            Theme.DarkTips(tip);
            tip.InitialDelay = 280;
            tip.ReshowDelay = 80;
            tip.AutoPopDelay = 4000;
            tip.ShowAlways = true;
            Tip(tip, RectButton, "矩形");
            Tip(tip, EllipseButton, "圆圈");
            Tip(tip, ArrowButton, "箭头");
            Tip(tip, PenButton, "画笔");
            Tip(tip, DoodleButton, "涂鸦");
            Tip(tip, TextButton, "文字");
            Tip(tip, PinButton, "钉");
            Tip(tip, UndoButton, "撤回");
            Tip(tip, OcrButton, "识别文字");
            Tip(tip, TranslateButton, "翻译");
            Tip(tip, CopyButton, "复制");
            Tip(tip, SaveButton, "保存");
            Tip(tip, VerticalButton, "纵向长图");
            Tip(tip, HorizontalButton, "横向长图");
            Tip(tip, RecordButton, "录屏");
            Tip(tip, DismissButton, "取消");
            SetActive(AnnotateTool.None);
        }

        public void FocusPhrase()
        {
            if (_phraseHost.Visible)
            {
                _phrase.Focus();
            }
        }

        public void LoadPhrase(string text, Color color)
        {
            _phrase.Text = text ?? "";
            if (_phrase.TextLength > _phrase.MaxLength)
            {
                _phrase.Text = _phrase.Text.Substring(0, _phrase.MaxLength);
            }

            _ink = Color.FromArgb(255, color.R, color.G, color.B);
            int alpha = color.A;
            if (alpha < 40)
            {
                alpha = 255;
            }

            _opacity = alpha;
            _slider.Value = alpha;
            SwatchDot match = _swatches[0];
            for (int i = 0; i < _swatches.Length; i++)
            {
                if (_swatches[i].GetInk().ToArgb() == _ink.ToArgb())
                {
                    match = _swatches[i];
                    break;
                }
            }

            PaintSwatches(match);
            RefreshHint();
        }

        public void SetActive(AnnotateTool tool)
        {
            for (int i = 0; i < _tools.Length; i++)
            {
                _tools[i].Active = false;
            }

            IconButton active = null;
            if (tool == AnnotateTool.Rect) active = RectButton;
            else if (tool == AnnotateTool.Ellipse) active = EllipseButton;
            else if (tool == AnnotateTool.Arrow) active = ArrowButton;
            else if (tool == AnnotateTool.Pen) active = PenButton;
            else if (tool == AnnotateTool.Doodle) active = DoodleButton;
            else if (tool == AnnotateTool.Text) active = TextButton;
            else if (tool == AnnotateTool.Pin) active = PinButton;
            if (active != null)
            {
                active.Active = true;
            }

            bool drawing = active != null;
            _options.Visible = drawing;
            _phraseHost.Visible = tool == AnnotateTool.Text;
            PerformLayout();
        }

        static void Tip(ToolTip tip, Control control, string text)
        {
            tip.SetToolTip(control, text);
        }

        void RefreshHint()
        {
            _hint.Visible = _phrase.TextLength == 0 && !_phrase.Focused;
            if (_hint.Visible)
            {
                _hint.BringToFront();
            }
        }

        void Pick(SwatchDot dot)
        {
            _ink = dot.GetInk();
            PaintSwatches(dot);
            RaiseInk();
        }

        void PaintSwatches(SwatchDot selected)
        {
            for (int i = 0; i < _swatches.Length; i++)
            {
                _swatches[i].Chosen = _swatches[i] == selected;
            }
        }

        void RaiseInk()
        {
            if (InkChanged != null)
            {
                InkChanged(this, EventArgs.Empty);
            }
        }

        static FlowLayoutPanel Row()
        {
            var row = new FlowLayoutPanel();
            row.AutoSize = true;
            row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.WrapContents = false;
            row.FlowDirection = FlowDirection.LeftToRight;
            row.BackColor = Theme.Surface;
            row.Margin = new Padding(0);
            row.Padding = new Padding(0);
            return row;
        }

        static IconButton MakeIcon(ToolGlyph glyph, string name)
        {
            var button = new IconButton(glyph);
            button.AccessibleName = name;
            return button;
        }

        static Panel Sep(int height)
        {
            var line = new Panel();
            line.Size = new Size(1, height);
            line.Margin = new Padding(6, 8, 6, 0);
            line.BackColor = Theme.Line;
            return line;
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
                cp.ClassStyle |= 0x00020000;
                cp.ExStyle |= 0x08000000;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.RoundCorners(Handle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Line))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }

            using (LinearGradientBrush glow = Theme.AccentBrush(new RectangleF(0, 0, Width, 2)))
            {
                e.Graphics.FillRectangle(glow, 0, 0, Width, 2);
            }
        }
    }

    sealed class IconButton : Control
    {
        readonly ToolGlyph _glyph;
        bool _hover;
        bool _active;
        bool _accent;
        bool _clickSent;

        public IconButton(ToolGlyph glyph)
        {
            _glyph = glyph;
            Size = new Size(32, 32);
            Margin = new Padding(1, 1, 1, 1);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Active
        {
            get { return _active; }
            set
            {
                _active = value;
                Invalidate();
            }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Accent
        {
            get { return _accent; }
            set
            {
                _accent = value;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _clickSent = false;
            base.OnMouseDown(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_clickSent && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
            {
                OnClick(EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            if (_clickSent)
            {
                return;
            }

            _clickSent = true;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Theme.Surface);
            var bounds = new RectangleF(1.5f, 1.5f, Width - 3f, Height - 3f);
            Color hole = Theme.Surface;
            using (GraphicsPath path = Theme.Round(bounds, 7))
            {
                if (_active)
                {
                    using (LinearGradientBrush brush = Theme.AccentBrush(bounds))
                    {
                        g.FillPath(brush, path);
                    }

                    hole = Theme.Accent;
                }
                else if (_hover)
                {
                    using (var brush = new SolidBrush(Theme.SurfaceHigh))
                    {
                        g.FillPath(brush, path);
                    }

                    hole = Theme.SurfaceHigh;
                }
            }

            Color ink = _active ? Color.White : (_accent ? Theme.Accent : (_hover ? Color.White : Theme.Text));
            using (var pen = new Pen(ink, 1.6f))
            using (var brush = new SolidBrush(ink))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                DrawGlyph(g, pen, brush, _glyph, hole);
            }
        }

        static void DrawGlyph(Graphics g, Pen pen, Brush brush, ToolGlyph glyph, Color hole)
        {
            if (glyph == ToolGlyph.Rect)
            {
                g.DrawRectangle(pen, 8, 9, 15, 13);
            }
            else if (glyph == ToolGlyph.Ellipse)
            {
                g.DrawEllipse(pen, 8, 9, 15, 13);
            }
            else if (glyph == ToolGlyph.Arrow)
            {
                g.DrawLine(pen, 8, 22, 22, 9);
                g.FillPolygon(brush, new PointF[]
                {
                    new PointF(23, 8),
                    new PointF(15, 9),
                    new PointF(20, 16)
                });
            }
            else if (glyph == ToolGlyph.Pen)
            {
                g.DrawLine(pen, 9, 22, 20, 10);
                g.DrawLine(pen, 18, 8, 23, 13);
                g.DrawLine(pen, 18, 8, 20, 10);
                g.DrawLine(pen, 23, 13, 21, 12);
            }
            else if (glyph == ToolGlyph.Doodle)
            {
                g.DrawCurve(pen, new PointF[]
                {
                    new PointF(7, 20),
                    new PointF(12, 10),
                    new PointF(16, 22),
                    new PointF(24, 11)
                }, 0.55f);
            }
            else if (glyph == ToolGlyph.Text)
            {
                g.DrawLine(pen, 9, 9, 23, 9);
                g.DrawLine(pen, 16, 9, 16, 23);
            }
            else if (glyph == ToolGlyph.Pin)
            {
                g.FillEllipse(brush, 11, 5, 10, 10);
                g.FillPolygon(brush, new PointF[]
                {
                    new PointF(16, 26),
                    new PointF(12.2f, 14),
                    new PointF(19.8f, 14)
                });
                using (var dot = new SolidBrush(hole))
                {
                    g.FillEllipse(dot, 14, 7.5f, 4.2f, 4.2f);
                }
            }
            else if (glyph == ToolGlyph.Undo)
            {
                g.DrawArc(pen, 10, 9, 13, 13, 200, 230);
                g.FillPolygon(brush, new PointF[]
                {
                    new PointF(8, 13),
                    new PointF(14, 10),
                    new PointF(13, 16)
                });
            }
            else if (glyph == ToolGlyph.Ocr)
            {
                g.DrawRectangle(pen, 8, 8, 12, 15);
                g.DrawLine(pen, 11, 12, 17, 12);
                g.DrawLine(pen, 11, 16, 17, 16);
                g.DrawEllipse(pen, 16, 16, 8, 8);
            }
            else if (glyph == ToolGlyph.Translate)
            {
                g.DrawLine(pen, 7, 10, 15, 10);
                g.DrawLine(pen, 11, 8, 11, 10);
                g.DrawBezier(pen, 13.5f, 10, 13, 14, 10.5f, 16.5f, 7.5f, 18);
                g.DrawBezier(pen, 9, 12, 10, 15, 12.5f, 17, 15, 18);
                g.DrawLine(pen, 16.5f, 25, 20.5f, 15);
                g.DrawLine(pen, 20.5f, 15, 24.5f, 25);
                g.DrawLine(pen, 18, 21.5f, 23, 21.5f);
            }
            else if (glyph == ToolGlyph.Copy)
            {
                g.DrawLines(pen, new PointF[]
                {
                    new PointF(8, 16),
                    new PointF(13, 21),
                    new PointF(24, 10)
                });
            }
            else if (glyph == ToolGlyph.Save)
            {
                g.DrawLine(pen, 16, 7, 16, 18);
                g.DrawLine(pen, 11, 14, 16, 19);
                g.DrawLine(pen, 21, 14, 16, 19);
                g.DrawLine(pen, 8, 22, 24, 22);
            }
            else if (glyph == ToolGlyph.Down)
            {
                g.DrawLine(pen, 16, 7, 16, 20);
                g.FillPolygon(brush, new PointF[]
                {
                    new PointF(16, 25),
                    new PointF(10, 17),
                    new PointF(22, 17)
                });
            }
            else if (glyph == ToolGlyph.Right)
            {
                g.DrawLine(pen, 7, 16, 20, 16);
                g.FillPolygon(brush, new PointF[]
                {
                    new PointF(25, 16),
                    new PointF(17, 10),
                    new PointF(17, 22)
                });
            }
            else if (glyph == ToolGlyph.Record)
            {
                g.DrawEllipse(pen, 8, 8, 16, 16);
                g.FillEllipse(brush, 12, 12, 8, 8);
            }
            else if (glyph == ToolGlyph.Close)
            {
                g.DrawLine(pen, 10, 10, 22, 22);
                g.DrawLine(pen, 22, 10, 10, 22);
            }
        }

    }

    sealed class SwatchDot : Control
    {
        readonly Color _ink;
        bool _chosen;
        bool _clickSent;

        public SwatchDot(Color color)
        {
            _ink = color;
            Size = new Size(20, 20);
            Margin = new Padding(3, 6, 3, 0);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
        }

        public Color GetInk()
        {
            return _ink;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Chosen
        {
            get { return _chosen; }
            set
            {
                _chosen = value;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _clickSent = false;
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_clickSent && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
            {
                OnClick(EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            if (_clickSent)
            {
                return;
            }

            _clickSent = true;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var dot = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var brush = new SolidBrush(_ink))
            {
                g.FillEllipse(brush, dot);
            }

            if (_ink.R + _ink.G + _ink.B < 150)
            {
                using (var pen = new Pen(Theme.SubText))
                {
                    g.DrawEllipse(pen, dot);
                }
            }

            if (!_chosen)
            {
                return;
            }

            Color mark = (_ink.R + _ink.G + _ink.B) > 500 ? Color.Black : Color.White;
            using (var pen = new Pen(mark, 1.7f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLines(pen, new PointF[]
                {
                    new PointF(5f, 10f),
                    new PointF(8.5f, 13.5f),
                    new PointF(15f, 6.5f)
                });
            }
        }
    }

    sealed class OpacityBar : Control
    {
        int _value = 255;

        public event EventHandler ValueChanged;

        public OpacityBar()
        {
            Size = new Size(112, 24);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get { return _value; }
            set
            {
                int next = value;
                if (next < 40) next = 40;
                if (next > 255) next = 255;
                if (next == _value)
                {
                    return;
                }

                _value = next;
                Invalidate();
                if (ValueChanged != null)
                {
                    ValueChanged(this, EventArgs.Empty);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                Capture = true;
                ApplyX(e.X);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button == MouseButtons.Left && Capture)
            {
                ApplyX(e.X);
            }
        }

        void ApplyX(int x)
        {
            int left = 8;
            int right = Width - 8;
            float t = (x - left) / (float)Math.Max(1, right - left);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            Value = 40 + (int)Math.Round(t * 215f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int y = Height / 2;
            int left = 8;
            int right = Width - 8;
            float t = (_value - 40) / 215f;
            int x = left + (int)Math.Round((right - left) * t);
            using (var track = new Pen(Theme.Line, 3f))
            using (LinearGradientBrush gradient = Theme.AccentBrush(new RectangleF(left, y - 2, right - left, 4)))
            using (var fill = new Pen(gradient, 3f))
            {
                track.StartCap = LineCap.Round;
                track.EndCap = LineCap.Round;
                fill.StartCap = LineCap.Round;
                fill.EndCap = LineCap.Round;
                g.DrawLine(track, left, y, right, y);
                g.DrawLine(fill, left, y, x, y);
            }

            using (var brush = new SolidBrush(Color.White))
            using (var pen = new Pen(Theme.Accent, 2f))
            {
                g.FillEllipse(brush, x - 6, y - 6, 12, 12);
                g.DrawEllipse(pen, x - 6, y - 6, 12, 12);
            }
        }
    }
}
