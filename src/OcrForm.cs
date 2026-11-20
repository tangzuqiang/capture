using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Jietu
{
    sealed class OcrForm : Form
    {
        readonly TextBox _box;
        readonly TextBox _result;
        readonly SplitContainer _split;
        readonly Label _status;
        readonly Button _translate;
        readonly Button _copyResult;
        bool _translating;

        public OcrForm(string text, bool translate)
        {
            Text = translate ? "识别并翻译" : "识别文字";
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.White;
            MinimumSize = new Size(560, 320);
            Size = new Size(680, translate ? 560 : 420);
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
            _status.Font = Theme.UiFont();
            _status.AutoEllipsis = true;
            _status.Padding = new Padding(12, 0, 0, 0);
            _status.Text = translate ? "正在翻译…" : "已复制到剪贴板，可以改完再复制";

            var model = Theme.Button("模型配置", false);
            _translate = Theme.Button("翻译", false);
            var copy = Theme.Button("复制原文", !translate);
            _copyResult = Theme.Button("复制译文", true);
            var close = Theme.Button("关闭", false);
            _copyResult.Visible = false;
            model.Click += (s, e) => ModelForm.Edit(this);
            _translate.Click += (s, e) => Translate();
            copy.Click += (s, e) => CopyText(_box.Text, "已复制原文");
            _copyResult.Click += (s, e) => CopyText(_result.Text, "已复制译文");
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
            buttons.Controls.Add(copy);
            buttons.Controls.Add(_translate);
            buttons.Controls.Add(model);
            bar.Controls.Add(_status);
            bar.Controls.Add(buttons);

            _box = Editor(text ?? "");
            _result = Editor("");

            _split = new SplitContainer();
            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Horizontal;
            _split.BackColor = Theme.Line;
            _split.SplitterWidth = 1;
            _split.Panel1.Controls.Add(Section("原文", _box));
            _split.Panel2.Controls.Add(Section("译文", _result));
            _split.Panel2Collapsed = !translate;

            Controls.Add(_split);
            Controls.Add(bar);

            if (translate)
            {
                Shown += (s, e) => Translate();
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            CenterSplit();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Enter))
            {
                CopyText(_box.Text, "已复制原文");
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

        static TextBox Editor(string text)
        {
            var box = new TextBox();
            box.Multiline = true;
            box.ScrollBars = ScrollBars.Both;
            box.WordWrap = true;
            box.BorderStyle = BorderStyle.None;
            box.Dock = DockStyle.Fill;
            box.HideSelection = false;
            box.Text = text;
            return box;
        }

        static Control Section(string caption, TextBox box)
        {
            var host = new Panel();
            host.Dock = DockStyle.Fill;
            host.Padding = new Padding(12, 4, 12, 4);
            host.BackColor = Color.White;

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

        void CenterSplit()
        {
            if (!_split.Panel2Collapsed && _split.Height > 120)
            {
                _split.SplitterDistance = _split.Height / 2;
            }
        }

        void Translate()
        {
            if (_translating)
            {
                return;
            }

            string source = (_box.Text ?? "").Trim();
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

            bool wasCollapsed = _split.Panel2Collapsed;
            _split.Panel2Collapsed = false;
            if (wasCollapsed)
            {
                if (Height < 520)
                {
                    Height = 560;
                }

                CenterSplit();
            }

            _translating = true;
            _translate.Enabled = false;
            _copyResult.Visible = false;
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
            _copyResult.Visible = _result.TextLength > 0;
            if (_result.TextLength > 0)
            {
                CopyText(_result.Text, "译文已复制到剪贴板");
            }
            else
            {
                ShowStatus("模型没有返回译文", true);
            }
        }

        void CopyText(string text, string done)
        {
            try
            {
                ScreenGrab.CopyText(text ?? "");
                ShowStatus(done, false);
            }
            catch (Exception ex)
            {
                ShowStatus(ex.Message, true);
            }
        }

        void ShowStatus(string text, bool bad)
        {
            _status.Text = text;
            _status.ForeColor = bad ? Markup.Red : Theme.SubText;
        }
    }
}
