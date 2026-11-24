using System;
using System.Runtime.InteropServices;

namespace EdgePad
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPERTYKEY { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPVARIANT { public ushort vt; public ushort r1, r2, r3; public IntPtr p; public IntPtr p2; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr n);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr n);
        [PreserveSig] int GetChannelCount(out int c);
        [PreserveSig] int SetMasterVolumeLevel(float l, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float l, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float l);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float l);
        [PreserveSig] int SetChannelVolumeLevel(int ch, float l, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(int ch, float l, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(int ch, out float l);
        [PreserveSig] int GetChannelVolumeLevelScalar(int ch, out float l);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    internal static class Audio
    {
        private static IAudioEndpointVolume _vol;

        private static IAudioEndpointVolume Volume()
        {
            if (_vol != null) return _vol;
            try
            {
                var en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(0, 0, out dev) != 0) return null;
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                object o;
                if (dev.Activate(ref iid, 1, IntPtr.Zero, out o) != 0) return null;
                _vol = (IAudioEndpointVolume)o;
            }
            catch { _vol = null; }
            return _vol;
        }

        public static bool TryGetMute(out bool mute)
        {
            mute = false;
            var v = Volume();
            return v != null && v.GetMute(out mute) == 0;
        }

        public static bool TryGetVolume(out float level)
        {
            level = 0;
            var v = Volume();
            return v != null && v.GetMasterVolumeLevelScalar(out level) == 0;
        }

        public static bool TrySetMute(bool mute)
        {
            var v = Volume();
            var ctx = Guid.Empty;
            return v != null && v.SetMute(mute, ref ctx) == 0;
        }

        public static string EndpointName()
        {
            try
            {
                var en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(0, 0, out dev) != 0) return "?";
                IPropertyStore store;
                if (dev.OpenPropertyStore(0, out store) != 0) return "?";
                var key = new PROPERTYKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };
                PROPVARIANT pv;
                if (store.GetValue(ref key, out pv) != 0 || pv.vt != 31) return "?";
                return Marshal.PtrToStringUni(pv.p) ?? "?";
            }
            catch { return "?"; }
        }
    }
}
