using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage.Streams;
using WinRT;

namespace Jietu
{
    sealed class ScreenRecorder
    {
        const int MaxFps = 60;
        const int QueueLimit = 120;
        static readonly TimeSpan Period = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / MaxFps);

        static readonly Guid IID_IDXGIDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        static readonly Guid IID_IDXGISurface = new Guid("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");
        static readonly Guid IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        static readonly Guid IID_ID3D10Multithread = new Guid("9b7e4e00-342c-4106-a19f-4f2704f689f0");
        static readonly Guid IID_DxgiInterfaceAccess = new Guid("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");
        static readonly Guid IID_IGraphicsCaptureItemInterop = new Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
        static readonly Guid IID_IGraphicsCaptureItem = new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
        static readonly Guid IID_IGraphicsCaptureSession3 = new Guid("f2cdd966-22ae-5ea1-9596-3a289344c3be");

        sealed class Sample
        {
            public IDirect3DSurface Surface;
            public TimeSpan Time;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct TextureDesc
        {
            public uint Width;
            public uint Height;
            public uint MipLevels;
            public uint ArraySize;
            public uint Format;
            public uint SampleCount;
            public uint SampleQuality;
            public uint Usage;
            public uint BindFlags;
            public uint CpuAccessFlags;
            public uint MiscFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Box
        {
            public uint Left;
            public uint Top;
            public uint Front;
            public uint Right;
            public uint Bottom;
            public uint Back;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public int Flags;
        }

        [DllImport("d3d11.dll")]
        static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels, uint levels, uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr context);

        [DllImport("d3d11.dll")]
        static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        [DllImport("d3d11.dll")]
        static extern int CreateDirect3D11SurfaceFromDXGISurface(IntPtr dxgiSurface, out IntPtr graphicsSurface);

        [DllImport("combase.dll")]
        static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);

        [DllImport("combase.dll", CharSet = CharSet.Unicode)]
        static extern int WindowsCreateString(string text, int length, out IntPtr hstring);

        [DllImport("combase.dll")]
        static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        readonly object _gate = new object();
        readonly BlockingCollection<Sample> _queue = new BlockingCollection<Sample>(new ConcurrentQueue<Sample>());
        readonly string _path;
        Rectangle _region;
        Rectangle _monitor;
        int _width;
        int _height;
        IntPtr _device;
        IntPtr _context;
        IntPtr _lastTexture;
        IDirect3DDevice _graphicsDevice;
        GraphicsCaptureItem _item;
        Direct3D11CaptureFramePool _pool;
        GraphicsCaptureSession _session;
        FileStream _file;
        Task _encoding;
        Sample _pending;
        TimeSpan _nextDue;
        bool _hasFrame;
        bool _stopped;

        ScreenRecorder(string path)
        {
            _path = path;
        }

        public string Path
        {
            get { return _path; }
        }

        public Size FrameSize
        {
            get { return new Size(_width, _height); }
        }

        public static Rectangle Fit(Rectangle region)
        {
            Rectangle monitor = MonitorOf(region);
            region.Intersect(monitor);
            region.Width &= ~1;
            region.Height &= ~1;
            return region;
        }

        public static async Task<ScreenRecorder> StartAsync(Rectangle region, string path)
        {
            if (!GraphicsCaptureSession.IsSupported())
            {
                throw new NotSupportedException("当前系统不支持录屏，需要 Windows 10 2004 或更高版本。");
            }

            var recorder = new ScreenRecorder(path);
            try
            {
                await recorder.Open(region);
                return recorder;
            }
            catch
            {
                recorder.Release();
                try
                {
                    File.Delete(path);
                }
                catch
                {
                }

                throw;
            }
        }

