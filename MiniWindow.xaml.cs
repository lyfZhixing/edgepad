using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;

namespace EdgePad
{
    public partial class MiniWindow : Window
    {
        public event Action ExpandRequested;

        public MiniWindow()
        {
            InitializeComponent();

            BtnExpand.Click += (s, e) =>
            {
                var h = ExpandRequested;
                if (h != null) h();
            };

            SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int dark = 1;
                Native.DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
                Native.DwmSetWindowAttribute(hwnd, 19, ref dark, 4);
            };

            Loaded += (s, e) =>
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - ActualWidth - 24;
                Top = wa.Top + 24;
            };
        }

        public void UpdateState(List<Contact> contacts, int maxX, int maxY,
                                double rate, double frameMs, double procMs,
                                int peak, int devices, int liveContacts,
                                double bandL, double bandR, double bandT, double bandB)
        {
            Pad.BandL = bandL;
            Pad.BandR = bandR;
            Pad.BandT = bandT;
            Pad.BandB = bandB;

            Pad.SetContacts(contacts, maxX, maxY);
            Pad.InvalidateVisual();

            TxtMetrics.Text = rate.ToString("0") + "/s  帧 " + frameMs.ToString("0.0") + "ms"
                              + "  处理 " + procMs.ToString("0.00") + "ms"
                              + "  触点 " + liveContacts + "  峰值 " + peak + "  设备 " + devices;
        }
    }
}
