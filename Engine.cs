using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Input;

namespace EdgePad
{
    public sealed class Engine
    {
        public Settings S = Settings.CreateDefault();
        public bool DryRun;
        public event Action<string> Notify;
        public event Action<string, ActionSpec> Fired;
        public event Action<string> Trace;

        private bool _down, _multi, _armed, _latched, _frozen, _moved;
        private string _edge;
        private double _sx, _sy;
        private int _steps;

        // 双指轨迹手势（L / X / 圈）：记录两指中点的轨迹，抬手时判定
        private bool _twoActive;
        private long _twoStartMs;
        private int _twoMaxX = 1800, _twoMaxY = 1275;
        private double _twoSep0 = 1, _twoSepMin, _twoSepMax;
        private readonly List<double> _twoX = new List<double>();
        private readonly List<double> _twoY = new List<double>();

        private int _fires;    // 本次手势已触发的动作次数（收尾日志用）
        private int _reversals; // 连续动作里反向的次数（抖动/手掌会一直来回）
        private long _firstRevMs;
        private int _lastDir;
        private bool _noisy;    // 已经判定为抖动，停止响应
        private double _rx0, _ry0, _rx, _ry;   // 起手/最后位置（设备坐标，只用于日志）
        private const int MaxStepsPerReport = 6;   // 单帧最多补几档，防止卡帧后一次性倾泻

        public IWindowTarget Target = new SystemWindowTarget();

        private bool _dragging;
        private bool _fiveActive;
        private long _fiveAtMs;
        private double _spread0, _c0x, _c0y;
        private bool _pinchFired;
        private long _lastFiveMs;
        private const int DragArmMs = 150;
        private const double PinchInRatio = 0.88;
        private const double PinchOutRatio = 1.12;
        private const double PinchMaxDrift = 0.06;
        private const double MinSpread = 0.01;
        private IntPtr _dragHwnd;
        private double _dragCx, _dragCy;
        private int _dragX0, _dragY0;

        private sealed class PalmTrack
        {
            public bool Palm;
        }

        private readonly Dictionary<int, PalmTrack> _palm = new Dictionary<int, PalmTrack>();
        private readonly HashSet<int> _palmSeen = new HashSet<int>();
        private readonly List<int> _palmGone = new List<int>();
        private readonly List<Contact> _palmKept = new List<Contact>(8);
        private long _lastTwoTraceMs;   // 双指“未识别”日志限流用



        private long _lastKeyMs;
        private long _downAtMs, _lastSampleMs;

        public void NotifyKeystroke() { _lastKeyMs = Now(); }

        public bool DebugDown { get { return _down; } }
        public double DebugSy { get { return _sy; } }
        public int DebugSteps { get { return _steps; } }

        private bool InPalmZone(double nx, double ny)
        {
            // 右下角常驻防误触区
            return nx >= S.PalmX0 && ny >= S.PalmY0;
        }
        // 轻点链：zone 是 "L/R/T/B"（边缘）或 "TL/TR/BL/BR"（角落）
        private string _tapZone;
        private int _tapCount;
        private long _tapAtMs;
        private double _tapX, _tapY;

        // 有“三击”动作的区域，双击要等三击窗口过去再发，避免抢在第三次点击之前
        private string _pendingZone;
        private long _pendingAtMs;

        private const int TapMaxMs = 250;
        private const int DoubleTapMs = 500;
        private const int TripleTapMs = 500;    // 第二击到第三击的窗口，同时也是双击的延迟
        private const double MoveTol = 0.02;
        private const double TapNearTol = 0.06;

        public Func<long> Clock = () => Environment.TickCount64;
        private long Now() { return Clock(); }

        public bool Frozen { get { return _frozen; } }

        public void Reset()
        {
            ReleaseFreeze();
            _down = false;
            _multi = false;
            _armed = false;
            _latched = false;
            _moved = false;
            _edge = null;
            _steps = 0;
            _fires = 0;
            _reversals = 0;
            _firstRevMs = 0;
            _lastDir = 0;
            _noisy = false;
        }

        /// <summary>抬手（或报文中断）时结算：判断这一段是不是一次轻点。</summary>
        private void EndGesture(long tUp)
        {
            if (_armed)
            {
                long dur = tUp - _downAtMs;
                bool isTap = !_multi && !_moved && !_latched && dur <= TapMaxMs;
                Trace?.Invoke("抬手 " + ZoneName(_tapZone ?? _edge) + " 时长=" + dur + "ms moved=" + _moved
                              + " latched=" + _latched + " tap=" + isTap
                              + (_fires > 0 ? " 触发=" + _fires + "次" : "")
                              + " 起=(" + Math.Round(_rx0) + "," + Math.Round(_ry0) + ")"
                              + " 终=(" + Math.Round(_rx) + "," + Math.Round(_ry) + ")"
                              + (_reversals > 0 ? " 反向=" + _reversals + "次" : ""));
                if (isTap) RegisterTap(_tapZone ?? _edge, tUp, _sx, _sy);
            }
            Reset();
        }