        async Task Open(Rectangle region)
        {
            _monitor = MonitorOf(region);
            _region = Fit(region);
            _width = _region.Width;
            _height = _region.Height;
            if (_width < 32 || _height < 32)
            {
                throw new InvalidOperationException("录屏区域太小。");
            }

            CreateDevice();
            _item = CreateItem(_region);
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_graphicsDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, _item.Size);
            _pool.FrameArrived += OnFrameArrived;
            _session = _pool.CreateCaptureSession(_item);
            _session.IsCursorCaptureEnabled = true;
            HideCaptureBorder(_session);
            _session.StartCapture();

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path));
            _file = new FileStream(_path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            IRandomAccessStream stream = _file.AsRandomAccessStream();
            var transcoder = new MediaTranscoder();
            transcoder.HardwareAccelerationEnabled = true;

            bool hevcFirst = _width > 4096 || _height > 4096;
            PrepareTranscodeResult prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(CreateSource(), stream, CreateProfile(hevcFirst));
            if (!prepared.CanTranscode)
            {
                _file.SetLength(0);
                prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(CreateSource(), stream, CreateProfile(!hevcFirst));
            }

            if (!prepared.CanTranscode)
            {
                throw new InvalidOperationException("系统视频编码器不可用：" + prepared.FailureReason);
            }

            _encoding = prepared.TranscodeAsync().AsTask();
        }

        public async Task StopAsync()
        {
            lock (_gate)
            {
                if (_stopped)
                {
                    return;
                }

                _stopped = true;
                AppendTail();
            }

            _queue.CompleteAdding();
            try
            {
                CloseCapture();
                if (_encoding != null)
                {
                    await _encoding;
                }
            }
            finally
            {
                Release();
            }

            if (!_hasFrame)
            {
                try
                {
                    File.Delete(_path);
                }
                catch
                {
                }

                throw new InvalidOperationException("没有录到画面。");
            }
        }

        MediaStreamSource CreateSource()
        {
            var input = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)_width, (uint)_height);
            var source = new MediaStreamSource(new VideoStreamDescriptor(input));
            source.BufferTime = TimeSpan.Zero;
            source.Starting += OnStarting;
            source.SampleRequested += OnSampleRequested;
            return source;
        }

        MediaEncodingProfile CreateProfile(bool hevc)
        {
            var profile = new MediaEncodingProfile();
            profile.Container = new ContainerEncodingProperties();
            profile.Container.Subtype = MediaEncodingSubtypes.Mpeg4;
            var video = new VideoEncodingProperties();
            video.Subtype = hevc ? MediaEncodingSubtypes.Hevc : MediaEncodingSubtypes.H264;
            if (!hevc)
            {
                video.ProfileId = H264ProfileIds.High;
            }

            video.Width = (uint)_width;
            video.Height = (uint)_height;
            video.Bitrate = Bitrate(_width, _height, hevc);
            video.FrameRate.Numerator = MaxFps;
            video.FrameRate.Denominator = 1;
            video.PixelAspectRatio.Numerator = 1;
            video.PixelAspectRatio.Denominator = 1;
            profile.Video = video;
            profile.Audio = null;
            return profile;
        }

        static uint Bitrate(int width, int height, bool hevc)
        {
            double bits = (double)width * height * MaxFps * (hevc ? 0.18 : 0.25);
            bits = Math.Max(bits, 12000000);
            bits = Math.Min(bits, 200000000);
            return (uint)bits;
        }

        void OnStarting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args)
        {
            if (_pending == null)
            {
                Sample first;
                if (_queue.TryTake(out first, -1))
                {
                    _pending = first;
                }
            }

            if (_pending != null)
            {
                args.Request.SetActualStartPosition(_pending.Time);
            }
        }

        void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
        {
            Sample next = _pending;
            _pending = null;
            if (next == null && !_queue.TryTake(out next, -1))
            {
                args.Request.Sample = null;
                return;
            }

            try
            {
                args.Request.Sample = MediaStreamSample.CreateFromDirect3D11Surface(next.Surface, next.Time);
            }
            finally
            {
                next.Surface.Dispose();
            }
        }

        void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            using (Direct3D11CaptureFrame frame = sender.TryGetNextFrame())
            {
                if (frame == null)
                {
                    return;
                }

                lock (_gate)
                {
                    if (_stopped)
                    {
                        return;
                    }

                    TimeSpan time = frame.SystemRelativeTime;
                    if (_hasFrame && time < _nextDue - TimeSpan.FromTicks(Period.Ticks / 4))
                    {
                        return;
                    }

                    if (_queue.Count >= QueueLimit)
                    {
                        return;
                    }

                    IntPtr source = TextureOf(frame.Surface);
                    try
                    {
                        IntPtr copy = CopyRegion(source, frame.ContentSize.Width, frame.ContentSize.Height);
                        if (copy == IntPtr.Zero)
                        {
                            return;
                        }

                        Enqueue(copy, time);
                    }
                    finally
                    {
                        Marshal.Release(source);
                    }

                    _nextDue = _hasFrame && time - _nextDue < Period ? _nextDue + Period : time + Period;
                    _hasFrame = true;
                }
            }
        }

        unsafe void AppendTail()
        {
            if (!_hasFrame || _lastTexture == IntPtr.Zero)
            {
                return;
            }

            TimeSpan now = TimeSpan.FromTicks((long)(Stopwatch.GetTimestamp() * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)));
            if (now <= _nextDue - Period)
            {
                return;
            }

            IntPtr copy = NewTexture();
            ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)Slot(_context, 47))(_context, copy, _lastTexture);
            Enqueue(copy, now);
        }

        void Enqueue(IntPtr texture, TimeSpan time)
        {
            try
            {
                IDirect3DSurface surface = SurfaceOf(texture);
                if (_lastTexture != IntPtr.Zero)
                {
                    Marshal.Release(_lastTexture);
                }

                Marshal.AddRef(texture);
                _lastTexture = texture;
                _queue.Add(new Sample { Surface = surface, Time = time });
            }
            finally
            {
                Marshal.Release(texture);
            }
        }

        unsafe IntPtr CopyRegion(IntPtr source, int sourceWidth, int sourceHeight)
        {
            int left = _region.X - _monitor.X;
            int top = _region.Y - _monitor.Y;
            int right = Math.Min(left + _width, sourceWidth);
            int bottom = Math.Min(top + _height, sourceHeight);
            if (left < 0 || top < 0 || right <= left || bottom <= top)
            {
                return IntPtr.Zero;
            }

            IntPtr target = NewTexture();
            var box = new Box { Left = (uint)left, Top = (uint)top, Front = 0, Right = (uint)right, Bottom = (uint)bottom, Back = 1 };
            ((delegate* unmanaged<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, Box*, void>)Slot(_context, 46))(_context, target, 0, 0, 0, 0, source, 0, &box);
            return target;
        }

        unsafe IntPtr NewTexture()
        {
            var desc = new TextureDesc
            {
                Width = (uint)_width,
                Height = (uint)_height,
                MipLevels = 1,
                ArraySize = 1,
                Format = 87,
                SampleCount = 1,
                Usage = 0,
                BindFlags = 0x8 | 0x20
            };
            IntPtr texture;
            Check(((delegate* unmanaged<IntPtr, TextureDesc*, IntPtr, IntPtr*, int>)Slot(_device, 5))(_device, &desc, IntPtr.Zero, &texture));
            return texture;
        }

        unsafe void CreateDevice()
        {
            int level;
            int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out _device, out level, out _context);
            if (hr < 0)
            {
                hr = D3D11CreateDevice(IntPtr.Zero, 5, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out _device, out level, out _context);
            }

            Check(hr);
            IntPtr guard = Query(_context, IID_ID3D10Multithread);
            try
            {
                ((delegate* unmanaged<IntPtr, int, int>)Slot(guard, 5))(guard, 1);
            }
            finally
            {
                Marshal.Release(guard);
            }

            IntPtr dxgi = Query(_device, IID_IDXGIDevice);
            try
            {
                IntPtr inspectable;
                Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
                try
                {
                    _graphicsDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                }
                finally
                {
                    Marshal.Release(inspectable);
                }
            }
            finally
            {
                Marshal.Release(dxgi);
            }
        }

        unsafe static GraphicsCaptureItem CreateItem(Rectangle region)
        {
            var rect = new Rect { Left = region.Left, Top = region.Top, Right = region.Right, Bottom = region.Bottom };
            IntPtr monitor = MonitorFromRect(ref rect, 2);
            const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
            IntPtr hstring;
            Check(WindowsCreateString(name, name.Length, out hstring));
            IntPtr factory;
            try
            {
                Guid iid = IID_IGraphicsCaptureItemInterop;
                Check(RoGetActivationFactory(hstring, ref iid, out factory));
            }
            finally
            {
                WindowsDeleteString(hstring);
            }

            try
            {
                Guid itemIid = IID_IGraphicsCaptureItem;
                IntPtr raw;
                Check(((delegate* unmanaged<IntPtr, IntPtr, Guid*, IntPtr*, int>)Slot(factory, 4))(factory, monitor, &itemIid, &raw));
                try
                {
                    return GraphicsCaptureItem.FromAbi(raw);
                }
                finally
                {
                    Marshal.Release(raw);
                }
            }
            finally
            {
                Marshal.Release(factory);
            }
        }

        static Rectangle MonitorOf(Rectangle region)
        {
            var rect = new Rect { Left = region.Left, Top = region.Top, Right = region.Right, Bottom = region.Bottom };
            var info = new MonitorInfo();
            info.Size = Marshal.SizeOf(typeof(MonitorInfo));
            if (!GetMonitorInfo(MonitorFromRect(ref rect, 2), ref info))
            {
                return System.Windows.Forms.Screen.FromRectangle(region).Bounds;
            }

            return Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
        }

        unsafe static void HideCaptureBorder(GraphicsCaptureSession session)
        {
            try
            {
                IntPtr self = ((IWinRTObject)session).NativeObject.ThisPtr;
                Guid iid = IID_IGraphicsCaptureSession3;
                IntPtr session3;
                if (Marshal.QueryInterface(self, in iid, out session3) == 0)
                {
                    ((delegate* unmanaged<IntPtr, byte, int>)Slot(session3, 7))(session3, 0);
                    Marshal.Release(session3);
                }
            }
            catch
            {
            }
        }

        unsafe static IntPtr TextureOf(IDirect3DSurface surface)
        {
            IntPtr unknown = MarshalInterface<IDirect3DSurface>.FromManaged(surface);
            try
            {
                IntPtr access = Query(unknown, IID_DxgiInterfaceAccess);
                try
                {
                    Guid iid = IID_ID3D11Texture2D;
                    IntPtr texture;
                    Check(((delegate* unmanaged<IntPtr, Guid*, IntPtr*, int>)Slot(access, 3))(access, &iid, &texture));
                    return texture;
                }
                finally
                {
                    Marshal.Release(access);
                }
            }
            finally
            {
                Marshal.Release(unknown);
            }
        }

        static IDirect3DSurface SurfaceOf(IntPtr texture)
        {
            IntPtr dxgi = Query(texture, IID_IDXGISurface);
            try
            {
                IntPtr inspectable;
                Check(CreateDirect3D11SurfaceFromDXGISurface(dxgi, out inspectable));
                try
                {
                    return MarshalInterface<IDirect3DSurface>.FromAbi(inspectable);
                }
                finally
                {
                    Marshal.Release(inspectable);
                }
            }
            finally
            {
                Marshal.Release(dxgi);
            }
        }

        void CloseCapture()
        {
            GraphicsCaptureSession session = _session;
            Direct3D11CaptureFramePool pool = _pool;
            _session = null;
            _pool = null;
            if (session != null)
            {
                session.Dispose();
            }

            if (pool != null)
            {
                pool.Dispose();
            }
        }

        void Release()
        {
            lock (_gate)
            {
                _stopped = true;
            }

            CloseCapture();
            lock (_gate)
            {
                Sample left;
                while (_queue.TryTake(out left))
                {
                    left.Surface.Dispose();
                }

                if (_pending != null)
                {
                    _pending.Surface.Dispose();
                    _pending = null;
                }

                if (_file != null)
                {
                    _file.Dispose();
                    _file = null;
                }

                if (_lastTexture != IntPtr.Zero)
                {
                    Marshal.Release(_lastTexture);
                    _lastTexture = IntPtr.Zero;
                }

                if (_graphicsDevice != null)
                {
                    _graphicsDevice.Dispose();
                    _graphicsDevice = null;
                }

                if (_context != IntPtr.Zero)
                {
                    Marshal.Release(_context);
                    _context = IntPtr.Zero;
                }

                if (_device != IntPtr.Zero)
                {
                    Marshal.Release(_device);
                    _device = IntPtr.Zero;
                }
            }
        }

        static IntPtr Query(IntPtr unknown, Guid iid)
        {
            IntPtr result;
            Check(Marshal.QueryInterface(unknown, in iid, out result));
            return result;
        }

        unsafe static IntPtr Slot(IntPtr com, int index)
        {
            return (*(IntPtr**)com)[index];
        }

        static void Check(int hr)
        {
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }
        }
    }
}
