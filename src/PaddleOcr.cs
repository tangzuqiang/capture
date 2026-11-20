#if NET
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.ML.OnnxRuntime;

namespace Jietu
{
    // PP-OCRv5 mobile (RapidOCR ONNX export): DB text detection + CTC line recognition.
    static class PaddleOcr
    {
        const float BinaryThreshold = 0.3f;
        const float BoxThreshold = 0.5f;
        const float UnclipRatio = 1.6f;
        const int RecHeight = 48;
        const int RecMinWidth = 320;
        const int RecMaxWidth = 3200;
        const int RecBatch = 6;

        static readonly object Gate = new object();
        static InferenceSession _det;
        static InferenceSession _rec;
        static string[] _keys;
        static string _loadError;

        public static string ModelFolder
        {
            get { return Path.Combine(AppContext.BaseDirectory, "models", "ocr"); }
        }

        public static bool ModelsPresent
        {
            get
            {
                string dir = ModelFolder;
                return File.Exists(Path.Combine(dir, "det.onnx"))
                    && File.Exists(Path.Combine(dir, "rec.onnx"))
                    && File.Exists(Path.Combine(dir, "keys.txt"));
            }
        }

        public static string Recognize(Bitmap bitmap)
        {
            EnsureLoaded();
            using (Bitmap source = ToBgr24(bitmap))
            {
                List<Rectangle> boxes = Detect(source);
                if (boxes.Count == 0)
                {
                    return "";
                }

                string[] texts = RecognizeBoxes(source, boxes);
                return Layout(boxes, texts);
            }
        }

        static void EnsureLoaded()
        {
            lock (Gate)
            {
                if (_det != null)
                {
                    return;
                }

                if (_loadError != null)
                {
                    throw new InvalidOperationException(_loadError);
                }

                try
                {
                    string dir = ModelFolder;
                    var options = new SessionOptions();
                    options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                    options.IntraOpNumThreads = Math.Max(1, Math.Min(8, Environment.ProcessorCount / 2));
                    _det = new InferenceSession(Path.Combine(dir, "det.onnx"), options);
                    _rec = new InferenceSession(Path.Combine(dir, "rec.onnx"), options);
                    _keys = File.ReadAllLines(Path.Combine(dir, "keys.txt"), Encoding.UTF8)
                        .Select(line => line.TrimEnd('\r', '\n'))
                        .ToArray();
                }
                catch (Exception ex)
                {
                    _det = null;
                    _rec = null;
                    _loadError = "PaddleOCR 模型加载失败：" + ex.Message;
                    throw new InvalidOperationException(_loadError, ex);
                }
            }
        }

