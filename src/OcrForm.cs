using System;
using System.Drawing;
using System.Windows.Forms;

namespace Shiying
{
    sealed class OcrForm : Form
    {
        readonly TextBox _box;
        readonly Label _status;
        readonly Button _edit;
        string _savedText;

        public OcrForm(string text)
        {
            Text = "识别文字";
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
            Theme.Apply(this);
            MinimumSize = new Size(440, 280);
            Size = new Size(620, 420);
            TopMost = true;
            KeyPreview = true;

            var bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 52;
            bar.BackColor = Theme.Page;

            _status = new Label();
            _status.AutoSize = false;
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Font = Theme.UiFont();
            _status.AutoEllipsis = true;
            _status.Padding = new Padding(12, 0, 0, 0);

            _edit = Theme.Button("编辑", false);
            var copy = Theme.Button("复制", true);
            var close = Theme.Button("关闭", false);
            _edit.Click += (s, e) => ToggleEdit();
            copy.Click += (s, e) => CopyAll();
            close.Click += (s, e) => Close();

            var buttons = new FlowLayoutPanel();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Dock = DockStyle.Right;
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.WrapContents = false;
            buttons.Padding = new Padding(0, 8, 8, 0);
            buttons.Controls.Add(close);
            buttons.Controls.Add(copy);
            buttons.Controls.Add(_edit);
            bar.Controls.Add(_status);
            bar.Controls.Add(buttons);

            _box = new TextBox();
            _box.Multiline = true;
            _box.ScrollBars = ScrollBars.Both;
            _box.WordWrap = true;
            _box.BorderStyle = BorderStyle.None;
            _box.Dock = DockStyle.Fill;
            _box.HideSelection = false;
            _box.ReadOnly = true;
            Theme.Input(_box);
            _box.Text = text ?? "";
            _box.DoubleClick += (s, e) =>
            {
                if (_box.ReadOnly)
                {
                    ToggleEdit();
                }
            };

            var host = new Panel();
            host.Dock = DockStyle.Fill;
            host.Padding = new Padding(14, 12, 14, 8);
            host.BackColor = Theme.Surface;
            host.Margin = new Padding(0);
            host.Controls.Add(_box);

            Controls.Add(host);
            Controls.Add(bar);
            ShowStatus("已复制到剪贴板，点“编辑”可修改", false);
        }

        public bool Editing
        {
            get { return !_box.ReadOnly; }
        }

        public string Content
        {
            get { return _box.Text; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _box.SelectionStart = 0;
            _box.SelectionLength = 0;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Enter))
            {
                CopyAll();
                return true;
            }

            if (keyData == (Keys.Control | Keys.E))
            {
                ToggleEdit();
                return true;
            }

            if (keyData == Keys.Escape)
            {
                if (Editing)
                {
                    _box.Text = _savedText;
                    SetEditing(false);
                    ShowStatus("已撤销本次修改", false);
                    return true;
                }

                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void ToggleEdit()
        {
            if (Editing)
            {
                SetEditing(false);
                ShowStatus("修改已保存，点“复制”带走", false);
                return;
            }

            _savedText = _box.Text;
            SetEditing(true);
            _box.Focus();
            _box.SelectionStart = _box.TextLength;
            _box.SelectionLength = 0;
            ShowStatus("编辑中：改完点“完成”，Esc 撤销修改", false);
        }

        void SetEditing(bool editing)
        {
            _box.ReadOnly = !editing;
            _box.BackColor = editing ? Theme.SurfaceHigh : Theme.Surface;
            if (_box.Parent != null)
            {
                _box.Parent.BackColor = _box.BackColor;
            }
            _edit.Text = editing ? "完成" : "编辑";
        }

        public void CopyAll()
        {
            try
            {
                ScreenGrab.CopyText(_box.Text ?? "");
                ShowStatus(Editing ? "已复制当前内容" : "已复制全部文字", false);
            }
            catch (Exception ex)
            {
                ShowStatus(ex.Message, true);
            }
        }

        void ShowStatus(string text, bool bad)
        {
            _status.Text = text;
            _status.ForeColor = bad ? Theme.Danger : Theme.SubText;
        }
    }
}
