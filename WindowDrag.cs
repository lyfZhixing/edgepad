using System;
using System.Runtime.InteropServices;
using System.Text;

namespace EdgePad
{
    public enum SnapZone { None, Left, Right, Maximize, TopLeft, TopRight, BottomLeft, BottomRight }

    /// <summary>拖动目标抽象，便于自检替换。</summary>
    public interface IWindowTarget
    {
        int ScreenWidth { get; }
        int ScreenHeight { get; }
        bool TryCapture(out IntPtr hwnd, out int x, out int y);
        void Move(IntPtr hwnd, int x, int y);
        SnapZone EvaluateSnap(IntPtr hwnd);
        void ApplySnap(IntPtr hwnd, SnapZone zone);
        bool TryCurrent(out IntPtr hwnd);
        bool CycleWindow(IntPtr hwnd);
        bool MaximizeWindow(IntPtr hwnd);
    }

    public sealed class SystemWindowTarget : IWindowTarget
    {
        private const uint GA_ROOT = 2;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        public int ScreenWidth { get { return Native.GetSystemMetrics(0); } }
        public int ScreenHeight { get { return Native.GetSystemMetrics(1); } }

        public bool TryCapture(out IntPtr hwnd, out int x, out int y)
        {
            hwnd = IntPtr.Zero;
            x = 0;
            y = 0;

            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return false;

            IntPtr h = Native.WindowFromPoint(p);
            if (h == IntPtr.Zero) return false;

            IntPtr root = Native.GetAncestor(h, GA_ROOT);
            if (root != IntPtr.Zero) h = root;
            if (h == IntPtr.Zero) return false;

            // 桌面/任务栏/开始菜单不是可拖动窗口
            if (IsSystemWindow(h)) return false;

            if (Native.IsZoomed(h)) Native.ShowWindow(h, 9);   // SW_RESTORE，最大化窗口先还原

            Native.RECT r;
            if (!Native.GetWindowRect(h, out r)) return false;

            hwnd = h;
            x = r.Left;
            y = r.Top;
            return true;
        }

        public void Move(IntPtr hwnd, int x, int y)
        {
            if (hwnd == IntPtr.Zero) return;
            Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private static bool WorkArea(IntPtr hwnd, out Native.RECT work)
        {
            work = new Native.RECT();
            IntPtr mon = Native.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (mon == IntPtr.Zero) return false;
            var mi = new Native.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
            if (!Native.GetMonitorInfoW(mon, ref mi)) return false;
            work = mi.rcWork;
            return true;
        }

        public SnapZone EvaluateSnap(IntPtr hwnd)
        {
            Native.RECT r, w;
            if (!Native.GetWindowRect(hwnd, out r)) return SnapZone.None;
            if (!WorkArea(hwnd, out w)) return SnapZone.None;

            int t = (int)(24.0 * Native.GetDpiForWindow(hwnd) / 96.0);
            if (t < 12) t = 12;

            bool left = r.Left <= w.Left + t;
            bool right = r.Right >= w.Right - t;
            bool top = r.Top <= w.Top + t;
            bool bottom = r.Bottom >= w.Bottom - t;

            if (top && left) return SnapZone.TopLeft;
            if (top && right) return SnapZone.TopRight;
            if (top) return SnapZone.Maximize;
            if (bottom && left) return SnapZone.BottomLeft;
            if (bottom && right) return SnapZone.BottomRight;
            if (left) return SnapZone.Left;
            if (right) return SnapZone.Right;
            return SnapZone.None;
        }

        public void ApplySnap(IntPtr hwnd, SnapZone zone)
        {
            if (zone == SnapZone.None) return;
            if (zone == SnapZone.Maximize) { Native.ShowWindow(hwnd, 3); return; }

            Native.RECT w;
            if (!WorkArea(hwnd, out w)) return;

            int halfW = (w.Right - w.Left) / 2;
            int halfH = (w.Bottom - w.Top) / 2;
            int x = w.Left, y = w.Top, cx = halfW, cy = w.Bottom - w.Top;

            switch (zone)
            {
                case SnapZone.Left: break;
                case SnapZone.Right: x = w.Left + halfW; break;
                case SnapZone.TopLeft: cy = halfH; break;
                case SnapZone.TopRight: x = w.Left + halfW; cy = halfH; break;
                case SnapZone.BottomLeft: y = w.Top + halfH; cy = halfH; break;
                case SnapZone.BottomRight: x = w.Left + halfW; y = w.Top + halfH; cy = halfH; break;
            }

            Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, cx, cy, SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public bool TryCurrent(out IntPtr hwnd)
        {
            hwnd = IntPtr.Zero;
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return false;
            IntPtr h = Native.WindowFromPoint(p);
            if (h == IntPtr.Zero) return false;
            IntPtr root = Native.GetAncestor(h, GA_ROOT);
            if (root != IntPtr.Zero) h = root;
            if (h == IntPtr.Zero) return false;
            if (IsSystemWindow(h)) return false;
            hwnd = h;
            return true;
        }

        /// <summary>最大化 → 还原；否则 → 最小化。</summary>
        public bool CycleWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            if (Native.IsZoomed(hwnd)) return Native.ShowWindow(hwnd, 9);    // SW_RESTORE
            return Native.ShowWindow(hwnd, 6);                               // SW_MINIMIZE
        }

        public bool MaximizeWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            return Native.ShowWindow(hwnd, 3);                               // SW_MAXIMIZE
        }

        private static bool IsSystemWindow(IntPtr h)
        {
            var cls = new StringBuilder(128);
            Native.GetClassNameW(h, cls, cls.Capacity);
            string n = cls.ToString();
            return n == "Progman" || n == "WorkerW" || n == "Shell_TrayWnd"
                || n == "Shell_SecondaryTrayWnd" || n == "Windows.UI.Core.CoreWindow";
        }

        public static string ZoneName(SnapZone z)
        {
            switch (z)
            {
                case SnapZone.Left: return "左半屏";
                case SnapZone.Right: return "右半屏";
                case SnapZone.Maximize: return "最大化";
                case SnapZone.TopLeft: return "左上 1/4";
                case SnapZone.TopRight: return "右上 1/4";
                case SnapZone.BottomLeft: return "左下 1/4";
                case SnapZone.BottomRight: return "右下 1/4";
                default: return "无";
            }
        }

        public static string TitleOf(IntPtr h)
        {
            var sb = new StringBuilder(256);
            Native.GetWindowTextW(h, sb, sb.Capacity);
            string t = sb.ToString();
            return string.IsNullOrEmpty(t) ? "(无标题)" : t;
        }
    }
}
