using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace EdgePad
{
    /// <summary>
    /// 界面线程卡顿探针：后台线程每 20ms 给界面线程投一条消息，量它多久被处理。
    /// <para>
    /// 这个数字以前没人看得见：界面线程一卡，窗口不刷新、低级鼠标钩子的回调也排在后面，
    /// 全系统的鼠标都跟着一起卡。现在它会把 10 秒窗口内的最大值写到日志里。
    /// </para>
    /// </summary>
    public static class UiPing
    {
        public const int WM_PING = 0x0400 + 77;   // WM_APP + 77

        [DllImport("user32.dll")]
        private static extern bool PostMessageW(IntPtr hWnd, int msg, IntPtr w, IntPtr l);

        private static Thread _thread;
        private static IntPtr _hwnd;
        private static volatile int _pending;
        private static long _sentAt;
        private static long _windowAt;
        private static long _winMax;
        private static int _winOver20, _winOver50, _winCount;

        public static long LastMax { get { return _winMax; } }
        public static event Action<string> Log;

        /// <summary>界面线程此刻在干什么（只在卡顿时才写进日志，用来定位是谁卡住的）。</summary>
        public static string Busy = "";

        public static void Enter(string tag) { Busy = tag; }
        public static void Exit() { Busy = ""; }

        public static void Start(IntPtr hwnd)
        {
            _hwnd = hwnd;
            if (_thread != null) return;
            _windowAt = Environment.TickCount64;
            _thread = new Thread(Loop) { IsBackground = true, Name = "EdgePad.UiPing" };
            _thread.Start();
        }

        /// <summary>界面线程处理到探针消息时调用，返回这次投递等了多久。</summary>
        public static void OnPingHandled()
        {
            _pending = 0;
            long d = Environment.TickCount64 - Interlocked.Read(ref _sentAt);
            _winCount++;
            if (d > _winMax) _winMax = d;
            if (d > 20) _winOver20++;
            if (d > 50) _winOver50++;

            // 单次大卡顿立刻记一笔，带上“当时界面在干什么”
            if (d >= 50)
            {
                var h0 = Log;
                if (h0 != null) h0("界面卡顿 " + d + "ms，当时在：" + (Busy.Length == 0 ? "空闲（等消息）" : Busy));
            }

            long now = Environment.TickCount64;
            if (now - _windowAt >= 10000)
            {
                // 只在真的卡了才写日志，免得刷屏
                if (_winMax >= 20)
                {
                    var h = Log;
                    if (h != null)
                        h("界面卡顿：10 秒内最久等待 " + _winMax + "ms，超 20ms " + _winOver20
                          + " 次，超 50ms " + _winOver50 + " 次（采样 " + _winCount + "）");
                }
                _windowAt = now;
                _winMax = 0;
                _winOver20 = 0;
                _winOver50 = 0;
                _winCount = 0;
            }
        }

        private static void Loop()
        {
            while (true)
            {
                if (_pending == 0 && _hwnd != IntPtr.Zero)
                {
                    _pending = 1;
                    Interlocked.Exchange(ref _sentAt, Environment.TickCount64);
                    if (!PostMessageW(_hwnd, WM_PING, IntPtr.Zero, IntPtr.Zero)) _pending = 0;
                }
                Thread.Sleep(20);
            }
        }
    }
}
