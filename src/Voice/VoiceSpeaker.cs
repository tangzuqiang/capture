using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;
using SherpaOnnx;

namespace Shiying.Voice
{
    // 播报：sherpa-onnx 的 VITS 模型在本地合成，NAudio 出声。
    // 合成结果按"文本+语速+音色"缓存，固定提示语第二次就不再推理。
    class VoiceSpeaker : IDisposable
    {
        class Clip
        {
            public byte[] Pcm;
            public int SampleRate;
        }

        readonly VoiceOptions _opt;
        readonly object _gate = new object();
        readonly object _ttsGate = new object();
        readonly Dictionary<string, Clip> _cache = new Dictionary<string, Clip>();

        OfflineTts _tts;
        WaveOut _out;
        TaskCompletionSource<bool> _playTcs;
        bool _disposed;

        // true = 开始出声，false = 播报结束（用于界面状态和打断判断）
        public event EventHandler<bool> SpeakingChanged;

        public VoiceSpeaker(VoiceOptions opt)
        {
            _opt = opt;
        }

        public bool IsSpeaking
        {
            get
            {
                lock (_gate)
                {
                    return _out != null && _out.PlaybackState == PlaybackState.Playing;
                }
            }
        }

        // 合成并播放，返回的 Task 在播完（或被 Stop 打断）时完成
        public async Task SpeakAsync(string text)
        {
            if (_disposed || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string key = _opt.SpeakerId + "|" + _opt.Speed + "|" + text;
            Clip clip;
            lock (_gate)
            {
                clip = _cache.TryGetValue(key, out Clip cached) ? cached : null;
            }

            if (clip == null)
            {
                clip = await Task.Run(() => Synthesize(text)).ConfigureAwait(false);
                lock (_gate)
                {
                    _cache[key] = clip;
                }
            }

            await PlayAsync(clip).ConfigureAwait(false);
        }

        // 打断：立即停声，正在 await 的 SpeakAsync 随之返回
        public void Stop()
        {
            WaveOut dev;
            lock (_gate)
            {
                dev = _out;
                _out = null;
            }

            if (dev != null)
            {
                try
                {
                    dev.Stop();
                }
                catch (Exception)
                {
                }
            }
        }

        // 只合成不出声，方便做固定提示音的 wav 预生成
        public void SaveWav(string text, string path)
        {
            Clip clip = Synthesize(text);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                VoiceSpeaker.WriteWavHeader(fs, clip.Pcm.Length, clip.SampleRate);
                fs.Write(clip.Pcm, 0, clip.Pcm.Length);
            }
        }

        Task PlayAsync(Clip clip)
        {
            WaveFormat format = new WaveFormat(clip.SampleRate, 16, 1);
            RawSourceWaveStream stream = new RawSourceWaveStream(new MemoryStream(clip.Pcm), format);
            WaveOut dev = new WaveOut();
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            EventHandler<StoppedEventArgs> handler = null;
            handler = delegate (object s, StoppedEventArgs e)
            {
                try
                {
                    dev.PlaybackStopped -= handler;
                }
                catch (Exception)
                {
                }
                try
                {
                    dev.Dispose();
                }
                catch (Exception)
                {
                }
                try
                {
                    stream.Dispose();
                }
                catch (Exception)
                {
                }
                tcs.TrySetResult(true);
            };

            dev.PlaybackStopped += handler;
            try
            {
                dev.Init(stream);
            }
            catch (Exception)
            {
                dev.Dispose();
                stream.Dispose();
                throw;
            }

            lock (_gate)
            {
                _out = dev;
                _playTcs = tcs;
            }

            RaiseSpeaking(true);
            dev.Play();

            Task task = tcs.Task.ContinueWith(delegate (Task t)
            {
                lock (_gate)
                {
                    if (_playTcs == tcs)
                    {
                        _playTcs = null;
                    }
                }
                RaiseSpeaking(false);
            }, TaskScheduler.Default);
            return task;
        }

