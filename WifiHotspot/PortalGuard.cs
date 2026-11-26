using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;

namespace WifiHotspot
{
    public static class PortalGuard
    {
        private const int LayerNetworkForward = 1;
        private const short GuardPriority = 1000;
        private const ulong FlagDrop = 0x0002;

        private static IntPtr _handle = IntPtr.Zero;
        private static volatile bool _running;
        private static string _lastMessage = "未运行";

        [DllImport("WinDivert.dll", EntryPoint = "WinDivertOpen", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr WinDivertOpen(string filter, int layer, short priority, ulong flags);

        [DllImport("WinDivert.dll", EntryPoint = "WinDivertClose", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertClose(IntPtr handle);

        public static bool IsRunning { get { return _running; } }
        public static string LastMessage { get { return _lastMessage; } }

        public static List<string> ParseIPs(string primary, string secondary)
        {
            var result = new List<string>();
            AddIPs(result, primary);
            AddIPs(result, secondary);
            return result;
        }

        private static void AddIPs(List<string> result, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            string[] parts = value.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                IPAddress addr;
                if (!IPAddress.TryParse(part.Trim(), out addr) || addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                string text = addr.ToString();
                if (!result.Contains(text)) result.Add(text);
            }
        }

        private static string BuildFilter(List<string> ips)
        {
            var parts = new List<string>();
            foreach (string ip in ips)
            {
                parts.Add("ip.DstAddr == " + ip);
                parts.Add("ip.SrcAddr == " + ip);
            }
            return string.Join(" or ", parts.ToArray());
        }

        public static HotspotResult Start(string primary, string secondary, int interfaceIndex = 0)
        {
            List<string> ips = ParseIPs(primary, secondary);
            if (ips.Count == 0)
            {
                _lastMessage = "Portal 封堵未配置 IP";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }
            if (_running)
            {
                _lastMessage = "Portal 封堵已在运行";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }
            if (!HotspotManager.IsAdministrator())
            {
                _lastMessage = "Portal 封堵需要管理员权限";
                return new HotspotResult { Success = false, Message = _lastMessage };
            }
            if (!NetworkNormalizer.DriverAvailable())
            {
                _lastMessage = "Portal 封堵需要完整的 WinDivert 2.x 文件";
                return new HotspotResult { Success = false, Message = _lastMessage };
            }

            IntPtr handle = IntPtr.Zero;
            string filter = BuildFilter(ips);
            if (interfaceIndex > 0) filter = "ifIdx == " + interfaceIndex + " and (" + filter + ")";
            try
            {
                handle = WinDivertOpen(filter, LayerNetworkForward, GuardPriority, FlagDrop);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                    throw new InvalidOperationException("WinDivertOpen 失败，错误码 " + Marshal.GetLastWin32Error());
            }
            catch (Exception ex)
            {
                if (handle != IntPtr.Zero && handle != new IntPtr(-1))
                {
                    try { WinDivertClose(handle); } catch { }
                }
                _lastMessage = "Portal 封堵启动失败: " + ex.Message;
                return new HotspotResult { Success = false, Message = _lastMessage };
            }

            _handle = handle;
            _running = true;
            string scope = interfaceIndex > 0 ? "，仅热点接口 ifIdx=" + interfaceIndex : "，全局";
            _lastMessage = "Portal 封堵已启动（" + string.Join(", ", ips.ToArray()) + "）" + scope;
            return new HotspotResult { Success = true, Message = _lastMessage };
        }

        public static HotspotResult Stop()
        {
            if (!_running && _handle == IntPtr.Zero)
            {
                _lastMessage = "Portal 封堵未运行";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }
            _running = false;
            IntPtr handle = _handle;
            _handle = IntPtr.Zero;
            if (handle != IntPtr.Zero && handle != new IntPtr(-1))
            {
                try { WinDivertClose(handle); } catch { }
            }
            _lastMessage = "Portal 封堵已停止";
            return new HotspotResult { Success = true, Message = _lastMessage };
        }
    }
}
