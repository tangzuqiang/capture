using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Jietu
{
    sealed class ModelForm : Form
    {
        readonly ComboBox _preset;
        readonly Label _note;
        readonly TextBox _baseUrl;
        readonly TextBox _apiKey;
        readonly TextBox _model;
        readonly ComboBox _target;
        readonly Label _status;
        readonly Button _test;
        readonly Button _save;
        bool _filling;

        public ModelForm()
        {
            Text = "模型配置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(540, 432);
            Theme.Apply(this);
            Font = Theme.UiFont();
            TopMost = true;
            KeyPreview = true;

            var title = new Label();
            title.Text = "翻译模型";
            title.Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold, GraphicsUnit.Point);
            title.ForeColor = Theme.Text;
            title.AutoSize = true;
            title.Location = new Point(24, 18);

            var sub = new Label();
            sub.Text = "兼容 OpenAI 的 /chat/completions 接口都可以用。API Key 加密保存在本机。";
            sub.ForeColor = Theme.SubText;
            sub.AutoSize = false;
            sub.Location = new Point(24, 48);
            sub.Size = new Size(492, 20);

            _preset = new ComboBox();
            _preset.DropDownStyle = ComboBoxStyle.DropDownList;
            Theme.Input(_preset);
            _preset.Items.AddRange(LlmPreset.All);
            _preset.SelectedIndexChanged += (s, e) => ApplyPreset();

            _note = new Label();
            _note.ForeColor = Theme.SubText;
            _note.AutoSize = false;
            _note.Location = new Point(120, 112);
            _note.Size = new Size(396, 20);

            _baseUrl = Field();
            _apiKey = Field();
            _apiKey.UseSystemPasswordChar = true;
            _model = Field();

            var reveal = new CheckBox();
            reveal.Text = "显示";
            reveal.AutoSize = true;
            reveal.ForeColor = Theme.SubText;
            reveal.Location = new Point(462, 180);
            reveal.CheckedChanged += (s, e) => _apiKey.UseSystemPasswordChar = !reveal.Checked;

            _target = new ComboBox();
            _target.DropDownStyle = ComboBoxStyle.DropDownList;
            Theme.Input(_target);
            _target.Items.AddRange(TranslateTarget.All);

            AddRow("服务商", _preset, 80, 396);
            AddRow("接口地址", _baseUrl, 140, 396);
            AddRow("API Key", _apiKey, 176, 334);
            AddRow("模型名称", _model, 212, 396);
            AddRow("翻译成", _target, 248, 396);

            var line = new Panel();
            line.BackColor = Theme.Line;
            line.Location = new Point(24, 292);
            line.Size = new Size(492, 1);

            var ocr = new Label();
            ocr.ForeColor = Theme.SubText;
            ocr.AutoSize = false;
            ocr.Location = new Point(24, 304);
            ocr.Size = new Size(492, 40);
            ocr.Text = OcrEngineText();

            _status = new Label();
            _status.AutoSize = false;
            _status.Location = new Point(24, 348);
            _status.Size = new Size(492, 22);
            _status.ForeColor = Theme.SubText;
            _status.AutoEllipsis = true;

            _test = Theme.Button("测试连接", false);
            _save = Theme.Button("保存", true);
            var cancel = Theme.Button("取消", false);
            _test.Click += (s, e) => TestConnection();
            _save.Click += (s, e) => SaveAndClose();
            cancel.Click += (s, e) => Close();

            var buttons = new FlowLayoutPanel();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.Location = new Point(24, 380);
            buttons.Size = new Size(496, 40);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(_save);
            buttons.Controls.Add(_test);

            Controls.Add(title);
            Controls.Add(sub);
            Controls.Add(_note);
            Controls.Add(reveal);
            Controls.Add(line);
            Controls.Add(ocr);
            Controls.Add(_status);
            Controls.Add(buttons);
            AcceptButton = _save;

            LoadSettings();
        }

        public static bool Edit(IWin32Window owner)
        {
            using (var form = new ModelForm())
            {
                return form.ShowDialog(owner) == DialogResult.OK;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        static string OcrEngineText()
        {
#if NET
            if (PaddleOcr.ModelsPresent)
            {
                return "文字识别：PaddleOCR PP-OCRv5（ONNX，离线，已内置）。识别失败时自动改用 Windows 自带识别。";
            }

            return "文字识别：没有找到 PaddleOCR 模型，正在使用 Windows 自带识别。";
#else
            return "文字识别和翻译需要使用安装包里的程序。";
#endif
        }

        TextBox Field()
        {
            var box = new TextBox();
            box.BorderStyle = BorderStyle.FixedSingle;
            Theme.Input(box);
            return box;
        }

        void AddRow(string caption, Control input, int y, int width)
        {
            var label = new Label();
            label.Text = caption;
            label.AutoSize = false;
            label.Location = new Point(24, y + 2);
            label.Size = new Size(92, 22);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = Theme.SubText;
            input.Location = new Point(120, y);
            input.Width = width;
            Controls.Add(label);
            Controls.Add(input);
        }

        void LoadSettings()
        {
            _filling = true;
            try
            {
                LlmPreset preset = LlmPreset.Find(AppSettings.LlmPreset);
                if (preset == null)
                {
                    preset = AppSettings.LlmConfigured ? LlmPreset.All[LlmPreset.All.Length - 1] : LlmPreset.All[0];
                }

                _preset.SelectedItem = preset;
                _note.Text = preset.Note;
                _baseUrl.Text = AppSettings.LlmConfigured ? AppSettings.LlmBaseUrl : preset.BaseUrl;
                _model.Text = AppSettings.LlmConfigured ? AppSettings.LlmModel : preset.Model;
                _apiKey.Text = AppSettings.LlmApiKey;
                _target.SelectedItem = TranslateTarget.Find(AppSettings.TranslateTarget);
            }
            finally
            {
                _filling = false;
            }
        }

        void ApplyPreset()
        {
            var preset = _preset.SelectedItem as LlmPreset;
            if (preset == null)
            {
                return;
            }

            _note.Text = preset.Note;
            if (_filling || preset.BaseUrl.Length == 0)
            {
                return;
            }

            _baseUrl.Text = preset.BaseUrl;
            _model.Text = preset.Model;
        }

        LlmConfig CurrentConfig()
        {
            var config = new LlmConfig();
            config.BaseUrl = _baseUrl.Text.Trim();
            config.ApiKey = _apiKey.Text.Trim();
            config.Model = _model.Text.Trim();
            var target = _target.SelectedItem as TranslateTarget;
            config.Target = target == null ? "auto" : target.Code;
            return config;
        }

        string Validate(LlmConfig config)
        {
            if (config.BaseUrl.Length == 0)
            {
                return "请填写接口地址";
            }

            Uri uri;
            if (!Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                return "接口地址要以 http:// 或 https:// 开头";
            }

            if (config.Model.Length == 0)
            {
                return "请填写模型名称";
            }

            var preset = _preset.SelectedItem as LlmPreset;
            if (preset != null && preset.NeedsKey && config.ApiKey.Length == 0)
            {
                return "这个服务商需要 API Key";
            }

            return null;
        }

        void ShowStatus(string text, bool bad)
        {
            _status.Text = text;
            _status.ForeColor = bad ? Theme.Danger : Theme.SubText;
        }

        void TestConnection()
        {
            LlmConfig config = CurrentConfig();
            string problem = Validate(config);
            if (problem != null)
            {
                ShowStatus(problem, true);
                return;
            }

            config.Target = "en";
            _test.Enabled = false;
            ShowStatus("正在请求模型…", false);
            Task.Run(() => LlmTranslator.Translate("你好，世界", config)).ContinueWith(done =>
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }

                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        _test.Enabled = true;
                        if (done.IsFaulted)
                        {
                            Exception ex = done.Exception.GetBaseException();
                            ShowStatus("失败：" + ex.Message, true);
                        }
                        else
                        {
                            ShowStatus("连接成功：你好，世界 → " + done.Result.Replace("\r\n", " "), false);
                        }
                    }));
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        void SaveAndClose()
        {
            LlmConfig config = CurrentConfig();
            string problem = Validate(config);
            if (problem != null)
            {
                ShowStatus(problem, true);
                return;
            }

            var preset = _preset.SelectedItem as LlmPreset;
            AppSettings.LlmPreset = preset == null ? "" : preset.Name;
            AppSettings.LlmBaseUrl = config.BaseUrl;
            AppSettings.LlmApiKey = config.ApiKey;
            AppSettings.LlmModel = config.Model;
            AppSettings.TranslateTarget = config.Target;
            AppSettings.Save();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
