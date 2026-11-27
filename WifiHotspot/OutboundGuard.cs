using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace WifiHotspot
{
    public static class OutboundGuard
    {
        private const int BufferSize = 65575;
        private const int LayerNetworkForward = 1;

        private static IntPtr _handle = IntPtr.Zero;
        private static Thread _thread;
        private static volatile bool _running;
        private static string _lastMessage = "未运行";
        private static int _dropped;

        [StructLayout(LayoutKind.Sequential)]
        private struct WinDivertAddress
        {
            public long Timestamp;
            public uint Flags;
            public uint Reserved2;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public byte[] Reserved3;
        }

        [DllImport("WinDivert.dll", EntryPoint = "WinDivertOpen", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr WinDivertOpen(string filter, int layer, short priority, ulong flags);

        [DllImport("WinDivert.dll", EntryPoint = "WinDivertRecv", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertRecv(IntPtr handle, IntPtr packet, uint packetLen, out uint recvLen, ref WinDivertAddress address);

        [DllImport("WinDivert.dll", EntryPoint = "WinDivertClose", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertClose(IntPtr handle);

        public static bool IsRunning { get { return _running; } }

        public static string LastMessage { get { return _lastMessage; } }

        public static int DroppedPackets { get { return Interlocked.CompareExchange(ref _dropped, 0, 0); } }

        public static string StatsText
        {
            get
            {
                return "丢弃=" + DroppedPackets + "；" + _lastMessage;
            }
        }

        public static string BuildFilter(ProxySettings settings, int interfaceIndex)
        {
            int[] udpPorts;
            int[] tcpPorts;
            ProxyConfigManager.ParsePortsText(settings.OutboundPortsText, out udpPorts, out tcpPorts);
            var parts = new List<string>();
            if (udpPorts.Length > 0) parts.Add("udp and (" + PortFilter("udp", udpPorts) + ")");
            if (tcpPorts.Length > 0) parts.Add("tcp and (" + PortFilter("tcp", tcpPorts) + ")");
            if (settings.OutboundBlockBroadcast) parts.Add("ip.DstAddr == 255.255.255.255");
            if (settings.OutboundBlockMulticast)
                parts.Add("(ip.DstAddr >= 224.0.0.0 and ip.DstAddr <= 239.255.255.255) or (ipv6.DstAddr >= ff00:: and ipv6.DstAddr <= ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff)");
            if (parts.Count == 0) return "";
            string filter = "outbound and (" + string.Join(" or ", parts.ToArray()) + ")";
            if (interfaceIndex > 0) filter = "ifIdx == " + interfaceIndex + " and " + filter;
            return filter;
        }

        private static string PortFilter(string protocol, int[] ports)
        {
            var parts = new List<string>();
            foreach (int port in ports) parts.Add(protocol + ".DstPort == " + port);
            return string.Join(" or ", parts.ToArray());
        }

        public static HotspotResult Start(ProxySettings settings, int interfaceIndex = 0)
        {
            if (!settings.OutboundGuardEnabled)
            {
                _lastMessage = "出站封堵未启用";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }
            if (_running)
            {
                _lastMessage = "出站封堵已在运行；" + StatsText;
                return new HotspotResult { Success = true, Message = _lastMessage };
            }
            if (!HotspotManager.IsAdministrator())
            {
                _lastMessage = "出站封堵需要管理员权限";
                return new HotspotResult { Success = false, Message = _lastMessage };
            }
            if (!NetworkNormalizer.DriverAvailable())
            {
                _lastMessage = "出站封堵需要完整的 WinDivert 2.x 文件";
                return new HotspotResult { Success = false, Message = _lastMessage };
            }
            string filter = BuildFilter(settings, interfaceIndex);
            if (string.IsNullOrWhiteSpace(filter))
            {
                _lastMessage = "出站封堵未配置任何规则";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }

            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = WinDivertOpen(filter, LayerNetworkForward, 1000, 0);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                    throw new InvalidOperationException("WinDivertOpen 失败，错误码 " + Marshal.GetLastWin32Error());
            }
            catch (Exception ex)
            {
                if (handle != IntPtr.Zero && handle != new IntPtr(-1))
                {
                    try { WinDivertClose(handle); } catch { }
                }
                _lastMessage = "出站封堵启动失败: " + ex.Message;
                return new HotspotResult { Success = false, Message = _lastMessage };
            }

            _handle = handle;
            _running = true;
            Interlocked.Exchange(ref _dropped, 0);
            string scope = interfaceIndex > 0 ? "，仅热点接口 ifIdx=" + interfaceIndex : "，全局转发";
            _lastMessage = "出站封堵已启动（不回注匹配包，计数可见）" + scope;
            _thread = new Thread(new ThreadStart(Loop));
            _thread.IsBackground = true;
            _thread.Start();
            return new HotspotResult { Success = true, Message = _lastMessage };
        }

        private static void Loop()
        {
            IntPtr buffer = Marshal.AllocHGlobal(BufferSize);
            var address = new WinDivertAddress();
            try
            {
                while (_running)
                {
                    uint received = 0;
                    bool ok = WinDivertRecv(_handle, buffer, BufferSize, out received, ref address);
                    if (!ok)
                    {
                        if (_running)
                        {
                            int error = Marshal.GetLastWin32Error();
                            _lastMessage = "出站封接收包失败，错误码 " + error + "；规则已失效，请重启热点";
                            _running = false;
                        }
                        break;
                    }
                    Interlocked.Increment(ref _dropped);
                }
            }
            catch (Exception ex)
            {
                if (_running) _lastMessage = "出站封堵线程异常: " + ex.Message;
                _running = false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public static HotspotResult Stop()
        {
            if (!_running && _handle == IntPtr.Zero)
            {
                _lastMessage = "出站封堵未运行";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }
            _running = false;
            Thread thread = _thread;
            _thread = null;
            if (thread != null && thread.IsAlive)
            {
                try { thread.Join(1000); } catch { }
            }
            IntPtr handle = _handle;
            _handle = IntPtr.Zero;
            if (handle != IntPtr.Zero && handle != new IntPtr(-1))
            {
                try { WinDivertClose(handle); } catch { }
            }
            string stats = "总丢弃=" + DroppedPackets;
            _lastMessage = "出站封堵已停止（" + stats + "）";
            return new HotspotResult { Success = true, Message = _lastMessage };
        }
    }
}
