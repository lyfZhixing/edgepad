using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text;

namespace EdgePad
{
    /// <summary>量化“滚动是否丝滑”：逐帧的档位分布、卡顿间隔，以及日志写入的耗时。</summary>
    internal static class ScrollBench
    {
        private const int MAX_X = 1800;
        private const int MAX_Y = 1275;

        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("EdgePad 滚动流畅度测量 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            sb.AppendLine("== 1. 日志写入成本（每档滚动要写一行）==");
            sb.AppendLine(SyncLogCost());

            sb.AppendLine("== 1b. SendInput 发布一档滚动的耗时（300 次，含分配 INPUT 数组）==");
            sb.AppendLine(WheelCost());

            sb.AppendLine("== 2. 右边缘滑动滚动的档位发布节奏（45 帧 / 8ms 一帧 / 缓入缓出）==");
            sb.AppendLine(Swipe(true, 40));
            sb.AppendLine(Swipe(true, 25));
            sb.AppendLine(Swipe(true, 40, 1150) + "   ← 起点落在右下角常驻防误触区内");

            sb.AppendLine("== 3. 托盘图标与开机启动 ==");
            sb.AppendLine(ShellSection());

            File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "scrollbench.txt"), sb.ToString());
        }

        private static string ShellSection()
        {
            var sb = new StringBuilder();
            try
            {
                using (var icon = Tray.LoadIcon())
                    sb.AppendLine("托盘图标: " + icon.Width + "×" + icon.Height + "（16~256 里取到的是 " + icon.Width + "）");
            }
            catch (Exception ex) { sb.AppendLine("托盘图标: 失败 " + ex.Message); }

            bool before = Autostart.IsEnabled();
            sb.AppendLine("开机启动 初始=" + before);
            sb.AppendLine("写入命令: " + Autostart.Command);

            bool okOn = Autostart.Set(true);
            string written = "";
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Autostart.RunKey, false))
                    written = (k == null ? "(无键)" : (k.GetValue(Autostart.ValueName) as string ?? "(无值)"));
            }
            catch { }
            sb.AppendLine("开启: 返回=" + okOn + " 注册表实际值=" + written + " 读回=" + Autostart.IsEnabled());

            bool okOff = Autostart.Set(false);
            sb.AppendLine("关闭: 返回=" + okOff + " 读回=" + Autostart.IsEnabled());

            bool restored = Autostart.Set(before);
            sb.AppendLine("已还原为 " + before + " → " + Autostart.IsEnabled() + "（还原返回 " + restored + "）");
            return sb.ToString();
        }

        private static string SyncLogCost()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "edgepad-bench");
            string path = System.IO.Path.Combine(dir, "bench.log");
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }

            const int N = 300;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < N; i++)
            {
                try
                {
                    string d = System.IO.Path.GetDirectoryName(path);
                    if (!System.IO.Directory.Exists(d)) System.IO.Directory.CreateDirectory(d);
                    var fi = new System.IO.FileInfo(path);
                    if (fi.Exists && fi.Length > 2 * 1024 * 1024) fi.Delete();
                    System.IO.File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + "  动作 R.up = 向上滚动 -> 已发送" + Environment.NewLine);
                }
                catch { }
            }
            sw.Stop();
            double perMs = sw.Elapsed.TotalMilliseconds / N;
            double perUs = perMs * 1000;

            // 新版：异步日志只入队
            var sw2 = Stopwatch.StartNew();
            for (int i = 0; i < N; i++) Log.Write("动作 R.up = 向上滚动 -> 已提交");
            sw2.Stop();
            double perUs2 = sw2.Elapsed.TotalMilliseconds / N * 1000;
            Log.Shutdown();

            var sb = new StringBuilder();
            sb.AppendLine("旧（同步写盘）: " + N + " 次共 " + sw.Elapsed.TotalMilliseconds.ToString("0.0") + "ms，单次 " + perUs.ToString("0.0") + "µs");
            sb.AppendLine("新（入队异步）: " + N + " 次共 " + sw2.Elapsed.TotalMilliseconds.ToString("0.0") + "ms，单次 " + perUs2.ToString("0.0") + "µs"
                          + "，快 " + (perUs / Math.Max(0.01, perUs2)).ToString("0") + " 倍");
            sb.AppendLine("旧方案按每秒 40 档算，日志每秒占用 UI 线程 " + (perMs * 40).ToString("0.0") + "ms");
            try { Directory.Delete(dir, true); } catch { }
            return sb.ToString();
        }

        private static string WheelCost()
        {
            const int N = 300;
            var sync = new double[N];
            var sw = new Stopwatch();
            for (int i = 0; i < N; i++)
            {
                sw.Restart();
                Engine.WheelNow(1);
                sw.Stop();
                sync[i] = sw.Elapsed.TotalMilliseconds;
                System.Threading.Thread.Sleep(2);
            }
            Array.Sort(sync);

            var post = new double[N];
            for (int i = 0; i < N; i++)
            {
                sw.Restart();
                Engine.Wheel(1);
                sw.Stop();
                post[i] = sw.Elapsed.TotalMilliseconds;
                System.Threading.Thread.Sleep(2);
            }
            Array.Sort(post);

            var sb = new StringBuilder();
            sb.AppendLine("旧（在报文线程同步 SendInput）: 平均 " + (sync.Sum() / N * 1000).ToString("0") + "µs，中位 "
                          + (sync[N / 2] * 1000).ToString("0") + "µs，P99 " + (sync[(int)(N * 0.99)] * 1000).ToString("0")
                          + "µs，最大 " + (sync[N - 1] * 1000).ToString("0") + "µs");
            sb.AppendLine("新（入队，后台线程发）: 平均 " + (post.Sum() / N * 1000).ToString("0.0") + "µs，中位 "
                          + (post[N / 2] * 1000).ToString("0.0") + "µs，P99 " + (post[(int)(N * 0.99)] * 1000).ToString("0.0")
                          + "µs，最大 " + (post[N - 1] * 1000).ToString("0.0") + "µs");
            Injector.Flush(3000);
            sb.AppendLine("注入线程单次 SendInput 实测: 最近一次 " + Injector.LastMs.ToString("0.00") + "ms，已发送 " + Injector.Sent + " 批，丢弃 " + Injector.Dropped);
            return sb.ToString();
        }

        private static string Swipe(bool vertical, int step, int startY = 780)
        {
            long now = 1000;
            var e = new Engine();
            e.DryRun = true;
            e.Clock = () => now;
            e.S = Settings.CreateDefault();
            e.S.Bindings["R.up"] = Actions.FromPreset("scroll.up");
            e.S.Bindings["R.down"] = Actions.FromPreset("scroll.down");
            e.S.ScrollStep = step;

            const int Frames = 45;
            double Travel = vertical ? Math.Min(700, startY - 40) : 900;
            var fires = new List<long>();
            e.Fired += (k, s) => fires.Add(now);

            var perFrame = new List<int>();
            for (int i = 0; i < Frames; i++)
            {
                double t = (double)i / (Frames - 1);
                double moved = Travel * (1 - Math.Cos(Math.PI * t)) / 2;   // 缓入缓出
                int before = fires.Count;
                var cs = new List<Contact>();
                if (vertical) cs.Add(new Contact { Id = 1, X = 1760, Y = startY - moved });
                else cs.Add(new Contact { Id = 1, X = 900 + moved, Y = 1270 });
                e.OnSample(cs, 1, MAX_X, MAX_Y);
                perFrame.Add(fires.Count - before);
                now += 8;
            }
            e.OnSample(new List<Contact>(), 0, MAX_X, MAX_Y);

            int maxBurst = 0, total = 0, burstyFrames = 0;
            foreach (var n in perFrame) { if (n > maxBurst) maxBurst = n; total += n; if (n >= 3) burstyFrames++; }

            double maxGap = 0, sumGap = 0;
            for (int i = 1; i < fires.Count; i++)
            {
                double g = fires[i] - fires[i - 1];
                if (g > maxGap) maxGap = g;
                sumGap += g;
            }
            double avgGap = fires.Count > 1 ? sumGap / (fires.Count - 1) : 0;

            var sb = new StringBuilder();
            sb.AppendLine((vertical ? "竖向 起点 y=" + startY : "横向") + "，位移 " + Travel.ToString("0") + " 单位 / 每档 " + step + " 单位 → 共 " + total + " 档");
            sb.AppendLine("单帧最多 " + maxBurst + " 档（≥3 档的帧 " + burstyFrames + " 个）；档间隔 平均 "
                          + avgGap.ToString("0.0") + "ms，最大 " + maxGap.ToString("0.0") + "ms");
            sb.Append("逐帧档位: ");
            foreach (var n in perFrame) sb.Append(n);
            sb.AppendLine();
            return sb.ToString();
        }
    }
}
