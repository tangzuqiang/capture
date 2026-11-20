#if NET
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Jietu
{
    sealed class AudioCapture : IDisposable
    {
        public const int Rate = 48000;
        public const int Channels = 2;
        static readonly TimeSpan Lag = TimeSpan.FromMilliseconds(120);

        readonly List<IWaveIn> _inputs = new List<IWaveIn>();
        readonly MixingSampleProvider _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, Channels));
        readonly List<string> _missing = new List<string>();
        WasapiOut _keepAlive;
        Thread _pump;
        volatile bool _running;
        Action<byte[], TimeSpan, TimeSpan> _sink;
        TimeSpan _origin;
        long _written;
        float[] _mix = new float[0];

        public AudioCapture(bool system, bool microphone)
        {
            _mixer.ReadFully = true;
            var devices = new MMDeviceEnumerator();
            if (system)
            {
                try
                {
                    MMDevice speaker = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    _keepAlive = new WasapiOut(speaker, AudioClientShareMode.Shared, true, 100);
                    _keepAlive.Init(new SilenceProvider(speaker.AudioClient.MixFormat));
                    Add(new WasapiLoopbackCapture(speaker));
                }
                catch
                {
                    _missing.Add("系统声音");
                }
            }

            if (microphone)
            {
                try
                {
                    Add(new WasapiCapture(devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)));
                }
                catch
                {
                    _missing.Add("麦克风");
                }
            }
        }

        public bool HasSource
        {
            get { return _inputs.Count > 0; }
        }

        public string Missing
        {
            get { return _missing.Count == 0 ? null : string.Join("、", _missing); }
        }

        void Add(IWaveIn input)
        {
            var buffer = new BufferedWaveProvider(input.WaveFormat);
            buffer.BufferDuration = TimeSpan.FromSeconds(3);
            buffer.DiscardOnBufferOverflow = true;
            buffer.ReadFully = true;
            input.DataAvailable += (s, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            ISampleProvider samples = new StereoSampleProvider(buffer.ToSampleProvider());
            if (samples.WaveFormat.SampleRate != Rate)
            {
                samples = new WdlResamplingSampleProvider(samples, Rate);
            }

            _mixer.AddMixerInput(samples);
            _inputs.Add(input);
        }

        public void Start(TimeSpan origin, Action<byte[], TimeSpan, TimeSpan> sink)
        {
            _origin = origin;
            _sink = sink;
            if (_keepAlive != null)
            {
                _keepAlive.Play();
            }

            for (int i = 0; i < _inputs.Count; i++)
            {
                _inputs[i].StartRecording();
            }

            _running = true;
            _pump = new Thread(Pump);
            _pump.IsBackground = true;
            _pump.Priority = ThreadPriority.AboveNormal;
            _pump.Name = "Jietu audio";
            _pump.Start();
        }

        public void Stop(TimeSpan until)
        {
            if (_pump == null)
            {
                return;
            }

            _running = false;
            _pump.Join();
            _pump = null;
            Emit(until);
        }

        void Pump()
        {
            while (_running)
            {
                Thread.Sleep(10);
                Emit(ScreenRecorder.Now() - Lag);
            }
        }

        void Emit(TimeSpan until)
        {
            long target = (long)((until - _origin).TotalSeconds * Rate);
            while (_written < target)
            {
                int frames = (int)Math.Min(target - _written, Rate / 5);
                int count = frames * Channels;
                if (_mix.Length < count)
                {
                    _mix = new float[count];
                }

                _mixer.Read(_mix, 0, count);
                var pcm = new byte[count * 2];
                for (int i = 0; i < count; i++)
                {
                    float value = _mix[i];
                    if (value > 1f) value = 1f;
                    else if (value < -1f) value = -1f;
                    short sample = (short)(value * 32767f);
                    pcm[i * 2] = (byte)sample;
                    pcm[i * 2 + 1] = (byte)(sample >> 8);
                }

                _sink(pcm, _origin + Span(_written), Span(frames));
                _written += frames;
            }
        }

        static TimeSpan Span(long frames)
        {
            return TimeSpan.FromTicks(frames * TimeSpan.TicksPerSecond / Rate);
        }

        public void Dispose()
        {
            _running = false;
            if (_pump != null)
            {
                _pump.Join();
                _pump = null;
            }

            for (int i = 0; i < _inputs.Count; i++)
            {
                try
                {
                    _inputs[i].StopRecording();
                }
                catch
                {
                }

                _inputs[i].Dispose();
            }

            _inputs.Clear();
            if (_keepAlive != null)
            {
                try
                {
                    _keepAlive.Stop();
                }
                catch
                {
                }

                _keepAlive.Dispose();
                _keepAlive = null;
            }
        }

        sealed class StereoSampleProvider : ISampleProvider
        {
            readonly ISampleProvider _source;
            readonly int _channels;
            float[] _buffer = new float[0];

            public StereoSampleProvider(ISampleProvider source)
            {
                _source = source;
                _channels = source.WaveFormat.Channels;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
            }

            public WaveFormat WaveFormat { get; private set; }

            public int Read(float[] buffer, int offset, int count)
            {
                int frames = count / 2;
                int need = frames * _channels;
                if (_buffer.Length < need)
                {
                    _buffer = new float[need];
                }

                int got = _source.Read(_buffer, 0, need) / _channels;
                for (int i = 0; i < got; i++)
                {
                    int at = i * _channels;
                    float left;
                    float right;
                    if (_channels == 1)
                    {
                        left = right = _buffer[at];
                    }
                    else
                    {
                        left = _buffer[at];
                        right = _buffer[at + 1];
                        if (_channels >= 3)
                        {
                            left += 0.707f * _buffer[at + 2];
                            right += 0.707f * _buffer[at + 2];
                        }

                        if (_channels >= 6)
                        {
                            left += 0.707f * _buffer[at + 4];
                            right += 0.707f * _buffer[at + 5];
                        }
                    }

                    buffer[offset + i * 2] = left;
                    buffer[offset + i * 2 + 1] = right;
                }

                return got * 2;
            }
        }
    }
}
#endif
