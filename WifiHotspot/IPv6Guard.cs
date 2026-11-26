using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace WifiHotspot
{
    public static class IPv6Guard
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

        public static bool IsRunning
        {
            get { return _running; }
        }

        public static string LastMessage
        {
            get { return _lastMessage; }
        }

        public static bool IsEnabled(string policy)
        {
            string value = (policy ?? "block").ToLowerInvariant();
            return value != "allow";
        }

        public static HotspotResult Start(string policy, int interfaceIndex = 0)
        {
            if (!IsEnabled(policy))
            {
                _lastMessage = "IPv6 转发封禁未启用";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }
            if (_running)
            {
                _lastMessage = "IPv6 转发封禁已在运行";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }
            if (!HotspotManager.IsAdministrator())
            {
                _lastMessage = "IPv6 转发封禁需要管理员权限";
                return new HotspotResult { Success = false, Message = _lastMessage };
            }
            if (!NetworkNormalizer.DriverAvailable())
            {
                _lastMessage = "IPv6 转发封禁需要完整的 WinDivert 2.x 文件";
                return new HotspotResult { Success = false, Message = _lastMessage };
            }

            string activePolicy = (policy ?? "block").ToLowerInvariant();
            string filter = activePolicy == "ula-only"
                ? "ipv6 and not ((ipv6.SrcAddr >= fd00:: and ipv6.SrcAddr <= fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff and ipv6.DstAddr >= fd00:: and ipv6.DstAddr <= fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff) or (ipv6.SrcAddr >= fe80:: and ipv6.SrcAddr <= febf:ffff:ffff:ffff:ffff:ffff:ffff:ffff and ipv6.DstAddr >= fe80:: and ipv6.DstAddr <= febf:ffff:ffff:ffff:ffff:ffff:ffff:ffff))"
                : "ipv6";
            if (interfaceIndex > 0) filter = "ifIdx == " + interfaceIndex + " and (" + filter + ")";
            IntPtr handle = IntPtr.Zero;
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
                _lastMessage = "IPv6 转发封禁启动失败: " + ex.Message;
                return new HotspotResult { Success = false, Message = _lastMessage };
            }

            _handle = handle;
            _running = true;
            string scope = interfaceIndex > 0 ? "，仅热点接口 ifIdx=" + interfaceIndex : "，全局";
            _lastMessage = (activePolicy == "ula-only"
                ? "IPv6 转发封禁已启动（仅允许 ULA/Link-local）"
                : "IPv6 转发封禁已启动（丢弃全部转发 IPv6）") + scope;
            return new HotspotResult { Success = true, Message = _lastMessage };
        }

        public static HotspotResult Stop()
        {
            if (!_running && _handle == IntPtr.Zero)
            {
                _lastMessage = "IPv6 转发封禁未运行";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }

            _running = false;
            IntPtr handle = _handle;
            _handle = IntPtr.Zero;
            if (handle != IntPtr.Zero && handle != new IntPtr(-1))
            {
                try { WinDivertClose(handle); } catch { }
            }
            _lastMessage = "IPv6 转发封禁已停止";
            return new HotspotResult { Success = true, Message = _lastMessage };
        }
    }
}
