using System;
using System.Collections.Generic;

namespace EdgePad
{
    /// <summary>
    /// 双指轨迹识别：L / X / 圈。
    /// 只做几何判定（等弧长重采样 → 归一化 → 角点数 / 自交 / 闭合度 / 圆度），与坐标量程无关，便于单独测。
    /// </summary>
    public static class Patterns
    {
        public enum Kind { None, L, X, Circle }

        private const int Samples = 48;          // 重采样点数
        private const double MinLength = 0.14;   // 轨迹总长（占板面比例）下限，太短当滑动
        private const double MinScale = 0.10;    // 包围盒最长边下限

        private struct Features
        {
            public double Len, Scale, Perim, CloseDist, Area, Roundness, MaxTurn;
            public int Corners, Crossings, CornerIdx;
            public bool Closed;
            public double[] Rx, Ry;
        }

        // ---------- 诊断 ----------

        public static string Describe(IList<double> xs, IList<double> ys, int maxX, int maxY)
        {
            Features f;
            if (!Compute(xs, ys, maxX, maxY, out f)) return "轨迹太短，不参与判定";
            return "len=" + f.Len.ToString("0.000")
                 + " scale=" + f.Scale.ToString("0.000")
                 + " close=" + f.CloseDist.ToString("0.00")
                 + " round=" + f.Roundness.ToString("0.00")
                 + " corners=" + f.Corners
                 + " maxTurn=" + f.MaxTurn.ToString("0")
                 + " cross=" + f.Crossings
                 + " closed=" + f.Closed;
        }

        // ---------- 判定 ----------

        public static Kind Classify(IList<double> xs, IList<double> ys, int maxX, int maxY)
        {
            Features f;
            if (!Compute(xs, ys, maxX, maxY, out f)) return Kind.None;

            // 圈：闭合 + 够圆
            if (f.Closed && f.Corners <= 1 && f.Roundness > 0.55 && f.Crossings <= 1) return Kind.Circle;

            // L：一个角 + 两条又直又都不短的腿
            if (!f.Closed && f.Corners == 1 && f.Crossings == 0 && f.CornerIdx > 6 && f.CornerIdx < Samples - 7)
            {
                double leg1 = PathLen(f.Rx, f.Ry, 0, f.CornerIdx);
                double leg2 = PathLen(f.Rx, f.Ry, f.CornerIdx, Samples - 1);
                double total = Math.Max(1e-6, leg1 + leg2);
                bool share = leg1 / total > 0.25 && leg2 / total > 0.25;
                bool straight1 = Chord(f.Rx, f.Ry, 0, f.CornerIdx) / Math.Max(1e-6, leg1) > 0.80;
                bool straight2 = Chord(f.Rx, f.Ry, f.CornerIdx, Samples - 1) / Math.Max(1e-6, leg2) > 0.80;
                bool turnOk = f.MaxTurn >= 65 && f.MaxTurn <= 115;   // 直角 ±25°，太尖像对勾、太平像弧
                if (share && straight1 && straight2 && turnOk) return Kind.L;
            }

            // X：一笔画过去自己交叉（沙漏/交叉线），不闭合、也不是来回折返
            if (!f.Closed && f.Crossings >= 1 && f.Corners >= 1 && f.MaxTurn < 155 && f.Perim < 4.0) return Kind.X;

            return Kind.None;
        }

        // ---------- 特征 ----------

        private static bool Compute(IList<double> xs, IList<double> ys, int maxX, int maxY, out Features f)
        {
            f = new Features();
            int n = xs == null ? 0 : xs.Count;
            if (n < 10 || ys == null || ys.Count != n) return false;

            double mx = Math.Max(1, maxX), my = Math.Max(1, maxY);
            var px = new double[n];
            var py = new double[n];
            for (int i = 0; i < n; i++) { px[i] = xs[i] / mx; py[i] = ys[i] / my; }

            double len = 0;
            for (int i = 1; i < n; i++) len += Dist(px[i - 1], py[i - 1], px[i], py[i]);
            f.Len = len;
            if (len < MinLength) return false;

            var rx = new double[Samples];
            var ry = new double[Samples];
            Resample(px, py, len, rx, ry);

            double minx = double.MaxValue, maxx = double.MinValue, miny = double.MaxValue, maxy = double.MinValue;
            for (int i = 0; i < Samples; i++)
            {
                if (rx[i] < minx) minx = rx[i];
                if (rx[i] > maxx) maxx = rx[i];
                if (ry[i] < miny) miny = ry[i];
                if (ry[i] > maxy) maxy = ry[i];
            }
            double scale = Math.Max(maxx - minx, maxy - miny);
            f.Scale = scale;
            if (scale < MinScale) return false;
            double cx = (minx + maxx) / 2, cy = (miny + maxy) / 2;
            for (int i = 0; i < Samples; i++) { rx[i] = (rx[i] - cx) / scale; ry[i] = (ry[i] - cy) / scale; }

            double perim = 0;
            for (int i = 1; i < Samples; i++) perim += Dist(rx[i - 1], ry[i - 1], rx[i], ry[i]);
            f.Perim = perim;
            f.CloseDist = perim > 1e-9 ? Dist(rx[0], ry[0], rx[Samples - 1], ry[Samples - 1]) / perim : 1;
            f.Area = Math.Abs(PolyArea(rx, ry));
            f.Roundness = perim > 1e-9 ? 4 * Math.PI * f.Area / (perim * perim) : 0;

            int cornerIdx;
            double maxTurn;
            f.Corners = CountCorners(rx, ry, out cornerIdx, out maxTurn);
            f.CornerIdx = cornerIdx;
            f.MaxTurn = maxTurn;
            f.Crossings = CountCrossings(rx, ry);
            f.Closed = f.CloseDist < 0.22;
            f.Rx = rx;
            f.Ry = ry;
            return true;
        }