        static List<Rectangle> Detect(Bitmap source)
        {
            int width = source.Width;
            int height = source.Height;
            double ratio = 1.0;
            int longSide = Math.Max(width, height);
            if (longSide < 960)
            {
                ratio = Math.Min(2.0, 960.0 / longSide);
            }

            if (longSide * ratio > 2240)
            {
                ratio = 2240.0 / longSide;
            }

            int dw = Math.Max(32, (int)Math.Round(width * ratio / 32.0) * 32);
            int dh = Math.Max(32, (int)Math.Round(height * ratio / 32.0) * 32);
            float[] input = new float[3 * dw * dh];
            using (Bitmap resized = Resize(source, dw, dh))
            {
                FillNormalized(resized, input, dw, dh, 0, dw,
                    new float[] { 0.485f, 0.456f, 0.406f },
                    new float[] { 0.229f, 0.224f, 0.225f });
            }

            float[] prob;
            int ph;
            int pw;
            using (var tensor = OrtValue.CreateTensorValueFromMemory(input, new long[] { 1, 3, dh, dw }))
            using (var outputs = _det.Run(new RunOptions(), new[] { _det.InputMetadata.Keys.First() }, new[] { tensor }, _det.OutputMetadata.Keys.ToArray()))
            {
                OrtValue output = outputs[0];
                long[] shape = output.GetTensorTypeAndShape().Shape;
                ph = (int)shape[shape.Length - 2];
                pw = (int)shape[shape.Length - 1];
                prob = output.GetTensorDataAsSpan<float>().ToArray();
            }

            double sx = width / (double)pw;
            double sy = height / (double)ph;
            var boxes = new List<Rectangle>();
            var seen = new bool[pw * ph];
            var stack = new Stack<int>();
            for (int start = 0; start < prob.Length; start++)
            {
                if (seen[start] || prob[start] <= BinaryThreshold)
                {
                    continue;
                }

                int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
                seen[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int index = stack.Pop();
                    int x = index % pw;
                    int y = index / pw;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                    for (int ny = Math.Max(0, y - 1); ny <= Math.Min(ph - 1, y + 1); ny++)
                    {
                        for (int nx = Math.Max(0, x - 1); nx <= Math.Min(pw - 1, x + 1); nx++)
                        {
                            int next = ny * pw + nx;
                            if (!seen[next] && prob[next] > BinaryThreshold)
                            {
                                seen[next] = true;
                                stack.Push(next);
                            }
                        }
                    }
                }

                int bw = maxX - minX + 1;
                int bh = maxY - minY + 1;
                if (Math.Min(bw, bh) < 3)
                {
                    continue;
                }

                double sum = 0;
                for (int y = minY; y <= maxY; y++)
                {
                    int row = y * pw;
                    for (int x = minX; x <= maxX; x++)
                    {
                        sum += prob[row + x];
                    }
                }

                if (sum / (bw * bh) < BoxThreshold)
                {
                    continue;
                }

                double grow = bw * bh * UnclipRatio / (2.0 * (bw + bh));
                double left = (minX - grow) * sx;
                double top = (minY - grow) * sy;
                double right = (maxX + 1 + grow) * sx;
                double bottom = (maxY + 1 + grow) * sy;
                int l = Math.Max(0, (int)Math.Floor(left));
                int t = Math.Max(0, (int)Math.Floor(top));
                int r = Math.Min(width, (int)Math.Ceiling(right));
                int b = Math.Min(height, (int)Math.Ceiling(bottom));
                if (r - l >= 4 && b - t >= 4)
                {
                    boxes.Add(Rectangle.FromLTRB(l, t, r, b));
                }
            }

            return boxes;
        }

        static string[] RecognizeBoxes(Bitmap source, List<Rectangle> boxes)
        {
            var crops = new Bitmap[boxes.Count];
            var texts = new string[boxes.Count];
            try
            {
                for (int i = 0; i < boxes.Count; i++)
                {
                    Bitmap crop = source.Clone(boxes[i], PixelFormat.Format24bppRgb);
                    if (crop.Height >= crop.Width * 1.5)
                    {
                        crop.RotateFlip(RotateFlipType.Rotate270FlipNone);
                    }

                    crops[i] = crop;
                }

                int[] order = Enumerable.Range(0, boxes.Count)
                    .OrderBy(i => crops[i].Width / (double)crops[i].Height)
                    .ToArray();
                for (int start = 0; start < order.Length; start += RecBatch)
                {
                    int count = Math.Min(RecBatch, order.Length - start);
                    double widest = 0;
                    for (int k = 0; k < count; k++)
                    {
                        Bitmap crop = crops[order[start + k]];
                        widest = Math.Max(widest, crop.Width / (double)crop.Height);
                    }

                    int batchWidth = Math.Min(RecMaxWidth, Math.Max(RecMinWidth, (int)Math.Ceiling(RecHeight * widest)));
                    int plane = RecHeight * batchWidth;
                    float[] input = new float[count * 3 * plane];
                    for (int k = 0; k < count; k++)
                    {
                        Bitmap crop = crops[order[start + k]];
                        int w = Math.Min(batchWidth, Math.Max(1, (int)Math.Ceiling(RecHeight * crop.Width / (double)crop.Height)));
                        using (Bitmap line = Resize(crop, w, RecHeight))
                        {
                            FillNormalized(line, input, batchWidth, RecHeight, k * 3 * plane, w,
                                new float[] { 0.5f, 0.5f, 0.5f },
                                new float[] { 0.5f, 0.5f, 0.5f });
                        }
                    }

                    using (var tensor = OrtValue.CreateTensorValueFromMemory(input, new long[] { count, 3, RecHeight, batchWidth }))
                    using (var outputs = _rec.Run(new RunOptions(), new[] { _rec.InputMetadata.Keys.First() }, new[] { tensor }, _rec.OutputMetadata.Keys.ToArray()))
                    {
                        OrtValue output = outputs[0];
                        long[] shape = output.GetTensorTypeAndShape().Shape;
                        int steps = (int)shape[1];
                        int classes = (int)shape[2];
                        ReadOnlySpan<float> data = output.GetTensorDataAsSpan<float>();
                        for (int k = 0; k < count; k++)
                        {
                            texts[order[start + k]] = Decode(data.Slice(k * steps * classes, steps * classes), steps, classes);
                        }
                    }
                }
            }
            finally
            {
                for (int i = 0; i < crops.Length; i++)
                {
                    if (crops[i] != null)
                    {
                        crops[i].Dispose();
                    }
                }
            }

            return texts;
        }

