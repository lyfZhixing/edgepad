using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace EdgePad
{
    /// <summary>
    /// 热路径耗时实测：按 125Hz（8ms 一帧）把合成触点喂给 Engine，看单帧到底花多少微秒。
    /// 目的：把“加功能之后卡了”这种感受变成数字——双指轨迹记录本身到底占了多少。
    /// </summary>
    internal static class PerfTest
    {
        private const int MAXX = 1800;
        private const int MAXY = 1275;

        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("热路径耗时实测 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("逻辑核数 " + Environment.ProcessorCount + "，DryRun，坐标量程 " + MAXX + "×" + MAXY);
            sb.AppendLine();

            sb.AppendLine("单指 中部移动（基准）      " + Bench(1, Mode.Move));
            sb.AppendLine("双指 直线滚动（最常见）    " + Bench(2, Mode.Move));
            sb.AppendLine("双指 反复滚动 60 次        " + Bench(2, Mode.ScrollBurst));
            sb.AppendLine("双指 画 L（含抬手判定）    " + Bench(2, Mode.DrawL));
            sb.AppendLine();

            sb.AppendLine("识别器 Classify(400 点 L)  " + ClassifyBench());
            sb.AppendLine();
            sb.AppendLine("注：单帧预算 8000µs（8ms）；界面每帧重绘一次的成本远大于这个数。");

            System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "perftest.txt"), sb.ToString());
        }

        private enum Mode { Move, ScrollBurst, DrawL }

        /// <summary>按 8ms 一帧喂 frames 帧，统计每帧 OnSample 的耗时。</summary>
        private static string Bench(int fingers, Mode mode)
        {
            long now = 1000;
            var e = new Engine { S = Settings.CreateDefault(), DryRun = true, Clock = () => now };
            e.S.FreezeCursor = false;
            e.S.PalmGuard = true;
            e.S.TypingGuard = false;

            int gestures, perGesture;
            if (mode == Mode.DrawL) { gestures = 20; perGesture = 48; }
            else if (mode == Mode.ScrollBurst) { gestures = 60; perGesture = 12; }
            else { gestures = 1; perGesture = 1250; }

            var sw = new Stopwatch();
            var a = new Contact { Id = 1 };
            var b = new Contact { Id = 2 };
            var list = new List<Contact>(2);
            var empty = new List<Contact>();

            // 预热：首帧要 JIT（实测能到 1.5ms），不预热会把 JIT 当成“卡顿”
            for (int g = 0; g < 3; g++)
            {
                for (int i = 0; i < 40; i++)
                {
                    list.Clear();
                    a.X = 600 + i; a.Y = 560; list.Add(a);
                    if (fingers == 2) { b.X = 760; b.Y = 590; list.Add(b); }
                    e.OnSample(list, fingers, MAXX, MAXY);
                    now += 8;
                }
                e.OnSample(empty, 0, MAXX, MAXY);
                now += 40;
            }

            var us = new List<double>(gestures * perGesture);
            var lifts = new List<double>(gestures);
            for (int g = 0; g < gestures; g++)
            {
                for (int i = 0; i < perGesture; i++)
                {
                    double t = perGesture < 2 ? 0 : (double)i / (perGesture - 1);
                    double x, y;
                    if (mode == Mode.DrawL)
                    {
                        if (t < 0.5) { x = 500; y = 300 + t * 2 * 700; }          // 竖
                        else { x = 500 + (t - 0.5) * 2 * 760; y = 1000; }         // 横
                    }
                    else if (mode == Mode.ScrollBurst)
                    {
                        x = 600 + i * 40; y = 560 + i * 6;                        // 一次 12 帧的滚动
                    }
                    else
                    {
                        x = 620 + 260 * Math.Sin(t * Math.PI * 4); y = 520 + 140 * Math.Cos(t * Math.PI * 4);
                    }

                    list.Clear();
                    a.X = x; a.Y = y; list.Add(a);
                    if (fingers == 2) { b.X = x + 140; b.Y = y + 30; list.Add(b); }

                    sw.Restart();
                    e.OnSample(list, fingers, MAXX, MAXY);
                    sw.Stop();
                    us.Add(sw.Elapsed.TotalMilliseconds * 1000.0);
                    now += 8;
                }

                // 抬手帧：双指轨迹判定就发生在这里
                sw.Restart();
                e.OnSample(empty, 0, MAXX, MAXY);
                sw.Stop();
                lifts.Add(sw.Elapsed.TotalMilliseconds * 1000.0);
                now += 8;
            }

            var arr = us.ToArray();
            Array.Sort(arr);
            string s = "P50 " + Pct(arr, 0.50).ToString("0.0") + "µs · P99 " + Pct(arr, 0.99).ToString("0.0")
                     + "µs · max " + Pct(arr, 1.0).ToString("0.0") + "µs";
            if (fingers == 2)
            {
                var la = lifts.ToArray();
                Array.Sort(la);
                s += " ｜抬头帧 P50 " + Pct(la, 0.50).ToString("0") + "µs max " + Pct(la, 1.0).ToString("0")
                   + "µs（" + gestures + " 次手势）";
            }
            return s;
        }

        private static string ClassifyBench()
        {
            // 400 点的 L 形轨迹（真实情况下 Engine 最多记 400 点）
            var xs = new double[400];
            var ys = new double[400];
            for (int i = 0; i < 400; i++)
            {
                double t = i / 399.0;
                if (t < 0.5) { xs[i] = 500; ys[i] = 300 + t * 2 * 700; }
                else { xs[i] = 500 + (t - 0.5) * 2 * 760; ys[i] = 1000; }
            }

            for (int i = 0; i < 20; i++) Patterns.Classify(xs, ys, MAXX, MAXY);   // 预热

            var sw = new Stopwatch();
            long total = 0, max = 0;
            const int rounds = 200;
            for (int i = 0; i < rounds; i++)
            {
                sw.Restart();
                var k = Patterns.Classify(xs, ys, MAXX, MAXY);
                sw.Stop();
                long v = sw.ElapsedTicks * 1000000L / Stopwatch.Frequency;
                total += v;
                if (v > max) max = v;
                if (i == 0 && k != Patterns.Kind.L) return "轨迹判定异常（分类=" + k + "）";
            }
            return "平均 " + (total / rounds).ToString("0") + "µs · max " + max.ToString("0") + "µs（" + rounds + " 次）";
        }

        private static double Pct(double[] sorted, double p)
        {
            if (sorted.Length == 0) return 0;
            int i = (int)Math.Round((sorted.Length - 1) * p);
            if (i < 0) i = 0;
            if (i >= sorted.Length) i = sorted.Length - 1;
            return sorted[i];
        }
    }
}
