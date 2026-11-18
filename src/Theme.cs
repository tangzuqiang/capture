using System.Drawing;
using System.Windows.Forms;

namespace Jietu
{
    static class Theme
    {
        public static readonly Color Accent = Color.FromArgb(0, 137, 255);
        public static readonly Color AccentHover = Color.FromArgb(0, 120, 230);
        public static readonly Color AccentDown = Color.FromArgb(0, 104, 200);
        public static readonly Color Text = Color.FromArgb(31, 35, 41);
        public static readonly Color SubText = Color.FromArgb(100, 106, 115);
        public static readonly Color Line = Color.FromArgb(220, 223, 230);
        public static readonly Color Page = Color.FromArgb(247, 248, 250);
        public static readonly Color Canvas = Color.FromArgb(232, 234, 237);

        public static Font UiFont()
        {
            return new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Font TitleFont()
        {
            return new Font("Microsoft YaHei UI", 18f, FontStyle.Bold, GraphicsUnit.Point);
        }

        public static Button Button(string text, bool primary)
        {
            var button = new Button();
            button.Text = text;
            button.Font = UiFont();
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(12, 6, 12, 6);
            button.Margin = new Padding(4, 2, 4, 2);
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.BackColor = primary ? Accent : Color.White;
            button.ForeColor = primary ? Color.White : Text;
            button.Cursor = Cursors.Hand;
            button.TabStop = false;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = primary ? Accent : Line;
            button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Color.FromArgb(245, 247, 250);
            button.FlatAppearance.MouseDownBackColor = primary ? AccentDown : Color.FromArgb(232, 236, 242);
            return button;
        }
    }
}