        /// <summary>一次轻点结束。同一区域、时间与位置都接得上就累加成双击/三击。</summary>
        private void RegisterTap(string zone, long tUp, double nx, double ny)
        {
            if (string.IsNullOrEmpty(zone)) return;

            bool chain = _tapCount > 0 && zone == _tapZone
                         && tUp - _tapAtMs <= DoubleTapMs
                         && Math.Abs(nx - _tapX) <= TapNearTol
                         && Math.Abs(ny - _tapY) <= TapNearTol;

            if (chain) _tapCount++;
            else
            {
                if (_tapCount > 0 && _tapAtMs != 0)
                    Trace?.Invoke("轻点未配对 间隔=" + (tUp - _tapAtMs) + "ms 区域=" + ZoneName(zone) + "/" + ZoneName(_tapZone));
                _tapCount = 1;
            }

            _tapZone = zone;
            _tapAtMs = tUp;
            _tapX = nx;
            _tapY = ny;

            if (_tapCount >= 3)
            {
                if (HasAction(zone + ".tap3"))
                {
                    Trace?.Invoke("三击触发 " + ZoneName(zone) + " 间隔=" + (tUp - _pendingAtMs) + "ms");
                    _pendingZone = null;          // 三击来了，双击作废
                    _tapCount = 0;
                    Fire(zone + ".tap3");
                    return;
                }
                _tapCount = 0;                    // 没配三击：第三次当新的一轮
                return;
            }

            if (_tapCount == 2)
            {
                if (HasAction(zone + ".tap3"))
                {
                    // 这个区域还有三击：先挂起，等窗口过期再发双击（Tick 里发）
                    _pendingZone = zone;
                    _pendingAtMs = tUp;
                    Trace?.Invoke("双击待定 " + ZoneName(zone) + "（该区域配了三击，等 " + TripleTapMs + "ms）");
                    return;
                }
                _tapCount = 0;
                if (HasAction(zone + ".tap2"))
                {
                    Trace?.Invoke("双击触发 " + ZoneName(zone) + " 间隔=" + (tUp - _tapAtMs) + "ms");
                    Fire(zone + ".tap2");
                }
                else Trace?.Invoke("双击 " + ZoneName(zone) + "（该区域没配动作）");
                return;
            }
        }

        /// <summary>挂起的双击到点就发（由界面定时器驱动，不受报文有无影响）。</summary>
        private void TryFirePendingDouble(long now)
        {
            if (_pendingZone == null) return;
            if (now - _pendingAtMs < TripleTapMs) return;
            string zone = _pendingZone;
            _pendingZone = null;
            _tapCount = 0;
            if (!HasAction(zone + ".tap2")) return;
            Trace?.Invoke("双击触发 " + ZoneName(zone) + "（三击窗口到期）");
            Fire(zone + ".tap2");
        }

        private bool HasAction(string key)
        {
            var spec = S.Get(key);
            if (spec == null || !spec.Enabled || spec.Kind == "none") return false;
            if (spec.Kind == "windowcycle" || spec.Kind == "windowmax" || spec.Kind == "windowdrag"
                || spec.Kind == "syslock") return true;
            return !string.IsNullOrEmpty(spec.Value);
        }

        public static string ZoneName(string zone)
        {
            if (zone == "TL") return "左上角";
            if (zone == "TR") return "右上角";
            if (zone == "BL") return "左下角";
            if (zone == "BR") return "右下角";
            return EdgeName(zone);
        }


        /// <summary>没有报文时由界面定时器驱动，用来发现抬手。</summary>
        public void Tick()
        {
            TryFirePendingDouble(Now());
            if (Now() - _lastSampleMs <= 80) return;
            if (_twoActive) FinishTwo(_lastSampleMs);
            if (_down) EndGesture(_lastSampleMs);
            if (_dragging) EndDrag();
            if (_multi) { _multi = false; Trace?.Invoke("多指结束（无报文），恢复边缘判定"); }
        }

        /// <summary>识别并剔除“手掌”触点：起点在手掌区、且静止超过 150ms。</summary>
        private List<Contact> ApplyPalmGuard(List<Contact> all, int maxX, int maxY, long now, ref int ccEff)
        {
            if (!S.PalmGuard || all == null) { _palm.Clear(); return all; }

            // 每帧都 new 一遍集合会在 125Hz 上白烧 GC：改成复用
            var seen = _palmSeen;
            seen.Clear();
            foreach (var c in all)
            {
                seen.Add(c.Id);
                double nx = c.X / Math.Max(1, maxX);
                double ny = c.Y / Math.Max(1, maxY);

                PalmTrack t;
                if (!_palm.TryGetValue(c.Id, out t))
                {
                    t = new PalmTrack();
                    _palm[c.Id] = t;

                    // 右下角常驻：起手就判手掌，不等静止、也不因移动而撤销
                    if (InPalmZone(nx, ny))
                    {
                        t.Palm = true;
                        Reset();
                        Trace?.Invoke("防误触：触点 " + c.Id + " 起手在右下角，常驻忽略");
                    }
                }
            }

            var gone = _palmGone;
            gone.Clear();
            foreach (var kv in _palm) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var g in gone) _palm.Remove(g);

