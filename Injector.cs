using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace EdgePad
{
    /// <summary>
    /// 输入注入队列。实测 SendInput 发一档滚轮会阻塞 0.8ms（中位）、P99 9ms、最坏 15ms，
    /// 同步调用会把处理触控板报文的线程堵住，导致报文成串到达、手势被误判成抬手。
    /// 所有注入统一交给后台线程，UI 线程只入队（约 1µs）。
    /// </summary>
    public static class Injector
    {
        private sealed class Item
        {
            public Native.INPUT[] Batch;
            public Action Done;
        }

        private static readonly object Gate = new object();
        private static readonly Queue<Item> Queue = new Queue<Item>(128);
        private static Thread _worker;
        private static bool _run;
        private static volatile bool _inFlight;   // 已出队、SendInput 还没返回

        public static long Posted, Sent, Dropped;
        public static double LastMs;

        public static int Pending { get { lock (Gate) return Queue.Count; } }

        public static void Post(Native.INPUT[] batch, Action done = null)
        {
            if (batch == null || batch.Length == 0) return;
            lock (Gate)
            {
                EnsureWorker();
                if (Queue.Count >= 128) { Dropped++; }
                else Queue.Enqueue(new Item { Batch = batch, Done = done });
                Posted++;
                Monitor.Pulse(Gate);
            }
        }

        private static void EnsureWorker()
        {
            if (_worker != null) return;
            _run = true;
            _worker = new Thread(Loop)
            {
                IsBackground = true,
                Name = "EdgePad.Injector",
                Priority = ThreadPriority.AboveNormal
            };
            _worker.Start();
        }

        private static void Loop()
        {
            var sw = new Stopwatch();
            while (true)
            {
                Item item;
                lock (Gate)
                {
                    while (_run && Queue.Count == 0) Monitor.Wait(Gate, 250);
                    if (!_run) return;
                    item = Queue.Dequeue();
                }

                sw.Restart();
                _inFlight = true;
                try
                {
                    Native.SendInput((uint)item.Batch.Length, item.Batch,
                                     System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.INPUT)));
                }
                catch { }
                LastMs = sw.Elapsed.TotalMilliseconds;
                Sent++;
                _inFlight = false;

                if (item.Done != null) { try { item.Done(); } catch { } }
            }
        }

        /// <summary>等队列清空且最后一批真的发出去（自检、退出前用）。</summary>
        public static bool Flush(int timeoutMs)
        {
            int waited = 0;
            while ((Pending > 0 || _inFlight) && waited < timeoutMs) { Thread.Sleep(2); waited += 2; }
            return Pending == 0 && !_inFlight;
        }

        public static void Shutdown()
        {
            lock (Gate) { _run = false; Monitor.PulseAll(Gate); }
        }
    }
}
