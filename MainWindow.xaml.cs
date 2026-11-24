using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace EdgePad
{
    public partial class MainWindow : Window
    {
        private readonly TouchpadSource _source = new TouchpadSource();
        private readonly Engine _engine = new Engine();
        private HwndSource _hwnd;
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly DispatcherTimer _ui;
        private int _reports;
        private int _reportsAtTick;
        private long _tickAt;
        private List<Contact> _contacts = new List<Contact>();
        private MiniWindow _mini;
        private bool _startMini;
        private double _lastRate;
        private readonly double[] _gapRing = new double[256];
        private readonly double[] _procRing = new double[256];
        private int _gapIdx, _gapCount, _procIdx, _procCount;
        private long _lastReportTicks;
        private double _gapP50, _procP50, _procP99;
        private string _tab = "edge";
        private bool _applying;
        private bool _dirty;
        private int _curMaxX = 1800;
        private int _curMaxY = 1275;
        private long _lastReportMs;
        private long _lastRescanMs;
        private Tray _tray;
        private bool _startHidden;
        private bool _reallyExit;
        private bool _trayTipShown;
        private bool _exiting;
        private int _maxContacts, _maxContactCount, _mismatchLogged;

        public MainWindow()
        {
            InitializeComponent();

            foreach (var a in Environment.GetCommandLineArgs())
            {
                if (a == "--mini") _startMini = true;
                if (a == "--tray") _startHidden = true;
            }

            _engine.S = Settings.Load();
            _engine.Fired += OnActionFired;
            Autostart.SyncIfEnabled();
            _engine.Notify += OnNotify;
            _engine.Trace += LogTrace;
            ClickSuppressor.Log += LogTrace;
            ClickSuppressor.EnsureInstalled();   // 钩子线程提前起好（不阻塞）：装到界面线程上会让全系统鼠标跟着界面重绘卡
            LogTrace("== EdgePad 启动 ==");

            ApplySettings();
            TabEdge.Click += (s, e) => SetTab("edge");
            TabTap.Click += (s, e) => SetTab("tap");
            TabPad.Click += (s, e) => SetTab("pad");
            SetTab(_engine.S.LastTab == "pad" ? "pad" : _engine.S.LastTab == "tap" ? "tap" : "edge");

            // 定时器间隔自适应：有报文时 33ms（点阵图 30fps，跟手），空闲时 100ms。
            // 空闲时 3 倍的空转没有实测收益（CPU 差异被人手操作淹没），但少 2/3 的唤醒没坏处。
            _ui = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _ui.Tick += OnTick;
            _ui.Start();

            SourceInitialized += (s, e) => InitHid();

            ChkEnabled.Checked += OnEnabledChanged;
            ChkEnabled.Unchecked += OnEnabledChanged;
            ChkFreeze.Checked += OnFreezeChanged;
            ChkFreeze.Unchecked += OnFreezeChanged;
            ChkSnap.Checked += OnSnapChanged;
            ChkSnap.Unchecked += OnSnapChanged;
            ChkPalm.Checked += OnPalmChanged;
            ChkPalm.Unchecked += OnPalmChanged;
            ChkNoClick.Checked += OnNoClickChanged;
            ChkNoClick.Unchecked += OnNoClickChanged;
            BtnReset.Click += OnReset;
            BtnFloat.Click += (s, e) => ToggleMini();
            ContentRendered += (s, e) =>
            {
                if (_startMini) ToggleMini();
                if (_startHidden) { HideToTray(false); _startHidden = false; }
            };
            BtnClear.Click += (s, e) => Pad.ClearTrails();

            ChkTray.Checked += OnTraySettingChanged;
            ChkTray.Unchecked += OnTraySettingChanged;
            ChkStartup.Checked += OnStartupChanged;
            ChkStartup.Unchecked += OnStartupChanged;

            StateChanged += OnStateChanged;
            Closing += OnClosing;

            if (_startHidden) { ShowInTaskbar = false; WindowState = WindowState.Minimized; }
            BuildTray();
        }

        private void InitHid()
        {
            var helper = new WindowInteropHelper(this);
            _hwnd = HwndSource.FromHwnd(helper.Handle);
            if (_hwnd != null) _hwnd.AddHook(WndProc);

            int dark = 1;
            Native.DwmSetWindowAttribute(helper.Handle, 20, ref dark, 4);
            Native.DwmSetWindowAttribute(helper.Handle, 19, ref dark, 4);

            _source.DeviceLog += LogTrace;
            UiPing.Log += LogTrace;
            UiPing.Start(helper.Handle);   // 界面线程卡顿探针（量界面对后台消息的响应延迟）

            string msg;
            bool ok = _source.Initialize(out msg);

            // 无论当前有没有设备都注册：设备随时可能插上/唤醒/重新枚举
            _source.Register(helper.Handle);

            TxtDev.Text = ok ? msg : "未找到精准触摸板";
            TxtStatus.Text = ok
                ? "就绪 —— 在触控板边缘滑动试试"
                : msg + "。普通鼠标、老式 Synaptics/Elan（PS/2）触控板都不支持。";
            Pad.PadAspect = (double)_source.MaxX / Math.Max(1, _source.MaxY);
            _curMaxX = _source.MaxX;
            _curMaxY = _source.MaxY;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == UiPing.WM_PING)
            {
                UiPing.OnPingHandled();
                handled = true;
                return IntPtr.Zero;
            }

            if (msg == 0x00FF)
            {
                long t0 = Stopwatch.GetTimestamp();
                List<Contact> cs;
                int cc, mx, my;
                bool hid;
                if (_source.TryRead(l, out cs, out cc, out mx, out my, out hid))
                {
                    _reports++;
                    _dirty = true;
                    if (_ui != null && _ui.Interval.TotalMilliseconds > 40) _ui.Interval = TimeSpan.FromMilliseconds(33);
                    _lastReportMs = Environment.TickCount64;

                    // 诊断：记录触点峰值，用来判断设备到底能不能上报 5 指
                    if (_contacts.Count > _maxContacts || cc > _maxContactCount)
                    {
                        if (_contacts.Count > _maxContacts) _maxContacts = _contacts.Count;
                        if (cc > _maxContactCount) _maxContactCount = cc;
                        LogTrace("触点峰值 触点数=" + _maxContacts + " ContactCount=" + _maxContactCount);
                    }

                    // 诊断：ContactCount 与解出的触点数不一致，说明某个指槽没解出来
                    if (_contacts.Count >= 2 && cc != _contacts.Count && _mismatchLogged < 30)
                    {
                        _mismatchLogged++;
                        LogTrace("触点不一致 解出=" + _contacts.Count + " ContactCount=" + cc);
                    }
                    _contacts = cs ?? new List<Contact>();
                    _curMaxX = mx;
                    _curMaxY = my;
                    _engine.OnSample(_contacts, cc, mx, my);
                    OnReportTiming(t0);      // 解析 + 手势处理的整段耗时（注入已挪到后台线程）
                }
                else if (hid)
                {
                    // 这里剩下的 HID 报文只可能来自键盘（我们只注册了触控板与键盘两种 usage）
                    _engine.NotifyKeystroke();
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void OnTick(object sender, EventArgs e)
        {
            UiPing.Enter("界面定时器（指标/重扫）");
            try { OnTickCore(); }
            finally { UiPing.Exit(); }
        }

        private void OnTickCore()
        {
            _engine.Tick();

            long nowMs = Environment.TickCount64;

            // 手离开 300ms 后把定时器降频，空闲时不再 30fps 空转
            if (nowMs - _lastReportMs > 300 && _ui.Interval.TotalMilliseconds < 90)
                _ui.Interval = TimeSpan.FromMilliseconds(100);

            // 只有真的来了新报文、或轨迹还在淡出时才重绘。
            // 原来每个 tick 都 InvalidateVisual：没碰触控板也 30fps 重绘整块点阵图，纯烧 CPU。
            bool changed = _dirty || _source.DeviceCount == 0;
            if (changed)
            {
                Pad.SetContacts(_contacts, _curMaxX, _curMaxY);
                _dirty = false;
                Pad.InvalidateVisual();
            }
            else if (Pad.HasTrail)
            {
                Pad.PruneTrails(nowMs);
                Pad.InvalidateVisual();
            }

            // 设备可能被重新枚举：长时间没有报文时重新扫描一次
            if (nowMs - _lastRescanMs > 3000 && nowMs - _lastReportMs > 2000)
            {
                _lastRescanMs = nowMs;
                int before = _source.DeviceCount;
                long r0 = System.Diagnostics.Stopwatch.GetTimestamp();
                string prevBusy = UiPing.Busy;
                UiPing.Enter("设备重扫（枚举 HID）");
                try { _source.Rescan(); }
                finally { UiPing.Busy = prevBusy; }
                double rms = (System.Diagnostics.Stopwatch.GetTimestamp() - r0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (rms >= 4) LogTrace("设备重扫耗时 " + rms.ToString("0.0") + "ms");
                if (_source.DeviceCount != before || _source.DeviceCount > 0)
                    TxtDev.Text = _source.DeviceLabel + "，坐标范围 " + _source.MaxX + " × " + _source.MaxY;
            }

            long now = _sw.ElapsedMilliseconds;
            bool rateTick = false;
            if (_tickAt == 0) { _tickAt = now; _reportsAtTick = _reports; }
            else if (now - _tickAt >= 500)
            {
                rateTick = true;
                _lastRate = (_reports - _reportsAtTick) * 1000.0 / (now - _tickAt);
                _reportsAtTick = _reports;
                _tickAt = now;

                _gapP50 = Percentile(_gapRing, _gapCount, 0.5);
                _procP50 = Percentile(_procRing, _procCount, 0.5);
                _procP99 = Percentile(_procRing, _procCount, 0.99);

                TxtRate.Text = "报文 " + _lastRate.ToString("0") + "/s"
                             + " · 帧 " + _gapP50.ToString("0.0") + "ms"
                             + " · 处理 " + _procP50.ToString("0.00") + "ms (P99 " + _procP99.ToString("0.00") + ")"
                             + " · 触点峰值 " + _maxContacts;
            }

            if (_mini != null && (changed || rateTick))
            {
                _mini.Pad.PalmOn = Pad.PalmOn;
                _mini.Pad.PalmX0 = Pad.PalmX0;
                _mini.Pad.PalmY0 = Pad.PalmY0;
                _mini.UpdateState(_contacts, _curMaxX, _curMaxY, _lastRate, _gapP50, _procP50,
                                  _maxContacts, _source.DeviceCount, _contacts.Count,
                                  Pad.BandL, Pad.BandR, Pad.BandT, Pad.BandB);
            }
        }

        private static void LogTrace(string text)
        {
            Log.Write(text);   // 只入队，落盘在后台线程（原来同步写盘实测 320µs/行）
        }

        private void BuildTray()
        {
            if (_tray != null) return;
            _tray = new Tray(_engine.S.Enabled, _mini != null);
            _tray.OpenClicked += ShowMain;
            _tray.MiniClicked += ToggleMini;
            _tray.EnabledToggled += () =>
            {
                bool on = !(_engine.S.Enabled);
                _engine.S.Enabled = on;
                ChkEnabled.IsChecked = on;      // 走 OnEnabledChanged 统一处理
                _engine.S.Save();
            };
            _tray.ExitClicked += () => { _reallyExit = true; ExitApp(); };
            UpdateTrayState();
        }

        private void UpdateTrayState()
        {
            if (_tray != null) _tray.SetState(_engine.S.Enabled, _mini != null);
        }

        private void ShowMain()
        {
            if (_mini != null) CloseMini();
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        /// <summary>隐藏到托盘（不退出）。tip=false 时不弹气泡，用于静默启动。</summary>
        private void HideToTray(bool showTip)
        {
            Hide();
            if (showTip && _tray != null && !_trayTipShown)
            {
                _trayTipShown = true;
                _tray.Notify("EdgePad 仍在运行", "边缘手势继续生效；双击托盘图标可以打开主界面，右键可以退出。");
            }
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            if (WindowState != WindowState.Minimized) return;
            if (!_engine.S.MinimizeToTray && !_startHidden) return;
            HideToTray(true);
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_exiting) return;
            if (!_reallyExit && _engine.S.MinimizeToTray)
            {
                e.Cancel = true;
                HideToTray(true);
                return;
            }
            ExitApp();
        }

        private void ExitApp()
        {
            if (_exiting) return;
            _exiting = true;
            _reallyExit = true;

            try { _engine.Shutdown(); } catch { }
            try { _engine.S.Save(); } catch { }
            try { ClickSuppressor.Uninstall(); } catch { }

            var mini = _mini;
            _mini = null;
            if (mini != null) { try { mini.Close(); } catch { } }

            Injector.Flush(300);
            Injector.Shutdown();

            if (_tray != null) { _tray.Dispose(); _tray = null; }
            Toast.CloseCurrent();

            Log.Write("== EdgePad 退出 ==");
            Log.Shutdown();

            Application.Current.Shutdown();
        }

        private void OnTraySettingChanged(object sender, RoutedEventArgs e)
        {
            if (_applying) return;
            _engine.S.MinimizeToTray = ChkTray.IsChecked == true;
            _engine.S.Save();
        }

        private void OnStartupChanged(object sender, RoutedEventArgs e)
        {
            if (_applying) return;
            bool on = ChkStartup.IsChecked == true;
            if (!Autostart.Set(on))
            {
                _applying = true;
                ChkStartup.IsChecked = !on;
                _applying = false;
                OnNotify("开机启动设置失败（注册表写入被拒绝）");
                return;
            }
            _engine.S.StartWithWindows = on;
            _engine.S.Save();
            OnNotify(on ? "已设为开机启动（托盘静默启动）" : "已取消开机启动");
        }

        private void ToggleMini()
        {
            if (_mini != null) { CloseMini(); return; }

            _mini = new MiniWindow();
            _mini.ExpandRequested += () =>
            {
                CloseMini();
                Show();
                WindowState = WindowState.Normal;
                Activate();
            };
            _mini.Closed += (s, e) =>
            {
                _mini = null;
                UpdateTrayState();
                if (!IsVisible && !_exiting) Show();
            };

            _mini.Show();
            Hide();
            UpdateTrayState();
            LogTrace("切换到悬浮窗");
        }

        private void CloseMini()
        {
            if (_mini == null) return;
            var m = _mini;
            _mini = null;
            m.Close();
            UpdateTrayState();
        }

        private void OnReportTiming(long startTicks)
        {
            long end = Stopwatch.GetTimestamp();
            double proc = (end - startTicks) * 1000.0 / Stopwatch.Frequency;

            if (_lastReportTicks != 0)
            {
                double gap = (startTicks - _lastReportTicks) * 1000.0 / Stopwatch.Frequency;
                if (gap > 0 && gap < 300)
                {
                    _gapRing[_gapIdx] = gap;
                    _gapIdx = (_gapIdx + 1) % _gapRing.Length;
                    if (_gapCount < _gapRing.Length) _gapCount++;
                }
            }
            _lastReportTicks = startTicks;

            _procRing[_procIdx] = proc;
            _procIdx = (_procIdx + 1) % _procRing.Length;
            if (_procCount < _procRing.Length) _procCount++;
        }

        private static double Percentile(double[] ring, int count, double p)
        {
            if (count <= 0) return 0;
            var arr = new double[count];
            Array.Copy(ring, arr, count);
            Array.Sort(arr);
            int i = (int)Math.Round((count - 1) * p);
            if (i < 0) i = 0;
            if (i >= count) i = count - 1;
            return arr[i];
        }

        // ---- 截图回执：发完快捷键后用剪贴板序号判断到底有没有截到 ----
        private DispatcherTimer _shotTimer;
        private uint _shotSeq;
        private int _shotTry;
        private ActionSpec _shotSpec;

        private void OnActionFired(string key, ActionSpec spec)
        {
            if (Engine.IsMedia(spec)) { StartMediaWatch(spec); return; }
            if (!Engine.IsScreenshot(spec)) return;

            if (Engine.IsSnipping(spec))
            {
                Toast.Show("截图工具已打开", "拖选区域后会自动复制到剪贴板");
                OnNotify("截图：框选区域中…");
                return;
            }

            _shotSpec = spec;
            _shotSeq = Native.GetClipboardSequenceNumber();   // 记下当前剪贴板序号，等它变化
            _shotTry = 0;
            StartShotTimer(260);
        }

        // ---- 媒体键回执：发完等播放器更新，读 SMTC 的曲目信息 ----
        private bool _mediaWatching;

        private async void StartMediaWatch(ActionSpec spec)
        {
            if (_mediaWatching) return;      // 连按上一首/下一首时只跟一个
            _mediaWatching = true;
            try
            {
                string action = Engine.MediaActionName(spec);

                MediaWatch.Info before = null;
                try { before = await MediaWatch.Read(); } catch { }
                string baseKey = before == null ? null : before.Key;

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    await Task.Delay(attempt == 0 ? 350 : 480);
                    MediaWatch.Info now = null;
                    try { now = await MediaWatch.Read(); } catch { }

                    if (now != null && (baseKey == null || now.Key != baseKey))
                    {
                        Toast.Show(action, now.Describe());
                        OnNotify(action + "：" + now.Describe());
                        return;
                    }
                }

                // 没有变化（或播放器不支持 SMTC）：给个诚实的回执
                MediaWatch.Info last = null;
                try { last = await MediaWatch.Read(); } catch { }
                if (last != null)
                {
                    Toast.Show(action, "已发送媒体键 · " + last.Describe());
                    OnNotify(action + "：已发送媒体键（曲目没变化）");
                }
                else
                {
                    Toast.Show(action, "已发送媒体键（没读到播放器信息）");
                    OnNotify(action + "：已发送媒体键");
                }
            }
            finally { _mediaWatching = false; }
        }

        private void StartShotTimer(int ms)
        {
            if (_shotTimer == null)
            {
                _shotTimer = new DispatcherTimer();
                _shotTimer.Tick += OnShotTimer;
            }
            _shotTimer.Stop();
            _shotTimer.Interval = TimeSpan.FromMilliseconds(ms);
            _shotTimer.Start();
        }

        private void OnShotTimer(object sender, EventArgs e)
        {
            _shotTimer.Stop();
            _shotTry++;

            uint seq = Native.GetClipboardSequenceNumber();
            if (seq == _shotSeq)
            {
                // 目标程序可能还在处理，再等两轮
                if (_shotTry < 3) { StartShotTimer(320); return; }
                Toast.Show("没检测到截图", "快捷键发了，但剪贴板没变化——这个组合键可能没生效");
                OnNotify("截图：没检测到剪贴板变化");
                return;
            }

            string size = "";
            try
            {
                if (System.Windows.Clipboard.ContainsImage())
                {
                    var img = System.Windows.Clipboard.GetImage();
                    if (img != null) size = img.PixelWidth + "×" + img.PixelHeight;
                }
            }
            catch { }

            if (size.Length > 0)
            {
                Toast.Show("截图已完成", size + "，已复制到剪贴板");
                OnNotify("截图已完成 " + size);
            }
            else
            {
                Toast.Show("剪贴板已更新", "但不是图片——截图可能没落在剪贴板里");
                OnNotify("截图：剪贴板里不是图片");
            }
        }

        private void OnNotify(string text)
        {
            // 静音状态回读是在注入线程上触发的
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnNotify(text)));
                return;
            }
            TxtStatus.Text = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
        }

        private void ApplySettings()
        {
            _applying = true;
            ChkEnabled.IsChecked = _engine.S.Enabled;
            ChkFreeze.IsChecked = _engine.S.FreezeCursor;
            ChkSnap.IsChecked = _engine.S.SnapEdges;
            ChkNoClick.IsChecked = _engine.S.SuppressEdgeTapClick;
            ChkTray.IsChecked = _engine.S.MinimizeToTray;
            ChkStartup.IsChecked = Autostart.IsEnabled();
            _engine.S.StartWithWindows = ChkStartup.IsChecked == true;
            Pad.BandL = _engine.S.BandL;
            Pad.BandR = _engine.S.BandR;
            Pad.BandT = _engine.S.BandT;
            Pad.BandB = _engine.S.BandB;
            Pad.PalmOn = _engine.S.PalmGuard;
            Pad.PalmX0 = _engine.S.PalmX0;
            Pad.PalmY0 = _engine.S.PalmY0;
            ChkPalm.IsChecked = _engine.S.PalmGuard;
            Pad.BandsChanged -= OnBandsChanged;
            Pad.BandsChanged += OnBandsChanged;
            Pad.InvalidateVisual();
            _applying = false;
        }

        private void OnBandsChanged()
        {
            _engine.S.BandL = Pad.BandL;
            _engine.S.BandR = Pad.BandR;
            _engine.S.BandT = Pad.BandT;
            _engine.S.BandB = Pad.BandB;
            _engine.S.PalmX0 = Pad.PalmX0;
            _engine.S.PalmY0 = Pad.PalmY0;
        }

        private void OnEnabledChanged(object sender, RoutedEventArgs e)
        {
            if (_applying) return;
            _engine.S.Enabled = ChkEnabled.IsChecked == true;
            if (!_engine.S.Enabled) _engine.Reset();
            UpdateTrayState();
        }

        private void OnFreezeChanged(object sender, RoutedEventArgs e)
        {
            if (_applying) return;
            _engine.S.FreezeCursor = ChkFreeze.IsChecked == true;
        }

        private void OnSnapChanged(object sender, RoutedEventArgs e)
        {
            if (_applying) return;
            _engine.S.SnapEdges = ChkSnap.IsChecked == true;
        }

        private void OnNoClickChanged(object sender, RoutedEventArgs e)
        {
            if (_applying) return;
            _engine.S.SuppressEdgeTapClick = ChkNoClick.IsChecked == true;
            if (!_engine.S.SuppressEdgeTapClick) ClickSuppressor.Disarm();
        }

        private void OnPalmChanged(object sender, RoutedEventArgs e)
        {
            if (_applying) return;
            _engine.S.PalmGuard = ChkPalm.IsChecked == true;
            Pad.PalmOn = _engine.S.PalmGuard;
            Pad.InvalidateVisual();
        }

        private void OnReset(object sender, RoutedEventArgs e)
        {
            _engine.Reset();
            _engine.S = Settings.CreateDefault();
            ApplySettings();
            SetTab(_tab);
            _engine.S.Save();
            OnNotify("已恢复默认设置");
        }

        private void SetTab(string tab)
        {
            _tab = tab == "pad" ? "pad" : tab == "tap" ? "tap" : "edge";
            if (_engine.S.LastTab != _tab) { _engine.S.LastTab = _tab; _engine.S.Save(); }

            var accent = (Brush)FindResource("Accent");
            var dim = (Brush)FindResource("Dim");
            var active = new SolidColorBrush(Color.FromRgb(0x2A, 0x23, 0x40));

            Button[] tabs = { TabEdge, TabTap, TabPad };
            string[] ids = { "edge", "tap", "pad" };
            for (int i = 0; i < tabs.Length; i++)
            {
                bool on = ids[i] == _tab;
                tabs[i].Background = on ? active : Brushes.Transparent;
                tabs[i].Foreground = on ? accent : dim;
            }

            if (_tab == "edge")
                TxtHint.Text = "紫线是四条边缘带（可拖动）：从带内起手、沿带方向滑动才触发，起手在中间不触发。\n"
                             + "四个角落既是两块边缘的交界，也是「边缘点击」里单独的触发区。";
            else if (_tab == "tap")
                TxtHint.Text = "边缘双击、角落双击/三击都在这儿；角落里起手的滑动仍走边缘手势。\n"
                             + "同时配了双击+三击的区域，双击会等 0.5 秒三击窗口；右下角在防误触框内会被忽略。";
            else
                TxtHint.Text = "五指：停稳约 0.2 秒后平移拖动窗口，或收拢/张开做捏合（可能与系统四指手势冲突）。\n"
                             + "双指：可以“画”形状——L（先竖后横或反过来）、X（一笔画交叉）、圈。\n"
                             + "注意系统「双指滚动」也会同时响应，建议在系统设置里关掉（右边缘滚动已经够用）。";

            BuildBindings();
        }

        private void BuildBindings()
        {
            BindingsPanel.Children.Clear();
            var textBrush = (Brush)FindResource("Text");
            var dimBrush = (Brush)FindResource("Dim");
            var accentBrush = (Brush)FindResource("Accent");

            if (_tab == "pad")
            {
                BindingsPanel.Children.Add(new TextBlock
                {
                    Text = "全板手势不参与边缘判定，可在触控板任意位置起手。双指轨迹判定只看两指中点的路径，与画多大无关。",
                    Foreground = dimBrush,
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 0, 2, 10)
                });

                var settingsBtn = new Button
                {
                    Content = "打开系统触摸板设置",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(12, 5, 12, 5),
                    FontSize = 11.5,
                    Margin = new Thickness(2, 0, 2, 14)
                };
                settingsBtn.Click += (s, e) =>
                {
                    try { Process.Start(new ProcessStartInfo("ms-settings:devices-touchpad") { UseShellExecute = true }); }
                    catch { }
                };
                BindingsPanel.Children.Add(settingsBtn);
            }

            if (_tab == "tap")
            {
                BindingsPanel.Children.Add(new TextBlock
                {
                    Text = "角落 = 同时落在一条竖边带和一条横边带里（默认各占板面 10%×10%，随边缘带宽度变化）。",
                    Foreground = dimBrush,
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 0, 2, 10)
                });
            }

            string group = _tab == "edge" ? "swipe" : _tab;   // tap / pad 直接用组名
            foreach (var def in BindingDefs.All)
            {
                if (def.Group != group) continue;

                var spec = _engine.S.Get(def.Key);

                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x22)),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(0, 0, 0, 5)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var tb = new TextBlock { FontSize = 12, Opacity = spec.Enabled ? 1.0 : 0.42 };
                tb.Inlines.Add(new Run(def.EdgeName + "  ") { Foreground = dimBrush });
                tb.Inlines.Add(new Run(def.DirName) { Foreground = textBrush });
                tb.Inlines.Add(new Run("   →   ") { Foreground = dimBrush });
                tb.Inlines.Add(new Run(spec.Display) { Foreground = accentBrush });
                Grid.SetColumn(tb, 0);
                grid.Children.Add(tb);

                string key = def.Key;
                var sw = new CheckBox
                {
                    Style = (Style)FindResource("Switch"),
                    IsChecked = spec.Enabled,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                sw.Checked += (s, e) => SetBindingEnabled(key, tb, true);
                sw.Unchecked += (s, e) => SetBindingEnabled(key, tb, false);
                Grid.SetColumn(sw, 1);
                grid.Children.Add(sw);

                var btn = new Button { Content = "更改", Padding = new Thickness(12, 4, 12, 4), FontSize = 12 };
                btn.Click += (s, e) => EditBinding(key);
                Grid.SetColumn(btn, 2);
                grid.Children.Add(btn);

                border.Child = grid;
                BindingsPanel.Children.Add(border);
            }
        }

        private void SetBindingEnabled(string key, TextBlock tb, bool on)
        {
            var spec = _engine.S.Get(key);
            spec.Enabled = on;
            tb.Opacity = on ? 1.0 : 0.42;
            _engine.S.Save();
            LogTrace("绑定 " + key + (on ? " 启用" : " 停用"));
        }

        private void EditBinding(string key)
        {
            var current = _engine.S.Get(key);
            bool wasEnabled = current.Enabled;
            var dlg = new ActionDialog(current) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                dlg.Result.Enabled = wasEnabled;    // 改动作不改变开关状态
                _engine.S.Set(key, dlg.Result);
                _engine.S.Save();
                BuildBindings();
                OnNotify("已更新 " + key);
            }
        }
    }
}