            int palms = 0;
            foreach (var c in all)
            {
                PalmTrack t;
                if (_palm.TryGetValue(c.Id, out t) && t.Palm) palms++;
            }
            if (palms == 0) return all;

            ccEff = Math.Max(0, ccEff - palms);
            var kept = _palmKept;
            kept.Clear();
            foreach (var c in all)
            {
                PalmTrack t;
                if (_palm.TryGetValue(c.Id, out t) && t.Palm) continue;
                kept.Add(c);
            }
            return kept;
        }

        public void OnSample(List<Contact> contacts, int cc, int maxX, int maxY)
        {
            long now = Now();

            // 打字期间整体暂停：全板都可能被手掌碰到，此时不接受任何新手势
            if (S.TypingGuard && S.TypingPauseAll && now - _lastKeyMs < S.TypingQuietMs)
            {
                if (_down || _fiveActive) Reset();
                return;
            }

            int ccEff = cc;
            var active = ApplyPalmGuard(contacts, maxX, maxY, now, ref ccEff);
            contacts = active;

            int n = contacts == null ? 0 : contacts.Count;

            if (!S.Enabled)
            {
                if (_down) EndGesture(now);
                EndDrag();
                return;
            }

            if (_down && now - _lastSampleMs > 80) EndGesture(_lastSampleMs);
            _lastSampleMs = now;

            // 双指轨迹：手一离开两根指头就结算（必须放在 n==0 提前返回之前）
            if (_twoActive && n != 2) FinishTwo(now);

            if (n == 0)
            {
                if (_down) EndGesture(now);          // EndGesture 内部 Reset 会清 _multi
                EndDrag();
                if (_multi) { _multi = false; Trace?.Invoke("多指结束，恢复边缘判定"); }
                return;
            }

            // 两指一起画 L / X / 圈：先记轨迹
            if (n == 2 && ccEff == 2) HandleTwo(contacts, maxX, maxY, now);

            // 五指手势（拖动 / 捏合）：不属于边缘手势，单独处理
            if (n >= 5) { _lastFiveMs = now; HandleFive(contacts, maxX, maxY); return; }

            // 五指序列中短暂丢指（<80ms）：保持序列，别把捏合拦腰截断
            if (_fiveActive && now - _lastFiveMs <= 80)
            {
                Trace?.Invoke("五指：短暂丢指，保持序列");
                return;
            }

            _fiveActive = false;
            _pinchFired = false;
            _fiveAtMs = 0;
            if (_dragging) EndDrag();

            if (n >= 2 || ccEff >= 2)
            {
                if (_armed) Notify?.Invoke("多指，取消");
                bool wasMulti = _multi;
                Reset();
                _multi = true;
                if (!wasMulti) Trace?.Invoke("多指：暂停边缘判定");
                return;
            }
            if (_multi) return;

            double nx = contacts[0].X / Math.Max(1, maxX);
            double ny = contacts[0].Y / Math.Max(1, maxY);

            if (!_down)
            {
                _down = true;
                _downAtMs = now;
                _edge = HitEdge(nx, ny);
                _tapZone = HitCorner(nx, ny) ?? _edge;     // 角落优先：角落既是两块边缘的交界，也是独立触发区
                Trace?.Invoke("按下 (" + Math.Round(nx * maxX) + "," + Math.Round(ny * maxY) + ") 区域="
                              + ZoneName(_tapZone) + " 带内=" + (_edge != null));
                if (_edge == null) { _multi = true; return; }
                _armed = true;
                _latched = false;
                _moved = false;
                _steps = 0;

                // 该边缘绑定了双击动作时，短暂屏蔽系统“点按即点击”合成的那次点击
                if (S.SuppressEdgeTapClick && HasAction(_tapZone + ".tap2"))
                {
                    // 该区域还配了三击时，窗口要覆盖到三击，否则第三次轻点的系统点击会漏出去
                    int win = DoubleTapMs + (HasAction(_tapZone + ".tap3") ? TripleTapMs : 0) + 300;
                    ClickSuppressor.Arm(win);
                }
                _sx = nx; _sy = ny;
                _rx0 = contacts[0].X; _ry0 = contacts[0].Y;
                _rx = _rx0; _ry = _ry0;
                if (S.FreezeCursor) Freeze();
                Notify?.Invoke("起手 " + EdgeName(_edge));
                return;
            }

            if (!_armed) return;

            _rx = contacts[0].X; _ry = contacts[0].Y;

            if (Math.Abs(nx - _sx) > MoveTol || Math.Abs(ny - _sy) > MoveTol)
            {
                if (!_moved && S.SuppressEdgeTapClick) ClickSuppressor.Disarm();   // 变成滑动，不再需要屏蔽点击
                _moved = true;
            }

            double dx = (nx - _sx) * maxX;
            double dy = (ny - _sy) * maxY;

            if (_edge == "L" || _edge == "R")
            {
                if (Math.Abs(dx) > Math.Abs(dy)) return;
                HandleAxis(_edge, dy, true);
            }
            else
            {
                if (Math.Abs(dy) > Math.Abs(dx)) return;
                HandleAxis(_edge, dx, false);
            }
        }

