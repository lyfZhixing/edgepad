using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace EdgePad
{
    /// <summary>
    /// 在极短时间窗内吞掉鼠标左键事件，用于抑制系统“点按即点击”合成的那次点击。
    /// <para>
    /// 钩子装在**独立线程**上：低级鼠标钩子的回调是由“安装它的那个线程”执行的消息，
    /// 只要挂在那儿，全系统的每一次鼠标事件（包括移动）都要先排到那个线程。
    /// 以前它装在 WPF 界面线程上——界面每帧重绘几毫秒，全系统的鼠标就跟着卡几毫秒，
    /// 表现就是“正常操作明显发飘/卡顿”。现在那个线程只做一件事：等消息、判时间窗。
    /// </para>
    /// <para>
    /// 起线程/装钩子都不在调用方等待：实测第一次 SetWindowsHookEx 前后能卡住调用线程 1 秒以上，
    /// 而调用方是“收到触控板报文”的输入线程——那 1 秒就是一次肉眼可见的卡死。
    /// </para>
    /// </summary>
    public static class ClickSuppressor
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const uint WM_QUIT = 0x0012;

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public int mouseData;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public int time;
            public int ptX;
            public int ptY;
        }

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")]
        private static extern bool PeekMessageW(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessageW(ref MSG msg);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostThreadMessageW(int threadId, uint msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")]
        private static extern int GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string name);

        private static readonly object Gate = new object();

        private static HookProc _proc;          // 必须持有委托，否则会被 GC 回收
        private static volatile IntPtr _hook = IntPtr.Zero;
        private static Thread _thread;
        private static int _threadId;
        private static long _suppressUntilMs;
        private static int _swallowed;
        private static long _installMs = -1;
        private static long _startAt = -1;

        public static int Swallowed { get { return _swallowed; } }
        public static int HookThreadId { get { return _threadId; } }
        public static bool Installed { get { return _hook != IntPtr.Zero; } }
        /// <summary>钩子装好用了多久（毫秒，-1 = 还没装完）。</summary>
        public static long InstallMs { get { return _installMs; } }

        public static event Action<string> Log;

        /// <summary>起钩子线程（幂等、不阻塞）。启动时调一次，之后全系统鼠标事件都走那个空闲线程。</summary>
        public static void EnsureInstalled()
        {
            if (_hook != IntPtr.Zero) return;
            lock (Gate)
            {
                if (_thread != null && _thread.IsAlive) return;
                _startAt = Environment.TickCount64;
                _thread = new Thread(HookThread) { IsBackground = true, Name = "EdgePad.MouseHook" };
                _thread.Start();
            }
        }

        /// <summary>等钩子装好（只给自检用；正常路径不要等）。</summary>
        public static bool WaitInstalled(int ms)
        {
            EnsureInstalled();
            long until = Environment.TickCount64 + Math.Max(0, ms);
            while (Environment.TickCount64 < until)
            {
                if (_hook != IntPtr.Zero) return true;
                Thread.Sleep(5);
            }
            return _hook != IntPtr.Zero;
        }

        public static void Uninstall()
        {
            Thread t;
            int tid;
            lock (Gate) { t = _thread; tid = _threadId; }
            if (t == null) return;
            if (tid != 0) PostThreadMessageW(tid, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            t.Join(600);
            lock (Gate) { _thread = null; _threadId = 0; }
        }

        private static void HookThread()
        {
            long t0 = Environment.TickCount64;
            _threadId = GetCurrentThreadId();
            _proc = Callback;

            MSG peek;
            PeekMessageW(out peek, IntPtr.Zero, 0, 0, 0);   // 先把消息队列建出来

            _hook = SetWindowsHookExW(WH_MOUSE_LL, _proc, GetModuleHandleW(null), 0);
            _installMs = Environment.TickCount64 - _startAt;

            if (_hook == IntPtr.Zero)
            {
                Raise("鼠标钩子安装失败，错误码 " + Marshal.GetLastWin32Error() + "（边缘双击会带出系统点击）");
                return;
            }
            Raise("鼠标钩子已装在独立线程 " + _threadId + "，装上耗时 " + _installMs + "ms");

            MSG msg;
            while (GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }

            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        }

        /// <summary>在随后 ms 毫秒内吞掉左键按下/抬起（不阻塞，钩子没好就只是不吞）。</summary>
        public static void Arm(int ms)
        {
            EnsureInstalled();
            Volatile.Write(ref _suppressUntilMs, Environment.TickCount64 + Math.Max(0, ms));
        }

        public static void Disarm() { Volatile.Write(ref _suppressUntilMs, 0L); }

        private static void Raise(string s)
        {
            var h = Log;
            if (h != null) h(s);
        }

        private static IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if ((msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP || msg == WM_LBUTTONDBLCLK)
                    && Environment.TickCount64 <= Volatile.Read(ref _suppressUntilMs))
                {
                    _swallowed++;
                    return new IntPtr(1);   // 吞掉
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }
    }
}
