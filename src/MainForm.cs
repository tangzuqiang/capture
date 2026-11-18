using System;
using System.Drawing;
using System.Windows.Forms;

namespace Jietu
{
    sealed class MainForm : Form
    {
        readonly NotifyIcon _tray;
        readonly TextBox[] _hotkeyBoxes = new TextBox[3];
        readonly bool[] _hotkeyReady = new bool[3];
        readonly Label _hotkeyHint;
        readonly TextBox _savePath;
        int _captureSlot = -1;
        bool _exit;
        bool _tipped;
        static readonly int[] HotkeyIds = { 1, 2, 3 };
        static readonly string[] HotkeyNames = { "截图", "复制 RGB", "复制网页色" };

        public MainForm()
        {
            AppSettings.Load();
            Text = "截长图";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(560, 520);
            BackColor = Theme.Page;
            Font = Theme.UiFont();
            Icon = CreateIcon();

            var title = new Label();
            title.Text = "截长图";
            title.Font = Theme.TitleFont();
            title.ForeColor = Theme.Text;
            title.AutoSize = true;
            title.Location = new Point(28, 22);
            title.BackColor = Theme.Page;

            var sub = new Label();
            sub.Text = "先框选区域，再自动滚动窗口并拼接。交互接近钉钉的滚动截屏：表头和底栏只保留一次。";
            sub.ForeColor = Theme.SubText;
            sub.Location = new Point(28, 64);
            sub.Size = new Size(500, 44);
            sub.BackColor = Theme.Page;

            var pickFolder = Theme.Button("截图文件保存位置", false);
            pickFolder.Location = new Point(28, 108);
            pickFolder.Size = new Size(196, 36);
            pickFolder.AutoSize = false;
            pickFolder.Click += (s, e) => PickSaveFolder();

            _savePath = new TextBox();
            _savePath.Location = new Point(232, 113);
            _savePath.Size = new Size(300, 26);
            _savePath.Font = Theme.UiFont();
            _savePath.ReadOnly = true;
            _savePath.BackColor = Color.White;
            _savePath.ForeColor = Theme.Text;
            _savePath.BorderStyle = BorderStyle.FixedSingle;
            _savePath.TabStop = false;
            ShowSaveFolder();

            _hotkeyHint = new Label();
            _hotkeyHint.AutoSize = false;
            _hotkeyHint.Location = new Point(316, 156);
            _hotkeyHint.Size = new Size(216, 96);
            _hotkeyHint.TextAlign = ContentAlignment.TopLeft;
            _hotkeyHint.ForeColor = Theme.SubText;
            _hotkeyHint.BackColor = Theme.Page;
            _hotkeyHint.Text = "点击输入框后按下新键";

            var line = new Panel();
            line.BackColor = Theme.Line;
            line.Location = new Point(28, 268);
            line.Size = new Size(504, 1);

            var help = new Label();
            help.ForeColor = Theme.Text;
            help.BackColor = Theme.Page;
            help.Location = new Point(28, 280);
            help.Size = new Size(504, 168);
            help.Text =
                "1. 快捷键会截取鼠标所在的那块屏幕，拖出一个矩形。\r\n" +
                "2. 框选后可画红框、箭头、写文字、识别文字，再复制、保存或截长图。\r\n" +
                "3. 鼠标停在颜色上，按设置的快捷键复制 RGB 或 #色值。\r\n" +
                "4. 纵向向下滚，横向向右滚，把多屏拼成一张图。\r\n" +
                "5. 滚动过程中按 Esc 提前结束，到边缘会自动停下。\r\n\r\n" +
                "可以连着固定表头、底栏一起框。滚动时不动的部分只会留下一次。\r\n" +
                "尽量不要把滚动条框进去。";

            var paceLabel = new Label();
            paceLabel.Text = "滚动节奏";
            paceLabel.AutoSize = true;
            paceLabel.ForeColor = Theme.SubText;
            paceLabel.BackColor = Theme.Page;
            paceLabel.Location = new Point(28, 462);

            var pace = new ComboBox();
            pace.DropDownStyle = ComboBoxStyle.DropDownList;
            pace.FlatStyle = FlatStyle.Flat;
            pace.Font = Theme.UiFont();
            pace.Location = new Point(104, 458);
            pace.Size = new Size(120, 28);
            pace.Items.AddRange(new object[] { "慢，更稳", "标准", "快" });
            if (AppSettings.Pace == ScrollPace.Slow) pace.SelectedIndex = 0;
            else if (AppSettings.Pace == ScrollPace.Fast) pace.SelectedIndex = 2;
            else pace.SelectedIndex = 1;
            pace.SelectedIndexChanged += (s, e) =>
            {
                if (pace.SelectedIndex == 0) AppSettings.Pace = ScrollPace.Slow;
                else if (pace.SelectedIndex == 2) AppSettings.Pace = ScrollPace.Fast;
                else AppSettings.Pace = ScrollPace.Normal;
                AppSettings.Save();
            };

            Controls.Add(title);
            Controls.Add(sub);
            Controls.Add(pickFolder);
            Controls.Add(_savePath);
            for (int i = 0; i < _hotkeyBoxes.Length; i++)
            {
                int y = 156 + i * 34;
                Controls.Add(MakeHotkeyLabel(HotkeyNames[i], y));
                _hotkeyBoxes[i] = MakeHotkeyBox(i, y - 2);
                Controls.Add(_hotkeyBoxes[i]);
            }

            Controls.Add(_hotkeyHint);
            Controls.Add(line);
            Controls.Add(help);
            Controls.Add(paceLabel);
            Controls.Add(pace);

            _tray = new NotifyIcon();
            _tray.Icon = Icon;
            _tray.Text = TrayText();
            _tray.Visible = true;
            var menu = new ContextMenuStrip();
            menu.Items.Add("开始截图", null, (s, e) => OverlayForm.Run(this));
            menu.Items.Add("显示主窗口", null, (s, e) => ShowMain());
            menu.Items.Add("退出", null, (s, e) => ExitApp());
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => OverlayForm.Run(this);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            for (int i = 0; i < HotkeyIds.Length; i++)
            {
                RegisterSlot(i, GestureOf(i));
            }

            ShowHotkeyHint();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            for (int i = 0; i < HotkeyIds.Length; i++)
            {
                if (_hotkeyReady[i])
                {
                    NativeMethods.UnregisterHotKey(Handle, HotkeyIds[i]);
                    _hotkeyReady[i] = false;
                }
            }

            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HotkeyIds[0])
                {
                    BeginInvoke(new Action(() => OverlayForm.Run(this)));
                }
                else if (id == HotkeyIds[1])
                {
                    BeginInvoke(new Action(() => CopyCursorColor(false)));
                }
                else if (id == HotkeyIds[2])
                {
                    BeginInvoke(new Action(() => CopyCursorColor(true)));
                }
            }

            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!_tipped)
                {
                    _tipped = true;
                    Tip(_hotkeyReady[0]
                        ? "程序仍在托盘中，按 " + AppSettings.FormatGesture(AppSettings.Gesture) + " 开始截图。"
                        : "程序仍在托盘中，可从托盘菜单开始截图。");
                }

                return;
            }

            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _tray.Visible = false;
            _tray.Dispose();
            base.OnFormClosed(e);
        }

        public void Tip(string text)
        {
            if (_tray != null && text != null)
            {
                _tray.ShowBalloonTip(1600, "截长图", text, ToolTipIcon.Info);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_captureSlot >= 0)
            {
                Keys key = keyData & Keys.KeyCode;
                Keys mods = keyData & Keys.Modifiers;
                if (key == Keys.Escape)
                {
                    CancelCapture();
                    return true;
                }

                if (IsModifier(key))
                {
                    return true;
                }

                if (key != Keys.None && mods != Keys.None)
                {
                    ApplyGesture(mods | key);
                    return true;
                }

                _hotkeyHint.Text = "需要带上 Ctrl、Alt 或 Shift";
                _hotkeyHint.ForeColor = Color.FromArgb(196, 72, 48);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        void BeginCapture(int slot)
        {
            if (_captureSlot >= 0 && _captureSlot != slot)
            {
                CancelCapture();
            }

            _captureSlot = slot;
            if (_hotkeyReady[slot])
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyIds[slot]);
                _hotkeyReady[slot] = false;
            }

            _hotkeyBoxes[slot].Text = "请按下组合键";
            _hotkeyHint.Text = "Esc 取消";
            _hotkeyHint.ForeColor = Theme.SubText;
        }

        void CancelCapture()
        {
            int slot = _captureSlot;
            if (slot < 0)
            {
                return;
            }

            _captureSlot = -1;
            if (!_hotkeyReady[slot])
            {
                RegisterSlot(slot, GestureOf(slot));
            }

            _hotkeyBoxes[slot].Text = AppSettings.FormatGesture(GestureOf(slot));
            ShowHotkeyHint();
        }

        void ShowHotkeyHint()
        {
            string problem = null;
            for (int i = 0; i < _hotkeyReady.Length; i++)
            {
                if (!_hotkeyReady[i])
                {
                    problem = HotkeyNames[i] + " 快捷键被占用";
                    break;
                }
            }

            if (problem == null)
            {
                _hotkeyHint.Text = "点击输入框后按下新键";
                _hotkeyHint.ForeColor = Theme.SubText;
            }
            else
            {
                _hotkeyHint.Text = problem;
                _hotkeyHint.ForeColor = Color.FromArgb(196, 72, 48);
            }
        }

        void ApplyGesture(Keys gesture)
        {
            int slot = _captureSlot;
            if (slot < 0)
            {
                return;
            }

            gesture = AppSettings.Normalize(gesture);
            _captureSlot = -1;
            if (!AppSettings.IsValidGesture(gesture))
            {
                if (!_hotkeyReady[slot])
                {
                    RegisterSlot(slot, GestureOf(slot));
                }

                _hotkeyBoxes[slot].Text = AppSettings.FormatGesture(GestureOf(slot));
                _hotkeyHint.Text = "需要带上 Ctrl、Alt 或 Shift";
                _hotkeyHint.ForeColor = Color.FromArgb(196, 72, 48);
                return;
            }

            for (int i = 0; i < HotkeyIds.Length; i++)
            {
                if (i != slot && GestureOf(i) == gesture)
                {
                    if (!_hotkeyReady[slot])
                    {
                        RegisterSlot(slot, GestureOf(slot));
                    }

                    _hotkeyBoxes[slot].Text = AppSettings.FormatGesture(GestureOf(slot));
                    _hotkeyHint.Text = "已用于" + HotkeyNames[i];
                    _hotkeyHint.ForeColor = Color.FromArgb(196, 72, 48);
                    return;
                }
            }

            if (gesture == GestureOf(slot))
            {
                if (!_hotkeyReady[slot])
                {
                    RegisterSlot(slot, gesture);
                }

                _hotkeyBoxes[slot].Text = AppSettings.FormatGesture(gesture);
                ShowHotkeyHint();
                return;
            }

            if (!RegisterSlot(slot, gesture))
            {
                RegisterSlot(slot, GestureOf(slot));
                _hotkeyBoxes[slot].Text = AppSettings.FormatGesture(GestureOf(slot));
                _hotkeyHint.Text = "这个组合已被占用";
                _hotkeyHint.ForeColor = Color.FromArgb(196, 72, 48);
                return;
            }

            SetGesture(slot, gesture);
            AppSettings.Save();
            _hotkeyBoxes[slot].Text = AppSettings.FormatGesture(gesture);
            _tray.Text = TrayText();
            _hotkeyHint.Text = "已记住，下次仍有效";
            _hotkeyHint.ForeColor = Theme.SubText;
        }

        bool RegisterSlot(int slot, Keys gesture)
        {
            if (_hotkeyReady[slot])
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyIds[slot]);
                _hotkeyReady[slot] = false;
            }

            if (!IsHandleCreated || !AppSettings.IsValidGesture(gesture))
            {
                return false;
            }

            _hotkeyReady[slot] = NativeMethods.RegisterHotKey(
                Handle,
                HotkeyIds[slot],
                AppSettings.NativeModifiers(gesture),
                (uint)(gesture & Keys.KeyCode));
            return _hotkeyReady[slot];
        }

        void CopyCursorColor(bool web)
        {
            Color color;
            if (!OverlayForm.TryGetCursorColor(out color))
            {
                Point point = Cursor.Position;
                try
                {
                    using (Bitmap pixel = ScreenGrab.Capture(new Rectangle(point.X, point.Y, 1, 1)))
                    {
                        color = pixel.GetPixel(0, 0);
                    }
                }
                catch
                {
                    Tip("取色失败");
                    return;
                }
            }

            string text = web ? ColorText.Hex(color) : ColorText.Rgb(color);
            try
            {
                ScreenGrab.CopyText(text);
            }
            catch (Exception ex)
            {
                Tip(ex.Message);
                return;
            }

            if (!OverlayForm.NotifyCopied(text))
            {
                Tip("已复制 " + text);
            }
        }

        Label MakeHotkeyLabel(string text, int y)
        {
            var label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Location = new Point(28, y);
            label.Size = new Size(92, 26);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = Theme.SubText;
            label.BackColor = Theme.Page;
            return label;
        }

        TextBox MakeHotkeyBox(int slot, int y)
        {
            var box = new TextBox();
            box.Location = new Point(124, y);
            box.Size = new Size(180, 26);
            box.Font = Theme.UiFont();
            box.ReadOnly = true;
            box.ShortcutsEnabled = false;
            box.BackColor = Color.White;
            box.ForeColor = Theme.Text;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Cursor = Cursors.Hand;
            box.TabStop = false;
            box.Text = AppSettings.FormatGesture(GestureOf(slot));
            box.Enter += (s, e) => BeginCapture(slot);
            box.MouseDown += (s, e) => BeginCapture(slot);
            box.Leave += (s, e) =>
            {
                if (_captureSlot == slot)
                {
                    CancelCapture();
                }
            };
            return box;
        }

        static Keys GestureOf(int slot)
        {
            if (slot == 1) return AppSettings.GestureRgb;
            if (slot == 2) return AppSettings.GestureWeb;
            return AppSettings.Gesture;
        }

        static void SetGesture(int slot, Keys gesture)
        {
            if (slot == 1) AppSettings.GestureRgb = gesture;
            else if (slot == 2) AppSettings.GestureWeb = gesture;
            else AppSettings.Gesture = gesture;
        }

        static bool IsModifier(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.Menu || key == Keys.ShiftKey || key == Keys.LWin || key == Keys.RWin;
        }

        static string TrayText()
        {
            string text = "截长图  " + AppSettings.FormatGesture(AppSettings.Gesture);
            if (text.Length > 63)
            {
                text = text.Substring(0, 63);
            }

            return text;
        }

        void PickSaveFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择截图文件保存位置";
                dialog.ShowNewFolderButton = true;
                string current = AppSettings.SaveDirectory();
                if (!string.IsNullOrEmpty(current))
                {
                    dialog.SelectedPath = current;
                }

                if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(dialog.SelectedPath))
                {
                    return;
                }

                AppSettings.SaveFolder = dialog.SelectedPath;
                AppSettings.Save();
                ShowSaveFolder();
            }
        }

        void ShowSaveFolder()
        {
            string folder = string.IsNullOrEmpty(AppSettings.SaveFolder) ? AppSettings.SaveDirectory() : AppSettings.SaveFolder;
            _savePath.Text = folder;
            if (_savePath.Text.Length > 0)
            {
                _savePath.SelectionStart = _savePath.Text.Length;
            }
        }

        void ShowMain()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        void ExitApp()
        {
            _exit = true;
            Close();
        }

        static Icon CreateIcon()
        {
            using (var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var brush = new SolidBrush(Theme.Accent))
                    {
                        g.FillRectangle(brush, 1, 1, 30, 30);
                    }

                    using (var pen = new Pen(Color.White, 2f))
                    {
                        g.DrawRectangle(pen, 8, 6, 16, 11);
                        g.DrawLine(pen, 16, 17, 16, 25);
                        g.DrawLine(pen, 12, 22, 20, 22);
                    }
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (var icon = Icon.FromHandle(handle))
                    {
                        return (Icon)icon.Clone();
                    }
                }
                finally
                {
                    NativeMethods.DestroyIcon(handle);
                }
            }
        }
    }
}
