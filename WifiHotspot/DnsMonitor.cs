using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;

namespace WifiHotspot
{
    public class DnsMonitor
    {
        private const int PendingWindowSeconds = 4;
        private static readonly DnsMonitor SharedInstance = new DnsMonitor();
        private static readonly object LogLock = new object();
        private static readonly object StateLock = new object();

        private volatile bool _running;
        private string _lastMessage = "未运行";
        private DateTime _lastSweep = DateTime.UtcNow;
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
            if (settings == null || !settings.MonitorEnabled || !settings.MonitorDns)
            {
                _lastMessage = "DNS 端口监控未启用";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }
            StartObserver(settings);
            return new HotspotResult { Success = true, Message = _lastMessage };
        }

        public HotspotResult Stop()
        {
            StopObserver();
            return new HotspotResult { Success = true, Message = _lastMessage };
        }

        internal void StartObserver(ProxySettings settings)
        {
            if (_running) return;
            if (settings == null || !settings.MonitorEnabled || !settings.MonitorDns) return;
            RefreshLocalIPs();
            lock (StateLock)
            {
                _pending.Clear();
            }
            _lastSweep = DateTime.UtcNow;
            _running = true;
            _lastMessage = "DNS 端口监控已启动（复用统一 WinDivert 只读 sniff）";
            WriteProbe("[probe][监控] " + _lastMessage);
        }

        internal void StopObserver()
        {
            if (!_running) return;
            _running = false;
            lock (StateLock)
            {
                _pending.Clear();
            }
            _lastMessage = "DNS 端口监控已停止";
            WriteProbe("[probe][监控] " + _lastMessage);
        }

        internal void ObserveExternal(byte[] packet, int length, bool outbound)
        {
            if (!_running) return;
            Observe(packet, length, outbound);
        }

        internal void Tick()
        {
            if (!_running) return;
            DateTime now = DateTime.UtcNow;
            if (now - _lastSweep <= TimeSpan.FromSeconds(1)) return;
            _lastSweep = now;
            SweepPending();
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
            if (info.SourcePort != 53 && info.DestinationPort != 53) return;
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
