using System;
using System.Collections.Generic;
using System.Text;

namespace EdgePad
{
    /// <summary>双指轨迹识别的自检：喂合成轨迹，看分类对不对。</summary>
    internal static class PatternTest
    {
        private const int MAXX = 1800;
        private const int MAXY = 1275;

        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("双指轨迹识别自检 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            int pass = 0, fail = 0;

            Check(sb, "L 向下再向右", Kind(Line(500, 300, 500, 900, 1300, 900)), Patterns.Kind.L, ref pass, ref fail);
            Check(sb, "L 向右再向下", Kind(Line(400, 350, 1200, 350, 1200, 950)), Patterns.Kind.L, ref pass, ref fail);
            Check(sb, "L 旋转 180°", Kind(Line(1300, 950, 1300, 350, 500, 350)), Patterns.Kind.L, ref pass, ref fail);
            Check(sb, "X 一笔画（沙漏）", Kind(Line(450, 300, 1250, 950, 1250, 300, 450, 950)), Patterns.Kind.X, ref pass, ref fail);
            Check(sb, "圈", Kind(Circle(900, 640, 380)), Patterns.Kind.Circle, ref pass, ref fail);
            Check(sb, "直线（滚动）→ 不算", Kind(Line(500, 700, 1300, 700)), Patterns.Kind.None, ref pass, ref fail);
            Check(sb, "Z 形 → 不算", Kind(Line(450, 320, 1250, 320, 450, 950)), Patterns.Kind.None, ref pass, ref fail);
            Check(sb, "来回滚动 → 不算", Kind(Line(600, 700, 600, 380, 600, 700, 600, 380)), Patterns.Kind.None, ref pass, ref fail);
            Check(sb, "小抖动 → 不算", Kind(Line(900, 620, 915, 640, 900, 660)), Patterns.Kind.None, ref pass, ref fail);
            Check(sb, "V 形（对勾）→ 不算", Kind(Line(500, 300, 700, 900, 1200, 320)), Patterns.Kind.None, ref pass, ref fail);
            Check(sb, "C 形 → 不算", Kind(Arc(900, 640, 380, 40, 320)), Patterns.Kind.None, ref pass, ref fail);

            // ---- 引擎链路：真的喂给 Engine，看轨迹记录与触发 ----
            sb.AppendLine();
            sb.AppendLine("== 引擎链路 ==");
            sb.AppendLine(EngineRun("L", e =>
            {
                TwoPath(e, 500, 300, 500, 900);
                TwoPath(e, 500, 900, 1250, 900);
            }));
            sb.AppendLine(EngineRun("圈", e => TwoCircle(e, 900, 640, 320)));

            sb.AppendLine();
            sb.AppendLine("结果：通过 " + pass + " 项，失败 " + fail + " 项");
            System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "patterntest.txt"), sb.ToString());
        }

        private static long _now;

        private static string EngineRun(string name, Action<Engine> script)
        {
            _now = 1000;
            var e = new Engine { S = Settings.CreateDefault(), DryRun = true, Clock = () => _now };
            e.S.FreezeCursor = false;
            var fired = new List<string>();
            var trace = new StringBuilder();
            e.Fired += (k, s) => fired.Add(k);
            e.Trace += x => trace.AppendLine("    [trace] " + x);

            script(e);
            _now += 10;
            e.OnSample(new List<Contact>(), 0, MAXX, MAXY);

            var sb = new StringBuilder();
            sb.AppendLine(name + " → fired=[" + string.Join(",", fired) + "]");
            sb.Append(trace.ToString());
            return sb.ToString();
        }

        private static void TwoPath(Engine e, double x0, double y0, double x1, double y1)
        {
            const int Steps = 12;
            for (int i = 0; i <= Steps; i++)
            {
                double tt = (double)i / Steps;
                double x = x0 + (x1 - x0) * tt;
                double y = y0 + (y1 - y0) * tt;
                e.OnSample(new List<Contact> {
                    new Contact { Id = 0, X = x - 60, Y = y },
                    new Contact { Id = 1, X = x + 60, Y = y } }, 2, MAXX, MAXY);
                Advance(e);
            }
        }

        private static void TwoCircle(Engine e, double cx, double cy, double r)
        {
            const int Steps = 40;
            for (int i = 0; i <= Steps; i++)
            {
                double a = i / (double)Steps * Math.PI * 2;
                double x = cx + Math.Cos(a) * r;
                double y = cy + Math.Sin(a) * r * 0.75;
                e.OnSample(new List<Contact> {
                    new Contact { Id = 0, X = x - 60, Y = y },
                    new Contact { Id = 1, X = x + 60, Y = y } }, 2, MAXX, MAXY);
                Advance(e);
            }
        }

        private static void Advance(Engine e) { _now += 10; }

        private static string _last = "";

        private static Patterns.Kind Kind(Tuple<List<double>, List<double>> p)
        {
            var k = Patterns.Classify(p.Item1, p.Item2, MAXX, MAXY);
            _last = Patterns.Describe(p.Item1, p.Item2, MAXX, MAXY);
            return k;
        }

        /// <summary>折线：x1,y1,x2,y2,... 每段插值 40 点。</summary>
        private static Tuple<List<double>, List<double>> Line(params double[] xy)
        {
            var xs = new List<double>();
            var ys = new List<double>();
            for (int i = 0; i + 3 < xy.Length; i += 2)
            {
                double x0 = xy[i], y0 = xy[i + 1], x1 = xy[i + 2], y1 = xy[i + 3];
                for (int k = 0; k < 40; k++)
                {
                    double t = k / 40.0;
                    xs.Add(x0 + (x1 - x0) * t);
                    ys.Add(y0 + (y1 - y0) * t);
                }
            }
            xs.Add(xy[xy.Length - 2]);
            ys.Add(xy[xy.Length - 1]);
            return Tuple.Create(xs, ys);
        }

        private static Tuple<List<double>, List<double>> Circle(double cx, double cy, double r)
        {
            var xs = new List<double>();
            var ys = new List<double>();
            for (int k = 0; k <= 120; k++)
            {
                double a = k / 120.0 * Math.PI * 2;
                xs.Add(cx + Math.Cos(a) * r);
                ys.Add(cy + Math.Sin(a) * r * 0.75);   // 板子是 4:3，圈画得扁一点
            }
            return Tuple.Create(xs, ys);
        }

        private static Tuple<List<double>, List<double>> Arc(double cx, double cy, double r, double fromDeg, double toDeg)
        {
            var xs = new List<double>();
            var ys = new List<double>();
            for (int k = 0; k <= 90; k++)
            {
                double a = (fromDeg + (toDeg - fromDeg) * k / 90.0) * Math.PI / 180;
                xs.Add(cx + Math.Cos(a) * r);
                ys.Add(cy + Math.Sin(a) * r * 0.75);
            }
            return Tuple.Create(xs, ys);
        }

        private static void Check(StringBuilder sb, string name, Patterns.Kind got, Patterns.Kind want, ref int pass, ref int fail)
        {
            bool ok = got == want;
            if (ok) pass++; else fail++;
            sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name + "  期望 " + want + " 实际 " + got);
            sb.AppendLine("        " + _last);
        }
    }
}
