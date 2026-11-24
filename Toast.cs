using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace EdgePad
{
    /// <summary>
    /// 右下角的小提示条（截图完成之类）。要求：不抢焦点、不挡点击、1.6 秒后自己淡出。
    /// </summary>
    public sealed class Toast : Window
    {
        private readonly DispatcherTimer _hold;

        public Toast(string title, string detail)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;          // 不抢前台焦点
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Opacity = 0;

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0xFF, 0xB2)),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            });
            if (!string.IsNullOrEmpty(detail))
                stack.Children.Add(new TextBlock
                {
                    Text = detail,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xBF, 0xBF, 0xCC)),
                    FontSize = 11.5,
                    Margin = new Thickness(0, 3, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 300
                });

            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x18, 0x18, 0x20)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x7C, 0x5C, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 11, 16, 11),
                Child = stack
            };

            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                int ex = Native.GetWindowLong(h, -20);                       // GWL_EXSTYLE
                ex |= 0x08000000;   // WS_EX_NOACTIVATE
                ex |= 0x00000020;   // WS_EX_TRANSPARENT（鼠标穿透）
                ex |= 0x00000080;   // WS_EX_TOOLWINDOW
                Native.SetWindowLong(h, -20, ex);
            };

            Loaded += (s, e) =>
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - ActualWidth - 24;
                Top = wa.Bottom - ActualHeight - 24;
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120));
                BeginAnimation(OpacityProperty, fade);
            };

            _hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
            _hold.Tick += (s, e) =>
            {
                _hold.Stop();
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260));
                fade.Completed += (a, b) => { try { Close(); } catch { } };
                BeginAnimation(OpacityProperty, fade);
            };
            _hold.Start();
        }

        private static Toast _current;

        /// <summary>只留一个提示条：新的直接顶掉旧的。</summary>
        public static void Show(string title, string detail)
        {
            if (_current != null) { try { _current.Close(); } catch { } _current = null; }
            var t = new Toast(title, detail);
            _current = t;
            t.Closed += (s, e) => { if (ReferenceEquals(_current, t)) _current = null; };
            try { t.Show(); } catch { _current = null; }
        }

        public static void CloseCurrent()
        {
            if (_current == null) return;
            var t = _current;
            _current = null;
            try { t.Close(); } catch { }
        }
    }
}
