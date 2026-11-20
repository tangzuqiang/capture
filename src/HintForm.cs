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
            BackColor = Theme.Surface;
            Size = new Size(460, 42);
            KeyPreview = true;
            Padding = new Padding(0, 3, 0, 0);

            _label = new Label();
            _label.Dock = DockStyle.Fill;
            _label.ForeColor = Theme.Text;
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

        protected override void OnHandleCreated(System.EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.RoundCorners(Handle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var glow = Theme.AccentBrush(new RectangleF(0, 0, Width, 3)))
            {
                e.Graphics.FillRectangle(glow, 0, 0, Width, 3);
            }
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
