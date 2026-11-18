using System.Drawing;
using System.Windows.Forms;

namespace Jietu
{
    sealed class ToolbarForm : Form
    {
        public Button CopyButton;
        public Button SaveButton;
        public Button VerticalButton;
        public Button HorizontalButton;
        public Button DismissButton;

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

            CopyButton = Theme.Button("复制", true);
            SaveButton = Theme.Button("保存", false);
            VerticalButton = Theme.Button("纵向长图", false);
            HorizontalButton = Theme.Button("横向长图", false);
            DismissButton = Theme.Button("取消", false);

            var flow = new FlowLayoutPanel();
            flow.AutoSize = true;
            flow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flow.WrapContents = false;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.BackColor = Color.White;
            flow.Margin = new Padding(0);
            flow.Padding = new Padding(0);
            flow.Controls.Add(CopyButton);
            flow.Controls.Add(SaveButton);
            flow.Controls.Add(VerticalButton);
            flow.Controls.Add(HorizontalButton);
            flow.Controls.Add(DismissButton);
            Controls.Add(flow);

            var tip = new ToolTip();
            tip.SetToolTip(CopyButton, "复制当前选区（Enter）");
            tip.SetToolTip(SaveButton, "保存当前选区（Ctrl+S）");
            tip.SetToolTip(VerticalButton, "向下滚动并拼接（↓）");
            tip.SetToolTip(HorizontalButton, "向右滚动并拼接（→）");
            tip.SetToolTip(DismissButton, "取消（Esc）");
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
