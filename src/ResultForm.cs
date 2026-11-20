using System;
using System.Drawing;
using System.Windows.Forms;

namespace Jietu
{
    sealed class ResultForm : Form
    {
        readonly Bitmap _image;
        readonly Panel _canvas;
        readonly PictureBox _picture;
        readonly Label _info;
        readonly Button _fitButton;
        bool _fit = true;
        bool _layouting;

        public ResultForm(ScrollCapture.Outcome outcome)
        {
            _image = outcome.Image;
            Text = "截长图";
            StartPosition = FormStartPosition.CenterScreen;
            Theme.Apply(this);
            Font = Theme.UiFont();
            MinimumSize = new Size(640, 420);
            KeyPreview = true;

            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1080, area.Width - 80), Math.Min(760, area.Height - 80));

            var bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = 56;
            bar.BackColor = Theme.Surface;

            _info = new Label();
            _info.AutoSize = false;
            _info.Location = new Point(16, 8);
            _info.Size = new Size(460, 40);
            _info.ForeColor = Theme.Text;
            _info.TextAlign = ContentAlignment.MiddleLeft;
            string axis = outcome.Vertical ? "纵向" : "横向";
            _info.Text = string.Format("{0} · {1} 屏 · {2} × {3}\r\n{4}", axis, Math.Max(1, outcome.Frames), _image.Width, _image.Height, outcome.Message ?? "");

            var copy = Theme.Button("复制", true);
            var save = Theme.Button("保存", false);
            _fitButton = Theme.Button("实际大小", false);
            copy.Click += (s, e) => DoCopy();
            save.Click += (s, e) => DoSave();
            _fitButton.Click += (s, e) =>
            {
                _fit = !_fit;
                _fitButton.Text = _fit ? "实际大小" : "适应宽度";
                ApplyZoom();
            };

            var buttons = new FlowLayoutPanel();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Dock = DockStyle.Right;
            buttons.Width = 360;
            buttons.Padding = new Padding(0, 10, 12, 0);
            buttons.WrapContents = false;
            buttons.Controls.Add(save);
            buttons.Controls.Add(copy);
            buttons.Controls.Add(_fitButton);
            bar.Controls.Add(buttons);
            bar.Controls.Add(_info);

            _canvas = new Panel();
            _canvas.Dock = DockStyle.Fill;
            _canvas.AutoScroll = true;
            _canvas.BackColor = Theme.Canvas;
            _canvas.HandleCreated += (s, e) => Theme.DarkScrollbars(_canvas.Handle);
            _canvas.Resize += (s, e) =>
            {
                if (_fit)
                {
                    ApplyZoom();
                }
            };

            _picture = new PictureBox();
            _picture.Image = _image;
            _picture.SizeMode = PictureBoxSizeMode.Zoom;
            _picture.BackColor = Color.White;
            _picture.DoubleClick += (s, e) => _fitButton.PerformClick();
            _canvas.Controls.Add(_picture);

            Controls.Add(_canvas);
            Controls.Add(bar);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyZoom();
            Activate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_image != null)
            {
                _image.Dispose();
            }

            base.OnFormClosed(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C) || keyData == Keys.Enter)
            {
                DoCopy();
                return true;
            }

            if (keyData == (Keys.Control | Keys.S))
            {
                DoSave();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        void ApplyZoom()
        {
            if (_layouting || _image == null)
            {
                return;
            }

            _layouting = true;
            try
            {
                if (_fit)
                {
                    int width = Math.Max(80, _canvas.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
                    double scale = width / (double)_image.Width;
                    int height = Math.Max(1, (int)Math.Round(_image.Height * scale));
                    _picture.SizeMode = PictureBoxSizeMode.Zoom;
                    _picture.Location = new Point(4, 8);
                    _picture.Size = new Size(width, height);
                }
                else
                {
                    _picture.SizeMode = PictureBoxSizeMode.Normal;
                    _picture.Location = new Point(4, 8);
                    _picture.Size = _image.Size;
                }
            }
            finally
            {
                _layouting = false;
            }
        }

        void DoCopy()
        {
            try
            {
                ScreenGrab.CopyToClipboard(_image);
                _info.Text = string.Format("已复制到剪贴板\r\n{0} × {1}", _image.Width, _image.Height);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "截长图", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void DoSave()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "保存长图";
                dialog.Filter = "PNG 图片|*.png|JPEG 图片|*.jpg";
                dialog.FileName = "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                dialog.InitialDirectory = AppSettings.SaveDirectory();
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    ScreenGrab.Save(_image, dialog.FileName);
                    _info.Text = "已保存\r\n" + dialog.FileName;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "截长图", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }
    }
}
