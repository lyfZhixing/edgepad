using System;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace EdgePad
{
    /// <summary>
    /// 读当前媒体会话（SMTC）：按了上一首/下一首之后，用播放器自己报的曲目信息做回执。
    /// 注意：SMTC 的激活在 UI（STA）线程上可能同步卡住，所以整段都放到线程池（MTA）里跑，并带硬超时。
    /// </summary>
    public static class MediaWatch
    {
        public sealed class Info
        {
            public string Title = "";
            public string Artist = "";
            public string Status = "";

            public string Key { get { return Artist + "|" + Title; } }

            public string Describe()
            {
                string s = (Artist.Length > 0 && Title.Length > 0) ? Artist + " - " + Title
                         : Title.Length > 0 ? Title
                         : Artist;
                if (string.IsNullOrEmpty(s)) s = "（播放器没报曲目）";
                if (Status == "Playing") s += "  ▶";
                else if (Status == "Paused") s += "  ⏸";
                return s;
            }
        }

        public static Task<Info> Read()
        {
            return Task.Run<Info>(() =>
            {
                try
                {
                    var op = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
                    if (!op.Wait(2500)) return null;                 // 系统组件卡住就放弃
                    var mgr = op.Result;
                    var session = mgr == null ? null : mgr.GetCurrentSession();
                    if (session == null) return null;

                    var info = new Info();
                    var p = session.TryGetMediaPropertiesAsync().AsTask();
                    if (p.Wait(1500) && p.Result != null)
                    {
                        info.Title = p.Result.Title ?? "";
                        info.Artist = p.Result.Artist ?? "";
                    }
                    var play = session.GetPlaybackInfo();
                    if (play != null) info.Status = play.PlaybackStatus.ToString();
                    return info;
                }
                catch { return null; }
            });
        }

        public static Task<bool> Available()
        {
            return Task.Run(() =>
            {
                try
                {
                    var op = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
                    if (!op.Wait(2500)) return false;
                    return op.Result != null && op.Result.GetSessions() != null && op.Result.GetSessions().Count > 0;
                }
                catch { return false; }
            });
        }
    }
}
