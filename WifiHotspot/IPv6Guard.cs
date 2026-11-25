using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace WifiHotspot
{
    public static class IPv6Guard
    {
        private const int LayerNetworkForward = 1;
        private const uint ImpostorFlag = 1u << 19;

        private static IntPtr _handle = IntPtr.Zero;
        private static Thread _thread;
        private static volatile bool _running;
        private static string _lastMessage = "未运行";

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

        public static HotspotResult Start(string policy)
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

            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = WinDivertOpen("ipv6", LayerNetworkForward, 0, 0);
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

            string activePolicy = (policy ?? "block").ToLowerInvariant();
            _handle = handle;
            _running = true;
            _thread = new Thread(delegate() { Loop(handle, activePolicy); });
            _thread.IsBackground = true;
            _thread.Start();
            _lastMessage = activePolicy == "ula-only"
                ? "IPv6 转发封禁已启动（仅允许 ULA/Link-local）"
                : "IPv6 转发封禁已启动（丢弃全部转发 IPv6）";
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
            if (_thread != null && _thread.IsAlive) _thread.Join(3000);
            IntPtr handle = _handle;
            _handle = IntPtr.Zero;
            _thread = null;
            if (handle != IntPtr.Zero && handle != new IntPtr(-1))
            {
                try { WinDivertClose(handle); } catch { }
            }
            _lastMessage = "IPv6 转发封禁已停止";
            return new HotspotResult { Success = true, Message = _lastMessage };
        }

        private static void Loop(IntPtr handle, string policy)
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
                    if (received < 40 || received > bufferSize) continue;
                    if ((address.Flags & ImpostorFlag) != 0) continue;

                    Marshal.Copy(buffer, packet, 0, (int)received);
                    if ((packet[0] >> 4) != 6 || ShouldDrop(packet, policy)) continue;

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

        private static bool ShouldDrop(byte[] packet, string policy)
        {
            if (policy == "ula-only")
                return !IsAllowedIPv6Address(packet, 8) || !IsAllowedIPv6Address(packet, 24);
            return true;
        }

        private static bool IsAllowedIPv6Address(byte[] packet, int offset)
        {
            byte first = packet[offset];
            byte second = packet[offset + 1];
            bool uniqueLocal = (first & 0xfe) == 0xfc;
            bool linkLocal = first == 0xfe && (second & 0xc0) == 0x80;
            return uniqueLocal || linkLocal;
        }
    }
}