        Clip Synthesize(string text)
        {
            OfflineTts tts = EnsureTts();
            OfflineTtsGeneratedAudio audio = null;
            try
            {
                lock (_ttsGate)
                {
                    audio = tts.Generate(text, _opt.Speed, _opt.SpeakerId);
                }

                if (audio == null || audio.NumSamples <= 0)
                {
                    throw new InvalidOperationException("语音合成为空：" + text);
                }

                int n = audio.NumSamples;
                float[] samples = audio.Samples;
                byte[] pcm = new byte[n * 2];
                for (int i = 0; i < n; i++)
                {
                    float v = samples[i];
                    if (v > 1f)
                    {
                        v = 1f;
                    }
                    else if (v < -1f)
                    {
                        v = -1f;
                    }
                    short s = (short)(v * 32767f);
                    pcm[i * 2] = (byte)(s & 0xFF);
                    pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
                }

                Clip clip = new Clip();
                clip.Pcm = pcm;
                clip.SampleRate = audio.SampleRate;
                return clip;
            }
            finally
            {
                if (audio != null)
                {
                    audio.Dispose();
                }
            }
        }

        OfflineTts EnsureTts()
        {
            if (_tts != null)
            {
                return _tts;
            }

            lock (_ttsGate)
            {
                if (_tts != null)
                {
                    return _tts;
                }

                if (string.IsNullOrEmpty(_opt.VitsModel) || string.IsNullOrEmpty(_opt.VitsTokens))
                {
                    throw new InvalidOperationException(_opt.MissingHint());
                }

                OfflineTtsConfig cfg = new OfflineTtsConfig();
                cfg.Model.Vits.Model = _opt.VitsModel;
                cfg.Model.Vits.Tokens = _opt.VitsTokens;
                cfg.Model.Vits.Lexicon = _opt.VitsLexicon == null ? "" : _opt.VitsLexicon;
                cfg.Model.Vits.DictDir = _opt.VitsDictDir == null ? "" : _opt.VitsDictDir;
                cfg.Model.NumThreads = Math.Max(1, Environment.ProcessorCount / 2);
                cfg.Model.Provider = "cpu";
                cfg.MaxNumSentences = 1;

                try
                {
                    _tts = new OfflineTts(cfg);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("VITS 模型加载失败（" + _opt.VitsModel + "）：" + ex.Message, ex);
                }
            }
            return _tts;
        }

        void RaiseSpeaking(bool speaking)
        {
            EventHandler<bool> handler = SpeakingChanged;
            if (handler != null)
            {
                try
                {
                    handler(this, speaking);
                }
                catch (Exception)
                {
                }
            }
        }

        static void WriteWavHeader(Stream s, int pcmBytes, int sampleRate)
        {
            byte[] h = new byte[44];
            int byteRate = sampleRate * 2;
            h[0] = (byte)'R'; h[1] = (byte)'I'; h[2] = (byte)'F'; h[3] = (byte)'F';
            VoiceSpeaker.PutInt(h, 4, 36 + pcmBytes);
            h[8] = (byte)'W'; h[9] = (byte)'A'; h[10] = (byte)'V'; h[11] = (byte)'E';
            h[12] = (byte)'f'; h[13] = (byte)'m'; h[14] = (byte)'t'; h[15] = (byte)' ';
            VoiceSpeaker.PutInt(h, 16, 16);
            h[20] = 1; h[21] = 0;                       // PCM
            h[22] = 1; h[23] = 0;                       // 单声道
            VoiceSpeaker.PutInt(h, 24, sampleRate);
            VoiceSpeaker.PutInt(h, 28, byteRate);
            h[32] = 2; h[33] = 0;                       // block align
            h[34] = 16; h[35] = 0;                      // bits per sample
            h[36] = (byte)'d'; h[37] = (byte)'a'; h[38] = (byte)'t'; h[39] = (byte)'a';
            VoiceSpeaker.PutInt(h, 40, pcmBytes);
            s.Write(h, 0, h.Length);
        }

        static void PutInt(byte[] b, int offset, int value)
        {
            b[offset] = (byte)(value & 0xFF);
            b[offset + 1] = (byte)((value >> 8) & 0xFF);
            b[offset + 2] = (byte)((value >> 16) & 0xFF);
            b[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            Stop();

            lock (_ttsGate)
            {
                if (_tts != null)
                {
                    try
                    {
                        _tts.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                    _tts = null;
                }
            }
        }
    }
}
