using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Jietu
{
    static class NativeMethods
    {
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_NOREPEAT = 0x4000;
        public const int VK_ESCAPE = 0x1B;
        public const int VK_SHIFT = 0x10;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint MOUSEEVENTF_WHEEL = 0x0800;
        public const uint MOUSEEVENTF_HWHEEL = 0x1000;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_MOUSEHWHEEL = 0x020E;
        public const int WM_VSCROLL = 0x0115;
        public const int WM_HSCROLL = 0x0114;
        public const int SB_LINEUP = 0;
        public const int SB_LINEDOWN = 1;
        public const int GA_ROOT = 2;
        public const int SW_SHOW = 5;

        static readonly IntPtr DpiPerMonitorV2 = new IntPtr(-4);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll")]
        public static extern bool AttachConsole(int dwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(Point point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, uint dx, uint dy, int dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        public const int WH_MOUSE_LL = 14;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
        public const int WS_EX_TRANSPARENT = 0x20;
        public const int WS_EX_TOOLWINDOW = 0x80;
        public const int WS_EX_TOPMOST = 0x8;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct MouseHookInfo
        {
            public Point Point;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BlendFunction
        {
            public byte Op;
            public byte Flags;
            public byte Alpha;
            public byte Format;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string name);

        [DllImport("user32.dll")]
        public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        [DllImport("user32.dll")]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDc, ref Point dst, ref Size size, IntPtr srcDc, ref Point src, int key, ref BlendFunction blend, int flags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr obj);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(Point point, uint flags);

        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        public static float ScaleAt(Point point)
        {
            uint dpiX, dpiY;
            try
            {
                if (GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out dpiX, out dpiY) == 0 && dpiX > 0)
                {
                    return dpiX / 96f;
                }
            }
            catch (DllNotFoundException)
            {
            }

            return 1f;
        }

        public static void ExcludeFromCapture(IntPtr hwnd)
        {
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptProtectData(ref DataBlob dataIn, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr handle);

        public static byte[] ProtectBytes(byte[] plain)
        {
            return RunDpapi(plain, true);
        }

        public static byte[] UnprotectBytes(byte[] sealedBytes)
        {
            return RunDpapi(sealedBytes, false);
        }

        static byte[] RunDpapi(byte[] input, bool protect)
        {
            if (input == null || input.Length == 0)
            {
                return null;
            }

            var dataIn = new DataBlob();
            var dataOut = new DataBlob();
            dataIn.Size = input.Length;
            dataIn.Data = Marshal.AllocHGlobal(input.Length);
            try
            {
                Marshal.Copy(input, 0, dataIn.Data, input.Length);
                bool ok = protect
                    ? CryptProtectData(ref dataIn, "Jietu", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref dataOut)
                    : CryptUnprotectData(ref dataIn, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref dataOut);
                if (!ok || dataOut.Data == IntPtr.Zero)
                {
                    return null;
                }

                var result = new byte[dataOut.Size];
                Marshal.Copy(dataOut.Data, result, 0, dataOut.Size);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(dataIn.Data);
                if (dataOut.Data != IntPtr.Zero)
                {
                    LocalFree(dataOut.Data);
                }
            }
        }

        public static void MakeDpiAware()
        {
            if (!SetProcessDpiAwarenessContext(DpiPerMonitorV2))
            {
                SetProcessDPIAware();
            }
        }

        public static bool IsEscapeDown()
        {
            return (GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0;
        }

        public static void ForceForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            IntPtr root = GetAncestor(hwnd, GA_ROOT);
            if (root == IntPtr.Zero)
            {
                root = hwnd;
            }

            IntPtr foreground = GetForegroundWindow();
            uint foregroundThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
            uint currentThread = GetCurrentThreadId();
            bool attached = false;
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attached = AttachThreadInput(currentThread, foregroundThread, true);
            }

            ShowWindow(root, SW_SHOW);
            SetForegroundWindow(root);
            if (attached)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        public static IntPtr MakeLParam(Point point)
        {
            unchecked
            {
                int x = (ushort)(short)point.X;
                int y = ((int)(short)point.Y) << 16;
                return (IntPtr)(y | x);
            }
        }

        public static IntPtr MakeWheelWParam(int delta)
        {
            unchecked
            {
                return (IntPtr)(((int)(short)delta) << 16);
            }
        }
    }
}
