using System;
using System.Drawing;
using System.Windows.Forms;

namespace Jietu
{
    sealed class OcrForm : Form
    {
        readonly TextBox _box;
        readonly Label _status;

        public OcrForm(string text)
        {
            Text = "识别文字";
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.White;
            MinimumSize = new Size(380, 260);
            Size = new Size(520, 380);
            TopMost = true;
            KeyPreview = true;

            var bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 52;
            bar.BackColor = Color.White;

            _status = new Label();
            _status.AutoSize = false;
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.ForeColor = Theme.SubText;
            _status.Padding = new Padding(12, 0, 0, 0);
            _status.Text = "已复制到剪贴板，可以改完再复制";

            var copy = Theme.Button("复制全部", true);
            var close = Theme.Button("关闭", false);
            copy.Click += (s, e) => CopyAll();
            close.Click += (s, e) => Close();

            var buttons = new FlowLayoutPanel();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Dock = DockStyle.Right;
            buttons.Width = 230;
            buttons.WrapContents = false;
            buttons.Padding = new Padding(0, 8, 8, 0);
            buttons.Controls.Add(close);
            buttons.Controls.Add(copy);
            bar.Controls.Add(buttons);
            bar.Controls.Add(_status);

            _box = new TextBox();
            _box.Multiline = true;
            _box.ScrollBars = ScrollBars.Both;
            _box.WordWrap = false;
            _box.BorderStyle = BorderStyle.None;
            _box.Dock = DockStyle.Fill;
            _box.Font = Font;
            _box.HideSelection = false;
            _box.Text = text ?? "";

            var host = new Panel();
            host.Dock = DockStyle.Fill;
            host.Padding = new Padding(12, 10, 12, 4);
            host.BackColor = Color.White;
            host.Controls.Add(_box);

            Controls.Add(host);
            Controls.Add(bar);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Enter))
            {
                CopyAll();
                return true;
            }

            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        void CopyAll()
        {
            try
            {
                ScreenGrab.CopyText(_box.Text ?? "");
                _status.Text = "已复制全部文字";
                _status.ForeColor = Theme.SubText;
            }
            catch (Exception ex)
            {
                _status.Text = ex.Message;
                _status.ForeColor = Markup.Red;
            }
        }
    }
}
