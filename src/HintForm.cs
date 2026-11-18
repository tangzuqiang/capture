using System.Drawing;
using System.Windows.Forms;

namespace Jietu
{
    sealed class HintForm : Form
    {
        readonly Label _label;

        public HintForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(31, 35, 41);
            Size = new Size(460, 40);
            KeyPreview = true;

            _label = new Label();
            _label.Dock = DockStyle.Fill;
            _label.ForeColor = Color.White;
            _label.Font = Theme.UiFont();
            _label.TextAlign = ContentAlignment.MiddleCenter;
            _label.Text = "正在截长图…";
            Controls.Add(_label);
        }

        public void SetText(string text)
        {
            _label.Text = text;
            Invalidate();
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
                cp.ExStyle |= 0x08000000;
                cp.ExStyle |= 0x00000080;
                return cp;
            }
        }
    }
}
