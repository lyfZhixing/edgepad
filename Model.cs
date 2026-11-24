using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace EdgePad
{
    public sealed class ActionSpec
    {
        public string Kind { get; set; } = "none";
        public string Value { get; set; } = "";
        public string Display { get; set; } = "未设置";
        public bool Enabled { get; set; } = true;

        public ActionSpec Clone()
        {
            return new ActionSpec { Kind = Kind, Value = Value, Display = Display, Enabled = Enabled };
        }
    }

    public sealed class BindingDef
    {
        public string Key;
        public string Edge;
        public string Dir;
        public string EdgeName;
        public string DirName;
        public string Group;   // swipe = 边缘滑动 / tap = 边缘与角落点击 / pad = 全板
    }

    public static class BindingDefs
    {
        public static readonly BindingDef[] All = new BindingDef[]
        {
            // 边缘滑动
            new BindingDef { Key = "L.up",    Edge = "L", Dir = "up",    EdgeName = "左边缘", DirName = "上滑", Group = "swipe" },
            new BindingDef { Key = "L.down",  Edge = "L", Dir = "down",  EdgeName = "左边缘", DirName = "下滑", Group = "swipe" },
            new BindingDef { Key = "R.up",    Edge = "R", Dir = "up",    EdgeName = "右边缘", DirName = "上滑", Group = "swipe" },
            new BindingDef { Key = "R.down",  Edge = "R", Dir = "down",  EdgeName = "右边缘", DirName = "下滑", Group = "swipe" },
            new BindingDef { Key = "T.left",  Edge = "T", Dir = "left",  EdgeName = "顶边缘", DirName = "左滑", Group = "swipe" },
            new BindingDef { Key = "T.right", Edge = "T", Dir = "right", EdgeName = "顶边缘", DirName = "右滑", Group = "swipe" },
            new BindingDef { Key = "B.left",  Edge = "B", Dir = "left",  EdgeName = "底边缘", DirName = "左滑", Group = "swipe" },
            new BindingDef { Key = "B.right", Edge = "B", Dir = "right", EdgeName = "底边缘", DirName = "右滑", Group = "swipe" },

            // 四条边缘的双击
            new BindingDef { Key = "L.tap2",  Edge = "L", Dir = "tap2",  EdgeName = "左边缘", DirName = "双击", Group = "tap" },
            new BindingDef { Key = "R.tap2",  Edge = "R", Dir = "tap2",  EdgeName = "右边缘", DirName = "双击", Group = "tap" },
            new BindingDef { Key = "T.tap2",  Edge = "T", Dir = "tap2",  EdgeName = "顶边缘", DirName = "双击", Group = "tap" },
            new BindingDef { Key = "B.tap2",  Edge = "B", Dir = "tap2",  EdgeName = "底边缘", DirName = "双击", Group = "tap" },

            // 四个角落：单独的触发区，支持双击 / 三击
            new BindingDef { Key = "TL.tap2", Edge = "TL", Dir = "tap2", EdgeName = "左上角", DirName = "双击", Group = "tap" },
            new BindingDef { Key = "TL.tap3", Edge = "TL", Dir = "tap3", EdgeName = "左上角", DirName = "三击", Group = "tap" },
            new BindingDef { Key = "TR.tap2", Edge = "TR", Dir = "tap2", EdgeName = "右上角", DirName = "双击", Group = "tap" },
            new BindingDef { Key = "TR.tap3", Edge = "TR", Dir = "tap3", EdgeName = "右上角", DirName = "三击", Group = "tap" },
            new BindingDef { Key = "BL.tap2", Edge = "BL", Dir = "tap2", EdgeName = "左下角", DirName = "双击", Group = "tap" },
            new BindingDef { Key = "BL.tap3", Edge = "BL", Dir = "tap3", EdgeName = "左下角", DirName = "三击", Group = "tap" },
            new BindingDef { Key = "BR.tap2", Edge = "BR", Dir = "tap2", EdgeName = "右下角", DirName = "双击", Group = "tap" },
            new BindingDef { Key = "BR.tap3", Edge = "BR", Dir = "tap3", EdgeName = "右下角", DirName = "三击", Group = "tap" },

            // 全板
            new BindingDef { Key = "PAD.drag",     Edge = "PAD", Dir = "drag",     EdgeName = "全板", DirName = "五指拖动", Group = "pad" },
            new BindingDef { Key = "PAD.pinchin",  Edge = "PAD", Dir = "pinchin",  EdgeName = "全板", DirName = "五指向内捏合", Group = "pad" },
            new BindingDef { Key = "PAD.pinchout", Edge = "PAD", Dir = "pinchout", EdgeName = "全板", DirName = "五指向外捏合", Group = "pad" },
            new BindingDef { Key = "PAD.l",      Edge = "PAD", Dir = "l",      EdgeName = "双指", DirName = "画 L",   Group = "pad" },
            new BindingDef { Key = "PAD.x",      Edge = "PAD", Dir = "x",      EdgeName = "双指", DirName = "画 X",   Group = "pad" },
            new BindingDef { Key = "PAD.circle", Edge = "PAD", Dir = "circle", EdgeName = "双指", DirName = "画圈",  Group = "pad" },
        };
    }

    public static class Actions
    {
        public sealed class Preset
        {
            public string Id;
            public string Name;
            public string Kind = "keys";
            public string Value;
        }

        public static readonly Preset[] Presets = new Preset[]
        {
            new Preset { Id = "vol.up",     Name = "音量 +",          Value = "VolumeUp" },
            new Preset { Id = "vol.down",   Name = "音量 −",          Value = "VolumeDown" },
            new Preset { Id = "vol.mute",   Name = "静音",            Value = "VolumeMute" },
            new Preset { Id = "media.prev", Name = "上一首",          Value = "MediaPrev" },
            new Preset { Id = "media.next", Name = "下一首",          Value = "MediaNext" },
            new Preset { Id = "media.play", Name = "播放 / 暂停",     Value = "MediaPlayPause" },
            new Preset { Id = "tab.prev",   Name = "上一个标签",      Value = "Ctrl+Shift+Tab" },
            new Preset { Id = "tab.next",   Name = "下一个标签",      Value = "Ctrl+Tab" },
            new Preset { Id = "nav.back",   Name = "后退",            Value = "Alt+Left" },
            new Preset { Id = "nav.fwd",    Name = "前进",            Value = "Alt+Right" },
            new Preset { Id = "windowdrag", Name = "拖动窗口",        Kind = "windowdrag", Value = "" },
            new Preset { Id = "wincycle",   Name = "最大化⇄最小化",    Kind = "windowcycle", Value = "" },
            new Preset { Id = "winmaxwin",  Name = "最大化（当前窗口）", Kind = "windowmax", Value = "" },
            new Preset { Id = "scroll.up",   Name = "向上滚动",        Kind = "scroll", Value = "up" },
            new Preset { Id = "scroll.down", Name = "向下滚动",        Kind = "scroll", Value = "down" },
            new Preset { Id = "sys.lock",    Name = "锁屏",           Kind = "syslock", Value = "" },
            new Preset { Id = "sys.closewin", Name = "关闭当前窗口",   Value = "Alt+F4" },
            new Preset { Id = "win.max",    Name = "窗口最大化",      Value = "Win+Up" },
            new Preset { Id = "win.min",    Name = "窗口最小化",      Value = "Win+Down" },
            new Preset { Id = "win.left",   Name = "窗口贴左半屏",    Value = "Win+Left" },
            new Preset { Id = "win.right",  Name = "窗口贴右半屏",    Value = "Win+Right" },
            new Preset { Id = "taskview",   Name = "任务视图",        Value = "Win+Tab" },
            new Preset { Id = "desktop",    Name = "显示桌面",        Value = "Win+D" },
            new Preset { Id = "shot",       Name = "截图（框选）",     Value = "Win+Shift+S" },
            new Preset { Id = "shot.win",   Name = "窗口截图（Alt+PrtSc）",  Value = "Alt+PrintScreen" },
            new Preset { Id = "shot.full",  Name = "全屏截图（Ctrl+PrtSc）", Value = "Ctrl+PrintScreen" },
            new Preset { Id = "shot.prt",   Name = "全屏截图（PrtSc）",      Value = "PrintScreen" },
            new Preset { Id = "enter",      Name = "回车",            Value = "Enter" },
            new Preset { Id = "esc",        Name = "Esc",             Value = "Esc" },
        };

        public static Preset Find(string id)
        {
            foreach (var p in Presets) if (p.Id == id) return p;
            return null;
        }

        public static ActionSpec FromPreset(string id)
        {
            var p = Find(id);
            if (p == null) return new ActionSpec();
            return new ActionSpec { Kind = p.Kind, Value = p.Value, Display = p.Name };
        }

        public static ActionSpec None()
        {
            return new ActionSpec { Kind = "none", Value = "", Display = "未设置" };
        }
    }

    public sealed class Settings
    {
        public double BandL { get; set; } = 0.10;
        public double BandR { get; set; } = 0.10;
        public double BandT { get; set; } = 0.10;
        public double BandB { get; set; } = 0.10;
        public bool Enabled { get; set; } = true;
        public bool FreezeCursor { get; set; } = true;
        public int Threshold { get; set; } = 90;
        public int VolumeStep { get; set; } = 25;
        public int ScrollStep { get; set; } = 40;
        public double DragGain { get; set; } = 1.0;
        public bool SnapEdges { get; set; } = true;
        public string LastTab { get; set; } = "edge";
        public bool PalmGuard { get; set; } = true;

        // 常驻防误触区：右下角（归一化，区域左上角到触控板右下角）
        public double PalmX0 { get; set; } = 0.72;
        public double PalmY0 { get; set; } = 0.62;
        public bool TypingGuard { get; set; } = true;
        public int TypingQuietMs { get; set; } = 400; // 键盘停下多久算“不在打字”
        public bool TypingPauseAll { get; set; } = true; // 打字期间暂停全部手势
        public bool SuppressEdgeTapClick { get; set; } = true; // 边缘双击时屏蔽系统的点按点击
        public bool StartWithWindows { get; set; }              // 开机自启（写 HKCU Run，带 --tray 静默启动）
        public bool MinimizeToTray { get; set; } = true;        // 最小化 / 关窗时收进托盘
        public int Schema { get; set; }                         // 配置版本，用来做一次性迁移（0 = 老配置）
        public Dictionary<string, ActionSpec> Bindings { get; set; } = new Dictionary<string, ActionSpec>(StringComparer.Ordinal);

        public static string Path
        {
            get
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EdgePad");
                return System.IO.Path.Combine(dir, "settings.json");
            }
        }

        public static Settings CreateDefault()
        {
            var s = new Settings();
            s.Schema = 5;
            s.Bindings["L.up"] = Actions.FromPreset("vol.up");
            s.Bindings["L.down"] = Actions.FromPreset("vol.down");
            s.Bindings["R.up"] = Actions.FromPreset("media.prev");
            s.Bindings["R.down"] = Actions.FromPreset("media.next");
            s.Bindings["T.left"] = Actions.FromPreset("nav.back");
            s.Bindings["T.right"] = Actions.FromPreset("nav.fwd");
            s.Bindings["B.left"] = Actions.FromPreset("tab.prev");
            s.Bindings["B.right"] = Actions.FromPreset("tab.next");
            s.Bindings["L.tap2"] = Actions.FromPreset("vol.mute");
            s.Bindings["R.tap2"] = Actions.None();          // 右边缘双击默认留空
            s.Bindings["T.tap2"] = Actions.FromPreset("vol.mute");
            s.Bindings["B.tap2"] = Actions.FromPreset("vol.mute");

            // 角落：左上角双击 = Esc，右上角双击 = 窗口截图、三击 = 全屏截图，下面两个角默认不绑
            s.Bindings["TL.tap2"] = Actions.FromPreset("esc");
            s.Bindings["TR.tap2"] = Actions.FromPreset("shot.win");
            s.Bindings["TR.tap3"] = Actions.FromPreset("shot.full");
            s.Bindings["BL.tap2"] = Actions.None();
            s.Bindings["BL.tap3"] = Actions.None();
            s.Bindings["BR.tap2"] = Actions.None();
            s.Bindings["BR.tap3"] = Actions.None();
            s.Bindings["PAD.drag"] = Actions.FromPreset("windowdrag");
            s.Bindings["PAD.pinchin"] = Actions.FromPreset("wincycle");
            s.Bindings["PAD.pinchout"] = Actions.FromPreset("winmaxwin");
            s.Bindings["PAD.l"] = Actions.FromPreset("sys.lock");        // 双指画 L = 锁屏
            s.Bindings["PAD.x"] = Actions.FromPreset("sys.closewin");    // 双指画 X = 关闭当前窗口
            s.Bindings["PAD.circle"] = Actions.None();                   // 画圈先留空
            return s;
        }

        public ActionSpec Get(string key)
        {
            ActionSpec a;
            if (Bindings != null && Bindings.TryGetValue(key, out a) && a != null) return a;
            return Actions.None();
        }

        public void Set(string key, ActionSpec spec)
        {
            if (Bindings == null) Bindings = new Dictionary<string, ActionSpec>(StringComparer.Ordinal);
            Bindings[key] = spec;
        }

        /// <summary>读配置。path 只给自检用，正常走 %APPDATA%\EdgePad\settings.json。</summary>
        public static Settings Load(string path = null)
        {
            if (path == null) path = Path;
            Settings s = null;
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    s = JsonSerializer.Deserialize<Settings>(json);
                }
            }
            catch { }

            if (s == null) return CreateDefault();

            if (s.Bindings == null) s.Bindings = new Dictionary<string, ActionSpec>(StringComparer.Ordinal);

            // 迁移 1：右边缘双击默认改为“未设置”（v1）
            if (s.Schema < 1)
            {
                var rt = s.Get("R.tap");
                if (rt != null && rt.Kind == "keys" && rt.Value == "VolumeMute")
                    s.Set("R.tap", Actions.None());
                s.Schema = 1;
            }

            // 迁移 2：画图模式已删除（v3）。之前绑到它的边缘双击清回“未设置”并关掉开关。
            // 老配置里这个键叫 R.tap，改名迁移（v4）之后才叫 R.tap2，两处都处理。
            if (s.Schema < 3)
            {
                foreach (var key in new[] { "R.tap", "R.tap2" })
                {
                    var rt = s.Get(key);
                    if (rt == null || rt.Kind != "drawmode") continue;
                    var none = Actions.None();
                    none.Enabled = false;
                    s.Set(key, none);
                }
                s.Schema = 3;
            }

            // 迁移 3：边缘双击从 "X.tap" 改名成 "X.tap2"，把老键搬过去（v4）
            if (s.Schema < 4)
            {
                foreach (var e in new[] { "L", "R", "T", "B" })
                {
                    string oldKey = e + ".tap";
                    string newKey = e + ".tap2";
                    ActionSpec a;
                    if (s.Bindings.TryGetValue(oldKey, out a) && a != null)
                    {
                        if (!s.Bindings.ContainsKey(newKey) || s.Bindings[newKey] == null) s.Bindings[newKey] = a;
                        s.Bindings.Remove(oldKey);
                    }
                }
                s.Schema = 4;
            }

            // 迁移 4：锁屏从“合成 Win+L”改成系统调用 LockWorkStation（v5）。合成 Win+L 系统会忽略。
            if (s.Schema < 5)
            {
                var pl = s.Get("PAD.l");
                if (pl != null && pl.Kind == "keys"
                    && "Win+L".Equals(pl.Value, StringComparison.OrdinalIgnoreCase))
                    s.Set("PAD.l", Actions.FromPreset("sys.lock"));
                s.Schema = 5;
            }

            // 迁移：新版本新增的绑定键从此补上，用户已改过的键保持不变
            var defaults = CreateDefault();
            foreach (var kv in defaults.Bindings)
            {
                if (!s.Bindings.ContainsKey(kv.Key) || s.Bindings[kv.Key] == null)
                    s.Bindings[kv.Key] = kv.Value;
            }
            return s;
        }

        public void Save()
        {
            try
            {
                string path = Path;
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
