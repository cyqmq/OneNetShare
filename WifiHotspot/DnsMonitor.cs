using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WifiHotspot
{
    public class DnsMonitor
    {
        private const int BufferSize = 65575;
        private const int LayerNetwork = 0;
        private const ulong FlagSniff = 0x0001;
        private const uint FlagOutbound = 1u << 17;
        private const uint FlagLoopback = 1u << 18;
        private const uint FlagImpostor = 1u << 19;
        private const int PendingWindowSeconds = 4;
        private static readonly DnsMonitor SharedInstance = new DnsMonitor();
        private static readonly object LogLock = new object();
        private static readonly object StateLock = new object();

        private IntPtr _handle = IntPtr.Zero;
        private Thread _thread;
        private volatile bool _running;
        private string _lastMessage = "未运行";
        private readonly HashSet<string> _localIPs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _pending = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private long _phoneQueries;
        private long _upstreamQueries;
        private long _upstreamResponses;
        private long _phoneResponses;
        private long _notForwarded;

        public static DnsMonitor Shared
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

        [DllImport("WinDivert.dll", EntryPoint = "WinDivertClose", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertClose(IntPtr handle);

        private class PacketInfo
        {
            public IPAddress Source;
            public IPAddress Destination;
            public ushort SourcePort;
            public ushort DestinationPort;
            public int PayloadOffset;
        }

        private class DnsInfo
        {
            public string Name;
            public bool Response;
        }

        public bool IsRunning
        {
            get { return _running; }
        }

        public string LastMessage
        {
            get { return _lastMessage; }
        }

        public string StatusText
        {
            get
            {
                return (_running ? "运行" : "未运行") +
                       "；手机查询=" + Interlocked.Read(ref _phoneQueries) +
                       " 上行=" + Interlocked.Read(ref _upstreamQueries) +
                       " 上游响应=" + Interlocked.Read(ref _upstreamResponses) +
                       " 返回手机=" + Interlocked.Read(ref _phoneResponses) +
                       " 未转发=" + Interlocked.Read(ref _notForwarded);
            }
        }

        public HotspotResult Start(ProxySettings settings)
        {
            if (_running)
            {
                _lastMessage = "DNS 端口监控已在运行";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }
            if (settings == null || !settings.MonitorEnabled || !settings.MonitorDns)
            {
                _lastMessage = "DNS 端口监控未启用";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }
            if (!HotspotManager.IsAdministrator())
            {
                _lastMessage = "DNS 端口监控需要管理员权限";
                return new HotspotResult { Success = false, Message = _lastMessage };
            }
            if (!NetworkNormalizer.DriverAvailable())
            {
                _lastMessage = "DNS 端口监控缺少 WinDivert 文件";
                return new HotspotResult { Success = false, Message = _lastMessage };
            }

            try
            {
                _handle = WinDivertOpen("udp.SrcPort == 53 or udp.DstPort == 53", LayerNetwork, 0, FlagSniff);
                if (_handle == IntPtr.Zero || _handle == new IntPtr(-1))
                {
                    int error = Marshal.GetLastWin32Error();
                    _lastMessage = "WinDivert UDP:53 sniff 打开失败，错误码 " + error;
                    WriteProbe("[probe][监控] " + _lastMessage);
                    return new HotspotResult { Success = false, Message = _lastMessage };
                }
            }
            catch (Exception ex)
            {
                _lastMessage = "DNS 端口监控初始化失败: " + ex.Message;
                WriteProbe("[probe][监控] " + _lastMessage);
                return new HotspotResult { Success = false, Message = _lastMessage };
            }

            RefreshLocalIPs();
            lock (StateLock)
            {
                _pending.Clear();
            }
            _running = true;
            _thread = new Thread(new ThreadStart(delegate { Loop(_handle); }));
            _thread.IsBackground = true;
            _thread.Start();
            _lastMessage = "DNS 端口监控已启动（WinDivert 只读 sniff UDP:53）";
            WriteProbe("[probe][监控] " + _lastMessage);
            return new HotspotResult { Success = true, Message = _lastMessage };
        }

        public HotspotResult Stop()
        {
            if (!_running)
            {
                _lastMessage = "DNS 端口监控未运行";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }
            _running = false;
            if (_thread != null && _thread.IsAlive) _thread.Join(2000);
            CloseHandle();
            _lastMessage = "DNS 端口监控已停止";
            WriteProbe("[probe][监控] " + _lastMessage);
            return new HotspotResult { Success = true, Message = _lastMessage };
        }

        private void Loop(IntPtr handle)
        {
            IntPtr buffer = Marshal.AllocHGlobal(BufferSize);
            byte[] managed = new byte[BufferSize];
            DateTime lastSweep = DateTime.UtcNow;
            try
            {
                while (_running && handle != IntPtr.Zero && handle != new IntPtr(-1))
                {
                    WinDivertAddress address = NewAddress();
                    uint received;
                    if (!WinDivertRecv(handle, buffer, BufferSize, out received, ref address))
                    {
                        if (!_running) break;
                        Thread.Sleep(5);
                        continue;
                    }
                    if (received == 0 || received > BufferSize) continue;
                    if ((address.Flags & (FlagLoopback | FlagImpostor)) != 0) continue;
                    Marshal.Copy(buffer, managed, 0, (int)received);
                    Observe(managed, (int)received, (address.Flags & FlagOutbound) != 0);
                    if (DateTime.UtcNow - lastSweep > TimeSpan.FromSeconds(1))
                    {
                        SweepPending();
                        lastSweep = DateTime.UtcNow;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteProbe("[probe][监控] DNS 读取异常: " + ex.Message);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static WinDivertAddress NewAddress()
        {
            return new WinDivertAddress { Reserved3 = new byte[64] };
        }

        private void CloseHandle()
        {
            if (_handle == IntPtr.Zero || _handle == new IntPtr(-1)) return;
            try { WinDivertClose(_handle); } catch { }
            _handle = IntPtr.Zero;
        }

        private void RefreshLocalIPs()
        {
            _localIPs.Clear();
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (UnicastIPAddressInformation address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address == null) continue;
                    _localIPs.Add(address.Address.ToString());
                }
            }
        }

        private void Observe(byte[] packet, int length, bool outbound)
        {
            PacketInfo info;
            DnsInfo dns;
            if (!TryParsePacket(packet, length, out info)) return;
            if (!TryParseDNS(packet, length, info.PayloadOffset, out dns)) return;
            string name = string.IsNullOrEmpty(dns.Name) ? "-" : dns.Name;

            if (!dns.Response && !outbound && IsPrivateClient(info.Source) && IsLocalIP(info.Destination))
            {
                Interlocked.Increment(ref _phoneQueries);
                lock (StateLock)
                {
                    _pending[name] = DateTime.UtcNow;
                }
                WriteProbe("[probe][DNS监控] 手机查询 client=" + info.Source + " dst=" + info.Destination +
                           " name=" + name + "；等待电脑端转发");
                return;
            }

            if (!dns.Response && outbound && IsLocalIP(info.Source) && !IsLocalIP(info.Destination))
            {
                Interlocked.Increment(ref _upstreamQueries);
                bool matched = RemovePending(name);
                WriteProbe("[probe][DNS监控] 上行查询 src=" + info.Source + " dst=" + info.Destination +
                           " name=" + name);
                if (matched)
                {
                    WriteProbe("[probe][DNS证据] name=" + name + " 手机查询已到达电脑且已从上行网卡转发出去；判定=电脑端已收到并已转发");
                }
                return;
            }

            if (dns.Response && !outbound && !IsLocalIP(info.Source) && IsLocalIP(info.Destination))
            {
                Interlocked.Increment(ref _upstreamResponses);
                WriteProbe("[probe][DNS监控] 上游响应 src=" + info.Source + " dst=" + info.Destination +
                           " name=" + name);
                return;
            }

            if (dns.Response && outbound && IsLocalIP(info.Source) && IsPrivateClient(info.Destination))
            {
                Interlocked.Increment(ref _phoneResponses);
                RemovePending(name);
                WriteProbe("[probe][DNS监控] 返回手机 src=" + info.Source + " client=" + info.Destination +
                           " name=" + name + "；判定=DNS 已回到手机（缓存命中或上游响应）");
            }
        }

        private bool RemovePending(string name)
        {
            lock (StateLock)
            {
                return _pending.Remove(name);
            }
        }

        private void SweepPending()
        {
            var stale = new List<string>();
            lock (StateLock)
            {
                foreach (KeyValuePair<string, DateTime> pair in _pending)
                {
                    if ((DateTime.UtcNow - pair.Value).TotalSeconds > PendingWindowSeconds) stale.Add(pair.Key);
                }
                foreach (string key in stale) _pending.Remove(key);
            }
            foreach (string key in stale)
            {
                Interlocked.Increment(ref _notForwarded);
                WriteProbe("[probe][DNS证据] name=" + key + " 手机查询已到达电脑但 4 秒内未见上行查询或返回手机；判定=DNS Proxy 未运行或拦截未生效");
            }
        }

        private bool IsLocalIP(IPAddress address)
        {
            if (address == null) return false;
            string text = address.ToString();
            lock (StateLock)
            {
                return _localIPs.Contains(text);
            }
        }

        private static bool IsPrivateClient(IPAddress address)
        {
            if (address == null || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
            byte[] b = address.GetAddressBytes();
            if (b.Length != 4) return false;
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] == 169 && b[1] == 254) return true;
            return false;
        }

        private static bool TryParsePacket(byte[] packet, int length, out PacketInfo info)
        {
            info = null;
            if (packet == null || length < 20) return false;
            int version = packet[0] >> 4;
            IPAddress source;
            IPAddress destination;
            byte protocol;
            int offset;

            if (version == 4)
            {
                int headerLength = (packet[0] & 0x0f) * 4;
                if (headerLength < 20 || length < headerLength) return false;
                protocol = packet[9];
                source = new IPAddress(Copy(packet, 12, 4));
                destination = new IPAddress(Copy(packet, 16, 4));
                offset = headerLength;
            }
            else if (version == 6)
            {
                if (length < 40) return false;
                protocol = packet[6];
                source = new IPAddress(Copy(packet, 8, 16));
                destination = new IPAddress(Copy(packet, 24, 16));
                offset = 40;
                protocol = SkipIPv6Extensions(packet, length, ref offset, protocol);
            }
            else
            {
                return false;
            }
            if (protocol != 17 || offset + 8 > length) return false;

            info = new PacketInfo
            {
                Source = source,
                Destination = destination,
                SourcePort = ReadU16(packet, offset),
                DestinationPort = ReadU16(packet, offset + 2),
                PayloadOffset = offset + 8
            };
            return true;
        }

        private static byte SkipIPv6Extensions(byte[] packet, int length, ref int offset, byte next)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                if (next != 0 && next != 43 && next != 44 && next != 51 && next != 60) return next;
                if (offset + 2 > length) return next;
                int extensionLength = (packet[offset + 1] + 1) * 8;
                if (extensionLength < 8 || offset + extensionLength > length) return next;
                next = packet[offset];
                offset += extensionLength;
            }
            return next;
        }

        private static bool TryParseDNS(byte[] packet, int length, int offset, out DnsInfo info)
        {
            info = null;
            if (offset + 12 > length) return false;
            ushort flags = ReadU16(packet, offset + 2);
            ushort questions = ReadU16(packet, offset + 4);
            string name = questions > 0 ? DecodeDNSName(packet, offset + 12, offset, length) : "";
            info = new DnsInfo { Name = name, Response = (flags & 0x8000) != 0 };
            return true;
        }

        private static string DecodeDNSName(byte[] packet, int offset, int baseOffset, int length)
        {
            var labels = new List<string>();
            int position = offset;
            int jumps = 0;
            while (position < length && jumps < 32)
            {
                byte labelLength = packet[position];
                if ((labelLength & 0xc0) == 0xc0)
                {
                    if (position + 1 >= length) break;
                    int pointer = ((labelLength & 0x3f) << 8) | packet[position + 1];
                    position = baseOffset + pointer;
                    jumps++;
                    continue;
                }
                position++;
                if (labelLength == 0) break;
                if (position + labelLength > length) break;
                labels.Add(Encoding.ASCII.GetString(packet, position, labelLength));
                position += labelLength;
            }
            return string.Join(".", labels.ToArray()).ToLowerInvariant();
        }

        private static byte[] Copy(byte[] source, int offset, int count)
        {
            var result = new byte[count];
            Buffer.BlockCopy(source, offset, result, 0, count);
            return result;
        }

        private static ushort ReadU16(byte[] data, int offset)
        {
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        private static void WriteProbe(string message)
        {
            try
            {
                string path = ProxyProcessManager.ProbeLogPath;
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
                string line = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + " " + message;
                lock (LogLock)
                {
                    using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                        writer.WriteLine(line);
                }
            }
            catch { }
        }
    }
}
