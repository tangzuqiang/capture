using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Shiying
{
    // 检查到新版本后的对话框：说明 → 下载（带进度）→ 校验 → 启动静默安装
    sealed class UpdateForm : Form
    {
        readonly UpdateInfo _info;
        readonly Label _status;
        readonly ProgressBar _bar;
        readonly Button _go;
        readonly Button _later;
        readonly Button _skip;
        readonly CheckBox _auto;
        volatile bool _cancel;
        bool _busy;

        public UpdateForm(UpdateInfo info)
        {
            _info = info;
            Text = "软件更新";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(440, 264);
            Theme.Apply(this);
            Font = Theme.UiFont();

            var title = new Label();
            title.Text = "发现新版本 " + info.Display;
            title.Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold, GraphicsUnit.Point);
            title.ForeColor = Color.White;
            title.AutoSize = true;
            title.Location = new Point(24, 20);
            title.BackColor = Color.Transparent;

            var line = new Label();
            string size = info.SizeText.Length > 0 ? " · 安装包 " + info.SizeText : "";
            line.Text = "当前版本 v" + (AppInfo.Version.Length > 0 ? AppInfo.Version : "未知") + size;
            line.ForeColor = Theme.SubText;
            line.AutoSize = true;
            line.Location = new Point(26, 56);
            line.BackColor = Color.Transparent;

            var notes = new Label();
            notes.Text = info.Notes.Length > 0 ? info.Notes : "点击「立即更新」下载安装包，装完会自动启动新版本。";
            notes.ForeColor = Theme.Text;
            notes.Location = new Point(26, 82);
            notes.Size = new Size(388, 42);
            notes.BackColor = Color.Transparent;

            _bar = new ProgressBar();
            _bar.Minimum = 0;
            _bar.Maximum = 100;
            _bar.Style = ProgressBarStyle.Continuous;
            _bar.Location = new Point(26, 130);
            _bar.Size = new Size(388, 14);

            _status = new Label();
            _status.Text = "准备下载…";
            _status.ForeColor = Theme.SubText;
            _status.Location = new Point(26, 150);
            _status.Size = new Size(388, 20);
            _status.BackColor = Color.Transparent;

            _auto = new CheckBox();
            _auto.Text = "启动时自动检查更新";
            _auto.ForeColor = Theme.Text;
            _auto.BackColor = Color.Transparent;
            _auto.AutoSize = true;
            _auto.Checked = AppSettings.AutoCheckUpdate;
            _auto.Location = new Point(26, 176);
            _auto.CheckedChanged += (s, e) =>
            {
                AppSettings.AutoCheckUpdate = _auto.Checked;
                AppSettings.Save();
            };

            _later = Theme.Button("稍后", false);
            _later.AutoSize = false;
            _later.Location = new Point(26, 210);
            _later.Size = new Size(96, 34);
            _later.Click += (s, e) => Later();

            _skip = Theme.Button("跳过此版本", false);
            _skip.AutoSize = false;
            _skip.Location = new Point(132, 210);
            _skip.Size = new Size(124, 34);
            _skip.Click += (s, e) => Skip();

            _go = Theme.Button("立即更新", true);
            _go.AutoSize = false;
            _go.Location = new Point(290, 210);
            _go.Size = new Size(124, 34);
            _go.Click += (s, e) => StartDownload();

            Controls.Add(title);
            Controls.Add(line);
            Controls.Add(notes);
            Controls.Add(_bar);
            Controls.Add(_status);
            Controls.Add(_auto);
            Controls.Add(_later);
            Controls.Add(_skip);
            Controls.Add(_go);
        }

        void StartDownload()
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            _go.Enabled = false;
            _later.Text = "取消";
            _status.ForeColor = Theme.SubText;
            _status.Text = "正在下载安装包…";
            _bar.Value = 0;

            var thread = new Thread(DownloadLoop);
            thread.IsBackground = true;
            thread.Start();
        }

        void DownloadLoop()
        {
            long lastReport = -1;
            string error;
            bool ok = Updater.Download(_info, (done, total) =>
            {
                long percent = total > 0 ? done * 100 / total : -1;
                if (percent != lastReport)
                {
                    lastReport = percent;
                    Ui(() =>
                    {
                        if (IsDisposed) return;
                        if (percent >= 0) _bar.Value = (int)Math.Max(0, Math.Min(100, percent));
                        _status.Text = percent >= 0
                            ? "正在下载… " + percent + "%（" + (done / 1048576.0).ToString("0.0") + " / " + (total / 1048576.0).ToString("0.0") + " MB）"
                            : "正在下载… " + (done / 1048576.0).ToString("0.0") + " MB";
                    });
                }
            }, () => _cancel, out error);

            if (!ok)
            {
                Ui(() => Finish(false, error));
                return;
            }

            Ui(() => Finish(true, null));
        }

        void Finish(bool ok, string error)
        {
            _busy = false;
            _later.Text = "稍后";
            _go.Enabled = true;

            if (!ok)
            {
                _status.ForeColor = Theme.Danger;
                _status.Text = error == null || error.Length == 0 ? "下载失败" : "下载失败：" + error;
                _bar.Value = 0;
                return;
            }

            _bar.Value = 100;
            _status.ForeColor = Theme.SubText;
            _status.Text = "校验通过，正在启动安装程序…";

            string runError;
            if (!Updater.RunInstaller(out runError))
            {
                _status.ForeColor = Theme.Danger;
                _status.Text = "启动安装程序失败：" + runError;
                return;
            }

            _status.Text = "拾影将退出，安装完成后自动启动新版本。";
            DialogResult = DialogResult.OK;
        }

        void Later()
        {
            if (_busy)
            {
                _cancel = true;
                _status.ForeColor = Theme.SubText;
                _status.Text = "正在取消…";
                return;
            }

            DialogResult = DialogResult.Cancel;
        }

        void Skip()
        {
            AppSettings.SkipVersion = _info.Version;
            AppSettings.Save();
            DialogResult = DialogResult.Cancel;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cancel = true;
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
