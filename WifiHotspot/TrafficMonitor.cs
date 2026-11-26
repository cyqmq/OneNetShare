using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WifiHotspot
{
    public class TrafficMonitor
    {
        private const int BufferSize = 65575;
        private const int LayerNetwork = 0;
        private const ulong FlagSniff = 0x0001;
        private const uint FlagOutbound = 1u << 17;
        private const uint FlagLoopback = 1u << 18;
        private const uint FlagImpostor = 1u << 19;
        private const int FlowWindowSeconds = 15;
        private static readonly TrafficMonitor SharedInstance = new TrafficMonitor();
        private static readonly object LogLock = new object();
        private static readonly object StateLock = new object();

        private IntPtr _handle = IntPtr.Zero;
        private Thread _thread;
        private volatile bool _running;
        private volatile bool _ttlEnabled;
        private volatile bool _dnsEnabled;
        private string _lastMessage = "未运行";
        private readonly HashSet<string> _localIPs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FlowRecord> _inbound = new Dictionary<string, FlowRecord>();
        private readonly Dictionary<string, DateTime> _lastLogged = new Dictionary<string, DateTime>();
        private long _inboundCount;
        private long _outboundCount;
        private long _mismatchCount;
        private long _matchedCount;

        public static TrafficMonitor Shared
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

        private class FlowInfo
        {
            public byte TTL;
            public byte Protocol;
            public IPAddress Source;
            public IPAddress Destination;
            public ushort SourcePort;
            public ushort DestinationPort;
            public string ProtocolName;
        }

        private class FlowRecord
        {
            public FlowInfo Info;
            public DateTime At;
            public uint InterfaceIndex;
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
                       "；TTL=" + (_ttlEnabled ? "开" : "关") +
                       " DNS=" + (_dnsEnabled ? "开" : "关") +
                       " 入站=" + Interlocked.Read(ref _inboundCount) +
                       " 出站=" + Interlocked.Read(ref _outboundCount) +
                       " 差异=" + Interlocked.Read(ref _mismatchCount) +
                       " 保持=" + Interlocked.Read(ref _matchedCount);
            }
        }

        private static IntPtr OpenSniff(string filter, out int error)
        {
            error = 0;
            IntPtr handle = WinDivertOpen(filter, LayerNetwork, 0, FlagSniff);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                error = Marshal.GetLastWin32Error();
            return handle;
        }

        private static string RunSc(string arguments, out int exitCode)
        {
            exitCode = -1;
            try
            {
                var start = new ProcessStartInfo("sc.exe", arguments)
                {
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                using (var process = Process.Start(start))
                {
                    string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                    return output.Trim();
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static string ParseServicePath(string query)
        {
            foreach (string line in (query ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int marker = line.IndexOf("BINARY_PATH_NAME", StringComparison.OrdinalIgnoreCase);
                if (marker < 0) continue;
                int colon = line.IndexOf(':', marker + "BINARY_PATH_NAME".Length);
                if (colon < 0) continue;
                return line.Substring(colon + 1).Trim();
            }
            return string.Empty;
        }

        private static bool TryRepairWinDivertService(out string detail)
        {
            int queryCode;
            string query = RunSc("qc WinDivert", out queryCode);
            if (queryCode != 0)
            {
                detail = "WinDivert 服务不存在或无法查询（sc qc 退出码 " + queryCode + "）";
                return false;
            }
            string path = ParseServicePath(query);
            string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            bool stale = string.IsNullOrEmpty(path) || !File.Exists(path) ||
                         !path.StartsWith(appDir, StringComparison.OrdinalIgnoreCase);
            if (!stale)
            {
                detail = "WinDivert 服务存在且驱动路径有效（" + path + "），仍需重启以释放残留驱动状态";
                return false;
            }
            int stopCode;
            RunSc("stop WinDivert", out stopCode);
            int deleteCode;
            string delete = RunSc("delete WinDivert", out deleteCode);
            if (deleteCode != 0 && !delete.Contains("1060") && !delete.Contains("not exist"))
            {
                detail = "删除残留 WinDivert 服务失败：" + delete;
                return false;
            }
            detail = "已自动删除残留 WinDivert 服务（原驱动路径: " +
                     (string.IsNullOrEmpty(path) ? "<空>" : path) + "），将由 WinDivertOpen 重新注册";
            return true;
        }

        public static string ErrorHint(int error)
        {
            if (error == 3)
                return "错误码 3 (ERROR_PATH_NOT_FOUND)：通常是残留 WinDivert 服务的 BINARY_PATH_NAME 指向旧目录/旧驱动，不是当前程序目录缺文件。程序已尝试自动删除该服务；若仍失败，请以管理员运行 `sc stop WinDivert`、`sc delete WinDivert` 后重启电脑。`sc qc WinDivert` 可查看残留路径。";
            if (error == 2)
                return "错误码 2 (ERROR_FILE_NOT_FOUND)：缺少 WinDivert32.sys / WinDivert64.sys，请重新解压完整发行包并确认三个 WinDivert 文件在同一目录。";
            if (error == 5)
                return "错误码 5 (ERROR_ACCESS_DENIED)：请以管理员身份运行 WifiHotspot.exe。";
            if (error == 87)
                return "错误码 87 (ERROR_INVALID_PARAMETER)：WinDivert 过滤器或参数无效，请升级到最新发行包。";
            if (error == 577)
                return "错误码 577 (ERROR_INVALID_IMAGE_HASH)：WinDivert.sys 签名无效或被替换，请重新解压官方 WinDivert 2.2.2 文件。";
            if (error == 654)
                return "错误码 654 (ERROR_DRIVER_FAILED_PRIOR_UNLOAD)：已加载不兼容版本驱动，请以管理员运行 `sc stop WinDivert`、`sc delete WinDivert` 后重启电脑。";
            if (error == 1275)
                return "错误码 1275 (ERROR_DRIVER_BLOCKED)：驱动被安全软件或虚拟化环境阻止，请检查 Defender/第三方安全软件与虚拟机驱动策略。";
            if (error == 1450)
                return "错误码 1450 (ERROR_NO_SYSTEM_RESOURCES)：系统内核资源不足或驱动状态异常；请关闭其他 WinDivert/Clash/TUN 工具，重启电脑后重试。";
            return "请确认 WinDivert 2.2.2 文件完整且以管理员运行；必要时执行 `sc stop WinDivert`、`sc delete WinDivert` 后重启。";
        }

        public HotspotResult Start(ProxySettings settings)
        {
            if (_running)
            {
                _lastMessage = "核心监控已在运行";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }
            bool ttl = settings != null && settings.MonitorEnabled && settings.MonitorTtl && !settings.TunEnabled;
            bool dns = settings != null && settings.MonitorEnabled && settings.MonitorDns;
            if (!ttl && !dns)
            {
                _lastMessage = settings != null && settings.MonitorEnabled && settings.MonitorTtl && settings.TunEnabled
                    ? "全局 TUN 接管已启用且 DNS 监控未启用，核心监控跳过"
                    : "核心监控未启用";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }
            if (!HotspotManager.IsAdministrator())
            {
                _lastMessage = "核心监控需要管理员权限";
                return new HotspotResult { Success = false, Message = _lastMessage };
            }
            if (!NetworkNormalizer.DriverAvailable())
            {
                _lastMessage = "核心监控缺少 WinDivert 文件";
                return new HotspotResult { Success = false, Message = _lastMessage };
            }

            string dnsFilter = "udp.SrcPort == 53 or udp.DstPort == 53";
            string ttlFilter = "ip or ipv6";
            string filter = ttl ? ttlFilter : dnsFilter;
            int error = 0;
            bool repaired = false;
            string repairDetail = string.Empty;
            try
            {
                _handle = OpenSniff(filter, out error);
                if ((_handle == IntPtr.Zero || _handle == new IntPtr(-1)) && ttl && dns)
                {
                    string first = "WinDivert sniff 打开失败，错误码 " + error + " filter=" + filter;
                    WriteProbe("[probe][监控] " + first + "，尝试降级为 DNS-only 监控");
                    filter = dnsFilter;
                    _handle = OpenSniff(filter, out error);
                    if (_handle != IntPtr.Zero && _handle != new IntPtr(-1)) ttl = false;
                }
                if ((_handle == IntPtr.Zero || _handle == new IntPtr(-1)) && error == 3)
                {
                    string detail;
                    if (TryRepairWinDivertService(out detail))
                    {
                        repaired = true;
                        repairDetail = detail;
                        WriteProbe("[probe][监控] " + detail + "，重试 WinDivertOpen");
                        filter = ttl ? ttlFilter : dnsFilter;
                        _handle = OpenSniff(filter, out error);
                        if ((_handle == IntPtr.Zero || _handle == new IntPtr(-1)) && ttl && dns)
                        {
                            filter = dnsFilter;
                            _handle = OpenSniff(filter, out error);
                            if (_handle != IntPtr.Zero && _handle != new IntPtr(-1)) ttl = false;
                        }
                    }
                    else
                    {
                        repairDetail = detail;
                        WriteProbe("[probe][监控] 自动修复 WinDivert 服务失败：" + detail);
                    }
                }
                if (_handle == IntPtr.Zero || _handle == new IntPtr(-1))
                {
                    _lastMessage = "WinDivert sniff 打开失败，错误码 " + error + " filter=" + filter +
                                   (string.IsNullOrEmpty(repairDetail) ? string.Empty : "\n自动修复: " + repairDetail) +
                                   "\n" + ErrorHint(error);
                    WriteProbe("[probe][监控] " + _lastMessage);
                    return new HotspotResult { Success = false, Message = _lastMessage };
                }
            }
            catch (Exception ex)
            {
                _lastMessage = "核心监控初始化失败: " + ex.Message;
                WriteProbe("[probe][监控] " + _lastMessage);
                return new HotspotResult { Success = false, Message = _lastMessage };
            }

            _ttlEnabled = ttl;
            _dnsEnabled = dns;
            if (dns) DnsMonitor.Shared.StartObserver(settings);
            if (ttl)
            {
                RefreshLocalIPs();
                lock (StateLock)
                {
                    _inbound.Clear();
                    _lastLogged.Clear();
                }
            }
            _running = true;
            _thread = new Thread(new ThreadStart(delegate { Loop(_handle); }));
            _thread.IsBackground = true;
            _thread.Start();
            _lastMessage = "核心监控已启动（WinDivert 只读 sniff；TTL=" + (ttl ? "开" : "关") +
                           " DNS=" + (dns ? "开" : "关") + "）" +
                           (!ttl && dns ? "；TTL 因 filter 打开失败已降级关闭" : string.Empty) +
                           (repaired ? "；" + repairDetail : string.Empty);
            WriteProbe("[probe][监控] " + _lastMessage);
            return new HotspotResult { Success = true, Message = _lastMessage };
        }

        public HotspotResult Stop()
        {
            if (!_running && !DnsMonitor.Shared.IsRunning)
            {
                _lastMessage = "核心监控未运行";
                return new HotspotResult { Success = true, Message = _lastMessage };
            }
            _running = false;
            _ttlEnabled = false;
            _dnsEnabled = false;
            DnsMonitor.Shared.StopObserver();
            if (_thread != null && _thread.IsAlive) _thread.Join(2000);
            CloseHandle();
            _lastMessage = "核心监控已停止";
            WriteProbe("[probe][监控] " + _lastMessage);
            return new HotspotResult { Success = true, Message = _lastMessage };
        }

        private void Loop(IntPtr handle)
        {
            IntPtr buffer = Marshal.AllocHGlobal(BufferSize);
            byte[] managed = new byte[BufferSize];
            DateTime lastTick = DateTime.UtcNow;
            try
            {
                while (_running && handle != IntPtr.Zero && handle != new IntPtr(-1))
                {
                    WinDivertAddress address = NewAddress();
                    uint received;
                    if (!WinDivertRecv(handle, buffer, BufferSize, out received, ref address))
                    {
                        if (!_running) break;
                        DnsMonitor.Shared.Tick();
                        Thread.Sleep(5);
                        continue;
                    }
                    if (received == 0 || received > BufferSize) continue;
                    if ((address.Flags & (FlagLoopback | FlagImpostor)) != 0) continue;
                    Marshal.Copy(buffer, managed, 0, (int)received);
                    Observe(managed, (int)received, address);
                    DateTime now = DateTime.UtcNow;
                    if (now - lastTick > TimeSpan.FromSeconds(1))
                    {
                        DnsMonitor.Shared.Tick();
                        lastTick = now;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteProbe("[probe][监控] 核心监控读取异常: " + ex.Message);
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

        private void Observe(byte[] packet, int length, WinDivertAddress address)
        {
            bool outbound = (address.Flags & FlagOutbound) != 0;
            if (_dnsEnabled) DnsMonitor.Shared.ObserveExternal(packet, length, outbound);
            if (!_ttlEnabled) return;

            FlowInfo info;
            if (!TryParsePacket(packet, length, out info)) return;
            uint interfaceIndex = ReadU32(address.Reserved3, 0);

            if (outbound)
            {
                Interlocked.Increment(ref _outboundCount);
                if (!IsLocalIP(info.Source) || IsLocalIP(info.Destination)) return;
                CompareOutbound(info, interfaceIndex);
                return;
            }

            Interlocked.Increment(ref _inboundCount);
            if (IsLocalIP(info.Source) || !IsPrivateClient(info.Source)) return;
            StoreInbound(info, interfaceIndex);
        }

        private void StoreInbound(FlowInfo info, uint interfaceIndex)
        {
            string exactKey = FlowKey(info, true);
            string fallbackKey = FlowKey(info, false);
            var record = new FlowRecord { Info = info, At = DateTime.UtcNow, InterfaceIndex = interfaceIndex };
            lock (StateLock)
            {
                CleanupLocked(DateTime.UtcNow);
                _inbound[exactKey] = record;
                if (!string.Equals(exactKey, fallbackKey) && !_inbound.ContainsKey(fallbackKey)) _inbound[fallbackKey] = record;
                if (ShouldLogLocked("in:" + exactKey, 30))
                {
                    WriteProbe("[probe][TTL监控] 入站 src=" + info.Source + " ttl=" + info.TTL +
                               " dst=" + info.Destination + ":" + info.DestinationPort +
                               " proto=" + info.ProtocolName + " ifIdx=" + interfaceIndex);
                }
            }
        }

        private void CompareOutbound(FlowInfo info, uint interfaceIndex)
        {
            string exactKey = FlowKey(info, true);
            string fallbackKey = FlowKey(info, false);
            string matchedKey = null;
            FlowRecord record = null;
            lock (StateLock)
            {
                DateTime now = DateTime.UtcNow;
                CleanupLocked(now);
                if (_inbound.TryGetValue(exactKey, out record))
                {
                    matchedKey = exactKey;
                }
                else if (_inbound.TryGetValue(fallbackKey, out record))
                {
                    matchedKey = fallbackKey;
                }
                if (record == null || (now - record.At).TotalSeconds > FlowWindowSeconds) return;
                _inbound.Remove(exactKey);
                _inbound.Remove(fallbackKey);
                if (!ShouldLogLocked("out:" + exactKey, 30)) return;
            }

            int diff = record.Info.TTL - info.TTL;
            string target = info.Destination + ":" + info.DestinationPort;
            string association = string.Equals(matchedKey, exactKey) ? "端口一致" : "目标端点";
            if (diff == 1)
            {
                Interlocked.Increment(ref _mismatchCount);
                WriteProbe("[probe][TTL证据] 入站 " + record.Info.Source + " ttl=" + record.Info.TTL +
                           " -> 出站 " + info.Source + " ttl=" + info.TTL + " dst=" + target +
                           " proto=" + info.ProtocolName + " 关联=" + association + " ifIdx=" +
                           record.InterfaceIndex + "->" + interfaceIndex +
                           "；判定=Windows 原生路由递减 1 且未被纠正（疑似）");
                return;
            }
            if (diff != 0)
            {
                Interlocked.Increment(ref _mismatchCount);
                WriteProbe("[probe][TTL监控] TTL不一致 入站=" + record.Info.TTL + " 出站=" + info.TTL +
                           " dst=" + target + " proto=" + info.ProtocolName + " 关联=" + association);
                return;
            }
            Interlocked.Increment(ref _matchedCount);
            WriteProbe("[probe][TTL监控] TTL保持一致=" + info.TTL + " dst=" + target +
                       " proto=" + info.ProtocolName + " 关联=" + association);
        }

        private void CleanupLocked(DateTime now)
        {
            var stale = new List<string>();
            foreach (KeyValuePair<string, FlowRecord> pair in _inbound)
            {
                if ((now - pair.Value.At).TotalSeconds > 60) stale.Add(pair.Key);
            }
            foreach (string key in stale) _inbound.Remove(key);

            var staleLogs = new List<string>();
            foreach (KeyValuePair<string, DateTime> pair in _lastLogged)
            {
                if ((now - pair.Value).TotalSeconds > 120) staleLogs.Add(pair.Key);
            }
            foreach (string key in staleLogs) _lastLogged.Remove(key);
        }

        private bool ShouldLogLocked(string key, int seconds)
        {
            DateTime last;
            if (_lastLogged.TryGetValue(key, out last) && (DateTime.UtcNow - last).TotalSeconds < seconds) return false;
            _lastLogged[key] = DateTime.UtcNow;
            return true;
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
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;
            return false;
        }

        private static string FlowKey(FlowInfo info, bool includeSourcePort)
        {
            return info.Protocol + "|" + info.Destination + "|" + info.DestinationPort +
                   (includeSourcePort ? "|" + info.SourcePort : "");
        }

        private static bool TryParsePacket(byte[] packet, int length, out FlowInfo info)
        {
            info = null;
            if (packet == null || length < 20) return false;
            int version = packet[0] >> 4;
            byte ttl;
            byte protocol;
            IPAddress source;
            IPAddress destination;
            int offset;

            if (version == 4)
            {
                int headerLength = (packet[0] & 0x0f) * 4;
                if (headerLength < 20 || length < headerLength) return false;
                ttl = packet[8];
                protocol = packet[9];
                source = new IPAddress(Copy(packet, 12, 4));
                destination = new IPAddress(Copy(packet, 16, 4));
                offset = headerLength;
            }
            else if (version == 6)
            {
                if (length < 40) return false;
                ttl = packet[7];
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

            ushort sourcePort = 0;
            ushort destinationPort = 0;
            string name;
            if (protocol == 6)
            {
                if (offset + 4 > length) return false;
                sourcePort = ReadU16(packet, offset);
                destinationPort = ReadU16(packet, offset + 2);
                name = "TCP";
            }
            else if (protocol == 17)
            {
                if (offset + 4 > length) return false;
                sourcePort = ReadU16(packet, offset);
                destinationPort = ReadU16(packet, offset + 2);
                name = "UDP";
            }
            else if (protocol == 1 || protocol == 58)
            {
                if (offset + 6 <= length) sourcePort = ReadU16(packet, offset + 4);
                name = protocol == 1 ? "ICMP" : "ICMPv6";
            }
            else
            {
                return false;
            }

            info = new FlowInfo
            {
                TTL = ttl,
                Protocol = protocol,
                Source = source,
                Destination = destination,
                SourcePort = sourcePort,
                DestinationPort = destinationPort,
                ProtocolName = name
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

        private static uint ReadU32(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
                   ((uint)data[offset + 2] << 8) | data[offset + 3];
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
