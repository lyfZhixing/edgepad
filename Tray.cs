using System;
using System.Drawing;
using System.Windows;
using WF = System.Windows.Forms;

namespace EdgePad
{
    /// <summary>托盘图标。用托盘是唯一能让“关掉窗口但手势还在”可发现的做法，所以退出入口放在这里。</summary>
    public sealed class Tray : IDisposable
    {
        private readonly WF.NotifyIcon _icon;
        private readonly WF.ToolStripMenuItem _enabled;
        private readonly WF.ToolStripMenuItem _mini;

        public event Action OpenClicked;
        public event Action MiniClicked;
        public event Action EnabledToggled;
        public event Action ExitClicked;

        public Tray(bool enabled, bool miniShown)
        {
            _enabled = new WF.ToolStripMenuItem("启用边缘手势", null, (s, e) => { var h = EnabledToggled; if (h != null) h(); })
            {
                Checked = enabled,
                CheckOnClick = false
            };
            _mini = new WF.ToolStripMenuItem("悬浮窗", null, (s, e) => { var h = MiniClicked; if (h != null) h(); })
            {
                Checked = miniShown,
                CheckOnClick = false
            };

            var menu = new WF.ContextMenuStrip();
            menu.Items.Add(new WF.ToolStripMenuItem("打开主界面", null, (s, e) => { var h = OpenClicked; if (h != null) h(); }));
            menu.Items.Add(_mini);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add(_enabled);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add(new WF.ToolStripMenuItem("退出 EdgePad", null, (s, e) => { var h = ExitClicked; if (h != null) h(); }));

            _icon = new WF.NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "EdgePad · 触控板边缘手势",
                Visible = true,
                ContextMenuStrip = menu
            };
            _icon.DoubleClick += (s, e) => { var h = OpenClicked; if (h != null) h(); };
        }

        internal static Icon LoadIcon()
        {
            try
            {
                var res = Application.GetResourceStream(new Uri("app.ico", UriKind.Relative));
                if (res != null) return new Icon(res.Stream);
            }
            catch { }
            return SystemIcons.Application;
        }

        public void SetState(bool enabled, bool miniShown)
        {
            _enabled.Checked = enabled;
            _mini.Checked = miniShown;
        }

        public void Notify(string title, string text)
        {
            try { _icon.ShowBalloonTip(2500, title, text, WF.ToolTipIcon.Info); }
            catch { }
        }

        public void Dispose()
        {
            try { _icon.Visible = false; _icon.Dispose(); }
            catch { }
        }
    }
}
