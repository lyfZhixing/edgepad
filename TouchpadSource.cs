using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace EdgePad
{
    public sealed class Contact
    {
        public int Id;
        public double X;
        public double Y;
    }

    public sealed class TouchpadSource
    {
        private sealed class Dev
        {
            public string Path;
            public IntPtr Pp;
            public int MaxX = 1800;
            public int MaxY = 1275;
            public int Fingers = 5;
        }

        private readonly Dictionary<string, Dev> _devs = new Dictionary<string, Dev>(StringComparer.OrdinalIgnoreCase);
        private int _badFrames;

        // 热路径复用缓冲：这些以前每帧新建一次
        private readonly List<Contact> _buf = new List<Contact>(8);
        private readonly Contact[] _pool = new Contact[8];
        private readonly ushort[] _usages = new ushort[16];
        private readonly Dictionary<IntPtr, string> _pathCache = new Dictionary<IntPtr, string>();
        private IntPtr _rawBuf;
        private int _rawCap;
        private byte[] _report;

        public TouchpadSource()
        {
            for (int i = 0; i < _pool.Length; i++) _pool[i] = new Contact();
        }

        public int MaxX { get; private set; } = 1800;
        public int MaxY { get; private set; } = 1275;
        public int DeviceCount => _devs.Count;
        public string DeviceLabel { get; private set; } = "未找到精准触摸板";

        public event Action<string> DeviceLog;

        private long _lastScanMs;

        public bool Initialize(out string message)
        {
            Rescan();
            if (_devs.Count == 0)
            {
                message = "没有找到精准触摸板（设备管理器里应出现 “HID-compliant touch pad”）";
                return false;
            }
            message = DeviceLabel + "，坐标范围 " + MaxX + " × " + MaxY;
            return true;
        }

        /// <summary>重新枚举 HID 设备，补上新出现/重连的精准触摸板。可反复调用。</summary>
        public void Rescan()
        {
            _pathCache.Clear();
            int maxX = 0, maxY = 0;
            var names = new List<string>();

            foreach (var path in Native.EnumHidPaths())
            {
                if (_devs.ContainsKey(path)) continue;
                var h = Native.CreateFileW(path, 0, 0x1 | 0x2, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (h.IsInvalid) continue;

                IntPtr pp = IntPtr.Zero;
                if (!Native.HidD_GetPreparsedData(h, out pp)) { h.Dispose(); continue; }

                Native.HIDP_CAPS caps;
                if (Native.HidP_GetCaps(pp, out caps) != Native.HIDP_STATUS_SUCCESS) { h.Dispose(); pp = IntPtr.Zero; continue; }
                h.Dispose();

                // 只要求是“精准触摸板”集合，不再假设 5 指/30 字节
                bool full = caps.UsagePage == 0x0D && caps.Usage == 0x05
                            && caps.InputReportByteLength >= 8
                            && caps.NumberInputValueCaps >= 3;
                if (!full) { continue; }

                var dev = new Dev { Path = path, Pp = pp };
                ReadRanges(pp, caps, dev);
                _devs[path] = dev;

                if (DeviceLog != null)
                    DeviceLog("新增触摸板 " + ShortPath(path) + " 量程 " + dev.MaxX + "×" + dev.MaxY + " 触点数=" + dev.Fingers);
            }

            foreach (var kv in _devs)
            {
                if (kv.Value.MaxX > maxX) maxX = kv.Value.MaxX;
                if (kv.Value.MaxY > maxY) maxY = kv.Value.MaxY;
            }

            if (_devs.Count > 0)
            {
                MaxX = maxX > 0 ? maxX : 1800;
                MaxY = maxY > 0 ? maxY : 1275;
            }
            DeviceLabel = _devs.Count + " 个精准触摸板集合";
        }

        private static string ShortPath(string path)
        {
            int i = path.IndexOf("hid#", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return path;
            string s = path.Substring(i + 4);
            int j = s.IndexOf("#{", StringComparison.Ordinal);
            if (j > 0) s = s.Substring(0, j);
            return s;
        }

        private static string TryVid(string path)
        {
            int i = path.IndexOf("vid_", StringComparison.OrdinalIgnoreCase);
            if (i < 0 || i + 8 > path.Length) return "????";
            return path.Substring(i + 4, 4).ToUpperInvariant();
        }

        private static void ReadRanges(IntPtr pp, Native.HIDP_CAPS caps, Dev dev)
        {
            ushort nv = caps.NumberInputValueCaps;
            if (nv == 0) return;
            byte[] buf = new byte[72 * nv];
            ushort got = nv;
            if (Native.HidP_GetValueCaps(0, buf, ref got, pp) != Native.HIDP_STATUS_SUCCESS) return;
            int fingers = 0;
            for (int k = 0; k < got; k++)
            {
                int o = k * 72;
                if (buf[o + 12] != 0) continue;              // isRange
                ushort up = BitConverter.ToUInt16(buf, o);
                ushort usage = BitConverter.ToUInt16(buf, o + 56);
                int lmax = BitConverter.ToInt32(buf, o + 44);
                if (up == 0x0001 && usage == 0x0030 && lmax > 0) dev.MaxX = lmax;
                if (up == 0x0001 && usage == 0x0031 && lmax > 0) dev.MaxY = lmax;
                if (up == 0x000D && usage == 0x0051) fingers++;   // Contact Identifier 每个触点一份
            }
            if (fingers > 0) dev.Fingers = Math.Min(10, fingers);
        }

        public bool Register(IntPtr hwnd)
        {
            // 0x0D/0x05 触控板；0x01/0x06 键盘（只用来判断“正在打字”，不读取按键内容）
            var rid = new Native.RAWINPUTDEVICE[2];
            rid[0].usUsagePage = 0x0D;
            rid[0].usUsage = 0x05;
            rid[0].dwFlags = 0x00000100;   // RIDEV_INPUTSINK
            rid[0].hwndTarget = hwnd;

            rid[1].usUsagePage = 0x01;
            rid[1].usUsage = 0x06;
            rid[1].dwFlags = 0x00000100;
            rid[1].hwndTarget = hwnd;

            return Native.RegisterRawInputDevices(rid, 2, (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE)));
        }

        /// <summary>
        /// 伪触点判定：坐标位全 0 或全 1 时，解出来的值正好是板的两个角
        /// （0 或量程上限），真手指不会同时贴着两条边。这种帧会凭空触发边缘手势。
        /// </summary>
        public static bool IsPhantom(uint x, uint y, int maxX, int maxY)
        {
            return (x == 0 || x >= maxX) && (y == 0 || y >= maxY);
        }

        public bool TryRead(IntPtr lParam, out List<Contact> contacts, out int contactCount, out int maxX, out int maxY, out bool hidReport)
        {
            contacts = null;
            contactCount = 0;
            maxX = MaxX;
            maxY = MaxY;
            hidReport = false;

            uint hdrSz = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTHEADER));
            uint size = 0;
            Native.GetRawInputData(lParam, 0x10000003, IntPtr.Zero, ref size, hdrSz);
            if (size == 0) return false;

            // 原生缓冲复用：每帧 AllocHGlobal/FreeHGlobal 都是 125 次/秒的堆分配
            if (_rawCap < (int)size)
            {
                if (_rawBuf != IntPtr.Zero) Marshal.FreeHGlobal(_rawBuf);
                _rawCap = (int)size + 64;
                _rawBuf = Marshal.AllocHGlobal(_rawCap);
            }
            IntPtr buf = _rawBuf;

            if (Native.GetRawInputData(lParam, 0x10000003, buf, ref size, hdrSz) != size) return false;
            var hdr = (Native.RAWINPUTHEADER)Marshal.PtrToStructure(buf, typeof(Native.RAWINPUTHEADER));
            if (hdr.dwType != 2) return false;
            hidReport = true;

            var hid = (Native.RAWHID)Marshal.PtrToStructure(new IntPtr(buf.ToInt64() + hdrSz), typeof(Native.RAWHID));
            int off = (int)hdrSz + Marshal.SizeOf(typeof(Native.RAWHID));
            int len = (int)hid.dwSizeHid;
            if (len <= 0) return false;

            if (_report == null || _report.Length < len) _report = new byte[len];
            byte[] data = _report;
            Marshal.Copy(new IntPtr(buf.ToInt64() + off), data, 0, len);

            string path = PathOfDevice(hdr.hDevice);
            if (path == null) return false;

            Dev dev;
            if (!_devs.TryGetValue(path, out dev))
            {
                // 报文来自一台没登记的设备：说明设备被重新枚举过，现场补扫描（限流 300ms）
                long nowMs = Environment.TickCount64;
                if (nowMs - _lastScanMs < 300) return false;
                _lastScanMs = nowMs;
                Rescan();
                if (!_devs.TryGetValue(path, out dev)) return false;
            }

            maxX = dev.MaxX;
            maxY = dev.MaxY;

            uint cc = 0;
            Native.HidP_GetUsageValue(0, 0x0D, 0, 0x54, out cc, dev.Pp, data, (uint)len);
            contactCount = (int)cc;

            // 触点列表与 Contact 对象也复用：一帧 1~5 个对象 × 125Hz 全是垃圾
            var list = _buf;
            list.Clear();
            int slot = 0;
            for (ushort lc = 1; lc <= dev.Fingers; lc++)
            {
                uint ulen = 16;
                bool tip = false;
                if (Native.HidP_GetUsages(0, 0x0D, lc, _usages, ref ulen, dev.Pp, data, (uint)len) == Native.HIDP_STATUS_SUCCESS)
                    for (uint i = 0; i < ulen; i++) if (_usages[i] == 0x42) tip = true;
                if (!tip) continue;

                uint cid = 0, x = 0, y = 0;
                Native.HidP_GetUsageValue(0, 0x0D, lc, 0x51, out cid, dev.Pp, data, (uint)len);
                Native.HidP_GetUsageValue(0, 0x01, lc, 0x30, out x, dev.Pp, data, (uint)len);
                Native.HidP_GetUsageValue(0, 0x01, lc, 0x31, out y, dev.Pp, data, (uint)len);

                // 伪帧：坐标位全 0 或全 1 时会解成“正好贴着板的两个角”，
                // 真手指几乎不可能同时撞在两条边上。这种触点会凭空触发边缘手势（实测会误触音量/静音）。
                if (IsPhantom(x, y, dev.MaxX, dev.MaxY))
                {
                    if (_badFrames < 20)
                    {
                        _badFrames++;
                        if (DeviceLog != null)
                            DeviceLog("丢弃伪触点 (" + x + "," + y + ") 槽位 " + lc + " 共 " + _badFrames + " 次");
                    }
                    continue;
                }

                var c = _pool[slot % _pool.Length];
                slot++;
                c.Id = (int)cid % 8;
                c.X = x;
                c.Y = y;
                list.Add(c);
            }

            contacts = list;
            return true;
        }

        /// <summary>设备路径查一次就缓存：GetRawInputDeviceInfoW 每次都要走注册表，125Hz 上不便宜。</summary>
        private string PathOfDevice(IntPtr h)
        {
            string p;
            if (_pathCache.TryGetValue(h, out p)) return p;
            p = PathOf(h);
            if (p == null) return null;
            if (_pathCache.Count > 16) _pathCache.Clear();
            _pathCache[h] = p;
            return p;
        }

        private static string PathOf(IntPtr h)
        {
            uint sz = 0;
            Native.GetRawInputDeviceInfoW(h, 0x20000007, null, ref sz);
            if (sz == 0) return null;
            var sb = new StringBuilder((int)sz + 2);
            uint s2 = sz;
            if (Native.GetRawInputDeviceInfoW(h, 0x20000007, sb, ref s2) <= 0) return null;
            return sb.ToString();
        }
    }
}
