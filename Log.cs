using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace EdgePad
{
    /// <summary>
    /// 异步日志。原来每写一行都要 GetDirectoryName + Directory.Exists + FileInfo + AppendAllText，
    /// 实测 320µs/行，且是在处理触控板报文的线程上同步写盘——滚动时每档都要写一行，直接拖慢报文处理。
    /// 现在只入队（约 1µs），由后台定时器每 250ms 批量落盘。
    /// </summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private static readonly Queue<string> Queue = new Queue<string>(4096);
        private static Timer _timer;
        public static long Dropped;

        public static string Path
        {
            get
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EdgePad");
                return System.IO.Path.Combine(dir, "edgepad.log");
            }
        }

        public static void Write(string text)
        {
            lock (Gate)
            {
                if (Queue.Count >= 4000) { Queue.Dequeue(); Dropped++; }
                Queue.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + text);
                if (_timer == null) _timer = new Timer(_ => Flush(), null, 250, 250);
            }
        }

        public static void Flush()
        {
            string[] lines;
            lock (Gate)
            {
                if (Queue.Count == 0) return;
                lines = Queue.ToArray();
                Queue.Clear();
            }
            try
            {
                string path = Path;
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 2 * 1024 * 1024) fi.Delete();
                File.AppendAllLines(path, lines);
            }
            catch { }
        }

        public static void Shutdown()
        {
            Flush();
            lock (Gate) { if (_timer != null) { _timer.Dispose(); _timer = null; } }
        }
    }
}
