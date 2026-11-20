using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Jietu
{
    sealed class TranslateForm : Form
    {
        readonly TextBox _source;
        readonly TextBox _result;
        readonly SplitContainer _split;
        readonly Label _status;
        readonly Button _translate;
        readonly Button _copyResult;
        bool _translating;

        public TranslateForm(string text)
        {
            Text = "翻译";
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
            Theme.Apply(this);
            MinimumSize = new Size(560, 360);
            Size = new Size(680, 560);
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

            var model = Theme.Button("模型配置", false);
            _translate = Theme.Button("重新翻译", false);
            _copyResult = Theme.Button("复制译文", true);
            var close = Theme.Button("关闭", false);
            _copyResult.Enabled = false;
            model.Click += (s, e) => ModelForm.Edit(this);
            _translate.Click += (s, e) => Translate();
            _copyResult.Click += (s, e) => CopyResult();
            close.Click += (s, e) => Close();

            var buttons = new FlowLayoutPanel();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Dock = DockStyle.Right;
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.WrapContents = false;
            buttons.Padding = new Padding(0, 8, 8, 0);
            buttons.Controls.Add(close);
            buttons.Controls.Add(_copyResult);
            buttons.Controls.Add(_translate);
            buttons.Controls.Add(model);
            bar.Controls.Add(_status);
            bar.Controls.Add(buttons);

            _source = Editor(text ?? "", false);
            _result = Editor("", true);

            _split = new SplitContainer();
            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Horizontal;
            _split.BackColor = Theme.Line;
            _split.SplitterWidth = 1;
            _split.Panel1.Controls.Add(Section("原文（可修改后重新翻译）", _source));
            _split.Panel2.Controls.Add(Section("译文", _result));

            Controls.Add(_split);
            Controls.Add(bar);
            Shown += (s, e) => Translate();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (_split.Height > 120)
            {
                _split.SplitterDistance = _split.Height * 2 / 5;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Enter))
            {
                CopyResult();
                return true;
            }

            if (keyData == (Keys.Control | Keys.T))
            {
                Translate();
                return true;
            }

            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        static TextBox Editor(string text, bool readOnly)
        {
            var box = new TextBox();
            box.Multiline = true;
            box.ScrollBars = ScrollBars.Both;
            box.WordWrap = true;
            box.BorderStyle = BorderStyle.None;
            box.Dock = DockStyle.Fill;
            box.HideSelection = false;
            box.ReadOnly = readOnly;
            Theme.Input(box);
            box.Text = text;
            return box;
        }

        static Control Section(string caption, TextBox box)
        {
            var host = new Panel();
            host.Dock = DockStyle.Fill;
            host.Padding = new Padding(12, 4, 12, 4);
            host.BackColor = Theme.Surface;

            var label = new Label();
            label.Text = caption;
            label.Dock = DockStyle.Top;
            label.Height = 24;
            label.ForeColor = Theme.SubText;
            label.Font = Theme.UiFont();
            label.TextAlign = ContentAlignment.MiddleLeft;

            host.Controls.Add(box);
            host.Controls.Add(label);
            return host;
        }

        void Translate()
        {
            if (_translating)
            {
                return;
            }

            string source = (_source.Text ?? "").Trim();
            if (source.Length == 0)
            {
                ShowStatus("没有可翻译的文字", true);
                return;
            }

            if (!AppSettings.LlmConfigured)
            {
                ShowStatus("请先配置翻译模型", true);
                if (!ModelForm.Edit(this))
                {
                    return;
                }
            }

            _translating = true;
            _translate.Enabled = false;
            _copyResult.Enabled = false;
            _result.Text = "";
            ShowStatus("正在翻译…", false);
            LlmConfig config = LlmConfig.FromSettings();
            Task.Run(() => LlmTranslator.Translate(source, config)).ContinueWith(done =>
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }

                try
                {
                    BeginInvoke(new Action(() => FinishTranslate(done)));
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        void FinishTranslate(Task<string> task)
        {
            _translating = false;
            _translate.Enabled = true;
            if (task.IsFaulted || task.IsCanceled)
            {
                Exception ex = task.Exception == null ? null : task.Exception.GetBaseException();
                ShowStatus(ex == null ? "翻译失败" : "翻译失败：" + ex.Message, true);
                return;
            }

            _result.Text = task.Result ?? "";
            _copyResult.Enabled = _result.TextLength > 0;
            ShowStatus(_result.TextLength > 0 ? "翻译完成" : "模型没有返回译文", _result.TextLength == 0);
        }

        void CopyResult()
        {
            if (_result.TextLength == 0)
            {
                return;
            }

            try
            {
                ScreenGrab.CopyText(_result.Text);
                ShowStatus("已复制译文", false);
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
