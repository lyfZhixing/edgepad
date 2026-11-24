using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace EdgePad
{
    public static class Native
    {
        public const int HIDP_STATUS_SUCCESS = 0x00110000;

        public static readonly Guid GUID_DEVINTERFACE_HID = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SP_DEVICE_INTERFACE_DETAIL_DATA
        {
            public int cbSize; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)] public string DevicePath;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage; public ushort UsagePage;
            public ushort InputReportByteLength; public ushort OutputReportByteLength; public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps; public ushort NumberInputValueCaps; public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps; public ushort NumberOutputValueCaps; public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps; public ushort NumberFeatureValueCaps; public ushort NumberFeatureDataIndices;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr p, int f);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA d, ref SP_DEVICE_INTERFACE_DETAIL_DATA dd, int sz, out int need, IntPtr did);
        [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pp);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetAttributes(SafeFileHandle h, out HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr pp, out HIDP_CAPS caps);
        [DllImport("hid.dll")] public static extern int HidP_GetUsageValue(int t, ushort up, ushort lc, ushort u, out uint val, IntPtr pp, byte[] report, uint len);
        [DllImport("hid.dll", EntryPoint = "HidP_GetUsages")] public static extern int HidP_GetUsages(int t, ushort up, ushort lc, ushort[] list, ref uint len, IntPtr pp, byte[] report, uint len2);
        [DllImport("hid.dll")] public static extern int HidP_GetValueCaps(int t, byte[] caps, ref ushort len, IntPtr pp);

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE { public ushort usUsagePage; public ushort usUsage; public uint dwFlags; public IntPtr hwndTarget; }
        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTHEADER { public uint dwType; public uint dwSize; public IntPtr hDevice; public IntPtr wParam; }
        [StructLayout(LayoutKind.Sequential)]
        public struct RAWHID { public uint dwSizeHid; public uint dwCount; }

        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] d, uint n, uint sz);
        [DllImport("user32.dll")] public static extern uint GetRawInputData(IntPtr h, uint cmd, IntPtr data, ref uint sz, uint hdrSz);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint GetRawInputDeviceInfoW(IntPtr h, uint cmd, StringBuilder sb, ref uint sz);

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        public struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public INPUTUNION u; }

        [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint n, INPUT[] p, int cb);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool ClipCursor(ref RECT r);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool ClipCursor(IntPtr none);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll", SetLastError = true)] public static extern bool LockWorkStation();
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int val);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO mi);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

        public static List<string> EnumHidPaths()
        {
            var list = new List<string>();
            var g = GUID_DEVINTERFACE_HID;
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x02 | 0x10);
            if (set == new IntPtr(-1)) return list;
            for (int i = 0; ; i++)
            {
                var did = new SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref did)) break;
                var ddd = new SP_DEVICE_INTERFACE_DETAIL_DATA();
                ddd.cbSize = IntPtr.Size == 8 ? 8 : 6;
                int need;
                SetupDiGetDeviceInterfaceDetail(set, ref did, ref ddd, Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DETAIL_DATA)), out need, IntPtr.Zero);
                if (!string.IsNullOrEmpty(ddd.DevicePath)) list.Add(ddd.DevicePath);
            }
            SetupDiDestroyDeviceInfoList(set);
            return list;
        }
    }
}
