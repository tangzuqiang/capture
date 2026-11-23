using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Shiying.Voice;

namespace Shiying
{
    // 语音模型下载对话框：说明体积 → 下载（带进度）→ 解压 → 完成。
    // 下载和解压都在后台线程，关窗口即取消；下到一半的 .part 会留着，下次续着用完（按文件跳过）。
    sealed class VoiceModelForm : Form
    {
        readonly string _models;
        readonly Label _status;
        readonly Label _detail;
        readonly ProgressBar _bar;
        readonly Button _go;
        readonly Button _cancel;
        volatile bool _stop;
        bool _busy;
        bool _ok;

        public VoiceModelForm(string models)
        {
            _models = models;
            Text = "下载语音模型";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 268);
            Theme.Apply(this);
            Font = Theme.UiFont();

            var title = new Label();
            title.Text = "语音助手需要两个模型";
            title.Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold, GraphicsUnit.Point);
            title.ForeColor = Color.White;
            title.AutoSize = true;
            title.Location = new Point(24, 18);

            var desc = new Label();
            desc.Text =
                "语音模型约 160 MB，不方便放进安装包，改为第一次使用时下载。\r\n" +
                "· Vosk 小中文模型（43 MB）—— 听懂「截图」「开始录制」这些命令\r\n" +
                "· VITS 中文语音（114 MB）—— 本地合成朗读，不联网";
            desc.ForeColor = Theme.Text;
            desc.AutoSize = false;
            desc.Location = new Point(26, 54);
            desc.Size = new Size(428, 74);

            var where = new Label();
            where.Text = "下载到：" + _models;
            where.ForeColor = Theme.SubText;
            where.AutoSize = false;
            where.AutoEllipsis = true;
            where.Location = new Point(26, 134);
            where.Size = new Size(428, 20);
            var tip = new ToolTip();
            Theme.DarkTips(tip);
            tip.SetToolTip(where, _models);

            _bar = new ProgressBar();
            _bar.Minimum = 0;
            _bar.Maximum = 100;
            _bar.Style = ProgressBarStyle.Continuous;
            _bar.Location = new Point(26, 160);
            _bar.Size = new Size(428, 14);

            _status = new Label();
            _status.Text = "准备下载…";
            _status.ForeColor = Theme.SubText;
            _status.AutoSize = false;
            _status.AutoEllipsis = true;
            _status.Location = new Point(26, 178);
            _status.Size = new Size(428, 20);

            _detail = new Label();
            _detail.Text = "";
            _detail.ForeColor = Theme.SubText;
            _detail.AutoSize = false;
            _detail.Location = new Point(26, 198);
            _detail.Size = new Size(428, 20);

            _cancel = Theme.Button("取消", false);
            _cancel.AutoSize = false;
            _cancel.Location = new Point(26, 224);
            _cancel.Size = new Size(96, 32);
            _cancel.Click += (s, e) => Close();

            _go = Theme.Button("开始下载", true);
            _go.AutoSize = false;
            _go.Location = new Point(330, 224);
            _go.Size = new Size(124, 32);
            _go.Click += (s, e) =>
            {
                if (_ok)
                {
                    Close();
                    return;
                }
                StartDownload();
            };

            Controls.Add(title);
            Controls.Add(desc);
            Controls.Add(where);
            Controls.Add(_bar);
            Controls.Add(_status);
            Controls.Add(_detail);
            Controls.Add(_cancel);
            Controls.Add(_go);
            AcceptButton = _go;
        }

        // 返回 true 表示两个模型都齐了
        public static bool Ensure(IWin32Window owner, string models)
        {
            if (VoiceModelInstaller.Ready(models))
            {
                return true;
            }

            using (var form = new VoiceModelForm(models))
            {
                return form.ShowDialog(owner) == DialogResult.OK;
            }
        }

        void StartDownload()
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            _stop = false;
            _go.Enabled = false;
            _go.Text = "下载中…";
            _status.ForeColor = Theme.SubText;
            _bar.Value = 0;

            var thread = new Thread(Work);
            thread.IsBackground = true;
            thread.Start();
        }

        void Work()
        {
            long lastPercent = -1;
            try
            {
                VoiceModelInstaller.Install(_models,
                    text => Ui(() =>
                    {
                        if (!IsDisposed) _status.Text = text;
                    }),
                    (done, total) => Ui(() =>
                    {
                        if (IsDisposed)
                        {
                            return;
                        }
                        long percent = total > 0 ? done * 100 / total : -1;
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            if (percent >= 0)
                            {
                                _bar.Value = (int)Math.Max(0, Math.Min(100, percent));
                            }
                        }
                        _detail.Text = total > 0
                            ? (done / 1048576.0).ToString("0.0") + " / " + (total / 1048576.0).ToString("0.0") + " MB"
                            : (done / 1048576.0).ToString("0.0") + " MB";
                    }),
                    () => _stop);
                Ui(() => Finish(null));
            }
            catch (OperationCanceledException)
            {
                Ui(() => Finish("已取消"));
            }
            catch (Exception ex)
            {
                string message = ex.Message;
                Ui(() => Finish(message));
            }
        }

        void Finish(string error)
        {
            _busy = false;
            _go.Enabled = true;
            _cancel.Visible = error == null;

            if (error == null)
            {
                _ok = true;
                _bar.Value = 100;
                _status.ForeColor = Theme.SubText;
                _status.Text = "模型已就绪，可以用语音助手了。";
                _detail.Text = "下次换电脑时，托盘菜单里再点一次「下载语音模型…」即可。";
                _go.Text = "完成";
                DialogResult = DialogResult.None;
                return;
            }

            _status.ForeColor = Theme.Danger;
            _status.Text = error == "已取消" ? "已取消，未下载完的文件下次会接着用。" : "下载失败：" + error;
            _bar.Value = 0;
            _detail.Text = "";
            _go.Text = "重试";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _stop = true;
            if (DialogResult == DialogResult.None)
            {
                DialogResult = _ok ? DialogResult.OK : DialogResult.Cancel;
            }
            base.OnFormClosing(e);
        }

        void Ui(Action action)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