        // ---------- 几何工具 ----------

        private static double Dist(double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1, dy = y2 - y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static void Resample(double[] px, double[] py, double total, double[] rx, double[] ry)
        {
            int n = px.Length;
            double step = total / (rx.Length - 1);
            rx[0] = px[0]; ry[0] = py[0];
            int outIdx = 1;
            double walked = 0;
            for (int i = 1; i < n && outIdx < rx.Length; i++)
            {
                double seg = Dist(px[i - 1], py[i - 1], px[i], py[i]);
                while (seg > 1e-9 && walked + seg >= step * outIdx && outIdx < rx.Length)
                {
                    double t = (step * outIdx - walked) / seg;
                    rx[outIdx] = px[i - 1] + (px[i] - px[i - 1]) * t;
                    ry[outIdx] = py[i - 1] + (py[i] - py[i - 1]) * t;
                    outIdx++;
                }
                walked += seg;
            }
            while (outIdx < rx.Length) { rx[outIdx] = px[n - 1]; ry[outIdx] = py[n - 1]; outIdx++; }
        }

        private static double PathLen(double[] x, double[] y, int from, int to)
        {
            double s = 0;
            for (int i = from + 1; i <= to; i++) s += Dist(x[i - 1], y[i - 1], x[i], y[i]);
            return s;
        }

        private static double Chord(double[] x, double[] y, int from, int to)
        {
            return Dist(x[from], y[from], x[to], y[to]);
        }

        private static double PolyArea(double[] x, double[] y)
        {
            double a = 0;
            for (int i = 1; i < x.Length; i++) a += x[i - 1] * y[i] - x[i] * y[i - 1];
            return a / 2;
        }

        /// <summary>统计明显的转折点（窗口 3 点求夹角），返回角点数并给出最大转角。</summary>
        private static int CountCorners(double[] x, double[] y, out int firstIdx, out double maxTurn)
        {
            int n = x.Length;
            const int w = 3;
            var turn = new double[n];
            for (int i = w; i < n - w; i++)
            {
                double ax = x[i] - x[i - w], ay = y[i] - y[i - w];
                double bx = x[i + w] - x[i], by = y[i + w] - y[i];
                double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
                if (la < 1e-9 || lb < 1e-9) { turn[i] = 0; continue; }
                double cos = (ax * bx + ay * by) / (la * lb);
                if (cos > 1) cos = 1; else if (cos < -1) cos = -1;
                turn[i] = Math.Acos(cos) * 180 / Math.PI;   // 0 = 直行，90 = 直角拐弯，180 = 原路折返
            }

            firstIdx = -1;
            maxTurn = 0;
            int count = 0;
            int lastPeak = -99;
            for (int i = w; i < n - w; i++)
            {
                if (turn[i] < 45) continue;
                bool peak = true;
                for (int k = -w; k <= w; k++)
                {
                    int j = i + k;
                    if (j < w || j >= n - w) continue;
                    if (turn[j] > turn[i]) { peak = false; break; }
                }
                if (!peak) continue;
                if (i - lastPeak <= 6) continue;
                lastPeak = i;
                count++;
                if (firstIdx < 0) firstIdx = i;
                if (turn[i] > maxTurn) maxTurn = turn[i];
            }
            return count;
        }

        private static int CountCrossings(double[] x, double[] y)
        {
            int n = x.Length;
            int c = 0;
            for (int i = 0; i < n - 1; i++)
                for (int j = i + 2; j < n - 1; j++)
                {
                    if (i == 0 && j == n - 2) continue;
                    if (SegIntersect(x[i], y[i], x[i + 1], y[i + 1], x[j], y[j], x[j + 1], y[j + 1])) c++;
                    if (c > 4) return c;
                }
            return c;
        }

        private static bool SegIntersect(double ax, double ay, double bx, double by,
                                         double cx, double cy, double dx, double dy)
        {
            double d1 = Cross(cx, cy, dx, dy, ax, ay);
            double d2 = Cross(cx, cy, dx, dy, bx, by);
            double d3 = Cross(ax, ay, bx, by, cx, cy);
            double d4 = Cross(ax, ay, bx, by, dx, dy);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
                   && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private static double Cross(double ax, double ay, double bx, double by, double px, double py)
        {
            return (bx - ax) * (py - ay) - (by - ay) * (px - ax);
        }
    }
}
