using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Jietu
{
    static class SelfTest
    {
        public static int Run()
        {
            int failed = 0;
            failed += RunCase("vertical chrome", TestVerticalChrome);
            failed += RunCase("vertical duplicate tail", TestDuplicateTail);
            failed += RunCase("vertical no chrome", TestNoChrome);
            failed += RunCase("horizontal chrome", TestHorizontalChrome);
            failed += RunCase("repeating table rows", TestRepeatingRows);
            failed += RunCase("small shift stays exact", TestSmallShift);
            failed += RunCase("settings remembered", TestSettingsRemembered);
            failed += RunCase("color text", TestColorText);
            if (failed == 0)
            {
                Console.WriteLine("SELFTEST OK");
                return 0;
            }

            Console.WriteLine("SELFTEST FAIL " + failed);
            return 1;
        }

        static int RunCase(string name, Func<string> test)
        {
            try
            {
                string error = test();
                if (error == null)
                {
                    Console.WriteLine("OK  " + name);
                    return 0;
                }

                Console.WriteLine("FAIL " + name + " " + error);
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL " + name + " " + ex.GetType().Name + " " + ex.Message);
                return 1;
            }
        }

        static string TestVerticalChrome()
        {
            using (Bitmap doc = MakeDocument(80, 300, true))
            using (Bitmap header = MakeBar(80, 40, true))
            using (Bitmap footer = MakeBar(80, 24, false))
            {
                var frames = new List<Bitmap>();
                int[] positions = { 0, 50, 100, 150, 200, 210, 210 };
                try
                {
                    for (int i = 0; i < positions.Length; i++)
                    {
                        frames.Add(View(doc, header, footer, positions[i], 90, true));
                    }

                    int chromeStart;
                    int chromeEnd;
                    ImageStitcher.DetectChrome(frames[0], frames[1], true, 50, out chromeStart, out chromeEnd);
                    if (chromeStart != 40 || chromeEnd != 24)
                    {
                        return "chrome " + chromeStart + "," + chromeEnd;
                    }

                    ImageStitcher.ShiftMeasure measure = ImageStitcher.Measure(frames[0], frames[1], true);
                    if (!measure.Moved || measure.Offset != 50)
                    {
                        return "offset " + measure.Offset + " score " + measure.Score.ToString("0.0") + " zero " + measure.ZeroScore.ToString("0.0");
                    }

                    ImageStitcher.StitchOutput output = ImageStitcher.Stitch(frames, true);
                    using (output.Image)
                    {
                        if (output.Image == null)
                        {
                            return output.Note;
                        }

                        if (output.Image.Width != 80 || output.Image.Height != 364)
                        {
                            return "size " + output.Image.Width + "x" + output.Image.Height + " " + output.Note;
                        }

                        string pixel = ExpectRow(output.Image, header, 0, 0);
                        if (pixel != null) return "header " + pixel;
                        pixel = ExpectRow(output.Image, doc, 40, 0);
                        if (pixel != null) return "doc0 " + pixel;
                        pixel = ExpectRow(output.Image, doc, 140, 100);
                        if (pixel != null) return "doc100 " + pixel;
                        pixel = ExpectRow(output.Image, doc, 339, 299);
                        if (pixel != null) return "doc299 " + pixel;
                        pixel = ExpectRow(output.Image, footer, 363, 23);
                        if (pixel != null) return "footer " + pixel;
                    }
                }
                finally
                {
                    DisposeAll(frames);
                }
            }

            return null;
        }

        static string TestDuplicateTail()
        {
            using (Bitmap doc = MakeDocument(60, 220, true))
            using (Bitmap header = MakeBar(60, 16, true))
            using (Bitmap footer = MakeBar(60, 12, false))
            {
                var frames = new List<Bitmap>();
                try
                {
                    frames.Add(View(doc, header, footer, 0, 80, true));
                    frames.Add(View(doc, header, footer, 50, 80, true));
                    frames.Add(View(doc, header, footer, 50, 80, true));
                    ImageStitcher.StitchOutput output = ImageStitcher.Stitch(frames, true);
                    using (output.Image)
                    {
                        int expected = 16 + 130 + 12;
                        if (output.Image == null || output.Image.Height != expected)
                        {
                            return "height " + (output.Image == null ? 0 : output.Image.Height) + " want " + expected;
                        }
                    }
                }
                finally
                {
                    DisposeAll(frames);
                }
            }

            return null;
        }

        static string TestNoChrome()
        {
            using (Bitmap doc = MakeDocument(50, 180, true))
            {
                var frames = new List<Bitmap>();
                int[] positions = { 0, 40, 80, 110 };
                try
                {
                    for (int i = 0; i < positions.Length; i++)
                    {
                        frames.Add(Slice(doc, positions[i], 70, true));
                    }

                    ImageStitcher.StitchOutput output = ImageStitcher.Stitch(frames, true);
                    using (output.Image)
                    {
                        if (output.Image == null || output.Image.Height != 180 || output.Image.Width != 50)
                        {
                            return "size " + (output.Image == null ? "null" : output.Image.Width + "x" + output.Image.Height);
                        }

                        string pixel = ExpectRow(output.Image, doc, 60, 60);
                        if (pixel != null) return pixel;
                        pixel = ExpectRow(output.Image, doc, 179, 179);
                        if (pixel != null) return pixel;
                    }
                }
                finally
                {
                    DisposeAll(frames);
                }
            }

            return null;
        }

        static string TestHorizontalChrome()
        {
            using (Bitmap doc = MakeDocument(300, 70, false))
            using (Bitmap header = MakeBar(36, 70, true))
            using (Bitmap footer = MakeBar(18, 70, false))
            {
                var frames = new List<Bitmap>();
                int[] positions = { 0, 60, 120, 180, 200 };
                try
                {
                    for (int i = 0; i < positions.Length; i++)
                    {
                        frames.Add(View(doc, header, footer, positions[i], 100, false));
                    }

                    int chromeStart;
                    int chromeEnd;
                    ImageStitcher.DetectChrome(frames[0], frames[1], false, 60, out chromeStart, out chromeEnd);
                    if (chromeStart != 36 || chromeEnd != 18)
                    {
                        return "chrome " + chromeStart + "," + chromeEnd;
                    }

                    ImageStitcher.StitchOutput output = ImageStitcher.Stitch(frames, false);
                    using (output.Image)
                    {
                        if (output.Image == null || output.Image.Width != 354 || output.Image.Height != 70)
                        {
                            return "size " + (output.Image == null ? "null" : output.Image.Width + "x" + output.Image.Height) + " " + output.Note;
                        }

                        string pixel = ExpectCol(output.Image, header, 0, 0);
                        if (pixel != null) return "left " + pixel;
                        pixel = ExpectCol(output.Image, doc, 36, 0);
                        if (pixel != null) return "doc0 " + pixel;
                        pixel = ExpectCol(output.Image, doc, 136, 100);
                        if (pixel != null) return "doc100 " + pixel;
                        pixel = ExpectCol(output.Image, doc, 335, 299);
                        if (pixel != null) return "doc299 " + pixel;
                        pixel = ExpectCol(output.Image, footer, 353, 17);
                        if (pixel != null) return "right " + pixel;
                    }
                }
                finally
                {
                    DisposeAll(frames);
                }
            }

            return null;
        }

        static string TestRepeatingRows()
        {
            const int rowHeight = 34;
            const int rowCount = 18;
            const int width = 220;
            using (Bitmap doc = new Bitmap(width, rowCount * rowHeight, PixelFormat.Format24bppRgb))
            using (Bitmap header = MakeBar(width, 30, true))
            using (Bitmap footer = MakeBar(width, 16, false))
            {
                for (int row = 0; row < rowCount; row++)
                {
                    for (int y = 0; y < rowHeight; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            Color color;
                            if (x < 22)
                            {
                                int bit = ((row * 17 + x * 3 + y) * 13) & 255;
                                color = Color.FromArgb(bit, (row * 29) & 255, (x * 9 + row) & 255);
                            }
                            else if (y == 0 || y == rowHeight - 1)
                            {
                                color = Color.FromArgb(226, 228, 232);
                            }
                            else
                            {
                                color = Color.FromArgb(255, 255, 255);
                            }

                            doc.SetPixel(x, row * rowHeight + y, color);
                        }
                    }
                }

                var frames = new List<Bitmap>();
                int[] positions = { 0, 102, 204, 306, 408, 442 };
                try
                {
                    for (int i = 0; i < positions.Length; i++)
                    {
                        frames.Add(View(doc, header, footer, positions[i], rowHeight * 5, true));
                    }

                    ImageStitcher.ShiftMeasure measure = ImageStitcher.Measure(frames[0], frames[1], true);
                    if (!measure.Moved || measure.Offset != 102)
                    {
                        return "offset " + measure.Offset + " score " + measure.Score.ToString("0.0");
                    }

                    ImageStitcher.StitchOutput output = ImageStitcher.Stitch(frames, true);
                    using (output.Image)
                    {
                        int expected = 30 + rowCount * rowHeight + 16;
                        if (output.Image == null || output.Image.Height != expected)
                        {
                            return "height " + (output.Image == null ? 0 : output.Image.Height) + " want " + expected + " " + output.Note;
                        }

                        string pixel = ExpectRow(output.Image, doc, 30 + 400, 400);
                        if (pixel != null)
                        {
                            return pixel;
                        }
                    }
                }
                finally
                {
                    DisposeAll(frames);
                }
            }

            return null;
        }

        static string TestColorText()
        {
            Color white = Color.FromArgb(255, 255, 255);
            if (ColorText.Rgb(white) != "rgb(255, 255, 255)")
            {
                return ColorText.Rgb(white);
            }

            if (ColorText.Hex(white) != "#ffffff")
            {
                return ColorText.Hex(white);
            }

            Color mixed = Color.FromArgb(0, 15, 160);
            if (ColorText.Rgb(mixed) != "rgb(0, 15, 160)")
            {
                return ColorText.Rgb(mixed);
            }

            if (ColorText.Hex(mixed) != "#000fa0")
            {
                return ColorText.Hex(mixed);
            }

            return null;
        }

        static string TestSettingsRemembered()
        {
            ScrollPace pace = AppSettings.Pace;
            Keys gesture = AppSettings.Gesture;
            Keys gestureRgb = AppSettings.GestureRgb;
            Keys gestureWeb = AppSettings.GestureWeb;
            string saveFolder = AppSettings.SaveFolder;
            string path = Path.Combine(Path.GetTempPath(), "jietu-settings-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                if (AppSettings.IsValidGesture(Keys.A))
                {
                    return "bare key accepted";
                }

                if (!AppSettings.IsValidGesture(Keys.Control | Keys.Alt | Keys.A))
                {
                    return "default rejected";
                }

                if (AppSettings.FormatGesture(Keys.Control | Keys.Alt | Keys.A) != "Ctrl + Alt + A")
                {
                    return AppSettings.FormatGesture(Keys.Control | Keys.Alt | Keys.A);
                }

                uint mods = AppSettings.NativeModifiers(Keys.Control | Keys.Shift | Keys.F8);
                if ((mods & NativeMethods.MOD_CONTROL) == 0 || (mods & NativeMethods.MOD_SHIFT) == 0 || (mods & NativeMethods.MOD_ALT) != 0)
                {
                    return "modifiers";
                }

                AppSettings.Pace = ScrollPace.Slow;
                AppSettings.Gesture = Keys.Control | Keys.Shift | Keys.F8;
                AppSettings.GestureRgb = Keys.Control | Keys.Alt | Keys.R;
                AppSettings.GestureWeb = Keys.Control | Keys.Shift | Keys.D;
                AppSettings.SaveFolder = @"D:\截图 目录";
                AppSettings.Save(path);
                AppSettings.Pace = ScrollPace.Fast;
                AppSettings.Gesture = Keys.Control | Keys.Alt | Keys.A;
                AppSettings.GestureRgb = Keys.Control | Keys.Alt | Keys.C;
                AppSettings.GestureWeb = Keys.Control | Keys.Alt | Keys.X;
                AppSettings.SaveFolder = "";
                AppSettings.Load(path);
                if (AppSettings.Pace != ScrollPace.Slow)
                {
                    return "pace " + AppSettings.Pace;
                }

                if (AppSettings.Gesture != (Keys.Control | Keys.Shift | Keys.F8))
                {
                    return "gesture " + AppSettings.Gesture;
                }

                if (AppSettings.FormatGesture(AppSettings.Gesture) != "Ctrl + Shift + F8")
                {
                    return AppSettings.FormatGesture(AppSettings.Gesture);
                }

                if (AppSettings.GestureRgb != (Keys.Control | Keys.Alt | Keys.R))
                {
                    return "rgb " + AppSettings.GestureRgb;
                }

                if (AppSettings.GestureWeb != (Keys.Control | Keys.Shift | Keys.D))
                {
                    return "web " + AppSettings.GestureWeb;
                }

                if (AppSettings.SaveFolder != @"D:\截图 目录")
                {
                    return "folder " + AppSettings.SaveFolder;
                }
            }
            finally
            {
                AppSettings.Pace = pace;
                AppSettings.Gesture = gesture;
                AppSettings.GestureRgb = gestureRgb;
                AppSettings.GestureWeb = gestureWeb;
                AppSettings.SaveFolder = saveFolder;
                try
                {
                    File.Delete(path);
                }
                catch
                {
                }
            }

            return null;
        }

        static string TestSmallShift()
        {
            using (Bitmap doc = MakeDocument(90, 240, true))
            {
                var frames = new List<Bitmap>();
                try
                {
                    frames.Add(Slice(doc, 0, 100, true));
                    frames.Add(Slice(doc, 20, 100, true));
                    ImageStitcher.ShiftMeasure measure = ImageStitcher.Measure(frames[0], frames[1], true);
                    if (!measure.Moved || measure.Offset != 20)
                    {
                        return "offset " + measure.Offset + " score " + measure.Score.ToString("0.0");
                    }
                }
                finally
                {
                    DisposeAll(frames);
                }
            }

            return null;
        }

        static Bitmap MakeDocument(int width, int height, bool verticalRows)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int n = verticalRows ? y : x;
                    int r = (n * 17 + (verticalRows ? x : y) * 3) & 255;
                    int g = (n * 13 + 40) & 255;
                    int b = ((n / 3) + (verticalRows ? x : y) * 9) & 255;
                    bitmap.SetPixel(x, y, Color.FromArgb(r, g, b));
                }
            }

            return bitmap;
        }

        static Bitmap MakeBar(int width, int height, bool header)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (header)
                    {
                        bitmap.SetPixel(x, y, Color.FromArgb(210, 24 + (x % 5), 36 + (y % 4)));
                    }
                    else
                    {
                        bitmap.SetPixel(x, y, Color.FromArgb(20 + (y % 6), 70, 190 - (x % 5)));
                    }
                }
            }

            return bitmap;
        }

        static Bitmap View(Bitmap doc, Bitmap startChrome, Bitmap endChrome, int scroll, int viewContent, bool vertical)
        {
            if (vertical)
            {
                int width = doc.Width;
                int height = startChrome.Height + viewContent + endChrome.Height;
                var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                CopyRows(startChrome, 0, bitmap, 0, startChrome.Height);
                CopyRows(doc, scroll, bitmap, startChrome.Height, viewContent);
                CopyRows(endChrome, 0, bitmap, startChrome.Height + viewContent, endChrome.Height);
                return bitmap;
            }

            int w = startChrome.Width + viewContent + endChrome.Width;
            int h = doc.Height;
            var wide = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            CopyCols(startChrome, 0, wide, 0, startChrome.Width);
            CopyCols(doc, scroll, wide, startChrome.Width, viewContent);
            CopyCols(endChrome, 0, wide, startChrome.Width + viewContent, endChrome.Width);
            return wide;
        }

        static Bitmap Slice(Bitmap doc, int scroll, int viewContent, bool vertical)
        {
            if (vertical)
            {
                var bitmap = new Bitmap(doc.Width, viewContent, PixelFormat.Format24bppRgb);
                CopyRows(doc, scroll, bitmap, 0, viewContent);
                return bitmap;
            }

            var wide = new Bitmap(viewContent, doc.Height, PixelFormat.Format24bppRgb);
            CopyCols(doc, scroll, wide, 0, viewContent);
            return wide;
        }

        static void CopyRows(Bitmap source, int sourceY, Bitmap dest, int destY, int rows)
        {
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    dest.SetPixel(x, destY + y, source.GetPixel(x, sourceY + y));
                }
            }
        }

        static void CopyCols(Bitmap source, int sourceX, Bitmap dest, int destX, int cols)
        {
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    dest.SetPixel(destX + x, y, source.GetPixel(sourceX + x, y));
                }
            }
        }

        static string ExpectRow(Bitmap image, Bitmap expected, int imageY, int expectedY)
        {
            Color a = image.GetPixel(5, imageY);
            Color b = expected.GetPixel(5, expectedY);
            if (a.ToArgb() != b.ToArgb())
            {
                return "y" + imageY + " got " + a.R + "," + a.G + "," + a.B + " want " + b.R + "," + b.G + "," + b.B;
            }

            return null;
        }

        static string ExpectCol(Bitmap image, Bitmap expected, int imageX, int expectedX)
        {
            Color a = image.GetPixel(imageX, 8);
            Color b = expected.GetPixel(expectedX, 8);
            if (a.ToArgb() != b.ToArgb())
            {
                return "x" + imageX + " got " + a.R + "," + a.G + "," + a.B + " want " + b.R + "," + b.G + "," + b.B;
            }

            return null;
        }

        static void DisposeAll(List<Bitmap> frames)
        {
            for (int i = 0; i < frames.Count; i++)
            {
                frames[i].Dispose();
            }
        }
    }
}
