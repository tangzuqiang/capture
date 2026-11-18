using System;
using System.Drawing;
using System.Windows.Forms;

namespace Jietu
{
    sealed class ToolbarForm : Form
    {
        readonly Button[] _swatches;
        Color _ink;

        public Button RectButton;
        public Button ArrowButton;
        public Button TextButton;
        public Button OcrButton;
        public Button UndoButton;
        public Button CopyButton;
        public Button SaveButton;
        public Button VerticalButton;
        public Button HorizontalButton;
        public Button DismissButton;
        public event EventHandler InkChanged;

        public Color Ink
        {
            get { return _ink; }
        }

        public ToolbarForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Color.White;
            Padding = new Padding(6, 6, 6, 6);
            KeyPreview = true;

            RectButton = Tool("矩形");
            ArrowButton = Tool("箭头");
            TextButton = Tool("文字");
            OcrButton = Tool("识别文字");
            UndoButton = Tool("撤销");
            CopyButton = Theme.Button("复制", true);
            SaveButton = Theme.Button("保存", false);
            VerticalButton = Theme.Button("纵向长图", false);
            HorizontalButton = Theme.Button("横向长图", false);
            DismissButton = Theme.Button("取消", false);

            var tools = Row();
            tools.Controls.Add(RectButton);
            tools.Controls.Add(ArrowButton);
            tools.Controls.Add(TextButton);
            tools.Controls.Add(OcrButton);
            tools.Controls.Add(UndoButton);
            tools.Controls.Add(Sep());

            _swatches = new Button[Markup.Palette.Length];
            for (int i = 0; i < Markup.Palette.Length; i++)
            {
                _swatches[i] = Swatch(Markup.Palette[i]);
                tools.Controls.Add(_swatches[i]);
            }

            _ink = Markup.Red;
            PaintSwatches(_swatches[0]);

            var actions = Row();
            actions.Controls.Add(CopyButton);
            actions.Controls.Add(SaveButton);
            actions.Controls.Add(VerticalButton);
            actions.Controls.Add(HorizontalButton);
            actions.Controls.Add(DismissButton);

            var stack = new FlowLayoutPanel();
            stack.AutoSize = true;
            stack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            stack.FlowDirection = FlowDirection.TopDown;
            stack.WrapContents = false;
            stack.BackColor = Color.White;
            stack.Margin = new Padding(0);
            stack.Padding = new Padding(0);
            stack.Controls.Add(tools);
            stack.Controls.Add(actions);
            Controls.Add(stack);

            var tip = new ToolTip();
            tip.SetToolTip(RectButton, "拖拽画方框，默认红色");
            tip.SetToolTip(ArrowButton, "拖拽画箭头");
            tip.SetToolTip(TextButton, "点击后输入文字，Enter 完成");
            tip.SetToolTip(OcrButton, "识别选区里的文字并复制");
            tip.SetToolTip(UndoButton, "撤销上一笔（Ctrl+Z）");
            tip.SetToolTip(CopyButton, "复制当前选区（Enter）");
            tip.SetToolTip(SaveButton, "保存当前选区（Ctrl+S）");
            tip.SetToolTip(VerticalButton, "向下滚动并拼接（↓）");
            tip.SetToolTip(HorizontalButton, "向右滚动并拼接（→）");
            tip.SetToolTip(DismissButton, "取消（Esc）");
            SetActive(AnnotateTool.None);
        }

        public void SetActive(AnnotateTool tool)
        {
            PaintTool(RectButton, tool == AnnotateTool.Rect);
            PaintTool(ArrowButton, tool == AnnotateTool.Arrow);
            PaintTool(TextButton, tool == AnnotateTool.Text);
        }

        static FlowLayoutPanel Row()
        {
            var row = new FlowLayoutPanel();
            row.AutoSize = true;
            row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.WrapContents = false;
            row.FlowDirection = FlowDirection.LeftToRight;
            row.BackColor = Color.White;
            row.Margin = new Padding(0);
            row.Padding = new Padding(0);
            return row;
        }

        static Button Tool(string text)
        {
            Button button = Theme.Button(text, false);
            button.Padding = new Padding(8, 4, 8, 4);
            button.Margin = new Padding(3, 2, 3, 2);
            return button;
        }

        static Panel Sep()
        {
            var line = new Panel();
            line.Size = new Size(1, 22);
            line.Margin = new Padding(6, 8, 6, 0);
            line.BackColor = Theme.Line;
            return line;
        }

        Button Swatch(Color color)
        {
            var button = new Button();
            button.Size = new Size(18, 18);
            button.Margin = new Padding(3, 8, 3, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.BackColor = color;
            button.ForeColor = color;
            button.TabStop = false;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.BorderSize = 2;
            button.FlatAppearance.BorderColor = Theme.Line;
            button.Click += (s, e) =>
            {
                _ink = color;
                PaintSwatches(button);
                if (InkChanged != null)
                {
                    InkChanged(this, EventArgs.Empty);
                }
            };
            return button;
        }

        void PaintSwatches(Button selected)
        {
            for (int i = 0; i < _swatches.Length; i++)
            {
                bool on = _swatches[i] == selected;
                _swatches[i].FlatAppearance.BorderColor = on ? Color.FromArgb(32, 32, 32) : Theme.Line;
            }
        }

        static void PaintTool(Button button, bool on)
        {
            button.BackColor = on ? Color.FromArgb(255, 236, 232) : Color.White;
            button.ForeColor = on ? Markup.Red : Theme.Text;
            button.FlatAppearance.BorderColor = on ? Markup.Red : Theme.Line;
            button.FlatAppearance.MouseOverBackColor = on ? Color.FromArgb(255, 220, 214) : Color.FromArgb(245, 247, 250);
            button.FlatAppearance.MouseDownBackColor = on ? Color.FromArgb(255, 200, 194) : Color.FromArgb(232, 236, 242);
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Line))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
