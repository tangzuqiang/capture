using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Jietu
{
    static class Theme
    {
        public static readonly Color Accent = Color.FromArgb(0, 184, 255);
        public static readonly Color Accent2 = Color.FromArgb(139, 92, 246);
        public static readonly Color AccentHover = Color.FromArgb(56, 204, 255);
        public static readonly Color AccentDown = Color.FromArgb(0, 150, 220);
        public static readonly Color Text = Color.FromArgb(233, 237, 247);
        public static readonly Color SubText = Color.FromArgb(142, 150, 175);
        public static readonly Color Line = Color.FromArgb(50, 56, 78);
        public static readonly Color Page = Color.FromArgb(14, 16, 25);
        public static readonly Color Surface = Color.FromArgb(24, 27, 40);
        public static readonly Color SurfaceHigh = Color.FromArgb(36, 40, 58);
        public static readonly Color Canvas = Color.FromArgb(9, 10, 16);
        public static readonly Color Danger = Color.FromArgb(255, 107, 107);

        public static Font UiFont()
        {
            return new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Font TitleFont()
        {
            return new Font("Microsoft YaHei UI", 18f, FontStyle.Bold, GraphicsUnit.Point);
        }

        public static LinearGradientBrush AccentBrush(RectangleF bounds, int alpha = 255)
        {
            if (bounds.Width < 1) bounds.Width = 1;
            if (bounds.Height < 1) bounds.Height = 1;
            return new LinearGradientBrush(bounds, Color.FromArgb(alpha, Accent), Color.FromArgb(alpha, Accent2), LinearGradientMode.ForwardDiagonal);
        }

        public static GraphicsPath Round(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (d <= 0.5f)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Button Button(string text, bool primary)
        {
            var button = new GlowButton(primary);
            button.Text = text;
            button.Font = UiFont();
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(14, 7, 14, 7);
            button.Margin = new Padding(4, 2, 4, 2);
            button.Cursor = Cursors.Hand;
            button.TabStop = false;
            return button;
        }

        static Icon _appIcon;

        public static Icon AppIcon
        {
            get
            {
                if (_appIcon == null)
                {
                    using (System.IO.Stream stream = typeof(Theme).Assembly.GetManifestResourceStream("Jietu.capture.ico"))
                    {
                        _appIcon = stream != null ? new Icon(stream) : SystemIcons.Application;
                    }
                }

                return _appIcon;
            }
        }

        public static Icon TrayIcon()
        {
            return new Icon(AppIcon, SystemInformation.SmallIconSize);
        }

        public static void Apply(Form form)
        {
            form.Icon = AppIcon;
            form.BackColor = Page;
            form.ForeColor = Text;
            form.HandleCreated += (s, e) => DarkChrome(form.Handle);
        }

        public static void Input(TextBox box)
        {
            box.BackColor = Surface;
            box.ForeColor = Text;
            box.HandleCreated += (s, e) => DarkScrollbars(box.Handle);
        }

        public static void Input(ComboBox box)
        {
            box.FlatStyle = FlatStyle.Flat;
            box.BackColor = Surface;
            box.ForeColor = Text;
            box.DrawMode = DrawMode.OwnerDrawFixed;
            box.ItemHeight = Math.Max(box.ItemHeight, 20);
            box.HandleCreated += (s, e) => DarkScrollbars(box.Handle);
            box.DrawItem += (s, e) =>
            {
                bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
                bool selected = !edit && (e.State & DrawItemState.Selected) != 0;
                if (selected)
                {
                    using (LinearGradientBrush brush = AccentBrush(e.Bounds))
                    {
                        e.Graphics.FillRectangle(brush, e.Bounds);
                    }
                }
                else
                {
                    using (var brush = new SolidBrush(Surface))
                    {
                        e.Graphics.FillRectangle(brush, e.Bounds);
                    }
                }

                if (e.Index >= 0)
                {
                    var bounds = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height);
                    TextRenderer.DrawText(e.Graphics, box.GetItemText(box.Items[e.Index]), box.Font, bounds, selected ? Color.White : Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                }
            };
        }

        public static void DarkTips(ToolTip tip)
        {
            tip.OwnerDraw = true;
            tip.Draw += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var back = new SolidBrush(SurfaceHigh))
                using (var pen = new Pen(Line))
                {
                    e.Graphics.FillRectangle(back, e.Bounds);
                    e.Graphics.DrawRectangle(pen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
                }

                using (var font = UiFont())
                {
                    TextRenderer.DrawText(e.Graphics, e.ToolTipText, font, e.Bounds, Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            };
            tip.Popup += (s, e) =>
            {
                using (var font = UiFont())
                {
                    Size size = TextRenderer.MeasureText(tip.GetToolTip(e.AssociatedControl), font);
                    e.ToolTipSize = new Size(size.Width + 16, size.Height + 10);
                }
            };
        }

        public static void DarkMenu(ToolStrip menu)
        {
            menu.Renderer = new DarkMenuRenderer();
            menu.BackColor = Surface;
            menu.ForeColor = Text;
            menu.Font = UiFont();
        }

        sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer() : base(new DarkMenuColors())
            {
                RoundedEdges = false;
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Selected ? Color.White : Theme.Text;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected)
                {
                    base.OnRenderMenuItemBackground(e);
                    return;
                }

                var bounds = new RectangleF(3, 1, e.Item.Width - 6, e.Item.Height - 2);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = Round(bounds, 5))
                using (LinearGradientBrush brush = AccentBrush(bounds))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }
        }

        sealed class DarkMenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Surface; } }
            public override Color ImageMarginGradientBegin { get { return Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Surface; } }
            public override Color ImageMarginGradientEnd { get { return Surface; } }
            public override Color MenuBorder { get { return Line; } }
            public override Color MenuItemBorder { get { return Color.Transparent; } }
            public override Color MenuItemSelected { get { return SurfaceHigh; } }
            public override Color SeparatorDark { get { return Line; } }
            public override Color SeparatorLight { get { return Line; } }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        public static void DarkChrome(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                DwmSetWindowAttribute(hwnd, 20, ref on, 4);
                int round = 2;
                DwmSetWindowAttribute(hwnd, 33, ref round, 4);
                int caption = ColorRef(Page);
                DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
                int text = ColorRef(Text);
                DwmSetWindowAttribute(hwnd, 36, ref text, 4);
                int border = ColorRef(Line);
                DwmSetWindowAttribute(hwnd, 34, ref border, 4);
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        public static void RoundCorners(IntPtr hwnd)
        {
            try
            {
                int round = 2;
                DwmSetWindowAttribute(hwnd, 33, ref round, 4);
                int border = ColorRef(Line);
                DwmSetWindowAttribute(hwnd, 34, ref border, 4);
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        public static void DarkScrollbars(IntPtr hwnd)
        {
            try
            {
                SetWindowTheme(hwnd, "DarkMode_Explorer", null);
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        static int ColorRef(Color color)
        {
            return color.R | (color.G << 8) | (color.B << 16);
        }
    }

    sealed class GlowButton : Button
    {
        readonly bool _primary;
        bool _hover;
        bool _down;

        public GlowButton(bool primary)
        {
            _primary = primary;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Theme.Surface;
            ForeColor = primary ? Color.White : Theme.Text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _down = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _down = e.Button == MouseButtons.Left;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _down = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color parentBack = Parent != null ? Parent.BackColor : Theme.Page;
            g.Clear(parentBack);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath path = Theme.Round(bounds, 8))
            {
                if (_primary)
                {
                    int alpha = !Enabled ? 90 : (_down ? 200 : 255);
                    using (LinearGradientBrush brush = Theme.AccentBrush(bounds, alpha))
                    {
                        g.FillPath(brush, path);
                    }

                    if (_hover && Enabled)
                    {
                        using (var shine = new SolidBrush(Color.FromArgb(36, 255, 255, 255)))
                        {
                            g.FillPath(shine, path);
                        }
                    }
                }
                else
                {
                    Color fill = !Enabled ? Theme.Surface : (_down ? Theme.Line : (_hover ? Theme.SurfaceHigh : Theme.Surface));
                    using (var brush = new SolidBrush(fill))
                    {
                        g.FillPath(brush, path);
                    }

                    if (_hover && Enabled)
                    {
                        using (LinearGradientBrush ring = Theme.AccentBrush(bounds))
                        using (var pen = new Pen(ring, 1.2f))
                        {
                            g.DrawPath(pen, path);
                        }
                    }
                    else
                    {
                        using (var pen = new Pen(Theme.Line, 1f))
                        {
                            g.DrawPath(pen, path);
                        }
                    }
                }
            }

            Color text = !Enabled ? Theme.SubText : (_primary ? Color.White : Theme.Text);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
