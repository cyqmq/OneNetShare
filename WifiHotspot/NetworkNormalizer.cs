using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace WifiHotspot
{
    public class NetworkNormalizer
    {
        private const int BufferSize = 65575;
        private const int LayerNetwork = 0;
        private const int LayerNetworkForward = 1;
        private const uint ImpostorFlag = 1u << 19;

        private IntPtr _networkHandle = IntPtr.Zero;
        private IntPtr _forwardHandle = IntPtr.Zero;
        private Thread _networkThread;
        private Thread _forwardThread;
        private volatile bool _running;
        private string _lastMessage = "未运行";
        private int _ipIdCounter = 0x4000;
        private static readonly NetworkNormalizer SharedInstance = new NetworkNormalizer();

        public static NetworkNormalizer Shared
        {
            get { return SharedInstance; }
        }

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

        [DllImport("WinDivert.dll", EntryPoint = "WinDivertHelperCalcChecksums", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertHelperCalcChecksums(IntPtr packet, uint packetLen, ref WinDivertAddress address, ulong flags);

        [DllImport("WinDivert.dll", EntryPoint = "WinDivertClose", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertClose(IntPtr handle);

        public bool IsRunning
        {
            get { return _running; }
        }

        public string LastMessage
        {
            get { return _lastMessage; }
        }

        public static bool DriverAvailable()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            bool dll = File.Exists(Path.Combine(dir, "WinDivert.dll")) ||
                       File.Exists(Path.Combine(Environment.SystemDirectory, "WinDivert.dll"));
            return dll &&
                   File.Exists(Path.Combine(dir, "WinDivert32.sys")) &&
                   File.Exists(Path.Combine(dir, "WinDivert64.sys"));
        }

        public HotspotResult Start(ProxySettings settings)
        {
            if (_running)
            {
                _lastMessage = "网络层归一化已在运行";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }
            if (settings == null || !settings.NetworkEnabled)
            {
                _lastMessage = "网络层归一化未启用";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }
            if (!HotspotManager.IsAdministrator())
            {
                _lastMessage = "网络层归一化需要管理员权限";
                return new HotspotResult { Success = false, Message = _lastMessage };
            }
            if (!DriverAvailable())
            {
                _lastMessage = "未找到 WinDivert 2.x 文件。请把 WinDivert.dll、WinDivert32.sys、WinDivert64.sys 放到程序目录后重试。";
                return new HotspotResult { Success = false, Message = _lastMessage };
            }

            try
            {
                _networkHandle = OpenLayer(LayerNetwork);
                if (settings.NetworkForward)
                    _forwardHandle = OpenLayer(LayerNetworkForward);
            }
            catch (Exception ex)
            {
                CloseHandles();
                _lastMessage = "WinDivert 初始化失败: " + ex.Message + "\n需要以管理员权限运行，并确认 WinDivert 驱动已安装。";
                return new HotspotResult { Success = false, Message = _lastMessage };
            }

            _running = true;
            _networkThread = StartThread(_networkHandle, settings);
            if (_forwardHandle != IntPtr.Zero)
                _forwardThread = StartThread(_forwardHandle, settings);
            _lastMessage = "网络层指纹归一化已启动";
            return new HotspotResult { Success = true, Message = _lastMessage };
        }

        public HotspotResult Stop()
        {
            if (!_running)
            {
                _lastMessage = "网络层归一化未运行";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }
            _running = false;
            if (_networkThread != null && _networkThread.IsAlive) _networkThread.Join(3000);
            if (_forwardThread != null && _forwardThread.IsAlive) _forwardThread.Join(3000);
            CloseHandles();
            _lastMessage = "网络层指纹归一化已停止";
            return new HotspotResult { Success = true, Message = _lastMessage };
        }

        private static IntPtr OpenLayer(int layer)
        {
            IntPtr handle = WinDivertOpen("outbound and (ip or ipv6)", layer, 0, 0);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                throw new InvalidOperationException("WinDivertOpen 失败，错误码 " + Marshal.GetLastWin32Error());
            return handle;
        }

        private Thread StartThread(IntPtr handle, ProxySettings settings)
        {
            var thread = new Thread(delegate() { Loop(handle, settings); });
            thread.IsBackground = true;
            thread.Start();
            return thread;
        }

        private void Loop(IntPtr handle, ProxySettings settings)
        {
            IntPtr buffer = Marshal.AllocHGlobal(BufferSize);
            var managed = new byte[BufferSize];
            try
            {
                while (_running && handle != IntPtr.Zero && handle != new IntPtr(-1))
                {
                    WinDivertAddress address = NewAddress();
                    uint received = 0;
                    if (!WinDivertRecv(handle, buffer, BufferSize, out received, ref address))
                    {
                        if (!_running) break;
                        Thread.Sleep(2);
                        continue;
                    }
                    if (received == 0 || received > BufferSize) continue;
                    if ((address.Flags & ImpostorFlag) != 0) continue;

                    int length = (int)received;
                    Marshal.Copy(buffer, managed, 0, length);
                    bool changed = Rewrite(managed, ref length, settings);
                    if (changed)
                    {
                        Marshal.Copy(managed, 0, buffer, length);
                        WinDivertHelperCalcChecksums(buffer, (uint)length, ref address, 0);
                    }

                    uint sent = 0;
                    WinDivertSend(handle, buffer, (uint)length, out sent, ref address);
                }
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static WinDivertAddress NewAddress()
        {
            return new WinDivertAddress { Reserved3 = new byte[64] };
        }

        private bool Rewrite(byte[] packet, ref int length, ProxySettings settings)
        {
            bool changed = false;
            int version = packet[0] >> 4;
            int ipLength;
            int protocol;
            bool ipv4;

            if (version == 4)
            {
                ipLength = (packet[0] & 0x0f) * 4;
                if (ipLength < 20 || length < ipLength) return false;
                ipv4 = true;
                protocol = packet[9];

                if (settings.NetworkTTL > 0 && packet[8] != settings.NetworkTTL)
                {
                    packet[8] = (byte)settings.NetworkTTL;
                    changed = true;
                }
                if (settings.NetworkIPID)
                {
                    ushort id = (ushort)Interlocked.Increment(ref _ipIdCounter);
                    packet[4] = (byte)(id >> 8);
                    packet[5] = (byte)id;
                    changed = true;
                }
                int fragment = ReadU16(packet, 6);
                if ((fragment & 0x1fff) != 0 || (fragment & 0x2000) != 0) return changed;
            }
            else if (version == 6)
            {
                ipLength = 40;
                if (length < ipLength) return false;
                ipv4 = false;
                protocol = packet[6];

                if (settings.NetworkTTL > 0 && packet[7] != settings.NetworkTTL)
                {
                    packet[7] = (byte)settings.NetworkTTL;
                    changed = true;
                }
                if (settings.NetworkIPID && (packet[1] != 0 || packet[2] != 0 || packet[3] != 0))
                {
                    packet[1] = 0;
                    packet[2] = 0;
                    packet[3] = 0;
                    changed = true;
                }
            }
            else
            {
                return false;
            }

            if (protocol != 6) return changed;
            int mssClamp = settings.MtuClampEnabled && settings.MssClamp > 0 ? settings.MssClamp : 1460;
            if (settings.NetworkTCPOptions)
                return RewriteTcpOptions(packet, ref length, ipLength, ipv4, mssClamp) || changed;
            if (settings.MtuClampEnabled)
                return ClampTcpMss(packet, ipLength, length, mssClamp) || changed;
            return changed;
        }

        private static bool RewriteTcpOptions(byte[] packet, ref int length, int ipLength, bool ipv4, int mssClamp)
        {
            int tcp = ipLength;
            if (length < tcp + 20) return false;
            int dataOffset = (packet[tcp + 12] >> 4) * 4;
            if (dataOffset < 20 || tcp + dataOffset > length) return false;
            int flags = packet[tcp + 13];
            if ((flags & 0x02) == 0) return false;

            int optionLength = dataOffset - 20;
            int cursor = tcp + 20;
            int end = tcp + dataOffset;
            while (cursor < end)
            {
                int kind = packet[cursor];
                if (kind != 0 && kind != 1 && kind != 2 && kind != 3 && kind != 4 && kind != 8) return false;
                if (kind == 0) break;
                if (cursor + 1 >= end) break;
                int optionSize = packet[cursor + 1];
                if (optionSize < 2) break;
                cursor += optionSize;
            }

            WriteU16(packet, tcp + 14, 65535);
            byte[] options = BuildTcpOptions(mssClamp);
            if (optionLength == options.Length)
            {
                bool same = true;
                for (int i = 0; i < options.Length; i++)
                {
                    if (packet[tcp + 20 + i] != options[i]) { same = false; break; }
                }
                if (same) return true;
            }

            int delta = options.Length - optionLength;
            if (length + delta > packet.Length) return true;
            int payloadStart = tcp + dataOffset;
            int payloadLength = length - payloadStart;
            if (delta > 0)
                Array.Copy(packet, payloadStart, packet, payloadStart + delta, payloadLength);
            else if (delta < 0)
                Array.Copy(packet, payloadStart, packet, payloadStart + delta, payloadLength);
            Array.Copy(options, 0, packet, tcp + 20, options.Length);
            packet[tcp + 12] = (byte)(((20 + options.Length) / 4) << 4);
            length += delta;

            if (ipv4)
                WriteU16(packet, 2, (ushort)length);
            else
                WriteU16(packet, 4, (ushort)(length - 40));
            return true;
        }

        private static bool ClampTcpMss(byte[] packet, int tcp, int length, int mssClamp)
        {
            if (length < tcp + 20) return false;
            int dataOffset = (packet[tcp + 12] >> 4) * 4;
            if (dataOffset < 20 || tcp + dataOffset > length) return false;
            if ((packet[tcp + 13] & 0x02) == 0) return false;

            int cursor = tcp + 20;
            int end = tcp + dataOffset;
            while (cursor < end)
            {
                int kind = packet[cursor];
                if (kind == 0) break;
                if (kind == 1) { cursor++; continue; }
                if (cursor + 1 >= end) break;
                int optionSize = packet[cursor + 1];
                if (optionSize < 2) break;
                if (kind == 2 && optionSize >= 4)
                {
                    int current = ReadU16(packet, cursor + 2);
                    if (current <= mssClamp) return false;
                    WriteU16(packet, cursor + 2, (ushort)mssClamp);
                    return true;
                }
                cursor += optionSize;
            }
            return false;
        }

        private static byte[] BuildTcpOptions(int mssClamp)
        {
            int mss = mssClamp > 0 && mssClamp <= 65535 ? mssClamp : 1460;
            return new byte[]
            {
                2, 4, (byte)(mss >> 8), (byte)mss,
                3, 3, 8,
                4, 2,
                1, 1,
                0
            };
        }

        private static ushort ReadU16(byte[] data, int offset)
        {
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        private static void WriteU16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)(value >> 8);
            data[offset + 1] = (byte)value;
        }

        private void CloseHandles()
        {
            if (_networkHandle != IntPtr.Zero && _networkHandle != new IntPtr(-1))
            {
                try { WinDivertClose(_networkHandle); } catch { }
                _networkHandle = IntPtr.Zero;
            }
            if (_forwardHandle != IntPtr.Zero && _forwardHandle != new IntPtr(-1))
            {
                try { WinDivertClose(_forwardHandle); } catch { }
                _forwardHandle = IntPtr.Zero;
            }
        }
    }
}
