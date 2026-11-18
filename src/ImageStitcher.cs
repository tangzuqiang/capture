using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace Jietu
{
    /// <summary>
    /// 把同一视口连续截到的画面拼成一张长图。
    /// 向前滚动后，新画面的一条扫描线会和旧画面更靠后的同一条对齐。
    /// 固定表头、底栏在两次截图里停在原位，只保留一次。
    /// </summary>
    static class ImageStitcher
    {
        public sealed class ShiftMeasure
        {
            public int Offset;
            public double Score;
            public double ZeroScore;
            public int Votes;
            public bool Moved;
            public bool Reliable;
            public int[] Candidates;
        }

        public sealed class StitchOutput
        {
            public Bitmap Image;
            public int UsedFrames;
            public int StartChrome;
            public int EndChrome;
            public string Note;
        }

        sealed class Slice
        {
            public Bitmap Source;
            public int Start;
            public int Length;
        }

        sealed class LineMap
        {
            public readonly int Length;
            public readonly int Samples;
            public readonly byte[] Data;

            public LineMap(int length, int samples, byte[] data)
            {
                Length = length;
                Samples = samples;
                Data = data;
            }

            public static LineMap Build(Bitmap bitmap, bool vertical)
            {
                int length = vertical ? bitmap.Height : bitmap.Width;
                int cross = vertical ? bitmap.Width : bitmap.Height;
                int samples = cross >= 96 ? 72 : Math.Max(8, cross / 2);
                int margin = cross > 48 ? 1 : 0;
                int inner = cross - margin * 2;
                if (inner < 4)
                {
                    margin = 0;
                    inner = Math.Max(1, cross);
                }

                var data = new byte[length * samples * 3];
                BitmapData bits = bitmap.LockBits(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format24bppRgb);
                try
                {
                    unsafe
                    {
                        byte* scan = (byte*)bits.Scan0;
                        for (int pos = 0; pos < length; pos++)
                        {
                            for (int i = 0; i < samples; i++)
                            {
                                int c = margin;
                                if (samples > 1 && inner > 1)
                                {
                                    c = margin + (i * (inner - 1)) / (samples - 1);
                                }

                                byte* pixel = vertical
                                    ? scan + pos * bits.Stride + c * 3
                                    : scan + c * bits.Stride + pos * 3;
                                int offset = (pos * samples + i) * 3;
                                data[offset] = pixel[0];
                                data[offset + 1] = pixel[1];
                                data[offset + 2] = pixel[2];
                            }
                        }
                    }
                }
                finally
                {
                    bitmap.UnlockBits(bits);
                }

                return new LineMap(length, samples, data);
            }
        }

        public static ShiftMeasure Measure(Bitmap older, Bitmap newer, bool vertical)
        {
            int length = older == null ? 0 : (vertical ? older.Height : older.Width);
            return Measure(older, newer, vertical, 0, 0, length);
        }

        public static ShiftMeasure Measure(Bitmap older, Bitmap newer, bool vertical, int hint, int rangeStart, int rangeEnd)
        {
            if (older == null || newer == null || older.Width != newer.Width || older.Height != newer.Height)
            {
                return NotMoved();
            }

            if (older.PixelFormat != PixelFormat.Format24bppRgb || newer.PixelFormat != PixelFormat.Format24bppRgb)
            {
                return NotMoved();
            }

            int length = vertical ? older.Height : older.Width;
            if (rangeEnd <= rangeStart)
            {
                rangeStart = 0;
                rangeEnd = length;
            }

            if (rangeStart < 0)
            {
                rangeStart = 0;
            }

            if (rangeEnd > length)
            {
                rangeEnd = length;
            }

            LineMap a = LineMap.Build(older, vertical);
            LineMap b = LineMap.Build(newer, vertical);
            ShiftMeasure rough = SearchShift(a, b, rangeStart, rangeEnd, hint);
            if (!rough.Moved && (rough.Candidates == null || rough.Candidates.Length == 0))
            {
                return rough;
            }

            int[] candidates = rough.Candidates;
            if (candidates == null || candidates.Length == 0)
            {
                candidates = new[] { rough.Offset };
            }

            BitmapData aBits = older.LockBits(new Rectangle(0, 0, older.Width, older.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            BitmapData bBits = newer.LockBits(new Rectangle(0, 0, newer.Width, newer.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int bestOffset = rough.Offset > 0 ? rough.Offset : candidates[0];
                double bestScore = 9999;
                for (int i = 0; i < candidates.Length; i++)
                {
                    int refined;
                    double score;
                    RefinePixels(aBits, bBits, vertical, candidates[i], rangeStart, rangeEnd, out refined, out score);
                    bool closerHint = hint >= 2 && Math.Abs(refined - hint) < Math.Abs(bestOffset - hint);
                    if (score < bestScore - 0.8 || (closerHint && score <= bestScore + 0.8))
                    {
                        bestScore = score;
                        bestOffset = refined;
                    }
                }

                if (bestScore > 9000)
                {
                    return rough;
                }

                rough.Offset = bestOffset;
                rough.Score = bestScore;
                rough.Moved = AcceptShift(bestOffset, bestScore, rough.ZeroScore);
                rough.Reliable = rough.Moved && IsReliable(bestScore, rough.ZeroScore);
                if (!rough.Moved)
                {
                    rough.Offset = 0;
                    rough.Reliable = false;
                }

                return rough;
            }
            finally
            {
                older.UnlockBits(aBits);
                newer.UnlockBits(bBits);
            }
        }

        public static void DetectChrome(Bitmap older, Bitmap newer, bool vertical, int shift, out int startChrome, out int endChrome)
        {
            startChrome = 0;
            endChrome = 0;
            if (older == null || newer == null || shift < 2)
            {
                return;
            }

            if (older.Width != newer.Width || older.Height != newer.Height)
            {
                return;
            }

            LineMap a = LineMap.Build(older, vertical);
            LineMap b = LineMap.Build(newer, vertical);
            DetectChrome(a, b, shift, out startChrome, out endChrome);
        }

        public static StitchOutput Stitch(IList<Bitmap> frames, bool vertical)
        {
            var output = new StitchOutput();
            output.Note = "完成";
            if (frames == null || frames.Count == 0)
            {
                output.Note = "没有画面";
                return output;
            }

            if (frames.Count == 1)
            {
                output.Image = CloneBitmap(frames[0]);
                output.UsedFrames = 1;
                output.Note = "只有一屏";
                return output;
            }

            int width = frames[0].Width;
            int height = frames[0].Height;
            for (int i = 1; i < frames.Count; i++)
            {
                if (frames[i].Width != width || frames[i].Height != height)
                {
                    output.Note = "画面尺寸不一致";
                    output.Image = CloneBitmap(frames[0]);
                    output.UsedFrames = 1;
                    return output;
                }
            }

            int length = vertical ? height : width;
            ShiftMeasure first = Measure(frames[0], frames[1], vertical, 0, 0, length);
            if (!first.Moved)
            {
                output.Image = CloneBitmap(frames[0]);
                output.UsedFrames = 1;
                output.Note = "没有检测到滚动";
                return output;
            }

            int startChrome;
            int endChrome;
            DetectChrome(frames[0], frames[1], vertical, first.Offset, out startChrome, out endChrome);
            int contentStart = startChrome;
            int contentEnd = length - endChrome;
            if (contentEnd - contentStart < 16)
            {
                startChrome = 0;
                endChrome = 0;
                contentStart = 0;
                contentEnd = length;
            }

            ShiftMeasure tuned = Measure(frames[0], frames[1], vertical, first.Offset, contentStart, contentEnd);
            if (tuned.Moved && Math.Abs(tuned.Offset - first.Offset) > 4)
            {
                DetectChrome(frames[0], frames[1], vertical, tuned.Offset, out startChrome, out endChrome);
                contentStart = startChrome;
                contentEnd = length - endChrome;
                if (contentEnd - contentStart < 16)
                {
                    startChrome = 0;
                    endChrome = 0;
                    contentStart = 0;
                    contentEnd = length;
                }

                tuned = Measure(frames[0], frames[1], vertical, tuned.Offset, contentStart, contentEnd);
            }

            int shift = tuned.Moved ? tuned.Offset : first.Offset;
            if (shift < 2 || shift >= (contentEnd - contentStart) - 4)
            {
                output.Image = CloneBitmap(frames[0]);
                output.UsedFrames = 1;
                output.Note = "滚动幅度不适合拼接";
                return output;
            }

            var slices = new List<Slice>();
            slices.Add(new Slice
            {
                Source = frames[0],
                Start = contentStart,
                Length = contentEnd - contentStart
            });

            int contentTotal = contentEnd - contentStart;
            int used = 2;
            string note = "已到达边缘";
            int hint = shift;
            if (!AppendSlice(frames[1], contentEnd, shift, contentEnd - contentStart, slices, ref contentTotal))
            {
                note = "滚动幅度不适合拼接";
                used = 1;
            }
            else
            {
                for (int i = 2; i < frames.Count; i++)
                {
                    ShiftMeasure step = Measure(frames[i - 1], frames[i], vertical, hint, contentStart, contentEnd);
                    if (!step.Moved)
                    {
                        note = "已到达边缘";
                        break;
                    }

                    if (!step.Reliable || !AppendSlice(frames[i], contentEnd, step.Offset, contentEnd - contentStart, slices, ref contentTotal))
                    {
                        note = "画面变化较大，已保留已拼好的部分";
                        break;
                    }

                    hint = step.Offset;
                    used = i + 1;
                    if (contentTotal > 48000)
                    {
                        note = "已达到长度上限";
                        break;
                    }
                }
            }

            if (used < 2)
            {
                output.Image = CloneBitmap(frames[0]);
                output.UsedFrames = 1;
                output.Note = note;
                return output;
            }

            try
            {
                output.Image = Compose(frames[0], frames[used - 1], vertical, startChrome, endChrome, slices, contentTotal);
                output.UsedFrames = used;
                output.StartChrome = startChrome;
                output.EndChrome = endChrome;
                output.Note = note;
            }
            catch (Exception ex)
            {
                output.Image = CloneBitmap(frames[0]);
                output.UsedFrames = 1;
                output.Note = ex.Message;
            }

            return output;
        }

        static bool AppendSlice(Bitmap source, int contentEnd, int shift, int contentLength, List<Slice> slices, ref int contentTotal)
        {
            if (shift < 2 || shift >= contentLength - 4)
            {
                return false;
            }

            slices.Add(new Slice
            {
                Source = source,
                Start = contentEnd - shift,
                Length = shift
            });
            contentTotal += shift;
            return true;
        }

        static ShiftMeasure SearchShift(LineMap older, LineMap newer, int rangeStart, int rangeEnd, int hint)
        {
            int span = rangeEnd - rangeStart;
            if (older.Length != newer.Length || span < 16)
            {
                return NotMoved();
            }

            int minKeep = Math.Max(18, span / 5);
            int maxS = span - minKeep;
            if (maxS < 2)
            {
                return NotMoved();
            }

            var moving = new bool[older.Length];
            int movingCount = 0;
            for (int y = rangeStart; y < rangeEnd; y++)
            {
                if (DiffAt(older, y, newer, y) > 0.45)
                {
                    moving[y] = true;
                    movingCount++;
                }
            }

            if (movingCount < Math.Max(4, span / 24))
            {
                return NotMoved();
            }

            var scores = new double[maxS + 1];
            for (int s = 0; s <= maxS; s++)
            {
                scores[s] = 9999;
            }

            double zero = ScoreMoving(older, newer, moving, rangeStart, rangeEnd, 0);
            scores[0] = zero;
            double best = zero;
            int bestS = 0;
            int step = span > 420 ? 2 : 1;
            for (int s = 2; s <= maxS; s += step)
            {
                double score = ScoreMoving(older, newer, moving, rangeStart, rangeEnd, s);
                scores[s] = score;
                if (score < best)
                {
                    best = score;
                    bestS = s;
                }
            }

            FillAround(older, newer, moving, rangeStart, rangeEnd, scores, bestS, 4, maxS, ref best, ref bestS);
            if (hint >= 2)
            {
                FillAround(older, newer, moving, rangeStart, rangeEnd, scores, hint, 8, maxS, ref best, ref bestS);
            }

            int chosen = bestS;
            if (hint >= 2 && hint <= maxS)
            {
                int nearest = bestS;
                int distance = Math.Abs(bestS - hint);
                double gate = best + 1.2;
                for (int s = 2; s <= maxS; s++)
                {
                    if (scores[s] <= gate && Math.Abs(s - hint) < distance)
                    {
                        nearest = s;
                        distance = Math.Abs(s - hint);
                    }
                }

                chosen = nearest;
            }
            else
            {
                int target = Math.Max(2, (int)(span * 0.45));
                int nearest = bestS;
                int distance = Math.Abs(bestS - target);
                double gate = best + (best < 2 ? 0.35 : 0.8);
                for (int s = 2; s <= maxS; s++)
                {
                    if (scores[s] <= gate && Math.Abs(s - target) < distance)
                    {
                        nearest = s;
                        distance = Math.Abs(s - target);
                    }
                }

                chosen = nearest;
            }

            double chosenScore = scores[chosen];
            if (chosenScore > 9000)
            {
                chosenScore = ScoreMoving(older, newer, moving, rangeStart, rangeEnd, chosen);
            }

            ShiftMeasure measure = Finish(chosen, chosenScore, zero, movingCount);
            measure.Candidates = PickCandidates(scores, maxS, best, bestS, chosen, hint);
            if (measure.Candidates.Length > 0)
            {
                measure.Moved = true;
            }

            return measure;
        }

        static int[] PickCandidates(double[] scores, int maxS, double best, int bestS, int chosen, int hint)
        {
            var picked = new List<int>();
            double gate = best + 6.0;
            for (int s = 2; s <= maxS; s++)
            {
                if (scores[s] > gate)
                {
                    continue;
                }

                double left = s > 2 ? scores[s - 1] : 9999;
                double right = s < maxS ? scores[s + 1] : 9999;
                if (scores[s] <= left && scores[s] <= right)
                {
                    picked.Add(s);
                }
            }

            if (!picked.Contains(bestS) && bestS >= 2)
            {
                picked.Add(bestS);
            }

            if (!picked.Contains(chosen) && chosen >= 2)
            {
                picked.Add(chosen);
            }

            if (hint >= 2 && hint <= maxS && !picked.Contains(hint))
            {
                picked.Add(hint);
            }

            if (picked.Count > 8)
            {
                picked.Sort(delegate(int x, int y) { return scores[x].CompareTo(scores[y]); });
                picked.RemoveRange(8, picked.Count - 8);
            }

            if (bestS >= 2 && !picked.Contains(bestS))
            {
                picked.Add(bestS);
            }

            if (chosen >= 2 && !picked.Contains(chosen))
            {
                picked.Add(chosen);
            }

            if (hint >= 2 && hint <= maxS && !picked.Contains(hint))
            {
                picked.Add(hint);
            }

            return picked.ToArray();
        }

        static void FillAround(LineMap older, LineMap newer, bool[] moving, int rangeStart, int rangeEnd, double[] scores, int center, int radius, int maxS, ref double best, ref int bestS)
        {
            int from = Math.Max(2, center - radius);
            int to = Math.Min(maxS, center + radius);
            for (int s = from; s <= to; s++)
            {
                if (scores[s] > 9000)
                {
                    scores[s] = ScoreMoving(older, newer, moving, rangeStart, rangeEnd, s);
                }

                if (scores[s] < best)
                {
                    best = scores[s];
                    bestS = s;
                }
            }
        }

        static double ScoreMoving(LineMap older, LineMap newer, bool[] moving, int start, int end, int shift)
        {
            int last = end - shift;
            if (last <= start)
            {
                return 9999;
            }

            int step = (last - start) > 280 ? 2 : 1;
            double sum = 0;
            int count = 0;
            for (int y = start; y < last; y += step)
            {
                int olderPos = y + shift;
                if (olderPos < 0 || olderPos >= older.Length)
                {
                    continue;
                }

                if (shift != 0 && (!moving[y] || !moving[olderPos]))
                {
                    continue;
                }

                sum += DiffAt(older, olderPos, newer, y);
                count++;
            }

            if (count < 3)
            {
                return 9999;
            }

            return sum / count;
        }

        static void RefinePixels(BitmapData older, BitmapData newer, bool vertical, int shift, int start, int end, out int bestOffset, out double bestScore)
        {
            int span = end - start;
            int maxS = span - Math.Max(12, span / 5);
            int from = Math.Max(1, shift - 6);
            int to = Math.Min(maxS, shift + 6);
            bestOffset = shift;
            bestScore = 9999;
            if (to < from)
            {
                return;
            }

            for (int s = from; s <= to; s++)
            {
                double score = PixelScore(older, newer, vertical, s, start, end);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestOffset = s;
                }
            }
        }

        static unsafe double PixelScore(BitmapData older, BitmapData newer, bool vertical, int shift, int start, int end)
        {
            int last = end - shift;
            if (shift < 0 || last <= start)
            {
                return 9999;
            }

            int cross = vertical ? older.Width : older.Height;
            int lineStep = (last - start) > 320 ? 2 : 1;
            int crossStep = cross > 500 ? 2 : 1;
            long sum = 0;
            int count = 0;
            byte* aScan = (byte*)older.Scan0;
            byte* bScan = (byte*)newer.Scan0;
            int sameStep = Math.Max(crossStep * 2, 1);
            for (int pos = start; pos < last; pos += lineStep)
            {
                int sameSum = 0;
                int sameCount = 0;
                for (int c = 0; c < cross; c += sameStep)
                {
                    byte* sa = vertical
                        ? aScan + pos * older.Stride + c * 3
                        : aScan + c * older.Stride + pos * 3;
                    byte* sb = vertical
                        ? bScan + pos * newer.Stride + c * 3
                        : bScan + c * newer.Stride + pos * 3;
                    sameSum += Math.Abs(sa[0] - sb[0]) + Math.Abs(sa[1] - sb[1]) + Math.Abs(sa[2] - sb[2]);
                    sameCount += 3;
                }

                int olderPos = pos + shift;
                int shiftedSame = 0;
                int shiftedCount = 0;
                for (int c = 0; c < cross; c += sameStep)
                {
                    byte* sa = vertical
                        ? aScan + olderPos * older.Stride + c * 3
                        : aScan + c * older.Stride + olderPos * 3;
                    byte* sb = vertical
                        ? bScan + olderPos * newer.Stride + c * 3
                        : bScan + c * newer.Stride + olderPos * 3;
                    shiftedSame += Math.Abs(sa[0] - sb[0]) + Math.Abs(sa[1] - sb[1]) + Math.Abs(sa[2] - sb[2]);
                    shiftedCount += 3;
                }

                bool fixedLine = sameCount > 0 && sameSum / (double)sameCount <= 2;
                bool fixedTarget = shiftedCount > 0 && shiftedSame / (double)shiftedCount <= 2;
                if (shift != 0 && (fixedLine || fixedTarget))
                {
                    continue;
                }
                for (int c = 0; c < cross; c += crossStep)
                {
                    byte* pa = vertical
                        ? aScan + olderPos * older.Stride + c * 3
                        : aScan + c * older.Stride + olderPos * 3;
                    byte* pb = vertical
                        ? bScan + pos * newer.Stride + c * 3
                        : bScan + c * newer.Stride + pos * 3;
                    sum += Math.Abs(pa[0] - pb[0]) + Math.Abs(pa[1] - pb[1]) + Math.Abs(pa[2] - pb[2]);
                    count += 3;
                }
            }

            if (count == 0)
            {
                return 9999;
            }

            return sum / (double)count;
        }

        static ShiftMeasure Finish(int offset, double score, double zeroScore, int votes)
        {
            var measure = new ShiftMeasure();
            measure.Offset = offset;
            measure.Score = score;
            measure.ZeroScore = zeroScore;
            measure.Votes = votes;
            measure.Moved = AcceptShift(offset, score, zeroScore);
            measure.Reliable = measure.Moved && IsReliable(score, zeroScore);
            if (!measure.Moved)
            {
                measure.Offset = 0;
            }

            return measure;
        }

        static bool AcceptShift(int offset, double score, double zeroScore)
        {
            if (offset < 2 || score > 18 || zeroScore < 0.15)
            {
                return false;
            }

            return score + Math.Max(0.18, zeroScore * 0.22) < zeroScore;
        }

        static bool IsReliable(double score, double zeroScore)
        {
            return score <= 8 && score + 0.12 < zeroScore * 0.55;
        }

        static ShiftMeasure NotMoved()
        {
            return new ShiftMeasure();
        }

        static void DetectChrome(LineMap older, LineMap newer, int shift, out int startChrome, out int endChrome)
        {
            startChrome = 0;
            endChrome = 0;
            int length = older.Length;
            if (shift < 2 || shift >= length)
            {
                return;
            }

            bool seenStrong = false;
            for (int y = 0; y < length; y++)
            {
                LineKind kind = Classify(older, newer, y, shift, length);
                if (kind == LineKind.Moving || kind == LineKind.Unknown)
                {
                    break;
                }

                if (kind == LineKind.Strong)
                {
                    seenStrong = true;
                    startChrome = y + 1;
                }
                else if (kind == LineKind.Weak && seenStrong)
                {
                    startChrome = y + 1;
                }
            }

            if (!seenStrong)
            {
                startChrome = 0;
            }

            seenStrong = false;
            for (int i = 0; i < length; i++)
            {
                int y = length - 1 - i;
                LineKind kind = Classify(older, newer, y, shift, length);
                if (kind == LineKind.Moving || kind == LineKind.Unknown)
                {
                    break;
                }

                if (kind == LineKind.Strong)
                {
                    seenStrong = true;
                    endChrome = i + 1;
                }
                else if (kind == LineKind.Weak && seenStrong)
                {
                    endChrome = i + 1;
                }
            }

            if (!seenStrong)
            {
                endChrome = 0;
            }

            if (startChrome + endChrome > length - 24)
            {
                startChrome = 0;
                endChrome = 0;
            }
        }

        enum LineKind
        {
            Unknown,
            Moving,
            Strong,
            Weak
        }

        static LineKind Classify(LineMap older, LineMap newer, int y, int shift, int length)
        {
            double same = DiffAt(older, y, newer, y);
            double shifted = (y + shift < length) ? DiffAt(older, y + shift, newer, y) : 255;

            // 滚动后的内容行会和旧图更靠后的位置对上，同一位置则对不上。
            if (shifted + 6 < same && shifted <= 18)
            {
                return LineKind.Moving;
            }

            // 表格里大部分格子相同，平均差很小，对齐后仍会明显更低。
            if (same >= 0.6 && shifted * 4 + 0.25 < same && shifted <= 8)
            {
                return LineKind.Moving;
            }

            // 表头、底栏停在原位。即使和滚动后的内容灰度接近，也仍然算固定区域。
            if (same <= 12 && shifted >= same + 8)
            {
                return LineKind.Strong;
            }

            if (same <= 1.5 && shifted >= same + 2.5)
            {
                return LineKind.Strong;
            }

            if (same <= 12)
            {
                return LineKind.Weak;
            }

            return LineKind.Unknown;
        }

        static double DiffAt(LineMap a, int posA, LineMap b, int posB)
        {
            int sum = 0;
            int count = a.Samples * 3;
            int ia = posA * count;
            int ib = posB * count;
            byte[] da = a.Data;
            byte[] db = b.Data;
            for (int i = 0; i < count; i++)
            {
                sum += Math.Abs(da[ia + i] - db[ib + i]);
            }

            return sum / (double)count;
        }

        static Bitmap Compose(Bitmap first, Bitmap last, bool vertical, int startChrome, int endChrome, List<Slice> slices, int contentTotal)
        {
            int cross = vertical ? first.Width : first.Height;
            int total = startChrome + contentTotal + endChrome;
            if (total < 1 || total > 50000 || cross < 1 || cross > 20000)
            {
                throw new InvalidOperationException("长图尺寸超出限制");
            }

            Bitmap destination = vertical
                ? new Bitmap(cross, total, PixelFormat.Format24bppRgb)
                : new Bitmap(total, cross, PixelFormat.Format24bppRgb);
            destination.SetResolution(96, 96);

            int cursor = 0;
            if (startChrome > 0)
            {
                Blit(first, 0, startChrome, destination, cursor, vertical);
                cursor += startChrome;
            }

            for (int i = 0; i < slices.Count; i++)
            {
                Slice slice = slices[i];
                Blit(slice.Source, slice.Start, slice.Length, destination, cursor, vertical);
                cursor += slice.Length;
            }

            if (endChrome > 0)
            {
                int length = vertical ? last.Height : last.Width;
                Blit(last, length - endChrome, endChrome, destination, cursor, vertical);
            }

            return destination;
        }

        static unsafe void Blit(Bitmap source, int sourcePos, int length, Bitmap destination, int destPos, bool vertical)
        {
            BitmapData src = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            BitmapData dst = destination.LockBits(new Rectangle(0, 0, destination.Width, destination.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            try
            {
                if (vertical)
                {
                    int bytes = source.Width * 3;
                    for (int y = 0; y < length; y++)
                    {
                        byte* from = (byte*)src.Scan0 + (sourcePos + y) * src.Stride;
                        byte* to = (byte*)dst.Scan0 + (destPos + y) * dst.Stride;
                        Buffer.MemoryCopy(from, to, bytes, bytes);
                    }
                }
                else
                {
                    int bytes = length * 3;
                    for (int y = 0; y < source.Height; y++)
                    {
                        byte* from = (byte*)src.Scan0 + y * src.Stride + sourcePos * 3;
                        byte* to = (byte*)dst.Scan0 + y * dst.Stride + destPos * 3;
                        Buffer.MemoryCopy(from, to, bytes, bytes);
                    }
                }
            }
            finally
            {
                source.UnlockBits(src);
                destination.UnlockBits(dst);
            }
        }

        static Bitmap CloneBitmap(Bitmap source)
        {
            return source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format24bppRgb);
        }
    }
}
