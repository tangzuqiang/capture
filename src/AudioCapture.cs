using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Shiying
{
    sealed class AudioCapture : IDisposable
    {
        public const int Channels = 2;
        static readonly TimeSpan Lag = TimeSpan.FromMilliseconds(120);

        readonly List<AudioSource> _sources = new List<AudioSource>();
        readonly List<string> _missing = new List<string>();
        readonly int _rate = 48000;
        Thread _pump;
        volatile bool _running;
        Action<byte[], TimeSpan, TimeSpan> _sink;
        TimeSpan _origin;
        long _written;
        float[] _mix = new float[0];

        public AudioCapture(bool system, bool microphone)
        {
            if (system)
            {
                AudioSource speaker = AudioSource.Open(true);
                if (speaker != null)
                {
                    _sources.Add(speaker);
                    if (speaker.DeviceRate == 44100)
                    {
                        _rate = 44100;
                    }
                }
                else
                {
                    _missing.Add("系统声音");
                }
            }

            if (microphone)
            {
                AudioSource mic = AudioSource.Open(false);
                if (mic != null)
                {
                    _sources.Add(mic);
                }
                else
                {
                    _missing.Add("麦克风");
                }
            }
        }

        public int Rate
        {
            get { return _rate; }
        }

        public bool HasSource
        {
            get { return _sources.Count > 0; }
        }

        public string Missing
        {
            get { return _missing.Count == 0 ? null : string.Join("、", _missing.ToArray()); }
        }

        public void Start(TimeSpan origin, Action<byte[], TimeSpan, TimeSpan> sink)
        {
            _origin = origin;
            _sink = sink;
            for (int i = 0; i < _sources.Count; i++)
            {
                _sources[i].Start(_rate);
            }

            _running = true;
            _pump = new Thread(Pump);
            _pump.IsBackground = true;
            _pump.Priority = ThreadPriority.AboveNormal;
            _pump.Name = "Shiying audio mix";
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
            long target = (long)((until - _origin).TotalSeconds * _rate);
            while (_written < target)
            {
                int frames = (int)Math.Min(target - _written, _rate / 5);
                int count = frames * Channels;
                if (_mix.Length < count)
                {
                    _mix = new float[count];
                }

                Array.Clear(_mix, 0, count);
                for (int i = 0; i < _sources.Count; i++)
                {
                    _sources[i].MixInto(_mix, count);
                }

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

        TimeSpan Span(long frames)
        {
            return TimeSpan.FromTicks(frames * TimeSpan.TicksPerSecond / _rate);
        }

        public void Dispose()
        {
            _running = false;
            if (_pump != null)
            {
                _pump.Join();
                _pump = null;
            }

            for (int i = 0; i < _sources.Count; i++)
            {
                _sources[i].Dispose();
            }

            _sources.Clear();
        }
    }

    sealed unsafe class AudioSource : IDisposable
    {
        const int StreamLoopback = 0x00020000;
        const int BufferSilent = 0x2;
        const long BufferDuration = 10000000;

        static readonly Guid ClsidDeviceEnumerator = new Guid("bcde0395-e52f-467c-8e3d-c4579291692e");
        static readonly Guid IidAudioClient = new Guid("1cb9ad4c-dbfa-4c32-b178-c2f568a703b2");
        static readonly Guid IidCaptureClient = new Guid("c8adbd64-e71e-48a0-a4de-185c395cd317");
        static readonly Guid IidRenderClient = new Guid("f294acfc-3146-4483-a7bf-addca7c260e2");

        readonly bool _loopback;
        readonly ManualResetEvent _ready = new ManualResetEvent(false);
        readonly ManualResetEvent _go = new ManualResetEvent(false);
        readonly Ring _ring = new Ring();
        Thread _thread;
        volatile bool _running = true;
        Exception _failure;
        int _channels;
        int _bits;
        bool _float;
        int _blockAlign;
        int _outRate;

        AudioSource(bool loopback)
        {
            _loopback = loopback;
        }

        public int DeviceRate { get; private set; }

        public static AudioSource Open(bool loopback)
        {
            var source = new AudioSource(loopback);
            source._thread = new Thread(source.Run);
            source._thread.IsBackground = true;
            source._thread.Priority = ThreadPriority.Highest;
            source._thread.Name = loopback ? "Shiying speaker" : "Shiying microphone";
            source._thread.Start();
            if (!source._ready.WaitOne(5000) || source._failure != null)
            {
                source.Dispose();
                return null;
            }

            return source;
        }

        public void Start(int outRate)
        {
            _outRate = outRate;
            _go.Set();
        }

        public void MixInto(float[] mix, int count)
        {
            _ring.MixInto(mix, count);
        }

        void Run()
        {
            object enumeratorObject = null;
            IMMDevice device = null;
            IAudioClient client = null;
            IAudioCaptureClient capture = null;
            IAudioClient keepAlive = null;
            IAudioRenderClient render = null;
            IntPtr format = IntPtr.Zero;
            try
            {
                enumeratorObject = Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidDeviceEnumerator));
                var enumerator = (IMMDeviceEnumerator)enumeratorObject;
                enumerator.GetDefaultAudioEndpoint(_loopback ? 0 : 1, _loopback ? 1 : 2, out device);
                client = Activate(device);
                client.GetMixFormat(out format);
                ReadFormat(format);
                client.Initialize(0, _loopback ? StreamLoopback : 0, BufferDuration, 0, format, IntPtr.Zero);
                Guid captureId = IidCaptureClient;
                object service;
                client.GetService(ref captureId, out service);
                capture = (IAudioCaptureClient)service;

                if (_loopback)
                {
                    keepAlive = Activate(device);
                    keepAlive.Initialize(0, 0, BufferDuration / 5, 0, format, IntPtr.Zero);
                    Guid renderId = IidRenderClient;
                    keepAlive.GetService(ref renderId, out service);
                    render = (IAudioRenderClient)service;
                }
            }
            catch (Exception ex)
            {
                _failure = ex;
            }

            _ready.Set();
            try
            {
                if (_failure == null)
                {
                    _go.WaitOne();
                    if (_running)
                    {
                        Loop(client, capture, keepAlive, render);
                    }
                }
            }
            catch (Exception ex)
            {
                _failure = ex;
            }
            finally
            {
                if (format != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(format);
                }

                Free(render);
                Free(keepAlive);
                Free(capture);
                Free(client);
                Free(device);
                Free(enumeratorObject);
            }
        }

        void Loop(IAudioClient client, IAudioCaptureClient capture, IAudioClient keepAlive, IAudioRenderClient render)
        {
            uint renderSize = 0;
            if (keepAlive != null)
            {
                keepAlive.GetBufferSize(out renderSize);
                FeedSilence(keepAlive, render, renderSize);
                keepAlive.Start();
            }

            var resampler = new Resampler(DeviceRate, _outRate);
            float[] stereo = new float[0];
            client.Start();
            try
            {
                while (_running)
                {
                    Thread.Sleep(5);
                    if (keepAlive != null)
                    {
                        FeedSilence(keepAlive, render, renderSize);
                    }

                    uint packet;
                    capture.GetNextPacketSize(out packet);
                    while (packet > 0 && _running)
                    {
                        IntPtr data;
                        uint frames;
                        uint flags;
                        ulong devicePosition;
                        ulong qpcPosition;
                        capture.GetBuffer(out data, out frames, out flags, out devicePosition, out qpcPosition);
                        int count = (int)frames * 2;
                        if (stereo.Length < count)
                        {
                            stereo = new float[count];
                        }

                        if ((flags & BufferSilent) != 0 || data == IntPtr.Zero)
                        {
                            Array.Clear(stereo, 0, count);
                        }
                        else
                        {
                            ToStereo((byte*)data, (int)frames, stereo);
                        }

                        capture.ReleaseBuffer(frames);
                        resampler.Push(stereo, (int)frames, _ring);
                        capture.GetNextPacketSize(out packet);
                    }
                }
            }
            finally
            {
                client.Stop();
                if (keepAlive != null)
                {
                    keepAlive.Stop();
                }
            }
        }

        static void FeedSilence(IAudioClient client, IAudioRenderClient render, uint size)
        {
            uint padding;
            client.GetCurrentPadding(out padding);
            uint free = size - padding;
            if (free == 0)
            {
                return;
            }

            IntPtr data;
            render.GetBuffer(free, out data);
            render.ReleaseBuffer(free, BufferSilent);
        }

        void ReadFormat(IntPtr format)
        {
            int tag = (ushort)Marshal.ReadInt16(format, 0);
            _channels = Marshal.ReadInt16(format, 2);
            DeviceRate = Marshal.ReadInt32(format, 4);
            _blockAlign = Marshal.ReadInt16(format, 12);
            _bits = Marshal.ReadInt16(format, 14);
            if (tag == 0xFFFE)
            {
                tag = Marshal.ReadInt32(format, 24);
            }

            _float = tag == 3;
            if (tag != 1 && tag != 3)
            {
                throw new NotSupportedException("音频格式不支持");
            }
        }

        void ToStereo(byte* data, int frames, float[] stereo)
        {
            int size = _bits / 8;
            for (int i = 0; i < frames; i++)
            {
                byte* frame = data + i * _blockAlign;
                float left = Sample(frame, 0, size);
                float right = _channels > 1 ? Sample(frame, 1, size) : left;
                if (_channels >= 3)
                {
                    float center = 0.707f * Sample(frame, 2, size);
                    left += center;
                    right += center;
                }

                if (_channels >= 6)
                {
                    left += 0.707f * Sample(frame, 4, size);
                    right += 0.707f * Sample(frame, 5, size);
                }

                stereo[i * 2] = left;
                stereo[i * 2 + 1] = right;
            }
        }

        float Sample(byte* frame, int channel, int size)
        {
            byte* p = frame + channel * size;
            if (_float)
            {
                return size == 8 ? (float)*(double*)p : *(float*)p;
            }

            switch (size)
            {
                case 2:
                    return *(short*)p / 32768f;
                case 3:
                    return ((p[0] << 8) | (p[1] << 16) | (p[2] << 24)) / 2147483648f;
                case 4:
                    return *(int*)p / 2147483648f;
                default:
                    return (p[0] - 128) / 128f;
            }
        }

        static IAudioClient Activate(IMMDevice device)
        {
            Guid id = IidAudioClient;
            object client;
            device.Activate(ref id, 1, IntPtr.Zero, out client);
            return (IAudioClient)client;
        }

        static void Free(object com)
        {
            if (com != null && Marshal.IsComObject(com))
            {
                Marshal.ReleaseComObject(com);
            }
        }

        public void Dispose()
        {
            _running = false;
            _go.Set();
            if (_thread != null)
            {
                _thread.Join(3000);
                _thread = null;
            }
        }

        sealed class Resampler
        {
            readonly double _step;
            readonly bool _bypass;
            float[] _buffer = new float[2 * 64];
            float[] _output = new float[0];
            int _count = 1;
            double _position = 1;

            public Resampler(int inRate, int outRate)
            {
                _bypass = inRate == outRate;
                _step = inRate / (double)outRate;
            }

            public void Push(float[] input, int frames, Ring ring)
            {
                if (_bypass)
                {
                    ring.Write(input, frames * 2);
                    return;
                }

                if (_buffer.Length < (_count + frames) * 2)
                {
                    Array.Resize(ref _buffer, (_count + frames) * 4);
                }

                Array.Copy(input, 0, _buffer, _count * 2, frames * 2);
                _count += frames;
                int most = (int)((_count - _position) / _step) + 2;
                if (_output.Length < most * 2)
                {
                    _output = new float[most * 2];
                }

                int written = 0;
                while (_position + 2 < _count)
                {
                    int i = (int)_position;
                    float t = (float)(_position - i);
                    for (int c = 0; c < 2; c++)
                    {
                        float y0 = _buffer[(i - 1) * 2 + c];
                        float y1 = _buffer[i * 2 + c];
                        float y2 = _buffer[(i + 1) * 2 + c];
                        float y3 = _buffer[(i + 2) * 2 + c];
                        _output[written * 2 + c] = y1 + 0.5f * t * (y2 - y0 + t * (2f * y0 - 5f * y1 + 4f * y2 - y3 + t * (3f * (y1 - y2) + y3 - y0)));
                    }

                    written++;
                    _position += _step;
                }

                ring.Write(_output, written * 2);
                int drop = (int)_position - 1;
                if (drop > 0)
                {
                    Array.Copy(_buffer, drop * 2, _buffer, 0, (_count - drop) * 2);
                    _count -= drop;
                    _position -= drop;
                }
            }
        }
    }

    sealed class Ring
    {
        readonly object _gate = new object();
        readonly float[] _data = new float[48000 * 2 * 3];
        int _read;
        int _count;

        public void Write(float[] source, int count)
        {
            lock (_gate)
            {
                int start = 0;
                if (count > _data.Length)
                {
                    start = count - _data.Length;
                    count = _data.Length;
                }

                int overflow = _count + count - _data.Length;
                if (overflow > 0)
                {
                    _read = (_read + overflow) % _data.Length;
                    _count -= overflow;
                }

                int write = (_read + _count) % _data.Length;
                for (int i = 0; i < count; i++)
                {
                    _data[write] = source[start + i];
                    write++;
                    if (write == _data.Length)
                    {
                        write = 0;
                    }
                }

                _count += count;
            }
        }

        public void MixInto(float[] mix, int count)
        {
            lock (_gate)
            {
                int n = Math.Min(count, _count);
                for (int i = 0; i < n; i++)
                {
                    mix[i] += _data[_read];
                    _read++;
                    if (_read == _data.Length)
                    {
                        _read = 0;
                    }
                }

                _count -= n;
            }
        }
    }

    [ComImport]
    [Guid("a95664d2-9614-4f35-a746-de8db63617e6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport]
    [Guid("d666063f-1587-4e43-81f1-b948e807363f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    [ComImport]
    [Guid("1cb9ad4c-dbfa-4c32-b178-c2f568a703b2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        void Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        void GetBufferSize(out uint frames);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out uint frames);
        void IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        void GetMixFormat(out IntPtr format);
        void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(IntPtr handle);
        void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport]
    [Guid("c8adbd64-e71e-48a0-a4de-185c395cd317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        void GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        void ReleaseBuffer(uint frames);
        void GetNextPacketSize(out uint frames);
    }

    [ComImport]
    [Guid("f294acfc-3146-4483-a7bf-addca7c260e2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioRenderClient
    {
        void GetBuffer(uint frames, out IntPtr data);
        void ReleaseBuffer(uint frames, int flags);
    }
}