        static string Decode(ReadOnlySpan<float> data, int steps, int classes)
        {
            var text = new StringBuilder();
            int last = 0;
            for (int t = 0; t < steps; t++)
            {
                int best = 0;
                float bestValue = float.MinValue;
                int row = t * classes;
                for (int c = 0; c < classes; c++)
                {
                    float value = data[row + c];
                    if (value > bestValue)
                    {
                        bestValue = value;
                        best = c;
                    }
                }

                if (best != 0 && best != last)
                {
                    int key = best - 1;
                    if (key < _keys.Length)
                    {
                        text.Append(_keys[key]);
                    }
                    else
                    {
                        text.Append(' ');
                    }
                }

                last = best;
            }

            return text.ToString().Trim();
        }

        static string Layout(List<Rectangle> boxes, string[] texts)
        {
            var items = new List<int>();
            for (int i = 0; i < boxes.Count; i++)
            {
                if (!string.IsNullOrEmpty(texts[i]))
                {
                    items.Add(i);
                }
            }

            items.Sort((a, b) => (boxes[a].Top + boxes[a].Bottom).CompareTo(boxes[b].Top + boxes[b].Bottom));
            var lines = new List<List<int>>();
            foreach (int index in items)
            {
                Rectangle box = boxes[index];
                List<int> target = null;
                if (lines.Count > 0)
                {
                    List<int> line = lines[lines.Count - 1];
                    Rectangle anchor = boxes[line[0]];
                    int overlap = Math.Min(anchor.Bottom, box.Bottom) - Math.Max(anchor.Top, box.Top);
                    if (overlap > Math.Min(anchor.Height, box.Height) / 2)
                    {
                        target = line;
                    }
                }

                if (target == null)
                {
                    target = new List<int>();
                    lines.Add(target);
                }

                target.Add(index);
            }

            var result = new StringBuilder();
            foreach (List<int> line in lines)
            {
                line.Sort((a, b) => boxes[a].Left.CompareTo(boxes[b].Left));
                if (result.Length > 0)
                {
                    result.Append("\r\n");
                }

                for (int k = 0; k < line.Count; k++)
                {
                    if (k > 0)
                    {
                        result.Append(' ');
                    }

                    result.Append(texts[line[k]]);
                }
            }

            return result.ToString();
        }

        static Bitmap ToBgr24(Bitmap source)
        {
            var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
            bitmap.SetResolution(96, 96);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
            }

            return bitmap;
        }

        static Bitmap Resize(Bitmap source, int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            bitmap.SetResolution(96, 96);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var attributes = new ImageAttributes())
            {
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }

            return bitmap;
        }

        // Writes BGR planes (CHW). Columns at or beyond usedWidth stay 0, which is the padding value Paddle uses.
        static void FillNormalized(Bitmap bitmap, float[] target, int planeWidth, int planeHeight, int offset, int usedWidth, float[] mean, float[] std)
        {
            int plane = planeWidth * planeHeight;
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[data.Stride];
                int width = Math.Min(usedWidth, bitmap.Width);
                for (int y = 0; y < Math.Min(planeHeight, bitmap.Height); y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, data.Stride);
                    int baseIndex = offset + y * planeWidth;
                    for (int x = 0; x < width; x++)
                    {
                        int p = x * 3;
                        target[baseIndex + x] = (row[p] / 255f - mean[0]) / std[0];
                        target[baseIndex + plane + x] = (row[p + 1] / 255f - mean[1]) / std[1];
                        target[baseIndex + 2 * plane + x] = (row[p + 2] / 255f - mean[2]) / std[2];
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
    }
}
#endif
