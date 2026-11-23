using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NAudio.Wave;
using Vosk;

namespace Shiying.Voice
{
    enum VoiceState
    {
        Idle,
        Starting,
        Listening,
        Faulted
    }

    class VoiceCommandMatch
    {
        public string Text;            // 实际识别到的文字
        public string Intent;          // 匹配到的意图
        public string Phrase;          // 命中的说法
        public double Confidence;      // 识别置信度
        public bool Exact;             // 是否完全相等/包含
    }

    // 语音命令助手：16k 单声道采集 -> Vosk 识别 -> 命令匹配，播报交给 VoiceSpeaker。
    // 与录屏用的 AudioCapture(48k 立体声混音)分开，避免抢麦；开始录屏前请先 Stop()。
    class VoiceAssistant : IDisposable
    {
        const int SampleRate = 16000;

        readonly VoiceOptions _opt;
        readonly VoiceSpeaker _speaker;
        readonly object _asrGate = new object();
        WaveIn _in;
        Model _model;
        VoskRecognizer _rec;
        bool _disposed;
        VoiceState _state = VoiceState.Idle;

        public event EventHandler<string> PartialText;                       // 边说边出的中间结果
        public event EventHandler<VoiceCommandMatch> CommandRecognized;       // 命中命令
        public event EventHandler<string> Heard;                             // 听清了但没匹配上
        public event EventHandler<VoiceState> StateChanged;
        public event EventHandler<string> Error;

        public VoiceAssistant(VoiceOptions opt)
        {
            _opt = opt;
            _speaker = new VoiceSpeaker(opt);
        }

        public VoiceOptions Options
        {
            get { return _opt; }
        }

        public VoiceSpeaker Speaker
        {
            get { return _speaker; }
        }

        public VoiceState State
        {
            get { return _state; }
        }

        public bool IsListening
        {
            get { return _in != null; }
        }

        // 开始监听；deviceNumber = -1 表示系统默认输入设备
        public void Start(int deviceNumber)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("VoiceAssistant");
            }
            if (_in != null)
            {
                return;
            }

