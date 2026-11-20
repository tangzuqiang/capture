using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Shiying
{
    static class ScreenGrab
    {
        public static Bitmap Capture(Rectangle rect)
        {
            if (rect.Width < 1 || rect.Height < 1)
            {
                throw new ArgumentException("截图区域无效");
            }

            var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format24bppRgb);
            bitmap.SetResolution(96, 96);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(rect.X, rect.Y, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
            }

            return bitmap;
        }

        public static Bitmap Darken(Bitmap source, int alpha)
        {
            var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
            bitmap.SetResolution(96, 96);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);
                using (var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
                {
                    graphics.FillRectangle(brush, 0, 0, bitmap.Width, bitmap.Height);
                }
            }

            return bitmap;
        }

        public static Bitmap Crop(Bitmap source, Rectangle rect)
        {
            rect = Rectangle.Intersect(rect, new Rectangle(0, 0, source.Width, source.Height));
            if (rect.Width < 1 || rect.Height < 1)
            {
                throw new ArgumentException("裁剪区域无效");
            }

            return source.Clone(rect, PixelFormat.Format24bppRgb);
        }

        public static void CopyToClipboard(Image image)
        {
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    Clipboard.SetDataObject(image, true);
                    return;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(40);
                }
            }

            throw new InvalidOperationException("剪贴板正被其他程序占用，请再试一次。");
        }

        public static void CopyText(string text)
        {
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(40);
                }
            }

            throw new InvalidOperationException("剪贴板正被其他程序占用，请再试一次。");
        }

        public static void Save(Bitmap bitmap, string path)
        {
            string ext = System.IO.Path.GetExtension(path);
            if (ext != null && (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)))
            {
                ImageCodecInfo jpeg = null;
                ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
                for (int i = 0; i < codecs.Length; i++)
                {
                    if (codecs[i].MimeType == "image/jpeg")
                    {
                        jpeg = codecs[i];
                        break;
                    }
                }

                if (jpeg != null)
                {
                    using (var parameters = new EncoderParameters(1))
                    {
                        parameters.Param[0] = new EncoderParameter(Encoder.Quality, 92L);
                        bitmap.Save(path, jpeg, parameters);
                    }

                    return;
                }
            }

            bitmap.Save(path, ImageFormat.Png);
        }
    }
}
