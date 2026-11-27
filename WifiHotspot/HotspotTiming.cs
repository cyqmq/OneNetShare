using System;
using System.Diagnostics;

namespace WifiHotspot
{
    internal static class HotspotTiming
    {
        private static readonly object Sync = new object();
        private static Stopwatch _startWatch;
        private static DateTime _onlineSince;
        private static string _lastStart = "未知";
        private static string _lastOnline = "未知";

        public static string StatusText
        {
            get
            {
                lock (Sync)
                {
                    return "最近启动耗时=" + _lastStart + "；最近在线时长=" + _lastOnline;
                }
            }
        }

        public static void BeginStart()
        {
            lock (Sync)
            {
                _startWatch = Stopwatch.StartNew();
            }
        }

        public static void EndStart(bool success)
        {
            lock (Sync)
            {
                if (_startWatch != null)
                {
                    _startWatch.Stop();
                    _lastStart = FormatElapsed(_startWatch.Elapsed);
                    _startWatch = null;
                }
                _onlineSince = success ? DateTime.Now : DateTime.MinValue;
            }
        }

        public static void EndSession()
        {
            lock (Sync)
            {
                if (_onlineSince == DateTime.MinValue) return;
                _lastOnline = FormatElapsed(DateTime.Now - _onlineSince);
                _onlineSince = DateTime.MinValue;
            }
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            return elapsed.TotalSeconds >= 1
                ? elapsed.TotalSeconds.ToString("0.0") + " s"
                : elapsed.Milliseconds + " ms";
        }
    }
}