            SetState(VoiceState.Starting);
            try
            {
                lock (_asrGate)
                {
                    if (_model == null)
                    {
                        if (!_opt.HasVosk)
                        {
                            throw new InvalidOperationException(_opt.MissingHint());
                        }
                        Vosk.Vosk.SetLogLevel(-1);
                        _model = new Model(_opt.VoskModelDir);
                    }
                    if (_rec == null)
                    {
                        _rec = CreateRecognizer();
                    }
                }

                WaveIn ev = new WaveIn();
                ev.WaveFormat = new WaveFormat(SampleRate, 16, 1);
                ev.BufferMilliseconds = 100;
                ev.NumberOfBuffers = 4;
                if (deviceNumber >= 0)
                {
                    ev.DeviceNumber = deviceNumber;
                }
                ev.DataAvailable += OnDataAvailable;
                ev.RecordingStopped += OnRecordingStopped;
                ev.StartRecording();

                _in = ev;
                SetState(VoiceState.Listening);
            }
            catch (Exception ex)
            {
                SetState(VoiceState.Faulted);
                RaiseError(ex.Message);
                CleanupCapture();
            }
        }

        public void Stop()
        {
            CleanupCapture();
            SetState(_state == VoiceState.Faulted ? VoiceState.Faulted : VoiceState.Idle);
        }

        // 播报（异步，不阻塞界面；合成失败走 Error 事件）
        public void Say(string text)
        {
            Task task = _speaker.SpeakAsync(text);
            task.ContinueWith(delegate (Task t)
            {
                Exception err = t.Exception;
                if (err != null)
                {
                    RaiseError("播报失败：" + err.GetBaseException().Message);
                }
            }, TaskScheduler.Default);
        }

        // 打断播报
        public void ShutUp()
        {
            _speaker.Stop();
        }

        // 中文小模型的词表是按字/词条构建的，实测任何多字说法都会判成"词表外"，
        // 所以这里不做 Kaldi 词表约束，改为自由识别 + 命令匹配器（VoiceCommandMatcher）。
        VoskRecognizer CreateRecognizer()
        {
            VoskRecognizer rec = new VoskRecognizer(_model, (float)SampleRate);
            rec.SetWords(true);
            return rec;
        }

        void CleanupCapture()
        {
            WaveIn dev = _in;
            _in = null;
            if (dev != null)
            {
                try
                {
                    dev.StopRecording();
                }
                catch (Exception)
                {
                }
            }
        }

        void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            try
            {
                WaveIn dev = sender as WaveIn;
                if (dev != null)
                {
                    dev.Dispose();
                }
            }
            catch (Exception)
            {
            }

            if (e != null && e.Exception != null)
            {
                SetState(VoiceState.Faulted);
                RaiseError("麦克风采集失败：" + e.Exception.Message);
            }
        }

        void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            // 自己正在播报时先不喂音频，避免喇叭声被识别成命令
            if (_opt.MuteWhileSpeaking && _speaker.IsSpeaking)
            {
                return;
            }

            lock (_asrGate)
            {
                if (_rec == null)
                {
                    return;
                }

                try
                {
                    if (_rec.AcceptWaveform(e.Buffer, e.BytesRecorded))
                    {
                        HandleFinal(_rec.Result());
                    }
                    else
                    {
                        string partial = _rec.PartialResult();
                        string text = ReadJsonText(partial, "partial");
                        if (!string.IsNullOrEmpty(text))
                        {
                            EventHandler<string> handler = PartialText;
                            if (handler != null)
                            {
                                try
                                {
                                    handler(this, text);
                                }
                                catch (Exception)
                                {
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    RaiseError("识别出错：" + ex.Message);
                }
            }
        }

        void HandleFinal(string json)
        {
            string text = ReadJsonText(json, "text");
            text = text == null ? "" : text.Trim();
            if (text.Length == 0)
            {
                return;
            }

            double conf = ReadConfidence(json);
            if (conf > 0 && conf < _opt.MinConfidence)
            {
                EventHandler<string> heardLow = Heard;
                if (heardLow != null)
                {
                    try
                    {
                        heardLow(this, text);
                    }
                    catch (Exception)
                    {
                    }
                }
                return;
            }

            VoiceCommandMatch match = VoiceCommandMatcher.Match(text, _opt.Phrases);
            if (match == null)
            {
                EventHandler<string> handler = Heard;
                if (handler != null)
                {
                    try
                    {
                        handler(this, text);
                    }
                    catch (Exception)
                    {
                    }
                }
                return;
            }

            match.Confidence = conf;
            EventHandler<VoiceCommandMatch> cmd = CommandRecognized;
            if (cmd != null)
            {
                try
                {
                    cmd(this, match);
                }
                catch (Exception)
                {
                }
            }
        }

        static string ReadJsonText(string json, string field)
        {
            try
            {
                JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(field, out JsonElement el) && el.ValueKind == JsonValueKind.String)
                {
                    return el.GetString();
                }
            }
            catch (Exception)
            {
            }
            return "";
        }

        // Vosk 的 result 数组里每个词都有 conf，取最小值作为整句置信度
        static double ReadConfidence(string json)
        {
            try
            {
                JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("result", out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
                {
                    return 0;
                }

                double min = 0;
                bool any = false;
                foreach (JsonElement w in arr.EnumerateArray())
                {
                    if (w.TryGetProperty("conf", out JsonElement c) && c.ValueKind == JsonValueKind.Number)
                    {
                        double v = c.GetDouble();
                        if (!any || v < min)
                        {
                            min = v;
                        }
                        any = true;
                    }
                }
                return any ? min : 0;
            }
            catch (Exception)
            {
            }
            return 0;
        }

        void SetState(VoiceState state)
        {
            if (_state == state)
            {
                return;
            }
            _state = state;
            EventHandler<VoiceState> handler = StateChanged;
            if (handler != null)
            {
                try
                {
                    handler(this, state);
                }
                catch (Exception)
                {
                }
            }
        }

        void RaiseError(string message)
        {
            EventHandler<string> handler = Error;
            if (handler != null)
            {
                try
                {
                    handler(this, message);
                }
                catch (Exception)
                {
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            CleanupCapture();

            lock (_asrGate)
            {
                if (_rec != null)
                {
                    try
                    {
                        _rec.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                    _rec = null;
                }
                if (_model != null)
                {
                    try
                    {
                        _model.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                    _model = null;
                }
            }

            _speaker.Dispose();
        }
    }

    // 把识别文本映射到意图：先精确相等，再包含，最后近似（编辑距离）
    static class VoiceCommandMatcher
    {
        public static VoiceCommandMatch Match(string text, List<VoicePhrase> phrases)
        {
            if (phrases == null || phrases.Count == 0)
            {
                return null;
            }

            string norm = Normalize(text);
            if (norm.Length == 0)
            {
                return null;
            }

            // 说法长的优先，避免"停止"抢走"停止录制"
            List<VoicePhrase> sorted = new List<VoicePhrase>(phrases);
            sorted.Sort(delegate (VoicePhrase a, VoicePhrase b)
            {
                return Normalize(b.Phrase).Length - Normalize(a.Phrase).Length;
            });

            VoicePhrase best = null;
            int bestLen = 0;
            foreach (VoicePhrase p in sorted)
            {
                string phrase = Normalize(p.Phrase);
                if (phrase.Length == 0)
                {
                    continue;
                }
                if (phrase == norm)
                {
                    return Build(text, p, phrase, true);
                }
                // 长句里顺带出现关键词不算命令，避免聊天时误触发
                if (phrase.Length > bestLen && (norm.Length - phrase.Length) <= 3
                    && norm.IndexOf(phrase, StringComparison.Ordinal) >= 0)
                {
                    best = p;
                    bestLen = phrase.Length;
                }
            }

            if (best != null)
            {
                return Build(text, best, Normalize(best.Phrase), true);
            }

            // 近似匹配：只对 3 字以上的说法开，短词容易误伤
            VoicePhrase near = null;
            double nearScore = 0;
            foreach (VoicePhrase p in sorted)
            {
                string phrase = Normalize(p.Phrase);
                if (phrase.Length < 3)
                {
                    continue;
                }
                int maxLen = Math.Max(phrase.Length, norm.Length);
                if (maxLen == 0)
                {
                    continue;
                }
                double ratio = 1.0 - (double)Levenshtein(phrase, norm) / maxLen;
                if (ratio > nearScore)
                {
                    nearScore = ratio;
                    near = p;
                }
            }

            if (near != null && nearScore >= 0.8)
            {
                VoiceCommandMatch m = Build(text, near, Normalize(near.Phrase), false);
                m.Confidence = nearScore;
                return m;
            }

            return null;
        }

        static VoiceCommandMatch Build(string original, VoicePhrase p, string phrase, bool exact)
        {
            VoiceCommandMatch m = new VoiceCommandMatch();
            m.Text = original;
            m.Intent = p.Intent;
            m.Phrase = p.Phrase;
            m.Exact = exact;
            m.Confidence = 1.0;
            return m;
        }

        // 只保留中文、字母、数字，去掉空格标点（Vosk 中文模型有时按词切开输出）
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }

            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }
                if (char.IsPunctuation(c) || char.IsSymbol(c))
                {
                    continue;
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        static int Levenshtein(string a, string b)
        {
            if (a.Length == 0)
            {
                return b.Length;
            }
            if (b.Length == 0)
            {
                return a.Length;
            }

            int[] prev = new int[b.Length + 1];
            int[] cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++)
            {
                prev[j] = j;
            }

            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int del = prev[j] + 1;
                    int ins = cur[j - 1] + 1;
                    int sub = prev[j - 1] + cost;
                    int v = del < ins ? del : ins;
                    cur[j] = v < sub ? v : sub;
                }
                int[] tmp = prev;
                prev = cur;
                cur = tmp;
            }
            return prev[b.Length];
        }
    }
}
