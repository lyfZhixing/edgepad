using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace EdgePad
{
    /// <summary>配置迁移自检：老配置读进来要落到新默认值上，用户自己改过的不能被覆盖。</summary>
    internal static class MigrateTest
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("EdgePad 配置迁移自检 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            int pass = 0, fail = 0;

            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "edgepad-migrate");
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            Directory.CreateDirectory(dir);

            // A 老配置（没有 Schema）：L.tap=静音 → 内容不变，只改名成 L.tap2
            string a = System.IO.Path.Combine(dir, "a.json");
            File.WriteAllText(a, Old("L.tap", "keys", "VolumeMute", 0));
            var sa = Settings.Load(a);
            Check(sb, "老 L.tap 改名成 L.tap2 且内容不变", sa.Get("L.tap2").Value == "VolumeMute", ref pass, ref fail);
            Check(sb, "老键 L.tap 已被移除", !sa.Bindings.ContainsKey("L.tap"), ref pass, ref fail);
            Check(sb, "Schema → 5", sa.Schema == 5, ref pass, ref fail);

            // A2 老配置的 R.tap=静音 → 先按 v1 清空，再改名
            string a2 = System.IO.Path.Combine(dir, "a2.json");
            File.WriteAllText(a2, Old("R.tap", "keys", "VolumeMute", 0));
            var sa2 = Settings.Load(a2);
            Check(sb, "老 R.tap 清空后改名成 R.tap2", sa2.Get("R.tap2").Kind == "none", ref pass, ref fail);

            // B 用户自己把边缘双击设成别的 → 搬过去但不改内容
            string b = System.IO.Path.Combine(dir, "b.json");
            File.WriteAllText(b, Old("T.tap", "keys", "Win+Shift+S", 3));
            var sb2 = Settings.Load(b);
            Check(sb, "用户设的 T.tap 搬到 T.tap2", sb2.Get("T.tap2").Value == "Win+Shift+S", ref pass, ref fail);

            // C 上一版绑在已删除的“画图模式”上 → 清成未设置并关掉开关
            string c = System.IO.Path.Combine(dir, "c.json");
            File.WriteAllText(c, Old("R.tap", "drawmode", "", 2));
            var sc = Settings.Load(c);
            Check(sb, "画图模式绑定被清理", sc.Get("R.tap2").Kind == "none", ref pass, ref fail);
            Check(sb, "清理后开关是关的", !sc.Get("R.tap2").Enabled, ref pass, ref fail);

            // C2 上一版把「双指画 L」绑成合成的 Win+L → 改成系统调用
            string c2 = System.IO.Path.Combine(dir, "c2.json");
            File.WriteAllText(c2, Old("PAD.l", "keys", "Win+L", 4));
            var sc2 = Settings.Load(c2);
            Check(sb, "PAD.l 的 Win+L 改成 syslock", sc2.Get("PAD.l").Kind == "syslock", ref pass, ref fail);

            // D v4 配置原样
            string d = System.IO.Path.Combine(dir, "d.json");
            File.WriteAllText(d, Old("L.tap2", "keys", "VolumeMute", 4));
            var sd = Settings.Load(d);
            Check(sb, "v4 配置不再改动", sd.Get("L.tap2").Value == "VolumeMute", ref pass, ref fail);

            // E 缺键补默认（角落默认值）
            string e = System.IO.Path.Combine(dir, "e.json");
            File.WriteAllText(e, "{\"Schema\":4,\"Bindings\":{}}");
            var se = Settings.Load(e);
            Check(sb, "缺键补默认 L.tap2=静音", se.Get("L.tap2").Value == "VolumeMute", ref pass, ref fail);
            Check(sb, "默认 左上角双击=Esc", se.Get("TL.tap2").Value == "Esc", ref pass, ref fail);
            Check(sb, "默认 右上角双击=窗口截图", se.Get("TR.tap2").Value == "Alt+PrintScreen", ref pass, ref fail);
            Check(sb, "默认 右上角三击=全屏截图", se.Get("TR.tap3").Value == "Ctrl+PrintScreen", ref pass, ref fail);
            Check(sb, "默认 左下角双击=未设置", se.Get("BL.tap2").Kind == "none", ref pass, ref fail);
            Check(sb, "默认 右下角三击=未设置", se.Get("BR.tap3").Kind == "none", ref pass, ref fail);
            Check(sb, "默认 右边缘双击=未设置", se.Get("R.tap2").Kind == "none", ref pass, ref fail);

            // F 绑定表分组齐全
            int swipe = 0, tap = 0, pad = 0, corners = 0;
            foreach (var def in BindingDefs.All)
            {
                if (def.Group == "swipe") swipe++;
                else if (def.Group == "tap") tap++;
                else if (def.Group == "pad") pad++;
                if (def.Edge == "TL" || def.Edge == "TR" || def.Edge == "BL" || def.Edge == "BR") corners++;
            }
            Check(sb, "滑动 8 项 / 点击 12 项 / 全板 6 项", swipe == 8 && tap == 12 && pad == 6,
                  ref pass, ref fail);
            Check(sb, "四个角落共 8 个动作", corners == 8, ref pass, ref fail);

            try { Directory.Delete(dir, true); } catch { }
            sb.AppendLine();
            sb.AppendLine("结果：通过 " + pass + " 项，失败 " + fail + " 项");
            File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "migratetest.txt"), sb.ToString());
        }

        private static string Old(string key, string kind, string value, int schema)
        {
            var o = new JsonSerializerOptions { WriteIndented = true };
            var dict = new Dictionary<string, object>
            {
                ["L.up"] = new { Kind = "keys", Value = "VolumeUp", Display = "音量 +", Enabled = true },
                [key] = new { Kind = kind, Value = value, Display = "x", Enabled = true },
            };
            if (schema == 0)
                return JsonSerializer.Serialize(new { BandL = 0.1, PalmGuard = true, Bindings = dict }, o);

            var baseObj = new Dictionary<string, object>
            {
                ["BandL"] = 0.1,
                ["PalmGuard"] = true,
                ["Schema"] = schema,
                ["Bindings"] = dict,
            };
            return JsonSerializer.Serialize(baseObj, o);
        }

        private static void Check(StringBuilder sb, string name, bool ok, ref int pass, ref int fail)
        {
            if (ok) { pass++; sb.AppendLine("[PASS] " + name); }
            else { fail++; sb.AppendLine("[FAIL] " + name); }
        }
    }
}
