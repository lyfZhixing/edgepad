using System;
using Microsoft.Win32;

namespace EdgePad
{
    /// <summary>开机自启：只写 HKCU\...\Run，不需要管理员权限，也不建计划任务。</summary>
    public static class Autostart
    {
        internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string ValueName = "EdgePad";

        /// <summary>当前可执行文件。单文件发布时就是 EdgePad.exe；用 dotnet 跑的时候退回到同目录的 exe。</summary>
        public static string ExePath
        {
            get
            {
                string p = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(p) &&
                    !p.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                    return p;
                string local = System.IO.Path.Combine(AppContext.BaseDirectory, "EdgePad.exe");
                return System.IO.File.Exists(local) ? local : p;
            }
        }

        /// <summary>自启命令：--tray 表示启动后直接待在托盘，不弹窗。</summary>
        public static string Command { get { return "\"" + ExePath + "\" --tray"; } }

        public static bool IsEnabled()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    return !string.IsNullOrEmpty(k.GetValue(ValueName) as string);
                }
            }
            catch { return false; }
        }

        public static bool Set(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey, true))
                {
                    if (k == null) return false;
                    if (on) k.SetValue(ValueName, Command, RegistryValueKind.String);
                    else if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName, false);
                    return true;
                }
            }
            catch { return false; }
        }

        /// <summary>每次启动时对齐一次：程序被移动过/换过版本，路径要更新。</summary>
        public static void SyncIfEnabled()
        {
            if (!IsEnabled()) return;
            Set(true);
        }
    }
}
