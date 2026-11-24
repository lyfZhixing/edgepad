using System;
using System.Windows;

namespace EdgePad
{
    public partial class App : Application
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [System.Runtime.InteropServices.FieldOffset(0)] public MOUSEINPUT mi;
            [System.Runtime.InteropServices.FieldOffset(0)] public KEYBDINPUT ki;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct INPUT { public uint type; public INPUTUNION u; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint SendInput(uint n, INPUT[] p, int cb);

        private static void SymClick()
        {
            var a = new INPUT[1];
            a[0].type = 0;
            a[0].u.mi.dwFlags = 0x0002;   // LEFTDOWN
            SendInput(1, a, System.Runtime.InteropServices.Marshal.SizeOf(typeof(INPUT)));
            var b = new INPUT[1];
            b[0].type = 0;
            b[0].u.mi.dwFlags = 0x0004;   // LEFTUP
            SendInput(1, b, System.Runtime.InteropServices.Marshal.SizeOf(typeof(INPUT)));
        }

        private async void RunMediaTest(string mode)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("媒体会话（SMTC）自检 " + DateTime.Now.ToString("HH:mm:ss"));
            try
            {
                var before = await MediaWatch.Read();
                sb.AppendLine("有会话=" + await MediaWatch.Available());
                sb.AppendLine("之前：" + (before == null ? "（读不到）" : before.Describe()));

                if (mode == "next" || mode == "prev")
                {
                    var spec = Actions.FromPreset(mode == "next" ? "media.next" : "media.prev");
                    sb.AppendLine("发送 " + spec.Display + " → " + Engine.Run(spec));
                    Injector.Flush(800);
                    for (int i = 0; i < 3; i++)
                    {
                        await System.Threading.Tasks.Task.Delay(600);
                        var after = await MediaWatch.Read();
                        sb.AppendLine("发之后 " + (i + 1) + "：" + (after == null ? "（读不到）" : after.Describe()));
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("异常：" + ex.Message); }

            System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "mediatest.txt"), sb.ToString());
            Shutdown();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Length > 0 && e.Args[0] == "--selftest")
            {
                SelfTest.Run();
                Shutdown();
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--clicktest")
            {
                bool hooked = ClickSuppressor.WaitInstalled(2000);
                ClickSuppressor.Arm(3000);

                int before = ClickSuppressor.Swallowed;
                SymClick();
                System.Threading.Thread.Sleep(250);
                SymClick();
                int after = ClickSuppressor.Swallowed;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("钩子已安装=" + hooked + "（线程 " + ClickSuppressor.HookThreadId + "，装上耗时 " + ClickSuppressor.InstallMs + "ms），武装 3s");
                sb.AppendLine("两次合成点击后被吞数量 = " + (after - before));
                sb.AppendLine("结论：" + ((after - before) >= 2 ? "钩子生效" : "钩子未吞掉点击"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "clicktest.txt"), sb.ToString());
                ClickSuppressor.Uninstall();
                Shutdown();
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--perftest")
            {
                PerfTest.Run();
                Shutdown();
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--migratetest")
            {
                MigrateTest.Run();
                Shutdown();
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--startuptest")
            {
                string mode = e.Args.Length > 1 ? e.Args[1] : "on";
                bool want = mode == "on";
                bool ok = Autostart.Set(want);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("64位进程=" + Environment.Is64BitProcess);
                sb.AppendLine("目标命令=" + Autostart.Command);
                sb.AppendLine("写入(" + mode + ") 返回=" + ok);
                sb.AppendLine("IsEnabled()=" + Autostart.IsEnabled());
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "startuptest.txt"), sb.ToString());
                Shutdown();
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--mediatest")
            {
                RunMediaTest(e.Args.Length > 1 ? e.Args[1] : null);
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--patterntest")
            {
                PatternTest.Run();
                Shutdown();
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--toasttest-media")
            {
                Toast.Show("下一首", "南海姑娘 - 王菲  ▶");
                var tm = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
                tm.Tick += (s2, e2) => { tm.Stop(); Shutdown(); };
                tm.Start();
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--toasttest")
            {
                Toast.Show(e.Args.Length > 1 ? e.Args[1] : "截图已完成",
                           e.Args.Length > 2 ? e.Args[2] : "3840 × 2160，已复制到剪贴板");
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
                t.Tick += (s2, e2) => { t.Stop(); Shutdown(); };
                t.Start();
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--shottest")
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("截图动作链路自检 " + DateTime.Now.ToString("HH:mm:ss"));
                uint seq0 = Native.GetClipboardSequenceNumber();
                sb.AppendLine("发之前 剪贴板序号=" + seq0);

                var spec = Actions.FromPreset(e.Args.Length > 1 ? e.Args[1] : "shot.win");
                sb.AppendLine("动作 " + spec.Display + " = " + spec.Value + "  IsScreenshot=" + Engine.IsScreenshot(spec));
                bool ran = Engine.Run(spec);
                Injector.Flush(800);
                System.Threading.Thread.Sleep(700);

                uint seq1 = Native.GetClipboardSequenceNumber();
                sb.AppendLine("发之后 剪贴板序号=" + seq1 + " 变化=" + (seq1 != seq0));

                string size = "（无图片）";
                try
                {
                    if (System.Windows.Clipboard.ContainsImage())
                    {
                        var img = System.Windows.Clipboard.GetImage();
                        if (img != null) size = img.PixelWidth + "×" + img.PixelHeight;
                    }
                }
                catch (Exception ex) { size = "读取失败 " + ex.Message; }
                sb.AppendLine("剪贴板图片=" + size);
                sb.AppendLine("结论：" + (seq1 != seq0 && size != "（无图片）" ? "截图确实进了剪贴板" : "没拿到截图"));

                System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "shottest.txt"), sb.ToString());
                Shutdown();
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--scrollbench")
            {
                ScrollBench.Run();
                Shutdown();
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--audiotest")
            {
                var sb = new System.Text.StringBuilder();
                bool m0, m1, m2;
                float v0;
                bool k0 = Audio.TryGetMute(out m0);
                bool kv = Audio.TryGetVolume(out v0);
                sb.AppendLine("端点: " + Audio.EndpointName());
                sb.AppendLine("初始 静音=" + (k0 ? m0.ToString() : "?") + " 音量=" + (kv ? v0.ToString("0.00") : "?"));

                bool sent = Engine.Run(new ActionSpec { Kind = "keys", Value = "VolumeMute", Display = "静音" });
                System.Threading.Thread.Sleep(400);
                bool k1 = Audio.TryGetMute(out m1);
                sb.AppendLine("发静音键 返回=" + sent + " 之后 静音=" + (k1 ? m1.ToString() : "?"));

                Engine.Run(new ActionSpec { Kind = "keys", Value = "VolumeMute", Display = "静音" });
                System.Threading.Thread.Sleep(400);
                bool k2 = Audio.TryGetMute(out m2);
                sb.AppendLine("再发一次 之后 静音=" + (k2 ? m2.ToString() : "?"));

                if (k0) { Audio.TrySetMute(m0); }
                sb.AppendLine("已还原为 " + m0);

                System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "audiotest.txt"), sb.ToString());
                Shutdown();
                return;
            }

            new MainWindow().Show();
        }
    }
}