        /// <summary>双指轨迹：记两指中点的路径。同时盯着两指间距，变化大的算捏合、不参与轨迹判定。</summary>
        private void HandleTwo(List<Contact> contacts, int maxX, int maxY, long now)
        {
            if (contacts.Count < 2) return;
            double x = (contacts[0].X + contacts[1].X) / 2;
            double y = (contacts[0].Y + contacts[1].Y) / 2;
            double sep = Math.Abs(contacts[0].X - contacts[1].X) + Math.Abs(contacts[0].Y - contacts[1].Y);

            if (!_twoActive)
            {
                _twoActive = true;
                _twoStartMs = now;
                _twoMaxX = maxX;
                _twoMaxY = maxY;
                _twoSep0 = Math.Max(1, sep);
                _twoSepMin = sep;
                _twoSepMax = sep;
                _twoX.Clear();
                _twoY.Clear();
            }

            if (sep < _twoSepMin) _twoSepMin = sep;
            if (sep > _twoSepMax) _twoSepMax = sep;
            if (_twoX.Count >= 400) return;
            _twoX.Add(x);
            _twoY.Add(y);
        }

        /// <summary>双指抬起（或换手）：判定轨迹并触发对应动作。</summary>
        private void FinishTwo(long now)
        {
            if (!_twoActive) return;
            _twoActive = false;

            int n = _twoX.Count;
            long dur = now - _twoStartMs;
            if (n < 10) { _twoX.Clear(); _twoY.Clear(); return; }

            bool pinchy = _twoSep0 > 1 && (_twoSepMax - _twoSepMin) / _twoSep0 > 0.6;
            var kind = pinchy ? Patterns.Kind.None : Patterns.Classify(_twoX, _twoY, _twoMaxX, _twoMaxY);

            // 普通双指滚动每划一次就是一条“未识别”，日志会被刷满：未识别的限流到 2 秒一条
            bool log = kind != Patterns.Kind.None || now - _lastTwoTraceMs >= 2000;
            if (log && kind == Patterns.Kind.None) _lastTwoTraceMs = now;
            if (log)
                Trace?.Invoke("双指轨迹：点 " + n + " 时长 " + dur + "ms 间距变化 "
                              + (_twoSep0 > 1 ? ((_twoSepMax - _twoSepMin) / _twoSep0).ToString("0.00") : "?")
                              + " → " + KindName(kind));
            _twoX.Clear();
            _twoY.Clear();

            if (kind == Patterns.Kind.None) return;
            Notify?.Invoke("双指" + KindName(kind));
            Fire(kind == Patterns.Kind.L ? "PAD.l" : kind == Patterns.Kind.X ? "PAD.x" : "PAD.circle");
        }

        private static string KindName(Patterns.Kind k)
        {
            if (k == Patterns.Kind.L) return "画 L";
            if (k == Patterns.Kind.X) return "画 X";
            if (k == Patterns.Kind.Circle) return "画圈";
            return "未识别";
        }

