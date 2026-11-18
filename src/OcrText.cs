using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

#if NET
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
#endif

namespace Jietu
{
    static class OcrText
    {
        public static string Recognize(Bitmap bitmap)
        {
            if (bitmap == null)
            {
                throw new ArgumentNullException("bitmap");
            }

#if NET
            Bitmap fitted = Fit(bitmap);
            Bitmap source = fitted ?? bitmap;
            try
            {
                byte[] pixels = PackBgra(source);
                Task<string> task = Task.Run(() => RecognizeCore(pixels, source.Width, source.Height));
                if (!task.Wait(TimeSpan.FromSeconds(25)))
                {
                    throw new TimeoutException("文字识别超时");
                }

                return task.GetAwaiter().GetResult() ?? "";
            }
            finally
            {
                if (fitted != null)
                {
                    fitted.Dispose();
                }
            }
#else
            throw new NotSupportedException("文字识别需要使用安装包里的程序。");
#endif
        }

#if NET
        static async Task<string> RecognizeCore(byte[] pixels, int width, int height)
        {
            OcrEngine engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null)
            {
                engine = OcrEngine.TryCreateFromLanguage(new Language("zh-Hans"));
            }

            if (engine == null)
            {
                engine = OcrEngine.TryCreateFromLanguage(new Language("zh-CN"));
            }

            if (engine == null)
            {
                engine = OcrEngine.TryCreateFromLanguage(new Language("en-US"));
            }

            if (engine == null)
            {
                throw new InvalidOperationException("系统没有可用的文字识别语言。请到 Windows 设置 → 时间和语言 → 语言，为中文或英文添加“光学字符识别”。");
            }

            using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
            {
                BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    (uint)width,
                    (uint)height,
                    96,
                    96,
                    pixels);
                await encoder.FlushAsync();
                stream.Seek(0);
                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
                using (SoftwareBitmap soft = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied))
                {
                    OcrResult result = await engine.RecognizeAsync(soft);
                    if (result == null)
                    {
                        return "";
                    }

                    if (result.Lines == null || result.Lines.Count == 0)
                    {
                        return result.Text ?? "";
                    }

                    var text = new StringBuilder();
                    foreach (OcrLine line in result.Lines)
                    {
                        if (line == null || string.IsNullOrEmpty(line.Text))
                        {
                            continue;
                        }

                        if (text.Length > 0)
                        {
                            text.Append("\r\n");
                        }

                        text.Append(line.Text);
                    }

                    return text.Length > 0 ? text.ToString() : (result.Text ?? "");
                }
            }
        }

        static Bitmap Fit(Bitmap source)
        {
            const int maxSide = 2400;
            if (source.Width <= maxSide && source.Height <= maxSide)
            {
                return null;
            }

            double scale = maxSide / (double)Math.Max(source.Width, source.Height);
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            bitmap.SetResolution(96, 96);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, width, height));
            }

            return bitmap;
        }

        static byte[] PackBgra(Bitmap source)
        {
            var rect = new Rectangle(0, 0, source.Width, source.Height);
            BitmapData data = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = source.Width * 4;
                var pixels = new byte[rowBytes * source.Height];
                if (data.Stride == rowBytes)
                {
                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                    return pixels;
                }

                var row = new byte[Math.Max(data.Stride, rowBytes)];
                for (int y = 0; y < source.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, rowBytes);
                    System.Buffer.BlockCopy(row, 0, pixels, y * rowBytes, rowBytes);
                }

                return pixels;
            }
            finally
            {
                source.UnlockBits(data);
            }
        }
#endif
    }
}
