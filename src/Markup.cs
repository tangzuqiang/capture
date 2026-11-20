using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Shiying
{
    enum AnnotateTool
    {
        None,
        Rect,
        Ellipse,
        Arrow,
        Pen,
        Doodle,
        Text,
        Pin
    }

    enum MarkKind
    {
        Rect,
        Ellipse,
        Arrow,
        Pen,
        Doodle,
        Text,
        Pin
    }

    sealed class Mark
    {
        public MarkKind Kind;
        public Color Color;
        public int Thickness;
        public Point Start;
        public Point End;
        public string Text;
        public int FontPx;
        public int WrapWidth;
        public List<Point> Points;
    }

    static class Markup
    {
        public const int Thickness = 3;
        public const int FontPx = 22;
        public static readonly Color Red = Color.FromArgb(245, 74, 69);
        public static readonly Color[] Palette = new Color[]
        {
            Red,
            Color.FromArgb(255, 196, 0),
            Color.FromArgb(0, 122, 255),
            Color.FromArgb(52, 199, 89),
            Color.White,
            Color.FromArgb(142, 142, 147),
            Color.FromArgb(28, 28, 30)
        };

        public static Bitmap Compose(Bitmap source, Rectangle crop, IList<Mark> marks)
        {
            crop = Rectangle.Intersect(crop, new Rectangle(0, 0, source.Width, source.Height));
            if (crop.Width < 1 || crop.Height < 1)
            {
                throw new ArgumentException("裁剪区域无效");
            }

            var bitmap = new Bitmap(crop.Width, crop.Height, PixelFormat.Format24bppRgb);
            bitmap.SetResolution(96, 96);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(source, new Rectangle(0, 0, crop.Width, crop.Height), crop, GraphicsUnit.Pixel);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                if (marks != null)
                {
                    for (int i = 0; i < marks.Count; i++)
                    {
                        Draw(g, marks[i], 1f, -crop.X, -crop.Y);
                    }
                }
            }

            return bitmap;
        }

        public static void Draw(Graphics g, Mark mark, float scale, float originX, float originY)
        {
            if (g == null || mark == null || scale <= 0f)
            {
                return;
            }

            float x = (mark.Start.X + originX) * scale;
            float y = (mark.Start.Y + originY) * scale;
            float x2 = (mark.End.X + originX) * scale;
            float y2 = (mark.End.Y + originY) * scale;
            float thick = Math.Max(1f, mark.Thickness * scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (mark.Kind == MarkKind.Rect)
            {
                float left = Math.Min(x, x2);
                float top = Math.Min(y, y2);
                float width = Math.Abs(x2 - x);
                float height = Math.Abs(y2 - y);
                if (width < 1f || height < 1f)
                {
                    return;
                }

                using (var pen = new Pen(mark.Color, thick))
                {
                    pen.LineJoin = LineJoin.Miter;
                    pen.Alignment = PenAlignment.Inset;
                    g.DrawRectangle(pen, left, top, width, height);
                }

                return;
            }

            if (mark.Kind == MarkKind.Ellipse)
            {
                float left = Math.Min(x, x2);
                float top = Math.Min(y, y2);
                float width = Math.Abs(x2 - x);
                float height = Math.Abs(y2 - y);
                if (width < 1f || height < 1f)
                {
                    return;
                }

                using (var pen = new Pen(mark.Color, thick))
                {
                    g.DrawEllipse(pen, left, top, width, height);
                }

                return;
            }

            if (mark.Kind == MarkKind.Arrow)
            {
                DrawArrow(g, mark.Color, thick, scale, x, y, x2, y2);
                return;
            }

            if (mark.Kind == MarkKind.Pen || mark.Kind == MarkKind.Doodle)
            {
                DrawStroke(g, mark, scale, originX, originY, thick);
                return;
            }

            if (mark.Kind == MarkKind.Pin)
            {
                DrawPin(g, mark.Color, x, y, scale);
                return;
            }

            if (mark.Kind != MarkKind.Text || string.IsNullOrEmpty(mark.Text))
            {
                return;
            }

            float size = Math.Max(8f, mark.FontPx * scale);
            float wrap = mark.WrapWidth > 0 ? mark.WrapWidth * scale : 4000f;
            var layout = new RectangleF(x, y, Math.Max(8f, wrap), 4000f);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var font = new Font("Microsoft YaHei UI", size, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var format = new StringFormat())
            using (var brush = new SolidBrush(mark.Color))
            {
                format.Alignment = StringAlignment.Near;
                format.LineAlignment = StringAlignment.Near;
                format.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(mark.Text, font, brush, layout, format);
            }
        }

        public static Rectangle TextBounds(Mark mark)
        {
            if (mark == null || mark.Kind != MarkKind.Text || string.IsNullOrEmpty(mark.Text))
            {
                return Rectangle.Empty;
            }

            using (var font = new Font("Microsoft YaHei UI", Math.Max(8, mark.FontPx), FontStyle.Regular, GraphicsUnit.Pixel))
            {
                int wrap = mark.WrapWidth > 0 ? mark.WrapWidth : 4000;
                Size size = TextRenderer.MeasureText(
                    mark.Text,
                    font,
                    new Size(Math.Max(8, wrap), 4000),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
                return new Rectangle(mark.Start.X - 6, mark.Start.Y - 4, Math.Max(12, size.Width + 12), Math.Max(12, size.Height + 8));
            }
        }

        static void DrawArrow(Graphics g, Color color, float thick, float scale, float x, float y, float x2, float y2)
        {
            float dx = x2 - x;
            float dy = y2 - y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1f)
            {
                return;
            }

            float ux = dx / len;
            float uy = dy / len;
            float head = Math.Max(thick * 3.6f, 12f * scale);
            if (head > len * 0.48f)
            {
                head = Math.Max(len * 0.48f, thick * 2f);
            }

            float wing = head * 0.52f;
            float px = -uy;
            float py = ux;
            var tip = new PointF(x2, y2);
            var left = new PointF(x2 - ux * head + px * wing, y2 - uy * head + py * wing);
            var right = new PointF(x2 - ux * head - px * wing, y2 - uy * head - py * wing);
            using (var pen = new Pen(color, thick))
            using (var brush = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLine(pen, x, y, x2 - ux * head * 0.72f, y2 - uy * head * 0.72f);
                g.FillPolygon(brush, new PointF[] { tip, left, right });
            }
        }

        static void DrawStroke(Graphics g, Mark mark, float scale, float originX, float originY, float thick)
        {
            if (mark.Points == null || mark.Points.Count == 0)
            {
                return;
            }

            if (mark.Points.Count == 1)
            {
                float px = (mark.Points[0].X + originX) * scale;
                float py = (mark.Points[0].Y + originY) * scale;
                using (var brush = new SolidBrush(mark.Color))
                {
                    g.FillEllipse(brush, px - thick / 2f, py - thick / 2f, thick, thick);
                }

                return;
            }

            var points = new PointF[mark.Points.Count];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = new PointF((mark.Points[i].X + originX) * scale, (mark.Points[i].Y + originY) * scale);
            }

            using (var pen = new Pen(mark.Color, thick))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, points);
            }
        }

        static void DrawPin(Graphics g, Color color, float x, float y, float scale)
        {
            float head = 7f * scale;
            float cx = x;
            float cy = y - 12f * scale;
            using (var brush = new SolidBrush(color))
            {
                g.FillEllipse(brush, cx - head, cy - head, head * 2f, head * 2f);
                g.FillPolygon(brush, new PointF[]
                {
                    new PointF(x, y),
                    new PointF(cx - head * 0.62f, cy + head * 0.15f),
                    new PointF(cx + head * 0.62f, cy + head * 0.15f)
                });
            }

            Color hole = (color.R + color.G + color.B) > 520
                ? Color.FromArgb(color.A, 40, 40, 40)
                : Color.FromArgb(color.A, 255, 255, 255);
            float inner = head * 0.42f;
            using (var brush = new SolidBrush(hole))
            {
                g.FillEllipse(brush, cx - inner, cy - inner, inner * 2f, inner * 2f);
            }
        }
    }
}