        private void HandleFive(List<Contact> contacts, int maxX, int maxY)
        {
            long nowMs = Now();
            int n = contacts.Count;

            double cx = 0, cy = 0;
            foreach (var c in contacts) { cx += c.X / Math.Max(1, maxX); cy += c.Y / Math.Max(1, maxY); }
            cx /= n;
            cy /= n;

            double spread = 0;
            foreach (var c in contacts)
            {
                double dx = c.X / Math.Max(1, maxX) - cx;
                double dy = c.Y / Math.Max(1, maxY) - cy;
                spread += Math.Sqrt(dx * dx + dy * dy);
            }
            spread /= n;

            if (!_fiveActive)
            {
                _fiveActive = true;
                _fiveAtMs = nowMs;
                _spread0 = spread;
                _c0x = cx;
                _c0y = cy;
                _pinchFired = false;
                Trace?.Invoke("五指：起手 间距=" + spread.ToString("0.000"));
                return;
            }

            double ratio = _spread0 > MinSpread ? spread / _spread0 : 1.0;
            double drift = Math.Max(Math.Abs(cx - _c0x), Math.Abs(cy - _c0y));

            // 捏合 = 间距明显变化且质心基本不动；拖动 = 质心平移
            if (!_pinchFired && _spread0 > MinSpread && drift < PinchMaxDrift)
            {
                if (ratio <= PinchInRatio) { FirePinch("PAD.pinchin", "向内", ratio); return; }
                if (ratio >= PinchOutRatio) { FirePinch("PAD.pinchout", "向外", ratio); return; }
            }
            if (_pinchFired) return;

            var spec = S.Get("PAD.drag");
            if (!_dragging)
            {
                if (spec.Kind != "windowdrag" || !spec.Enabled) { _multi = true; Trace?.Invoke("五指：拖动窗口未启用，忽略"); return; }

                // 稳定门槛：五指必须先落稳 DragArmMs，期间不跟手。
                // 这样"手指陆续落下"的时间窗里没有位移，系统不会把它解读成三指滑动手势。
                if (nowMs - _fiveAtMs < DragArmMs) return;

                Reset();   // 结束可能正在进行的边缘手势

                IntPtr h;
                int x, y;
                if (!Target.TryCapture(out h, out x, out y))
                {
                    _multi = true;
                    Trace?.Invoke("五指拖动：光标下没有可拖动窗口");
                    return;
                }

                _dragging = true;
                _dragHwnd = h;
                _dragX0 = x;
                _dragY0 = y;
                _dragCx = cx;
                _dragCy = cy;

                if (S.FreezeCursor) Freeze();
                string title = SystemWindowTarget.TitleOf(h);
                Notify?.Invoke("五指拖动：" + title);
                Trace?.Invoke("五指拖动开始 窗口=" + title + " 起点=(" + x + "," + y + ")");
                return;
            }

            int mdx = (int)Math.Round((cx - _dragCx) * Target.ScreenWidth * S.DragGain);
            int mdy = (int)Math.Round((cy - _dragCy) * Target.ScreenHeight * S.DragGain);
            Target.Move(_dragHwnd, _dragX0 + mdx, _dragY0 + mdy);
        }

        private void FirePinch(string key, string name, double ratio)
        {
            _pinchFired = true;
            CancelDrag();
            Trace?.Invoke("五指" + name + "捏合 间距比=" + ratio.ToString("0.00"));
            Notify?.Invoke("五指" + name + "捏合");
            Fire(key);
        }

        private void CancelDrag()
        {
            if (!_dragging) return;
            _dragging = false;
            _dragHwnd = IntPtr.Zero;
            ReleaseFreeze();
            Trace?.Invoke("五指拖动取消（判定为捏合）");
        }

        public void EndDrag()
        {
            if (!_dragging) return;
            _dragging = false;
            ReleaseFreeze();

            if (S.SnapEdges && _dragHwnd != IntPtr.Zero)
            {
                SnapZone zone = Target.EvaluateSnap(_dragHwnd);
                if (zone != SnapZone.None)
                {
                    Target.ApplySnap(_dragHwnd, zone);
                    string name = SystemWindowTarget.ZoneName(zone);
                    Notify?.Invoke("吸附：" + name);
                    Trace?.Invoke("吸附 " + name);
                }
            }

            _dragHwnd = IntPtr.Zero;
            Trace?.Invoke("五指拖动结束");
        }

        private void HandleAxis(string edge, double delta, bool vertical)
        {
            string negKey = vertical ? edge + ".up" : edge + ".left";      // 负方向：上 / 左
            string posKey = vertical ? edge + ".down" : edge + ".right";   // 正方向：下 / 右

            var negSpec = S.Get(negKey);
            var posSpec = S.Get(posKey);

            if (IsContinuous(negSpec) || IsContinuous(posSpec))
            {
                if (_noisy) return;

                // 手感抖动/手掌漂移的拦截：连续动作必须“真的在扫”
                // 1) 先要走够 Threshold 才出第一档（几十单位的原地抖动一档都不出）
                // 2) 来回反向太多次 = 不是手势，直接停
                // 3) 一次连续手势超过 3 秒也不合理
                bool isVol = IsVolume(negSpec) || IsVolume(posSpec);
                if (isVol && Now() - _downAtMs > 3000)
                {
                    _noisy = true;
                    Trace?.Invoke("音量连续手势超过 3 秒，判定为手掌压着漂移，停止响应");
                    return;
                }

                int unit = (IsScroll(negSpec) || IsScroll(posSpec)) ? Math.Max(5, S.ScrollStep) : Math.Max(1, S.VolumeStep);
                double raw = delta / unit;
                int steps = (int)(raw < 0 ? Math.Ceiling(raw) : Math.Floor(raw));

                // 反向滞回：往回走要跨过半档才认。台阶边界上 ±几单位的抖动不会“前一档后一档”地响
                if (steps < _steps && raw > _steps - 0.5) steps = _steps;
                if (steps > _steps && raw < _steps + 0.5) steps = _steps;

                if (steps > 60) steps = 60;
                if (steps < -60) steps = -60;

                if (steps != _steps)
                {
                    int dir = Math.Sign(steps - _steps);
                    if (_lastDir != 0 && dir != _lastDir)
                    {
                        if (_reversals == 0) _firstRevMs = Now();
                        _reversals++;
                        // 反向太频繁才算抖动：人的来回是有间隔的，触点噪声是每帧都翻
                        long span = Now() - _firstRevMs;
                        if (_reversals > 6 && span < _reversals * 150L)
                        {
                            _noisy = true;
                            Trace?.Invoke("连续手势 " + span + "ms 内反向 " + _reversals + " 次，判定为抖动，停止响应");
                            return;
                        }
                    }
                    _lastDir = dir;
                }

                // 单帧最多补 6 档：触点跳变或卡帧后不要一口气倾泻
                if (steps > _steps + MaxStepsPerReport) _steps = steps - MaxStepsPerReport;
                if (steps < _steps - MaxStepsPerReport) _steps = steps + MaxStepsPerReport;

                while (_steps != steps)
                {
                    bool towardNeg = steps < _steps;
                    Fire(towardNeg ? negKey : posKey, true);   // 逐档不写日志，收尾统一记一行
                    _steps += towardNeg ? -1 : 1;
                }
                return;
            }

            if (_latched) return;
            if (Math.Abs(delta) < S.Threshold) return;
            _latched = true;
            Fire(delta < 0 ? negKey : posKey);
        }

