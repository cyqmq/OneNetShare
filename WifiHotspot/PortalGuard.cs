using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;

namespace WifiHotspot
{
    public static class PortalGuard
    {
        private const int LayerNetworkForward = 1;
        private const uint ImpostorFlag = 1u << 19;

        private static IntPtr _handle = IntPtr.Zero;
        private static Thread _thread;
        private static volatile bool _running;
        private static string _lastMessage = "未运行";
        private static readonly HashSet<uint> BlockedAddrs = new HashSet<uint>();

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

        [DllImport("WinDivert.dll", EntryPoint = "WinDivertSend", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertSend(IntPtr handle, IntPtr packet, uint packetLen, out uint sendLen, ref WinDivertAddress address);

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

        private static uint ToNetworkOrderUInt(IPAddress addr)
        {
            byte[] bytes = addr.GetAddressBytes();
            return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
        }

        public static HotspotResult Start(string primary, string secondary)
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
            try
            {
                handle = WinDivertOpen(filter, LayerNetworkForward, 0, 0);
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

            lock (BlockedAddrs)
            {
                BlockedAddrs.Clear();
                foreach (string ip in ips)
                {
                    IPAddress addr;
                    if (IPAddress.TryParse(ip, out addr))
                        BlockedAddrs.Add(ToNetworkOrderUInt(addr));
                }
            }

            _handle = handle;
            _running = true;
            _thread = new Thread(delegate() { Loop(handle); });
            _thread.IsBackground = true;
            _thread.Start();
            _lastMessage = "Portal 封堵已启动（" + string.Join(", ", ips.ToArray()) + "）";
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
            if (_thread != null && _thread.IsAlive) _thread.Join(3000);
            IntPtr handle = _handle;
            _handle = IntPtr.Zero;
            _thread = null;
            if (handle != IntPtr.Zero && handle != new IntPtr(-1))
            {
                try { WinDivertClose(handle); } catch { }
            }
            _lastMessage = "Portal 封堵已停止";
            return new HotspotResult { Success = true, Message = _lastMessage };
        }

        private static void Loop(IntPtr handle)
        {
            const int bufferSize = 65575;
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            byte[] packet = new byte[bufferSize];
            try
            {
                while (_running)
                {
                    WinDivertAddress address = new WinDivertAddress { Reserved3 = new byte[64] };
                    uint received = 0;
                    if (!WinDivertRecv(handle, buffer, bufferSize, out received, ref address))
                    {
                        if (!_running) break;
                        Thread.Sleep(2);
                        continue;
                    }
                    if (received < 20 || received > bufferSize) continue;
                    if ((address.Flags & ImpostorFlag) != 0) continue;
                    // Drop matched packets; filter already narrows, this is a safety check.
                    Marshal.Copy(buffer, packet, 0, (int)received);
                    if ((packet[0] >> 4) != 4) continue;
                    uint src = ReadU32(packet, 12);
                    uint dst = ReadU32(packet, 16);
                    bool drop;
                    lock (BlockedAddrs) { drop = BlockedAddrs.Contains(src) || BlockedAddrs.Contains(dst); }
                    if (drop) continue;

                    uint sent = 0;
                    WinDivertSend(handle, buffer, received, out sent, ref address);
                }
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static uint ReadU32(byte[] packet, int offset)
        {
            return (uint)((packet[offset] << 24) | (packet[offset + 1] << 16) | (packet[offset + 2] << 8) | packet[offset + 3]);
        }
    }
}
