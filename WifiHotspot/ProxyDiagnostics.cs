using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace WifiHotspot
{
    public static class ProxyDiagnostics
    {
        public static string Analyze(string configPath, string processOutput, int? exitCode)
        {
            var lines = new List<string>();
            lines.Add("代理自动诊断");
            lines.Add(exitCode.HasValue ? "进程退出码: " + exitCode.Value : "进程状态: 未捕获退出码");

            AnalyzeConfig(configPath, lines);
            AnalyzeIPv6(configPath, lines);
            AnalyzePortal(configPath, lines);
            AnalyzePorts(configPath, lines);
            AnalyzeLog(processOutput, lines);
            return string.Join("\n", lines.ToArray());
        }

        private static void AnalyzeConfig(string configPath, List<string> lines)
        {
            string path = string.IsNullOrEmpty(configPath) ? ProxyConfigManager.DefaultConfigPath : configPath;
            if (!File.Exists(path))
            {
                lines.Add("[错误] 配置文件不存在: " + path);
                lines.Add("[建议] 点击“仅保存”生成配置，再点“保存并重启代理”。");
                return;
            }

            string text = File.ReadAllText(path, Encoding.UTF8);
            object parsed;
            try
            {
                parsed = new JavaScriptSerializer().DeserializeObject(text);
            }
            catch (Exception ex)
            {
                lines.Add("[错误] config.json 不是有效 JSON: " + ex.Message);
                lines.Add("[建议] 点击“仅保存”重建默认配置，或手动修复 JSON 后重试。");
                return;
            }

            IDictionary<string, object> root = parsed as IDictionary<string, object>;
            if (root == null)
            {
                lines.Add("[错误] config.json 顶层不是对象。");
                lines.Add("[建议] 点击“仅保存”重建默认配置。");
                return;
            }

            lines.Add("[正常] config.json 可解析: " + path);
            int httpPort = ParsePort(GetString(root, "listen_http", "0.0.0.0:18080"), "0.0.0.0:18080", "HTTP 监听地址", lines);
            int httpsPort = ParsePort(GetString(root, "listen_https", "0.0.0.0:18443"), "0.0.0.0:18443", "HTTPS 监听地址", lines);
            IDictionary<string, object> deviceforge = GetDict(root, "deviceforge");
            IDictionary<string, object> udp = GetDict(deviceforge, "udp");
            int udpPort = ParsePort(GetString(udp, "listen", "0.0.0.0:18500"), "0.0.0.0:18500", "UDP 监听地址", lines);
            string mode = GetString(root, "mode", "forward");

            lines.Add("配置摘要: mode=" + mode +
                      ", default_profile=" + GetString(root, "default_profile", "chrome-win11") +
                      ", enabled=" + GetString(root, "enabled", "true") +
                      ", HTTP=" + httpPort + ", HTTPS=" + httpsPort + ", 状态端口=18089, UDP=" + udpPort);

            if (httpPort <= 0 || httpsPort <= 0) return;

            if (httpPort == httpsPort)
                lines.Add("[错误] HTTP 与 HTTPS 监听端口相同: " + httpPort);
            if (httpPort == 18089 || httpsPort == 18089)
                lines.Add("[错误] HTTP/HTTPS 监听端口与内置状态端口 18089 冲突。");

            IDictionary<string, object> network = GetDict(root, "network");
            IDictionary<string, object> tun = GetDict(root, "tun");
            bool networkEnabled = GetBool(network, "enabled", false);
            bool tunEnabled = GetBool(tun, "enabled", false);

            if (networkEnabled && tunEnabled)
                lines.Add("[错误] 网络层归一化与 Wintun 全局 TUN 同时开启，两者互斥；请只保留一个。");

            if (tunEnabled)
            {
                string upstream = GetString(tun, "upstream_interface", string.Empty);
                if (string.IsNullOrWhiteSpace(upstream))
                    lines.Add("[错误] 已启用 Wintun 全局 TUN，但没有选择物理出口网卡；请在“代理功能”窗口选择出口网卡。");
                if (!HotspotManager.WintunAvailable())
                    lines.Add("[错误] 已启用 Wintun 全局 TUN，但程序目录没有 wintun.dll。");
                if (!HotspotManager.IsAdministrator())
                    lines.Add("[错误] 已启用 Wintun 全局 TUN，但当前进程不是管理员权限。");
                if (!GetBool(deviceforge, "enabled", true) || !GetBool(udp, "enabled", false))
                    lines.Add("[警告] 已启用 Wintun 全局 TUN，但 DeviceForge 或 UDP 会话重建未开启，DNS/NTP 不会重建。");
            }

            if (networkEnabled)
            {
                if (!NetworkNormalizer.DriverAvailable())
                    lines.Add("[错误] 已启用网络层归一化，但 WinDivert.dll / WinDivert32.sys / WinDivert64.sys 不完整。");
                if (!HotspotManager.IsAdministrator())
                    lines.Add("[错误] 已启用网络层归一化，但当前进程不是管理员权限。");
            }
        }

        private static void AnalyzeIPv6(string configPath, List<string> lines)
        {
            string path = string.IsNullOrEmpty(configPath) ? ProxyConfigManager.DefaultConfigPath : configPath;
            string policy = "block";
            if (File.Exists(path))
            {
                try
                {
                    object parsed = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path, Encoding.UTF8));
                    IDictionary<string, object> root = parsed as IDictionary<string, object>;
                    if (root != null) policy = GetString(root, "ipv6_policy", "block");
                }
                catch { }
            }

            lines.Add("IPv6 策略: " + policy + "；" + CheckIPv6Safety());
            List<string> global = FindHotspotGlobalIPv6();
            if (global.Count == 0)
                lines.Add("[正常] 未检测到热点网卡公网/全局 IPv6 地址；仅 ULA(fdxx)/Link-local 时无需 IPv6 封禁。");
            else if (!IPv6Guard.IsRunning)
                lines.Add("[警告] 检测到热点网卡公网/全局 IPv6 地址，热点启动时将自动启用封禁；单改 Flow Label 不足以防止 IPv6 裸奔。");
            if (global.Count > 0 && IPv6Guard.IsEnabled(policy) && !IPv6Guard.IsRunning)
                lines.Add("[警告] 配置要求封禁 IPv6 转发，但 WinDivert 封禁当前未运行；热点启动后才会生效。");
        }

        private static void AnalyzePortal(string configPath, List<string> lines)
        {
            string path = string.IsNullOrEmpty(configPath) ? ProxyConfigManager.DefaultConfigPath : configPath;
            bool enabled = false;
            string primary = "";
            string secondary = "";
            if (File.Exists(path))
            {
                try
                {
                    object parsed = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path, Encoding.UTF8));
                    IDictionary<string, object> root = parsed as IDictionary<string, object>;
                    if (root != null)
                    {
                        enabled = GetBool(root, "portal_block_enabled", false);
                        primary = GetString(root, "portal_primary_ip", "");
                        secondary = GetString(root, "portal_secondary_ip", "");
                    }
                }
                catch { }
            }

            List<string> ips = PortalGuard.ParseIPs(primary, secondary);
            lines.Add("Portal 封堵: " + (enabled ? "已启用" : "未启用") +
                      (ips.Count > 0 ? "；IP=" + string.Join(",", ips.ToArray()) : "；未配置 IP") +
                      "；" + (PortalGuard.IsRunning ? "运行中" : "未运行"));
            if (enabled && ips.Count == 0)
                lines.Add("[警告] 已启用 Portal 封堵，但主/备 IP 为空；请填写认证网关主 IP 和第二个 IP。");
            if (enabled && ips.Count > 0 && !PortalGuard.IsRunning)
                lines.Add("[警告] Portal 封堵已配置但未运行；启动热点后会在 WinDivert FORWARD 层丢弃到这些 IP 的转发包。");
        }

        public static string CheckIPv6Safety()
        {
            List<string> globalAddresses = FindHotspotGlobalIPv6();
            if (globalAddresses.Count == 0)
                return "IPv6 安全: 未在热点虚拟网卡检测到公网 IPv6（仅 ULA/Link-local 或无 IPv6），无需封禁";

            if (IPv6Guard.IsRunning)
                return "IPv6 安全: 检测到公网 IPv6 " + string.Join(", ", globalAddresses.ToArray()) +
                       "，转发封禁正在运行";

            return "IPv6 裸露风险: 热点虚拟网卡检测到公网 IPv6 " + string.Join(", ", globalAddresses.ToArray()) +
                   "；热点启动将自动封禁全部转发 IPv6，单改 Flow Label 不足够";
        }

        public static List<string> FindHotspotGlobalIPv6()
        {
            var result = new List<string>();
            string hotspotName = string.Empty;
            try
            {
                var adapterInfo = new HotspotManager().GetHostedNetworkAdapter();
                if (adapterInfo != null) hotspotName = adapterInfo.Name ?? string.Empty;
            }
            catch { }

            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                string name = adapter.Name ?? string.Empty;
                string description = adapter.Description ?? string.Empty;
                bool hotspotAdapter = description.IndexOf("Hosted Network", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      description.IndexOf("承载网络", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      name.StartsWith("Local Area Connection*", StringComparison.OrdinalIgnoreCase) ||
                                      name.StartsWith("本地连接*", StringComparison.OrdinalIgnoreCase) ||
                                      (!string.IsNullOrEmpty(hotspotName) && string.Equals(name, hotspotName, StringComparison.OrdinalIgnoreCase));
                if (!hotspotAdapter || adapter.OperationalStatus != OperationalStatus.Up) continue;

                foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetworkV6) continue;
                    if (address.Address.IsIPv6LinkLocal || address.Address.IsIPv6SiteLocal) continue;
                    byte[] bytes = address.Address.GetAddressBytes();
                    bool uniqueLocal = (bytes[0] & 0xfe) == 0xfc;
                    if (uniqueLocal) continue;
                    if (!result.Contains(address.Address.ToString())) result.Add(address.Address.ToString());
                }
            }
            return result;
        }

        private static void AnalyzePorts(string configPath, List<string> lines)
        {
            string path = string.IsNullOrEmpty(configPath) ? ProxyConfigManager.DefaultConfigPath : configPath;
            int httpPort = 18080;
            int httpsPort = 18443;
            int udpPort = 18500;
            try
            {
                IDictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as IDictionary<string, object>;
                if (root != null)
                {
                    httpPort = ParsePortQuiet(GetString(root, "listen_http", "0.0.0.0:18080"), 18080);
                    httpsPort = ParsePortQuiet(GetString(root, "listen_https", "0.0.0.0:18443"), 18443);
                    IDictionary<string, object> deviceforge = GetDict(root, "deviceforge");
                    IDictionary<string, object> udp = GetDict(deviceforge, "udp");
                    udpPort = ParsePortQuiet(GetString(udp, "listen", "0.0.0.0:18500"), 18500);
                }
            }
            catch { }

            CheckPort(httpPort, "TCP", true, "HTTP 代理", lines);
            CheckPort(httpsPort, "TCP", true, "HTTPS 代理", lines);
            CheckPort(18089, "TCP", false, "GUI 状态/停止接口", lines);
            CheckPort(udpPort, "UDP", false, "DeviceForge UDP", lines);
        }

        private static void CheckPort(int port, string protocol, bool fatal, string label, List<string> lines)
        {
            if (port <= 0 || port > 65535)
            {
                lines.Add("[错误] " + label + " 端口无效: " + port);
                return;
            }

            string owner = GetPortOwner(port, protocol);
            string excluded = FindExcludedRange(port, protocol);
            string prefix = fatal ? "[错误] " : "[警告] ";
            if (!string.IsNullOrEmpty(excluded))
                lines.Add(prefix + label + " 端口 " + protocol + " " + port + " 落在 Windows 保留端口范围 " + excluded + "，代理无法绑定；请改用 18081/18444 等端口。");
            if (!string.IsNullOrEmpty(owner))
                lines.Add(prefix + label + " 端口 " + protocol + " " + port + " 已被 " + owner + " 占用。");
            if (string.IsNullOrEmpty(owner) && string.IsNullOrEmpty(excluded))
                lines.Add("[正常] " + label + " 端口 " + protocol + " " + port + " 未发现占用。");
        }

        private static string GetPortOwner(int port, string protocol)
        {
            string output = RunCommand("netstat", "-ano -p " + protocol);
            string suffix = ":" + port.ToString();
            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) continue;
                if (!parts[1].EndsWith(suffix, StringComparison.Ordinal)) continue;
                int pid;
                if (!int.TryParse(parts[parts.Length - 1], out pid)) continue;
                return GetProcessName(pid) + " (PID " + pid + ")";
            }
            return string.Empty;
        }

        private static string FindExcludedRange(int port, string protocol)
        {
            string output = RunCommand("netsh", "interface ipv4 show excludedportrange protocol=" + protocol.ToLowerInvariant());
            var regex = new Regex(@"^\s*(\d+)\s+(\d+)(?:\s+\d+)?\s*$");
            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match match = regex.Match(line);
                if (!match.Success) continue;
                int start;
                int end;
                if (!int.TryParse(match.Groups[1].Value, out start)) continue;
                if (!int.TryParse(match.Groups[2].Value, out end)) continue;
                if (port >= start && port <= end) return start + "-" + end;
            }
            return string.Empty;
        }

        private static void AnalyzeLog(string processOutput, List<string> lines)
        {
            var evidence = new List<string>();
            if (!string.IsNullOrWhiteSpace(processOutput))
            {
                foreach (string line in processOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    evidence.Add("进程输出: " + line);
            }

            string logPath = ProxyProcessManager.ProbeLogPath;
            if (File.Exists(logPath))
            {
                string[] logLines;
                using (var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    var collected = new List<string>();
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        collected.Add(line);
                        if (collected.Count > 400) collected.RemoveAt(0);
                    }
                    logLines = collected.ToArray();
                }
                foreach (string line in logLines)
                {
                    if (IsInterestingLog(line))
                        evidence.Add("probe.log: " + line);
                }
            }

            if (evidence.Count == 0)
            {
                lines.Add("[提示] probe.log 和进程输出没有发现错误行。");
                return;
            }

            int showStart = Math.Max(0, evidence.Count - 20);
            for (int i = showStart; i < evidence.Count; i++)
                lines.Add(evidence[i]);

            string joined = string.Join("\n", evidence.ToArray());
            if (joined.IndexOf("address already in use", StringComparison.OrdinalIgnoreCase) >= 0 ||
                joined.IndexOf("bind:", StringComparison.OrdinalIgnoreCase) >= 0)
                lines.Add("[建议] 日志显示端口绑定失败；结束占用进程，或把 config.json 的 listen_http/listen_https 改成未占用端口后保存并重启。");
            if (joined.IndexOf("permission denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                joined.IndexOf("operation not permitted", StringComparison.OrdinalIgnoreCase) >= 0)
                lines.Add("[建议] 日志显示权限不足；请右键以管理员身份运行 WifiHotspot.exe，或关闭 Wintun/网络层归一化。");
            if (joined.IndexOf("wintun", StringComparison.OrdinalIgnoreCase) >= 0)
                lines.Add("[建议] 日志包含 Wintun 相关错误；确认 wintun.dll 在程序目录、已选择物理出口网卡并以管理员运行。");
            if (joined.IndexOf("无效源", StringComparison.Ordinal) >= 0 || joined.IndexOf("invalid source", StringComparison.OrdinalIgnoreCase) >= 0)
                lines.Add("[建议] netsh 报告“无效源”参数，通常是带空格的 Wintun 适配器名未被正确引用；请升级到已修复该参数转义的最新版。");
            if (joined.IndexOf("config", StringComparison.OrdinalIgnoreCase) >= 0 ||
                joined.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
                lines.Add("[建议] 日志包含配置相关错误；点击“仅保存”重建 config.json，再点“保存并重启代理”。");
        }

        private static bool IsInterestingLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            string[] keys = { "失败", "错误", "panic", "error", "listen", "bind", "permission", "denied", "invalid", "config", "json", "wintun", "windivert", "TUN", "address already in use", "操作不允许", "没有找到" };
            foreach (string key in keys)
                if (line.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static string RunCommand(string file, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo(file, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (Process process = Process.Start(psi))
                {
                    if (process == null) return string.Empty;
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);
                    return output;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetProcessName(int pid)
        {
            try
            {
                using (Process process = Process.GetProcessById(pid)) return process.ProcessName;
            }
            catch
            {
                return "未知进程";
            }
        }

        private static int ParsePort(string endpoint, string fallback, string label, List<string> lines)
        {
            int port = ParsePortQuiet(endpoint, 0);
            if (port <= 0)
                lines.Add("[错误] " + label + " 格式无效: " + (string.IsNullOrEmpty(endpoint) ? fallback : endpoint) + "；应为 host:port，端口 1-65535。");
            return port;
        }

        private static int ParsePortQuiet(string endpoint, int fallback)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) return fallback;
            int colon = endpoint.LastIndexOf(':');
            if (colon < 0 || colon >= endpoint.Length - 1) return 0;
            int port;
            if (!int.TryParse(endpoint.Substring(colon + 1), out port)) return 0;
            if (port < 1 || port > 65535) return 0;
            return port;
        }

        private static IDictionary<string, object> GetDict(IDictionary<string, object> parent, string key)
        {
            if (parent == null || !parent.ContainsKey(key)) return new Dictionary<string, object>();
            return parent[key] as IDictionary<string, object> ?? new Dictionary<string, object>();
        }

        private static string GetString(IDictionary<string, object> dict, string key, string fallback)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value) || value == null) return fallback;
            return Convert.ToString(value);
        }

        private static bool GetBool(IDictionary<string, object> dict, string key, bool fallback)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value)) return fallback;
            if (value is bool) return (bool)value;
            bool parsed;
            return bool.TryParse(Convert.ToString(value), out parsed) ? parsed : fallback;
        }
    }
}
