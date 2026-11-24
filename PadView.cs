using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace EdgePad
{
    public sealed class PadView : FrameworkElement
    {
        // 四条边缘带宽度（占整板的比例）
        public double BandL = 0.10, BandR = 0.10, BandT = 0.10, BandB = 0.10;

        // 右下角常驻防误触区（归一化）
        public bool PalmOn = true;
        public double PalmX0 = 0.72, PalmY0 = 0.62;

        public double PadAspect = 1800.0 / 1275.0;
        public bool ShowTrails = true;

        public event Action BandsChanged;

        private sealed class TrailPoint { public Point P; public long T; }
        private readonly Dictionary<int, List<TrailPoint>> _trail = new Dictionary<int, List<TrailPoint>>();
        private List<Contact> _contacts = new List<Contact>();
        private int _maxX = 1800, _maxY = 1275;
        private Rect _pad;
        private int _drag = -1;

        private static readonly Brush PadFill = NewBrush(0xFF, 0x16, 0x16, 0x1B);
        private static readonly Brush PadStroke = NewBrush(0xFF, 0x2C, 0x2C, 0x3A);
        private static readonly Brush GridPen = NewBrush(0x22, 0xFF, 0xFF, 0xFF);
        private static readonly Brush BandFill = NewBrush(0x1F, 0x7C, 0x5C, 0xFF);
        private static readonly Brush BandEdge = NewBrush(0xFF, 0x7C, 0x5C, 0xFF);
        private static readonly Brush DotBrush = NewBrush(0xFF, 0x4A, 0xDE, 0x80);
        private static readonly Brush TrailBrush = NewBrush(0x66, 0x4A, 0xDE, 0x80);
        private static readonly Brush LabelBrush = NewBrush(0xFF, 0x9A, 0x9A, 0xA6);
        private static readonly Brush CornerEdge = NewBrush(0x8C, 0x5C, 0xD8, 0xFF);
        private static readonly Brush PalmFill = NewBrush(0x1C, 0xFF, 0xA0, 0x40);
        private static readonly Brush PalmEdge = NewBrush(0xAA, 0xFF, 0xA0, 0x40);
        private static readonly Brush ActEdge = NewBrush(0xFF, 0xF0, 0x9A, 0x3C);

        private static Brush NewBrush(byte a, byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }

        public PadView() { ClipToBounds = true; }

        protected override HitTestResult HitTestCore(PointHitTestParameters p)
        {
            return new PointHitTestResult(this, p.HitPoint);
        }

        public void SetContacts(List<Contact> contacts, int maxX, int maxY)
        {
            _contacts = contacts ?? new List<Contact>();
            _maxX = Math.Max(1, maxX);
            _maxY = Math.Max(1, maxY);

            if (ShowTrails && _pad.Width > 1)
            {
                long now = Environment.TickCount64;
                foreach (var c in _contacts)
                {
                    List<TrailPoint> list;
                    if (!_trail.TryGetValue(c.Id, out list)) { list = new List<TrailPoint>(40); _trail[c.Id] = list; }
                    if (list.Count > 0 && now - list[list.Count - 1].T > 220) list.Clear();
                    list.Add(new TrailPoint
                    {
                        P = new Point(
                            _pad.X + Math.Min(1.0, Math.Max(0.0, c.X / _maxX)) * _pad.Width,
                            _pad.Y + Math.Min(1.0, Math.Max(0.0, c.Y / _maxY)) * _pad.Height),
                        T = now
                    });
                    while (list.Count > 40) list.RemoveAt(0);
                }

                var stale = new List<int>();
                foreach (var kv in _trail)
                {
                    var lp = kv.Value;
                    while (lp.Count > 0 && now - lp[0].T > 700) lp.RemoveAt(0);
                    if (lp.Count == 0) stale.Add(kv.Key);
                }
                foreach (var k in stale) _trail.Remove(k);
            }
        }

        public void ClearTrails() { _trail.Clear(); InvalidateVisual(); }

        /// <summary>还有没有轨迹点要画（没有就不必按帧重绘了）。</summary>
        public bool HasTrail { get { return _trail.Count > 0; } }

        /// <summary>按时间淘汰旧轨迹点：没有新报文时也要能让轨迹消失，否则界面会一直重绘。</summary>
        public void PruneTrails(long now)
        {
            var stale = new List<int>();
            foreach (var kv in _trail)
            {
                var lp = kv.Value;
                while (lp.Count > 0 && now - lp[0].T > 700) lp.RemoveAt(0);
                if (lp.Count == 0) stale.Add(kv.Key);
            }
            foreach (var k in stale) _trail.Remove(k);
        }

        protected override void OnRender(DrawingContext dc)
        {
            UiPing.Enter("点阵图重绘");
            try { Render(dc); }
            finally { UiPing.Exit(); }
        }

        private void Render(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 60 || h < 40) return;

            double pw = w - 8, ph = pw / PadAspect;
            if (ph > h - 8) { ph = h - 8; pw = ph * PadAspect; }
            _pad = new Rect((w - pw) / 2, (h - ph) / 2, pw, ph);

            dc.DrawRoundedRectangle(PadFill, new Pen(PadStroke, 1), _pad, 10, 10);

            var gridPen = new Pen(GridPen, 1);
            for (int i = 1; i < 10; i++)
            {
                double x = _pad.X + _pad.Width * i / 10.0;
                double y = _pad.Y + _pad.Height * i / 10.0;
                dc.DrawLine(gridPen, new Point(x, _pad.Y), new Point(x, _pad.Bottom));
                dc.DrawLine(gridPen, new Point(_pad.X, y), new Point(_pad.Right, y));
            }

            // 四条边缘带（整板）
            double bl = _pad.Width * BandL, br = _pad.Width * BandR;
            double bt = _pad.Height * BandT, bb = _pad.Height * BandB;

            dc.DrawRectangle(BandFill, null, new Rect(_pad.Left, _pad.Top, bl, _pad.Height));
            dc.DrawRectangle(BandFill, null, new Rect(_pad.Right - br, _pad.Top, br, _pad.Height));
            dc.DrawRectangle(BandFill, null, new Rect(_pad.Left, _pad.Top, _pad.Width, bt));
            dc.DrawRectangle(BandFill, null, new Rect(_pad.Left, _pad.Bottom - bb, _pad.Width, bb));

            var bandPen = new Pen(BandEdge, 1.4);
            dc.DrawLine(bandPen, new Point(_pad.Left + bl, _pad.Top), new Point(_pad.Left + bl, _pad.Bottom));
            dc.DrawLine(bandPen, new Point(_pad.Right - br, _pad.Top), new Point(_pad.Right - br, _pad.Bottom));
            dc.DrawLine(bandPen, new Point(_pad.Left, _pad.Top + bt), new Point(_pad.Right, _pad.Top + bt));
            dc.DrawLine(bandPen, new Point(_pad.Left, _pad.Bottom - bb), new Point(_pad.Right, _pad.Bottom - bb));

            // 四个角落：独立触发区（双击 / 三击），画个虚框标出来
            if (bl > 0 && bt > 0 && br > 0 && bb > 0)
            {
                var cornerPen = new Pen(CornerEdge, 1.2);
                cornerPen.DashStyle = new DashStyle(new double[] { 5, 4 }, 0);
                double cw = bl, ch = bt;
                dc.DrawRectangle(null, cornerPen, new Rect(_pad.Left, _pad.Top, cw, ch));
                dc.DrawRectangle(null, cornerPen, new Rect(_pad.Right - br, _pad.Top, br, ch));
                dc.DrawRectangle(null, cornerPen, new Rect(_pad.Left, _pad.Bottom - bb, cw, bb));
                dc.DrawRectangle(null, cornerPen, new Rect(_pad.Right - br, _pad.Bottom - bb, br, bb));
            }

            // 右下角常驻防误触区
            if (PalmOn)
            {
                var pz = new Rect(_pad.X + _pad.Width * PalmX0, _pad.Y + _pad.Height * PalmY0,
                                  _pad.Width * (1 - PalmX0), _pad.Height * (1 - PalmY0));
                var palmPen = new Pen(PalmEdge, 1.4);
                palmPen.DashStyle = new DashStyle(new double[] { 4, 3 }, 0);
                dc.DrawRectangle(PalmFill, palmPen, pz);
                DrawLabel(dc, "常驻防误触", new Point(pz.X + 5, pz.Y + 4));
            }

            // 轨迹
            if (ShowTrails)
            {
                var trailPen = new Pen(TrailBrush, 2);
                foreach (var kv in _trail)
                {
                    var pts = kv.Value;
                    for (int i = 1; i < pts.Count; i++)
                        dc.DrawLine(trailPen, pts[i - 1].P, pts[i].P);
                }
            }

            // 触点
            foreach (var c in _contacts)
            {
                double x = _pad.X + Math.Min(1.0, Math.Max(0.0, c.X / _maxX)) * _pad.Width;
                double y = _pad.Y + Math.Min(1.0, Math.Max(0.0, c.Y / _maxY)) * _pad.Height;
                dc.DrawEllipse(DotBrush, null, new Point(x, y), 7, 7);
            }
        }

        private void DrawLabel(DrawingContext dc, string text, Point p, Brush brush = null)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, brush ?? LabelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, p);
        }

        private static string Pct(double v) { return (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%"; }

        // 0..3 活动区四条边，4..7 边缘带分界线
        // 0=左带 1=上带 2=右带 3=下带 4=防误触区左上角横线 5=其竖线
        private int HitLine(Point p)
        {
            if (_pad.Width < 1) return -1;
            double tol = 9;

            double lx = _pad.X + _pad.Width * BandL;
            double rx = _pad.Right - _pad.Width * BandR;
            double ty = _pad.Y + _pad.Height * BandT;
            double by = _pad.Bottom - _pad.Height * BandB;

            double pzx = _pad.X + _pad.Width * PalmX0;
            double pzy = _pad.Y + _pad.Height * PalmY0;

            if (PalmOn && Math.Abs(p.X - pzx) <= tol && p.Y >= pzy - tol) return 5;
            if (PalmOn && Math.Abs(p.Y - pzy) <= tol && p.X >= pzx - tol) return 4;

            if (Math.Abs(p.X - lx) <= tol) return 0;
            if (Math.Abs(p.X - rx) <= tol) return 2;
            if (Math.Abs(p.Y - ty) <= tol) return 1;
            if (Math.Abs(p.Y - by) <= tol) return 3;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_drag >= 0)
            {
                if (_pad.Width < 1) return;
                double nx = (p.X - _pad.X) / _pad.Width;
                double ny = (p.Y - _pad.Y) / _pad.Height;

                switch (_drag)
                {
                    case 0: BandL = ClampBand((p.X - _pad.X) / Math.Max(1, _pad.Width)); break;
                    case 2: BandR = ClampBand((_pad.Right - p.X) / Math.Max(1, _pad.Width)); break;
                    case 1: BandT = ClampBand((p.Y - _pad.Y) / Math.Max(1, _pad.Height)); break;
                    case 3: BandB = ClampBand((_pad.Bottom - p.Y) / Math.Max(1, _pad.Height)); break;
                    case 4: PalmY0 = Math.Min(0.95, Math.Max(0.05, (p.Y - _pad.Y) / Math.Max(1, _pad.Height))); break;
                    case 5: PalmX0 = Math.Min(0.95, Math.Max(0.05, (p.X - _pad.X) / Math.Max(1, _pad.Width))); break;
                }

                InvalidateVisual();
                if (BandsChanged != null) BandsChanged();
                return;
            }

            int hit = HitLine(p);
            Cursor = hit == 0 || hit == 2 || hit == 5 ? Cursors.SizeWE
                   : hit == 1 || hit == 3 || hit == 4 ? Cursors.SizeNS
                   : Cursors.Arrow;
        }

        private static double ClampBand(double v) { return Math.Min(0.45, Math.Max(0.02, v)); }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            _drag = HitLine(e.GetPosition(this));
            if (_drag >= 0) CaptureMouse();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (_drag >= 0) { ReleaseMouseCapture(); _drag = -1; }
        }
    }
}
