using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EdgePad
{
    internal static class SelfTest
    {
        private const int MAX_X = 1800;
        private const int MAX_Y = 1275;

        private static int _pass, _fail;

        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("EdgePad 引擎自检 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            // A 中部起手 → 整段忽略
            Check(sb, "中部起手下滑不触发", Play(e =>
            {
                Feed(e, 1, 900, 600);
                Feed(e, 1, 900, 800);
                Feed(e, 0, 0, 0);
            }), new string[0]);

            // B 起手中部，划进左边缘再上滑 → 仍不触发（起手锁定）
            Check(sb, "中部起手划进边缘不触发", Play(e =>
            {
                Feed(e, 1, 900, 600);
                Feed(e, 1, 60, 600);
                Feed(e, 1, 60, 480);
                Feed(e, 0, 0, 0);
            }), new string[0]);

            // C 左边缘上滑 → 音量连发 4 档
            Check(sb, "左边缘上滑=音量+ 4 档", Play(e =>
            {
                Feed(e, 1, 50, 700);
                Feed(e, 1, 50, 600);
                Feed(e, 0, 0, 0);
            }), new[] { "L.up", "L.up", "L.up", "L.up" });

            // D 左边缘下滑 → 音量 3 档
            Check(sb, "左边缘下滑=音量- 3 档", Play(e =>
            {
                Feed(e, 1, 50, 600);
                Feed(e, 1, 50, 675);
                Feed(e, 0, 0, 0);
            }), new[] { "L.down", "L.down", "L.down" });

            // E 右边缘下滑 → 只触发一次（锁存）
            Check(sb, "右边缘下滑只触发一次", Play(e =>
            {
                Feed(e, 1, 1760, 400);
                Feed(e, 1, 1760, 520);
                Feed(e, 1, 1760, 700);
                Feed(e, 0, 0, 0);
            }), new[] { "R.down" });

            // F 顶边缘左滑
            Check(sb, "顶边缘左滑", Play(e =>
            {
                Feed(e, 1, 900, 20);
                Feed(e, 1, 780, 22);
                Feed(e, 0, 0, 0);
            }), new[] { "T.left" });

            // G 底边缘右滑
            Check(sb, "底边缘右滑", Play(e =>
            {
                Feed(e, 1, 900, 1260);
                Feed(e, 1, 1020, 1258);
                Feed(e, 0, 0, 0);
            }), new[] { "B.right" });

            // H 竖向边缘上的横向滑动不触发
            Check(sb, "左边缘横向滑动不触发", Play(e =>
            {
                Feed(e, 1, 50, 600);
                Feed(e, 1, 300, 600);
                Feed(e, 0, 0, 0);
            }), new string[0]);

            // I 双指取消
            Check(sb, "双指不触发", Play(e =>
            {
                Feed(e, 2, 50, 700);
                Feed(e, 2, 50, 600);
                Feed(e, 0, 0, 0);
            }), new string[0]);

            // J 抬手后可再次触发
            Check(sb, "抬手后可再次触发", Play(e =>
            {
                Feed(e, 1, 50, 700);
                Feed(e, 1, 50, 600);
                Feed(e, 0, 0, 0);
                Feed(e, 1, 50, 700);
                Feed(e, 1, 50, 600);
                Feed(e, 0, 0, 0);
            }), new[] { "L.up", "L.up", "L.up", "L.up", "L.up", "L.up", "L.up", "L.up" });

            // K 滚动是连续动作：120 单位 / 每档 40 → 3 档
            Check(sb, "右边缘上滑=向上滚动 3 档", Play(e =>
            {
                Feed(e, 1, 1760, 700);
                Feed(e, 1, 1760, 580);
                Feed(e, 0, 0, 0);
            }, s =>
            {
                s.Bindings["R.up"] = Actions.FromPreset("scroll.up");
                s.Bindings["R.down"] = Actions.FromPreset("scroll.down");
            }), new[] { "R.up", "R.up", "R.up" });

            // L 滚动反向
            Check(sb, "右边缘下滑=向下滚动 5 档", Play(e =>
            {
                Feed(e, 1, 1760, 400);
                Feed(e, 1, 1760, 600);
                Feed(e, 0, 0, 0);
            }, s =>
            {
                s.Bindings["R.up"] = Actions.FromPreset("scroll.up");
                s.Bindings["R.down"] = Actions.FromPreset("scroll.down");
            }), new[] { "R.down", "R.down", "R.down", "R.down", "R.down" });

            // N 边缘双击（约 10ms 一帧，模仿真实报文节奏）
            long tn = 1000;
            Check(sb, "左边缘双击触发一次", Play(e =>
            {
                Hold(e, 50, 700, 6, ref tn, 10);
                Lift(e, ref tn, 10);
                tn += 100;
                Hold(e, 50, 700, 6, ref tn, 10);
                Lift(e, ref tn, 10);
            }, null, () => tn), new[] { "L.tap2" });

            // O 单击不触发
            long to = 1000;
            Check(sb, "边缘单击不触发", Play(e =>
            {
                Hold(e, 50, 700, 6, ref to, 10);
                Lift(e, ref to, 10);
            }, null, () => to), new string[0]);

            // P 两次点击间隔过长不触发
            long tp = 1000;
            Check(sb, "双击间隔过长不触发", Play(e =>
            {
                Hold(e, 50, 700, 6, ref tp, 10);
                Lift(e, ref tp, 10);
                tp += 600;
                Hold(e, 50, 700, 6, ref tp, 10);
                Lift(e, ref tp, 10);
            }, null, () => tp), new string[0]);

            // Q 按住超过轻点时长不算双击
            long tq = 1000;
            Check(sb, "长按不算轻点", Play(e =>
            {
                Hold(e, 50, 700, 40, ref tq, 10);
                Lift(e, ref tq, 10);
                tq += 100;
                Hold(e, 50, 700, 6, ref tq, 10);
                Lift(e, ref tq, 10);
            }, null, () => tq), new string[0]);

            // R 拖动后抬手不算轻点
            long tr = 1000;
            Check(sb, "拖动不参与双击判定", Play(e =>
            {
                Hold(e, 1760, 400, 4, ref tr, 10);
                for (int i = 1; i <= 6; i++) { tr += 10; Feed(e, 1, 1760, 400 + i * 10); }
                Lift(e, ref tr, 10);
                tr += 100;
                Hold(e, 1760, 400, 6, ref tr, 10);
                Lift(e, ref tr, 10);
            }, null, () => tr), new string[0]);

            // S 顶边缘双击
            long ts = 1000;
            Check(sb, "顶边缘双击触发一次", Play(e =>
            {
                Hold(e, 900, 20, 6, ref ts, 10);
                Lift(e, ref ts, 10);
                ts += 100;
                Hold(e, 900, 20, 6, ref ts, 10);
                Lift(e, ref ts, 10);
            }, null, () => ts), new[] { "T.tap2" });

            // U 不同量程的触控板（模拟 1024×600 的笔记本精准触摸板）
            Check(sb, "1024x600 触控板左边缘上滑=音量+ 4 档", Play(e =>
            {
                Feed(e, 1, 10, 300, 1024, 600);
                Feed(e, 1, 10, 200, 1024, 600);
                Feed(e, 0, 0, 0, 1024, 600);
            }), new[] { "L.up", "L.up", "L.up", "L.up" });

            // V 不同量程下的边缘比例仍然正确（右下角 5% 处应命中右/底边缘）
            Check(sb, "1024x600 触控板底边缘右滑", Play(e =>
            {
                Feed(e, 1, 500, 590, 1024, 600);
                Feed(e, 1, 620, 592, 1024, 600);
                Feed(e, 0, 0, 0, 1024, 600);
            }), new[] { "B.right" });

            // X 双指滚动之后，单指边缘手势必须仍然生效
            Check(sb, "双指后单指边缘仍可用", Play(e =>
            {
                Feed(e, 2, 900, 600);
                Feed(e, 2, 920, 610);
                Feed(e, 0, 0, 0);
                Feed(e, 1, 50, 700);
                Feed(e, 1, 50, 600);
                Feed(e, 0, 0, 0);
            }), new[] { "L.up", "L.up", "L.up", "L.up" });

            // Y 即使收不到抬手报文，超时后也要恢复
            long ty = 1000;
            Check(sb, "双指后无抬手报文也能恢复", Play(e =>
            {
                Feed(e, 2, 900, 600);
                ty += 150;
                e.Tick();
                Feed(e, 1, 50, 700);
                Feed(e, 1, 50, 600);
                Feed(e, 0, 0, 0);
            }, null, () => ty), new[] { "L.up", "L.up", "L.up", "L.up" });

            // AA 五指拖动移动窗口（质心右移 10% 板宽 → 假屏幕 1000px 的 10% = 100px）
            var dragTarget = new FakeTarget();
            long tdz = 1000;
            CheckBool(sb, "五指拖动移动窗口", () =>
            {
                Play(e =>
                {
                    e.Target = dragTarget;
                    Feed(e, 5, 900, 600); tdz += 10;     // 落下，开始计时
                    tdz += 200;
                    Feed(e, 5, 900, 600);                // 落稳 → 抓窗口
                    tdz += 10;
                    Feed(e, 5, 1080, 600);               // 移动 → 窗口跟手
                    tdz += 10;
                    Feed(e, 0, 0, 0);
                }, null, () => tdz);
                return dragTarget.Captures == 1 && dragTarget.LastX == 200 && dragTarget.LastY == 100;
            }, "抓取=" + dragTarget.Captures + " 移动=" + dragTarget.Moves + " 末位置=(" + dragTarget.LastX + "," + dragTarget.LastY + ")");

            // AB 松手后停止拖动
            var stopTarget = new FakeTarget();
            CheckBool(sb, "五指松开后停止拖动", () =>
            {
                int before = 0;
                long tst = 1000;
                Play(e =>
                {
                    e.Target = stopTarget;
                    Feed(e, 5, 900, 600); tst += 200;
                    Feed(e, 5, 900, 600); tst += 10;
                    Feed(e, 5, 1080, 600); tst += 10;
                    Feed(e, 0, 0, 0); tst += 10;
                    before = stopTarget.Moves;
                    Feed(e, 3, 300, 300); tst += 10;
                    Feed(e, 3, 1200, 900);
                }, null, () => tst);
                return stopTarget.Moves == before;
            }, "松手后仍在移动 " + stopTarget.Moves + " 次");

            // AC 未绑定时不抓窗口
            var noneTarget = new FakeTarget();
            CheckBool(sb, "未绑定拖动窗口时不抓窗口", () =>
            {
                long tnn = 1000;
                Play(e =>
                {
                    e.Target = noneTarget;
                    e.S.Bindings["PAD.drag"] = Actions.None();
                    Feed(e, 5, 900, 600); tnn += 200;
                    Feed(e, 5, 1080, 600);
                    Feed(e, 0, 0, 0);
                }, null, () => tnn);
                return noneTarget.Captures == 0 && noneTarget.Moves == 0;
            }, "抓取=" + noneTarget.Captures + " 移动=" + noneTarget.Moves);

            // AD 松手吸附
            var snapTarget = new FakeTarget { PendingZone = SnapZone.Left };
            CheckBool(sb, "松手吸附到左半屏", () =>
            {
                long tsp = 1000;
                Play(e =>
                {
                    e.Target = snapTarget;
                    Feed(e, 5, 900, 600); tsp += 200;
                    Feed(e, 5, 900, 600); tsp += 10;
                    Feed(e, 5, 1080, 600);
                    Feed(e, 0, 0, 0);
                }, null, () => tsp);
                return snapTarget.SnapApplies == 1 && snapTarget.AppliedZone == SnapZone.Left;
            }, "吸附次数=" + snapTarget.SnapApplies + " 区域=" + snapTarget.AppliedZone);

            // AE 关闭吸附后不吸附
            var noSnapTarget = new FakeTarget { PendingZone = SnapZone.Maximize };
            CheckBool(sb, "关闭吸附开关后不吸附", () =>
            {
                long tns = 1000;
                Play(e =>
                {
                    e.Target = noSnapTarget;
                    e.S.SnapEdges = false;
                    Feed(e, 5, 900, 600); tns += 200;
                    Feed(e, 5, 900, 600); tns += 10;
                    Feed(e, 5, 1080, 600);
                    Feed(e, 0, 0, 0);
                }, null, () => tns);
                return noSnapTarget.SnapApplies == 0;
            }, "吸附次数=" + noSnapTarget.SnapApplies);

            // AF 五指向内捏合 → 最大化⇄最小化
            var pinchIn = new FakeTarget();
            CheckBool(sb, "五指向内捏合", () =>
            {
                Play(e =>
                {
                    e.Target = pinchIn;
                    FeedSpread(e, 900, 600, 150);
                    FeedSpread(e, 900, 600, 100);   // 间距比 0.67
                    Feed(e, 0, 0, 0);
                }, null, null, false);
                return pinchIn.CycleCalls == 1 && pinchIn.MaxCalls == 0 && pinchIn.Moves == 0;
            }, "切换=" + pinchIn.CycleCalls + " 最大化=" + pinchIn.MaxCalls + " 移动=" + pinchIn.Moves);

            // AG 五指向外捏合 → 最大化
            var pinchOut = new FakeTarget();
            CheckBool(sb, "五指向外捏合", () =>
            {
                Play(e =>
                {
                    e.Target = pinchOut;
                    FeedSpread(e, 900, 600, 150);
                    FeedSpread(e, 900, 600, 260);   // 间距比 1.73
                    Feed(e, 0, 0, 0);
                }, null, null, false);
                return pinchOut.MaxCalls == 1 && pinchOut.CycleCalls == 0 && pinchOut.Moves == 0;
            }, "切换=" + pinchOut.CycleCalls + " 最大化=" + pinchOut.MaxCalls + " 移动=" + pinchOut.Moves);

            // AH 间距不变时仍按拖动处理
            var stillDrag = new FakeTarget();
            CheckBool(sb, "五指平移不误判为捏合", () =>
            {
                long tsl = 1000;
                Play(e =>
                {
                    e.Target = stillDrag;
                    FeedSpread(e, 900, 600, 150); tsl += 200;
                    FeedSpread(e, 900, 600, 152); tsl += 10;   // 落稳，间距几乎不变
                    FeedSpread(e, 1080, 600, 152);             // 平移
                    Feed(e, 0, 0, 0);
                }, null, () => tsl);
                return stillDrag.Moves > 0 && stillDrag.CycleCalls == 0 && stillDrag.MaxCalls == 0;
            }, "移动=" + stillDrag.Moves + " 切换=" + stillDrag.CycleCalls + " 最大化=" + stillDrag.MaxCalls);

            // AI 五指短暂丢指后仍能完成捏合
            var dropTarget = new FakeTarget();
            long tdrop = 1000;
            CheckBool(sb, "五指短暂丢指仍能捏合", () =>
            {
                Play(e =>
                {
                    e.Target = dropTarget;
                    FeedSpread(e, 900, 600, 150); tdrop += 10;
                    Feed(e, 4, 900, 600); tdrop += 20;          // 丢一指 20ms
                    FeedSpread(e, 900, 600, 100); tdrop += 10;  // 间距比 0.67
                    Feed(e, 0, 0, 0);
                }, null, () => tdrop, false);
                return dropTarget.CycleCalls == 1;
            }, "切换=" + dropTarget.CycleCalls);

            // AJ 停用的动作不触发，重新启用后恢复
            Check(sb, "停用的动作不触发", Play(e =>
            {
                e.S.Bindings["L.up"].Enabled = false;
                Feed(e, 1, 50, 700);
                Feed(e, 1, 50, 600);
                Feed(e, 0, 0, 0);

                e.S.Bindings["L.up"].Enabled = true;
                Feed(e, 1, 50, 700);
                Feed(e, 1, 50, 600);
                Feed(e, 0, 0, 0);
            }), new[] { "L.up", "L.up", "L.up", "L.up" });

            // AK 右下角常驻防误触：手掌压住，另一只手仍能划左边缘
            Check(sb, "右下角常驻防误触", Play(e =>
            {
                FeedList(e, 2, 1700, 1150, 50, 700);
                FeedList(e, 2, 1700, 1150, 50, 600);
                FeedList(e, 0, 0, 0);
            }), new[] { "L.up", "L.up", "L.up", "L.up" });

            // AL 右下角里的触点即使移动也仍被忽略
            Check(sb, "右下角内移动也被忽略", Play(e =>
            {
                FeedList(e, 1, 1700, 1150);
                FeedList(e, 1, 1600, 1050);
                FeedList(e, 1, 1500, 950);
                FeedList(e, 0, 0, 0);
            }), new string[0]);

            // AM 右下角之外的整块板都可正常起手
            Check(sb, "右下角之外任意位置可起手", Play(e =>
            {
                FeedList(e, 1, 1000, 1200);   // 底边中段（右下角之外）
                FeedList(e, 1, 900, 1200);
                FeedList(e, 0, 0, 0);
            }), new[] { "B.left" });

            // AN 打字期间：全部手势暂停
            long tty = 1000;
            Check(sb, "打字期间手势全部暂停", Play(e =>
            {
                e.NotifyKeystroke();
                FeedList(e, 1, 50, 700); tty += 100;
                FeedList(e, 1, 50, 600); tty += 100;
                FeedList(e, 0, 0, 0);
            }, null, () => tty), new string[0]);

            // AO 停止打字 0.5s 后恢复正常
            long trs = 1000;
            CheckBool(sb, "停止打字后恢复", () =>
            {
                int fires = 0;
                Play(e =>
                {
                    e.Fired += (k, s) => fires++;
                    e.NotifyKeystroke(); trs += 500;
                    FeedList(e, 1, 50, 700); trs += 10;
                    FeedList(e, 1, 50, 600); trs += 10;
                    FeedList(e, 0, 0, 0);
                }, null, () => trs);
                return fires == 4;
            }, "实际 " + 0);

            // AP 关闭防误触后，右下角也能起手
            Check(sb, "关闭防误触后右下角可起手", Play(e =>
            {
                e.S.PalmGuard = false;
                FeedList(e, 1, 1700, 1150);
                FeedList(e, 1, 1700, 1050);
                FeedList(e, 0, 0, 0);
            }, null, null), new[] { "R.up" });

            // Z 未达阈值不触发
            Check(sb, "位移不足阈值不触发", Play(e =>
            {
                Feed(e, 1, 1760, 400);
                Feed(e, 1, 1760, 460);
                Feed(e, 0, 0, 0);
            }), new string[0]);

            // BM 左上角双击 → Esc（默认绑定）
            {
                long ttl = 1000;
                var fired = new List<string>();
                CheckBool(sb, "左上角双击 = Esc", () =>
                {
                    fired = Play(e =>
                    {
                        Hold(e, 50, 20, 6, ref ttl, 10); Lift(e, ref ttl, 10);
                        ttl += 100;
                        Hold(e, 50, 20, 6, ref ttl, 10); Lift(e, ref ttl, 10);
                    }, null, () => ttl);
                    return fired.Count == 1 && fired[0] == "TL.tap2";
                }, "实际 " + fired.Count + " 次 [" + string.Join(",", fired) + "]");
            }

            // BN 右上角双击：该角还配了三击，要等三击窗口过期才发
            {
                long ttr = 1000;
                var fired = new List<string>();
                CheckBool(sb, "右上角双击等三击窗口后触发", () =>
                {
                    fired = Play(e =>
                    {
                        Hold(e, 1750, 20, 6, ref ttr, 10); Lift(e, ref ttr, 10);
                        ttr += 100;
                        Hold(e, 1750, 20, 6, ref ttr, 10); Lift(e, ref ttr, 10);
                        ttr += 600;              // 三击窗口过期
                        e.Tick();                // 界面定时器驱动补发
                    }, null, () => ttr);
                    return fired.Count == 1 && fired[0] == "TR.tap2";
                }, "实际 " + fired.Count + " 次 [" + string.Join(",", fired) + "]");
            }

            // BO 右上角三击 → 全屏截图，且不会再多发一次双击
            {
                long ttt = 1000;
                var fired = new List<string>();
                CheckBool(sb, "右上角三击 = 全屏截图", () =>
                {
                    fired = Play(e =>
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            Hold(e, 1750, 20, 6, ref ttt, 10); Lift(e, ref ttt, 10);
                            if (i < 2) ttt += 100;
                        }
                        ttt += 600;
                        e.Tick();
                    }, null, () => ttt);
                    return fired.Count == 1 && fired[0] == "TR.tap3";
                }, "实际 " + fired.Count + " 次 [" + string.Join(",", fired) + "]");
            }

            // BP 角落不抢边缘双击：左上角只出 TL.tap2，左边缘（非角）只出 L.tap2
            {
                long tbp = 1000;
                var fired = new List<string>();
                CheckBool(sb, "角落与边缘双击互不串台", () =>
                {
                    fired = Play(e =>
                    {
                        Hold(e, 50, 20, 6, ref tbp, 10); Lift(e, ref tbp, 10);
                        tbp += 100;
                        Hold(e, 50, 20, 6, ref tbp, 10); Lift(e, ref tbp, 10);
                        tbp += 800;
                        Hold(e, 50, 700, 6, ref tbp, 10); Lift(e, ref tbp, 10);
                        tbp += 100;
                        Hold(e, 50, 700, 6, ref tbp, 10); Lift(e, ref tbp, 10);
                    }, null, () => tbp);
                    return fired.Count == 2 && fired[0] == "TL.tap2" && fired[1] == "L.tap2";
                }, "实际 [" + string.Join(",", fired) + "]");
            }

            // BQ 角落里起手的滑动仍然走边缘手势
            Check(sb, "左上角起手的上滑仍是左边缘", Play(e =>
            {
                Feed(e, 1, 50, 120);
                Feed(e, 1, 50, 20);
                Feed(e, 0, 0, 0);
            }), new[] { "L.up", "L.up", "L.up", "L.up" });

            // BR 默认不绑的下角：双击什么都不做（右下角在防误触区里，这里关掉防误触单独验证绑定）
            Check(sb, "左下角/右下角默认不绑定", Play(e =>
            {
                e.S.PalmGuard = false;
                Feed(e, 1, 50, 1200); Feed(e, 0, 0, 0);
                Feed(e, 1, 50, 1200); Feed(e, 0, 0, 0);
                Feed(e, 1, 1750, 1200); Feed(e, 0, 0, 0);
                Feed(e, 1, 1750, 1200); Feed(e, 0, 0, 0);
            }, null), new string[0]);

            // BS 截屏动作识别：PrtSc 系与框选截图都算“截屏”，其它动作不算
            CheckBool(sb, "截屏动作识别",
                () => Engine.IsScreenshot(Actions.FromPreset("shot.win"))
                   && Engine.IsScreenshot(Actions.FromPreset("shot.full"))
                   && Engine.IsScreenshot(Actions.FromPreset("shot.prt"))
                   && Engine.IsScreenshot(Actions.FromPreset("shot"))          // Win+Shift+S
                   && Engine.IsScreenshot(new ActionSpec { Kind = "keys", Value = "Ctrl+PrtSc" })
                   && !Engine.IsScreenshot(Actions.FromPreset("vol.up"))
                   && !Engine.IsScreenshot(Actions.FromPreset("nav.back"))
                   && !Engine.IsScreenshot(Actions.None())
                   && !Engine.IsScreenshot(null),
                "IsScreenshot 判定不正确");

            // BT 框选截图要单独识别（不能立刻判定“已完成”）
            CheckBool(sb, "框选截图单独识别",
                () => Engine.IsSnipping(Actions.FromPreset("shot"))
                   && !Engine.IsSnipping(Actions.FromPreset("shot.win"))
                   && !Engine.IsSnipping(Actions.FromPreset("shot.prt"))
                   && !Engine.IsSnipping(null),
                "IsSnipping 判定不正确");

            // BU 媒体键识别：上一首/下一首/播放暂停要出回执，其它键不出
            CheckBool(sb, "媒体键识别",
                () => Engine.IsMedia(Actions.FromPreset("media.next"))
                   && Engine.IsMedia(Actions.FromPreset("media.prev"))
                   && Engine.IsMedia(Actions.FromPreset("media.play"))
                   && !Engine.IsMedia(Actions.FromPreset("vol.up"))
                   && !Engine.IsMedia(Actions.FromPreset("shot.win"))
                   && !Engine.IsMedia(Actions.None())
                   && !Engine.IsMedia(null)
                   && Engine.MediaActionName(Actions.FromPreset("media.next")) == "下一首"
                   && Engine.MediaActionName(Actions.FromPreset("media.prev")) == "上一首",
                "IsMedia / MediaActionName 判定不正确");

            // BV 双指画 L → PAD.l
            Check(sb, "双指画 L 触发", Play(e =>
            {
                TwoPath(e, 500, 300, 500, 900);       // 向下
                TwoPath(e, 500, 900, 1250, 900);      // 再向右
                Feed(e, 0, 0, 0);
            }), new[] { "PAD.l" });

            // BW 双指画 X（一笔画沙漏）→ PAD.x
            Check(sb, "双指画 X 触发", Play(e =>
            {
                TwoPath(e, 450, 300, 1250, 950);
                TwoPath(e, 1250, 950, 1250, 300);
                TwoPath(e, 1250, 300, 450, 950);
                Feed(e, 0, 0, 0);
            }), new[] { "PAD.x" });

            // BX 双指画圈 → PAD.circle（默认留空，这里临时绑一个动作来验证判定）
            Check(sb, "双指画圈触发", Play(e =>
            {
                TwoCircle(e, 900, 640, 320);
                Feed(e, 0, 0, 0);
            }, s => s.Bindings["PAD.circle"] = Actions.FromPreset("desktop")), new[] { "PAD.circle" });

            // BY 双指直线滑动（= 系统滚动）不能误判
            Check(sb, "双指直线滑动不触发", Play(e =>
            {
                TwoPath(e, 500, 700, 1300, 700);
                Feed(e, 0, 0, 0);
            }), new string[0]);

            // BZ 双指捏合（间距变化大）不能误判成轨迹
            Check(sb, "双指捏合不算轨迹", Play(e =>
            {
                for (int i = 0; i < 12; i++)
                {
                    double r = 320 - i * 20;
                    FeedContacts(e, 0, 900 - r, 640, 1, 900 + r, 640);
                }
                Feed(e, 0, 0, 0);
            }), new string[0]);

            // CA 锁屏必须走系统调用（合成的 Win+L 会被系统忽略）
            CheckBool(sb, "锁屏走系统调用",
                () => Engine.IsLockAction(Actions.FromPreset("sys.lock"))
                   && Engine.IsLockAction(new ActionSpec { Kind = "keys", Value = "Win+L" })
                   && !Engine.IsLockAction(Actions.FromPreset("vol.up"))
                   && !Engine.IsLockAction(Actions.FromPreset("win.max"))
                   && !Engine.IsLockAction(null)
                   && Actions.FromPreset("sys.lock").Kind == "syslock",
                "IsLockAction 判定不正确");

            // BJ 伪触点：坐标全 0/全 1 会解成“正好贴着两条边”，必须丢掉
            CheckBool(sb, "伪触点被丢弃",
                () => TouchpadSource.IsPhantom(0, 0, 1800, 1275)
                   && TouchpadSource.IsPhantom(1800, 0, 1800, 1275)
                   && TouchpadSource.IsPhantom(0, 1275, 1800, 1275)
                   && TouchpadSource.IsPhantom(1800, 1275, 1800, 1275)
                   && !TouchpadSource.IsPhantom(900, 0, 1800, 1275)     // 顶边中段是真触摸
                   && !TouchpadSource.IsPhantom(0, 600, 1800, 1275)     // 左边缘中段也是
                   && !TouchpadSource.IsPhantom(5, 4, 1800, 1275),      // 贴着角但没落正
                "IsPhantom 判定不正确");

            // BK 连续手势抖动：来回反向几次后自动停手，之后不论怎么滑都不再响
            {
                long tbk = 1000;
                int bkFires = 0, bkQuiet = 0;
                CheckBool(sb, "连续手势抖动会自动停手", () =>
                {
                    Play(e =>
                    {
                        e.Fired += (k, s) => bkFires++;
                        Feed(e, 1, 50, 700); tbk += 10;
                        for (int i = 0; i < 20; i++)
                        {
                            tbk += 10;
                            Feed(e, 1, 50, i % 2 == 0 ? 745 : 712);   // 在 +45 / +12 之间来回
                        }
                        bkQuiet = bkFires;
                        for (int i = 0; i < 10; i++)                  // 之后大幅上滑：不该再响
                        {
                            tbk += 10;
                            Feed(e, 1, 50, 745 + i * 30);
                        }
                        Feed(e, 0, 0, 0);
                    }, null, () => tbk);
                    return bkQuiet > 0 && bkFires == bkQuiet;
                }, "抖动期间 " + bkFires + " 次，判定停手后又触发 " + (bkFires - bkQuiet) + " 次");
            }

            // BL 连续手势超过 3 秒不再响应（手掌压着不动也会漂移）
            {
                long tbl = 1000;
                int blFires = 0, blAt3s = 0;
                CheckBool(sb, "连续手势超过 3 秒停手", () =>
                {
                    Play(e =>
                    {
                        e.Fired += (k, s) => blFires++;
                        tbl += 60;
                        Feed(e, 1, 50, 1200);
                        for (int i = 1; i <= 60; i++)          // 60 帧 × 60ms = 3.6 秒
                        {
                            tbl += 60;
                            Feed(e, 1, 50, 1200 - i * 8);
                        }
                        blAt3s = blFires;
                        tbl += 60;
                        Feed(e, 1, 50, 200);                    // 3 秒后再大幅度移动
                        Feed(e, 0, 0, 0);
                    }, null, () => tbl);
                    return blAt3s > 0 && blFires == blAt3s;
                }, "3 秒内 " + blAt3s + " 次，之后又触发 " + (blFires - blAt3s) + " 次");
            }

            sb.AppendLine();
            sb.AppendLine("结果：通过 " + _pass + " 项，失败 " + _fail + " 项");

            string path = Path.Combine(AppContext.BaseDirectory, "selftest.txt");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private sealed class FakeTarget : IWindowTarget
        {
            public int ScreenWidth { get { return 1000; } }
            public int ScreenHeight { get { return 1000; } }
            public int Captures;
            public int Moves;
            public int LastX = -1, LastY = -1;
            public int X0 = 100, Y0 = 100;

            public bool TryCapture(out IntPtr hwnd, out int x, out int y)
            {
                Captures++;
                hwnd = new IntPtr(123);
                x = X0;
                y = Y0;
                return true;
            }

            public void Move(IntPtr hwnd, int x, int y)
            {
                Moves++;
                LastX = x;
                LastY = y;
            }

            public SnapZone PendingZone = SnapZone.None;
            public SnapZone AppliedZone = SnapZone.None;
            public int SnapApplies;

            public int CycleCalls, MaxCalls;
            public IntPtr Current = new IntPtr(456);

            public bool TryCurrent(out IntPtr hwnd) { hwnd = Current; return true; }
            public bool CycleWindow(IntPtr hwnd) { CycleCalls++; return true; }
            public bool MaximizeWindow(IntPtr hwnd) { MaxCalls++; return true; }

            public SnapZone EvaluateSnap(IntPtr hwnd) { return PendingZone; }

            public void ApplySnap(IntPtr hwnd, SnapZone zone)
            {
                SnapApplies++;
                AppliedZone = zone;
            }
        }

        private static void CheckBool(StringBuilder sb, string name, Func<bool> test, string detail)
        {
            bool ok = false;
            string extra = "";
            try { ok = test(); }
            catch (Exception ex) { extra = " 异常:" + ex.Message; }
            if (ok) _pass++; else _fail++;
            sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok) sb.AppendLine("        " + detail + extra);
        }

        private static void FeedList(Engine e, int cc, params double[] xy)
        {
            var list = new List<Contact>();
            for (int i = 0; i + 1 < xy.Length && list.Count < cc; i += 2)
                list.Add(new Contact { Id = i / 2, X = xy[i], Y = xy[i + 1] });
            e.OnSample(list, cc, MAX_X, MAX_Y);
        }

        /// <summary>显式指定触点 ID 的喂法：三元组 (id, x, y)，用来模拟“某一根手指抬起、别的还按着”。</summary>
        private static void FeedContacts(Engine e, params double[] idXy)
        {
            var list = new List<Contact>();
            for (int i = 0; i + 2 < idXy.Length; i += 3)
                list.Add(new Contact { Id = (int)idXy[i], X = idXy[i + 1], Y = idXy[i + 2] });
            e.OnSample(list, list.Count, MAX_X, MAX_Y);
        }

        /// <summary>两指一起沿线段走（两指间距固定 120 单位），每次 10 点。</summary>
        private static void TwoPath(Engine e, double x0, double y0, double x1, double y1)
        {
            const int Steps = 12;
            for (int i = 0; i <= Steps; i++)
            {
                double t = (double)i / Steps;
                double x = x0 + (x1 - x0) * t;
                double y = y0 + (y1 - y0) * t;
                FeedContacts(e, 0, x - 60, y, 1, x + 60, y);
            }
        }

        private static void TwoCircle(Engine e, double cx, double cy, double r)
        {
            const int Steps = 40;
            for (int i = 0; i <= Steps; i++)
            {
                double a = i / (double)Steps * Math.PI * 2;
                double x = cx + Math.Cos(a) * r;
                double y = cy + Math.Sin(a) * r * 0.75;
                FeedContacts(e, 0, x - 60, y, 1, x + 60, y);
            }
        }

        private static void FeedSpread(Engine e, double cx, double cy, double radius)
        {
            var list = new List<Contact>();
            for (int i = 0; i < 5; i++)
            {
                double a = i * 2 * Math.PI / 5;
                list.Add(new Contact { Id = i, X = cx + Math.Cos(a) * radius, Y = cy + Math.Sin(a) * radius });
            }
            e.OnSample(list, 5, MAX_X, MAX_Y);
        }

        private static void Hold(Engine e, double x, double y, int samples, ref long t, long step)
        {
            for (int i = 0; i < samples; i++) { t += step; Feed(e, 1, x, y); }
        }

        private static void Lift(Engine e, ref long t, long step)
        {
            t += step;
            Feed(e, 0, 0, 0);
        }

        private static void Feed(Engine e, int count, double x, double y)
        {
            Feed(e, count, x, y, MAX_X, MAX_Y);
        }

        private static void Feed(Engine e, int count, double x, double y, int maxX, int maxY)
        {
            var list = new List<Contact>();
            for (int i = 0; i < count; i++) list.Add(new Contact { Id = i, X = x, Y = y });
            e.OnSample(list, count, maxX, maxY);
        }

        private static List<string> Play(Action<Engine> script, Action<Settings> setup = null, Func<long> clock = null, bool dry = true)
        {
            var e = new Engine { S = Settings.CreateDefault(), DryRun = dry };
            e.S.FreezeCursor = false;
            if (setup != null) setup(e.S);
            if (clock != null) e.Clock = clock;
            var fired = new List<string>();
            e.Fired += (key, spec) => fired.Add(key);
            script(e);
            return fired;
        }

        private static void Check(StringBuilder sb, string name, List<string> actual, string[] expected)
        {
            bool ok = actual.Count == expected.Length;
            if (ok)
                for (int i = 0; i < expected.Length; i++)
                    if (actual[i] != expected[i]) { ok = false; break; }

            if (ok) _pass++; else _fail++;

            sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok)
            {
                sb.AppendLine("        期望 " + expected.Length + " 次 [" + string.Join(",", expected) + "]");
                sb.AppendLine("        实际 " + actual.Count + " 次 [" + string.Join(",", actual) + "]");
            }
        }
    }
}