        private static bool IsVolume(ActionSpec a)
        {
            return a != null && a.Kind == "keys" && a.Value != null && a.Value.StartsWith("Volume", StringComparison.Ordinal);
        }

        private static bool IsScroll(ActionSpec a)
        {
            return a != null && a.Kind == "scroll";
        }

        private static bool IsContinuous(ActionSpec a)
        {
            return IsVolume(a) || IsScroll(a);
        }

        private void Fire(string key, bool quiet = false)
        {
            var spec = S.Get(key);
            if (!spec.Enabled) { if (!quiet) Trace?.Invoke("动作 " + key + " 已停用，忽略"); return; }
            _fires++;
            Fired?.Invoke(key, spec);
            if (DryRun) return;

            if (spec.Kind == "syslock")
            {
                bool locked = Run(spec);
                Trace?.Invoke("动作 " + key + " = " + spec.Display + " -> " + (locked ? "已锁屏" : "锁屏失败"));
                if (!locked) Notify?.Invoke("锁屏失败");
                return;
            }

            if (spec.Kind == "windowcycle" || spec.Kind == "windowmax")
            {
                IntPtr h;
                if (!Target.TryCurrent(out h))
                {
                    Notify?.Invoke("没有可操作的窗口");
                    Trace?.Invoke("动作 " + key + " 未找到窗口");
                    return;
                }
                bool wok = spec.Kind == "windowcycle" ? Target.CycleWindow(h) : Target.MaximizeWindow(h);
                Trace?.Invoke("动作 " + key + " = " + spec.Display + " -> " + (wok ? "已执行" : "失败"));
                if (!wok) Notify?.Invoke("动作未生效：" + spec.Display);
                return;
            }

            if (spec.Kind == "none" || string.IsNullOrEmpty(spec.Value))
            {
                if (!quiet) Trace?.Invoke("动作 " + key + " 未设置");
                return;
            }

            // 静音要把“实际状态”报出来，所以等注入完成后在注入线程上回读
            if (spec.Kind == "keys" && spec.Value == "VolumeMute")
            {
                var batch = KeySender.BuildChord(spec.Value);
                Injector.Post(batch, () =>
                {
                    bool m = false;
                    float v = 0f;
                    bool km = Audio.TryGetMute(out m);
                    bool kv = Audio.TryGetVolume(out v);
                    bool known = km && kv;
                    Trace?.Invoke("动作 " + key + " = " + spec.Display
                                  + (known ? " 静音=" + m + " 音量=" + v.ToString("0.00") : " 状态未知"));
                    if (known) Notify?.Invoke((m ? "已静音" : "已取消静音") + "（音量 " + (v * 100).ToString("0") + "%）");
                });
                return;
            }

            bool ok = Run(spec);
            if (!quiet) Trace?.Invoke("动作 " + key + " = " + spec.Display + " -> " + (ok ? "已提交" : "注入失败"));
            if (!ok) Notify?.Invoke("动作未生效：" + spec.Display);
        }

        /// <summary>是不是“截屏类”动作：发 PrtSc 的，或者框选截图（Win+Shift+S）。</summary>
        public static bool IsScreenshot(ActionSpec spec)
        {
            if (spec == null || spec.Kind != "keys" || string.IsNullOrEmpty(spec.Value)) return false;
            string v = spec.Value;
            return v.IndexOf("PrintScreen", StringComparison.OrdinalIgnoreCase) >= 0
                || v.IndexOf("PrtSc", StringComparison.OrdinalIgnoreCase) >= 0
                || v.Equals("Win+Shift+S", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>媒体控制键：上一首 / 下一首 / 播放暂停，发完要读播放器信息做回执。</summary>
        public static bool IsMedia(ActionSpec spec)
        {
            if (spec == null || spec.Kind != "keys" || string.IsNullOrEmpty(spec.Value)) return false;
            return spec.Value == "MediaNext" || spec.Value == "MediaPrev" || spec.Value == "MediaPlayPause";
        }

        public static string MediaActionName(ActionSpec spec)
        {
            if (spec == null) return "媒体键";
            if (spec.Value == "MediaNext") return "下一首";
            if (spec.Value == "MediaPrev") return "上一首";
            if (spec.Value == "MediaPlayPause") return "播放 / 暂停";
            return spec.Display;
        }

        /// <summary>框选截图（Win+Shift+S）：截图工具会弹出来等用户拖选，不能立刻判定“已完成”。</summary>
        public static bool IsSnipping(ActionSpec spec)
        {
            return spec != null && spec.Kind == "keys" && !string.IsNullOrEmpty(spec.Value)
                   && spec.Value.Equals("Win+Shift+S", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 锁屏必须走 LockWorkStation：SendInput 合成的 Win+L 会被系统忽略（受保护的热键），
        /// 所以手填的 "Win+L" 也统一走系统调用。
        /// </summary>
        public static bool IsLockAction(ActionSpec spec)
        {
            if (spec == null) return false;
            if (spec.Kind == "syslock") return true;
            return spec.Kind == "keys" && !string.IsNullOrEmpty(spec.Value)
                   && spec.Value.Equals("Win+L", StringComparison.OrdinalIgnoreCase);
        }

        public static bool Run(ActionSpec spec)
        {
            if (spec == null) return false;
            if (IsLockAction(spec))
            {
                try { return Native.LockWorkStation(); }
                catch { return false; }
            }
            if (spec.Kind == "none" || string.IsNullOrEmpty(spec.Value)) return false;
            if (spec.Kind == "keys") return KeySender.SendChord(spec.Value);
            if (spec.Kind == "scroll") return Wheel(spec.Value == "up" ? 1 : -1);
            if (spec.Kind == "open")
            {
                try
                {
                    Process.Start(new ProcessStartInfo("cmd.exe", "/c " + spec.Value) { CreateNoWindow = true, UseShellExecute = false });
                    return true;
                }
                catch { return false; }
            }
            return false;
        }

        /// <summary>滚一格（不是直接发，交给注入线程——SendInput 会阻塞 1~15ms）。</summary>
        public static bool Wheel(int notches)
        {
            if (notches == 0) return false;
            Injector.Post(MakeWheel(notches));
            return true;
        }

        /// <summary>同步发一格：只给测量用，正常路径不要调用。</summary>
        public static bool WheelNow(int notches)
        {
            if (notches == 0) return false;
            var arr = MakeWheel(notches);
            return Native.SendInput(1, arr, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.INPUT))) == 1;
        }

        private static Native.INPUT[] MakeWheel(int notches)
        {
            var arr = new Native.INPUT[1];
            arr[0].type = 0;                                  // INPUT_MOUSE
            arr[0].u.mi.dwFlags = 0x0800;                     // MOUSEEVENTF_WHEEL
            arr[0].u.mi.mouseData = unchecked((uint)(120 * notches));
            return arr;
        }

        /// <summary>角落：同时落在一条竖边带和一条横边带里。四角共 10%×10% 的板面。</summary>
        private string HitCorner(double nx, double ny)
        {
            bool left = nx <= S.BandL, right = nx >= 1 - S.BandR;
            bool top = ny <= S.BandT, bottom = ny >= 1 - S.BandB;
            if (left && top) return "TL";
            if (right && top) return "TR";
            if (left && bottom) return "BL";
            if (right && bottom) return "BR";
            return null;
        }

        private string HitEdge(double nx, double ny)
        {
            if (nx <= S.BandL) return "L";
            if (nx >= 1 - S.BandR) return "R";
            if (ny <= S.BandT) return "T";
            if (ny >= 1 - S.BandB) return "B";
            return null;
        }

        public static string EdgeName(string e)
        {
            if (e == "L") return "左边缘";
            if (e == "R") return "右边缘";
            if (e == "T") return "顶边缘";
            if (e == "B") return "底边缘";
            return "中部";
        }

        private void Freeze()
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return;
            var r = new Native.RECT { Left = p.X, Top = p.Y, Right = p.X + 1, Bottom = p.Y + 1 };
            if (Native.ClipCursor(ref r)) _frozen = true;
        }

        private void ReleaseFreeze()
        {
            if (_frozen)
            {
                Native.ClipCursor(IntPtr.Zero);
                _frozen = false;
            }
        }

        public void Shutdown() { Reset(); }
    }

    public static class KeySender
    {
        private static readonly Dictionary<string, ushort> Map = Build();

        private static Dictionary<string, ushort> Build()
        {
            var m = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 26; i++) m[((char)('A' + i)).ToString()] = (ushort)(0x41 + i);
            for (int i = 0; i < 10; i++) m[((char)('0' + i)).ToString()] = (ushort)(0x30 + i);
            m["Ctrl"] = 0xA2; m["Control"] = 0xA2;
            m["Shift"] = 0xA0;
            m["Alt"] = 0xA4;
            m["Win"] = 0x5B; m["Windows"] = 0x5B;
            m["Tab"] = 0x09; m["Enter"] = 0x0D; m["Esc"] = 0x1B; m["Escape"] = 0x1B;
            m["Space"] = 0x20; m["Backspace"] = 0x08; m["Delete"] = 0x2E; m["Insert"] = 0x2D;
            m["Up"] = 0x26; m["Down"] = 0x28; m["Left"] = 0x25; m["Right"] = 0x27;
            m["Home"] = 0x24; m["End"] = 0x23; m["PgUp"] = 0x21; m["PgDn"] = 0x22;
            m["VolumeUp"] = 0xAF; m["VolumeDown"] = 0xAE; m["VolumeMute"] = 0xAD;
            m["PrintScreen"] = 0x2C; m["PrtSc"] = 0x2C; m["PrtScn"] = 0x2C; m["Snapshot"] = 0x2C;
            m["MediaNext"] = 0xB0; m["MediaPrev"] = 0xB1; m["MediaPlayPause"] = 0xB3;
            for (int i = 1; i <= 12; i++) m["F" + i] = (ushort)(0x70 + i - 1);
            return m;
        }

        public static string KeyName(Key k)
        {
            if (k >= Key.A && k <= Key.Z) return ((char)('A' + (k - Key.A))).ToString();
            if (k >= Key.D0 && k <= Key.D9) return ((char)('0' + (k - Key.D0))).ToString();
            if (k >= Key.F1 && k <= Key.F12) return "F" + (k - Key.F1 + 1);
            if (k >= Key.NumPad0 && k <= Key.NumPad9) return "Num" + (k - Key.NumPad0);
            switch (k)
            {
                case Key.Tab: return "Tab";
                case Key.Enter: return "Enter";
                case Key.Escape: return "Esc";
                case Key.Space: return "Space";
                case Key.Back: return "Backspace";
                case Key.Delete: return "Delete";
                case Key.Insert: return "Insert";
                case Key.Up: return "Up";
                case Key.Down: return "Down";
                case Key.Left: return "Left";
                case Key.Right: return "Right";
                case Key.Home: return "Home";
                case Key.End: return "End";
                case Key.PageUp: return "PgUp";
                case Key.PageDown: return "PgDn";
                case Key.OemPlus: return "+";
                case Key.OemMinus: return "-";
                case Key.OemComma: return ",";
                case Key.OemPeriod: return ".";
                case Key.OemQuestion: return "/";
                case Key.OemSemicolon: return ";";
                case Key.OemQuotes: return "'";
                case Key.OemOpenBrackets: return "[";
                case Key.OemCloseBrackets: return "]";
                case Key.OemPipe: return "\\";
            }
            return null;
        }

        public static bool SendChord(string spec)
        {
            var batch = BuildChord(spec);
            if (batch == null) return false;
            Injector.Post(batch);
            return true;
        }

        /// <summary>把组合键展开成一次 SendInput 的按键序列（按下修饰键 → 主键 → 抬起），整批原子提交。</summary>
        public static Native.INPUT[] BuildChord(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return null;
            string[] parts = spec.Split('+');
            var mods = new List<ushort>();
            ushort key = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                string t = parts[i].Trim();
                if (t.Length == 0) continue;
                ushort vk;
                if (!Map.TryGetValue(t, out vk)) vk = 0;
                if (i == parts.Length - 1) key = vk; else mods.Add(vk);
            }
            if (key == 0) return null;

            var batch = new List<Native.INPUT>(mods.Count * 2 + 2);
            foreach (var m in mods) batch.Add(KeyInput(m, true));
            batch.Add(KeyInput(key, true));
            batch.Add(KeyInput(key, false));
            for (int i = mods.Count - 1; i >= 0; i--) batch.Add(KeyInput(mods[i], false));
            return batch.ToArray();
        }

        private static Native.INPUT KeyInput(ushort vk, bool down)
        {
            var arr = new Native.INPUT[1];
            arr[0].type = 1;
            arr[0].u.ki.wVk = vk;
            arr[0].u.ki.dwFlags = down ? 0u : 0x0002u;
            return arr[0];
        }
    }
}
