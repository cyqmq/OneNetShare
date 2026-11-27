using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WifiHotspot
{
    public enum HotspotStatus
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        Error,
        Unsupported
    }

    public class HotspotResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    internal enum HostedNetworkState
    {
        Stopped,
        Started,
        Unknown
    }

    public class HotspotManager
    {
        private readonly IcsManager _ics = new IcsManager();
        private string _activeUpstreamId;
        private string _activePrivateId;
        private IcsManager.NetworkAdapterInfo _dnsAdapter;
        private IcsManager.AdapterDnsState _dnsRestore;

        public static bool IsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public static bool WintunAvailable()
        {
            string appDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wintun.dll");
            string systemDir = Path.Combine(Environment.SystemDirectory, "wintun.dll");
            return File.Exists(appDir) || File.Exists(systemDir);
        }

        public static string DecodeText(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;

            var strictUtf8 = new UTF8Encoding(false, true);
            try
            {
                string utf8 = strictUtf8.GetString(bytes).TrimStart('\uFEFF');
                if (!HasHighByte(bytes) || ContainsCjk(utf8))
                {
                    if (ScoreText(utf8) >= 0) return utf8;
                }
            }
            catch (DecoderFallbackException) { }

            var candidates = new List<Encoding>();
            foreach (int codePage in new[] { 936, 54936, 950, System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage, Encoding.Default.CodePage })
            {
                try { candidates.Add(Encoding.GetEncoding(codePage)); }
                catch { }
            }

            string best = string.Empty;
            int bestScore = int.MinValue;
            foreach (var encoding in candidates)
            {
                try
                {
                    string decoded = encoding.GetString(bytes).TrimStart('\uFEFF');
                    int score = ScoreText(decoded);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = decoded;
                    }
                }
                catch (DecoderFallbackException) { }
            }
            return best;
        }

        private static bool HasHighByte(byte[] bytes)
        {
            foreach (byte b in bytes)
            {
                if (b >= 0x80) return true;
            }
            return false;
        }

        private static bool ContainsCjk(string text)
        {
            foreach (char c in text)
            {
                if ((c >= 0x3400 && c <= 0x4DBF) || (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF))
                    return true;
            }
            return false;
        }

        private static int ScoreText(string text)
        {
            int score = 0;
            foreach (char c in text)
            {
                if (c == '\uFFFD') score -= 100;
                else if ((c >= 0x3400 && c <= 0x4DBF) || (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF)) score += 10;
                else if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t') score -= 5;
            }
            return score;
        }

        private static HotspotResult RunCommand(string fileName, string arguments, int timeoutMs = 15000)
        {
            try
            {
                var psi = new ProcessStartInfo(fileName, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null)
                        return new HotspotResult { Success = false, Message = "无法启动进程: " + fileName };

                    using (var stdoutBuffer = new MemoryStream())
                    using (var stderrBuffer = new MemoryStream())
                    {
                        Task stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(stdoutBuffer);
                        Task stderrTask = process.StandardError.BaseStream.CopyToAsync(stderrBuffer);

                        bool exited = process.WaitForExit(timeoutMs);
                        if (!exited)
                        {
                            try { process.Kill(); } catch { }
                            exited = process.WaitForExit(1000);
                        }
                        Task.WaitAll(stdoutTask, stderrTask);

                        string stdout = DecodeText(stdoutBuffer.ToArray());
                        string stderr = DecodeText(stderrBuffer.ToArray());
                        string output = (stdout + "\n" + stderr).Trim();
                        int exitCode = -1;
                        try { if (exited) exitCode = process.ExitCode; }
                        catch { }
                        bool ok = exited && exitCode == 0;
                        return new HotspotResult
                        {
                            Success = ok,
                            Message = string.IsNullOrWhiteSpace(output) ? (ok ? "成功" : "命令执行失败") : output
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new HotspotResult { Success = false, Message = ex.Message };
            }
        }

        public HotspotResult CheckSupport()
        {
            if (MobileHotspotManager.IsAvailable)
            {
                string mobileDetail;
                MobileHotspotState mobileState = MobileHotspotManager.GetState(out mobileDetail);
                if (mobileState == MobileHotspotState.On || mobileState == MobileHotspotState.Off || mobileState == MobileHotspotState.InTransition)
                    return new HotspotResult { Success = true, Message = "检测到 Windows 移动热点 API (WinRT)，将优先使用。\n" + mobileDetail };
            }

            var r = RunCommand("netsh", "wlan show drivers");
            if (!r.Success)
                return new HotspotResult { Success = false, Message = "无法读取无线网卡驱动信息。请确认已安装无线网卡且服务正常。" };

            string output = r.Message;
            if (Regex.IsMatch(output, @"Hosted\s*network\s*supported\s*:\s*Yes", RegexOptions.IgnoreCase) ||
                output.Contains("承载网络") && output.Contains("是"))
            {
                return new HotspotResult { Success = true, Message = "无线网卡支持承载网络。" };
            }

            if (Regex.IsMatch(output, @"Hosted\s*network\s*supported\s*:\s*No", RegexOptions.IgnoreCase) ||
                output.Contains("承载网络") && output.Contains("否"))
            {
                return new HotspotResult { Success = false, Message = "当前无线网卡不支持承载网络 (Hosted Network)。\n" +
                    "Windows 10/11 部分较新网卡已移除此功能，建议:\n" +
                    "1. 更新无线网卡驱动到最新版\n" +
                    "2. 或使用 Windows 设置 → 网络和 Internet → 移动热点 手动创建\n" +
                    "3. 或使用支持 AP 模式的 USB 无线网卡" };
            }

            return new HotspotResult { Success = false, Message = "未找到承载网络支持信息。\n驱动输出:\n" + output };
        }

        public HotspotDiagnosis Diagnose(string rawError, IcsManager.NetworkAdapterInfo internetAdapter, string ssid, string password, IcsManager.NetworkAdapterInfo wirelessAdapter = null)
        {
            var diagnosis = new HotspotDiagnosis { RawMessage = rawError ?? string.Empty };

            if (!IsAdministrator())
                AddIssue(diagnosis, "未以管理员权限运行", "程序检测到当前进程没有管理员权限，无法配置网络共享。",
                    "右键 WifiHotspot.exe → 以管理员身份运行；或让程序请求管理员权限后重新启动。", string.Empty);

            try
            {
                ProxySettings proxySettings = ProxyConfigManager.Load();
                if (proxySettings.NetworkEnabled && !NetworkNormalizer.DriverAvailable())
                    AddIssue(diagnosis, "网络层归一化缺少 WinDivert", "已启用网络层指纹归一化，但程序目录缺少 WinDivert.dll 或 WinDivert32.sys/WinDivert64.sys 驱动。",
                        "把 WinDivert 2.x 的 WinDivert.dll、WinDivert32.sys、WinDivert64.sys 放到程序目录，并以管理员运行；或在“代理功能”窗口关闭网络层归一化。", string.Empty);

                if (proxySettings.TunEnabled)
                {
                    if (!WintunAvailable())
                        AddIssue(diagnosis, "全局 TUN 接管缺少 Wintun.dll", "已启用 Wintun 全局 TUN 接管，但程序目录和系统目录都没有 wintun.dll。",
                            "把官方 Wintun 0.14.1 的 x86 wintun.dll 放到程序目录，并以管理员运行；或在“代理功能”窗口关闭全局 TUN 接管。", string.Empty);
                    if (!IsAdministrator())
                        AddIssue(diagnosis, "全局 TUN 接管需要管理员权限", "创建 Wintun 虚拟网卡、修改默认路由和 DNS 需要管理员权限。",
                            "右键 WifiHotspot.exe → 以管理员身份运行。", string.Empty);
                    if (!proxySettings.DeviceForgeEnabled || !proxySettings.Udp)
                        AddIssue(diagnosis, "全局 TUN 接管未启用 UDP 伪装", "Wintun 已接管流量，但 DeviceForge 或 UDP 子开关关闭，DNS/NTP/mDNS 不会重建会话。",
                            "打开“代理功能”→勾选“设备伪装模块总开关”和“UDP 会话重建”，保存并重启代理。", string.Empty);
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(ssid))
                AddIssue(diagnosis, "网络名称为空", "SSID 不能为空。",
                    "程序窗口 → 网络名称 (SSID) → 填写名称后重试。", string.Empty);

            if (string.IsNullOrEmpty(password) || password.Length < 8)
                AddIssue(diagnosis, "密码不符合要求", "密码长度必须至少 8 位。",
                    "程序窗口 → 密码 → 填写至少 8 位后重试。", string.Empty);

            if (internetAdapter == null && !MobileHotspotManager.IsAvailable)
                AddIssue(diagnosis, "未选择上网网卡", "传统承载网络需要指定正在连接互联网的网卡。",
                    "程序窗口 → 上网网卡 → 选择当前正在上网的网卡。", string.Empty);

            bool mobileEnabled;
            string capability = MobileHotspotManager.GetCapability(out mobileEnabled);
            if (!MobileHotspotManager.IsAvailable)
            {
                AddIssue(diagnosis, "程序未能调用 Windows 移动热点 API", capability,
                    "程序已尝试命名空间程序集和 Windows 元数据程序集两种加载方式。请确认已安装 .NET Framework 4.8 和 Windows 更新，并使用最新版程序；旧系统只能使用传统承载网络。", "ms-settings:windowsupdate");
            }
            else
            {
                string mobileDetail;
                MobileHotspotState mobileState = MobileHotspotManager.GetState(out mobileDetail);
                if (mobileState == MobileHotspotState.Unavailable)
                    AddIssue(diagnosis, "没有可用的互联网连接", mobileDetail,
                        "设置 → 网络和 Internet → 状态；先连接可用 Wi-Fi、插入网线或启用移动宽带。", "ms-settings:network-status");
                else if (mobileState == MobileHotspotState.Error)
                    AddIssue(diagnosis, "移动热点状态读取失败", mobileDetail,
                        "设置 → 网络和 Internet → 移动热点 → 确认页面可正常打开。", "ms-settings:network-mobilehotspot");

                if (mobileState == MobileHotspotState.On || mobileState == MobileHotspotState.InTransition)
                    AddIssue(diagnosis, "系统移动热点占用无线网卡", mobileDetail + "；传统承载网络需要独占无线网卡。",
                        "设置 → 网络和 Internet → 移动热点 → 关闭系统移动热点；或退出正在使用热点的其他软件后重试。", "ms-settings:network-mobilehotspot");

                if (!mobileEnabled)
                    AddIssue(diagnosis, "移动热点能力被禁用", capability,
                        "设置 → 网络和 Internet → 移动热点；如由单位策略禁用，需要管理员在组策略中放开。", "ms-settings:network-mobilehotspot");
            }

            if (!IsServiceRunning("WlanSvc"))
                AddIssue(diagnosis, "WLAN AutoConfig 服务未运行", "sc query WlanSvc 未显示正在运行。",
                    "services.msc → WLAN AutoConfig → 启动，并将启动类型设为自动。", "services.msc");

            if (!IsServiceRunning("icssvc"))
                AddIssue(diagnosis, "Internet Connection Sharing 服务未运行", "sc query icssvc 未显示正在运行。",
                    "services.msc → Internet Connection Sharing (ICS) → 启动，并将启动类型设为手动或自动。", "services.msc");

            bool selectedWirelessReady = wirelessAdapter == null || string.IsNullOrEmpty(wirelessAdapter.Id) ||
                IsWirelessAdapterAdminEnabled(wirelessAdapter);
            if (!selectedWirelessReady || !HasEnabledWirelessAdapter())
                AddIssue(diagnosis, "没有启用的无线网卡", "程序未找到管理状态为已启用的 Wireless80211 网卡；未连接 Wi-Fi 不代表网卡被禁用。",
                    "设备管理器 → 网络适配器 → 右键无线网卡 → 启用设备；或在设置 → 网络和 Internet → Wi-Fi 中打开 Wi-Fi。", "ms-settings:network-wifi");

            bool legacyRelevant = !MobileHotspotManager.IsAvailable ||
                (rawError ?? string.Empty).IndexOf("承载网络", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (rawError ?? string.Empty).IndexOf("Hosted network", StringComparison.OrdinalIgnoreCase) >= 0;
            if (legacyRelevant)
            {
                var support = CheckSupport();
                if (!support.Success)
                    AddIssue(diagnosis, "传统承载网络不可用", support.Message,
                        "设备管理器 → 网络适配器 → 更新无线网卡驱动；或 Windows 更新 → 可选更新；也可使用支持 AP 模式的 USB 无线网卡。", "ms-settings:windowsupdate");
            }

            AddIssuesFromError(diagnosis, rawError ?? string.Empty);
            return diagnosis;
        }

        private static void AddIssue(HotspotDiagnosis diagnosis, string title, string detail, string fixPath, string settingsUri)
        {
            diagnosis.Issues.Add(new HotspotIssue
            {
                Title = title,
                Detail = detail,
                FixPath = fixPath,
                SettingsUri = settingsUri
            });
        }

        private static bool IsServiceRunning(string serviceName)
        {
            var r = RunCommand("sc", "query " + serviceName);
            return r.Success && IsWlanServiceRunning(r.Message);
        }

        private static bool HasEnabledWirelessAdapter()
        {
            Dictionary<string, bool> adminStates = GetInterfaceAdminStates();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) continue;
                if (nic.OperationalStatus == OperationalStatus.NotPresent) continue;
                bool enabled;
                if (adminStates.TryGetValue(nic.Name, out enabled) && !enabled) continue;
                return true;
            }
            return false;
        }

        private static bool IsWirelessAdapterAdminEnabled(IcsManager.NetworkAdapterInfo adapter)
        {
            if (adapter == null || string.IsNullOrEmpty(adapter.Name)) return true;

            Dictionary<string, bool> states = GetInterfaceAdminStates();
            bool enabled;
            if (states.TryGetValue(adapter.Name, out enabled)) return enabled;

            return true;
        }

        private static Dictionary<string, bool> GetInterfaceAdminStates()
        {
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var r = RunCommand("netsh", "interface show interface");
            if (!r.Success || string.IsNullOrWhiteSpace(r.Message)) return result;

            string[] lines = r.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string nicName in GetWirelessAdapterNames())
            {
                foreach (string line in lines)
                {
                    if (line.IndexOf(nicName, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    bool enabled;
                    if (TryParseInterfaceAdminState(line, out enabled))
                        result[nicName] = enabled;
                    break;
                }
            }
            return result;
        }

        private static List<string> GetWirelessAdapterNames()
        {
            var names = new List<string>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) continue;
                if (!string.IsNullOrEmpty(nic.Name) &&
                    !names.Exists(n => string.Equals(n, nic.Name, StringComparison.OrdinalIgnoreCase)))
                    names.Add(nic.Name);
            }
            return names;
        }

        private static bool IsInterfaceAdminEnabled(string name, OperationalStatus status)
        {
            if (string.IsNullOrEmpty(name)) return status != OperationalStatus.NotPresent;

            Dictionary<string, bool> states = GetInterfaceAdminStates();
            bool enabled;
            if (states.TryGetValue(name, out enabled)) return enabled;
            return status != OperationalStatus.NotPresent;
        }

        private static bool TryParseInterfaceAdminState(string line, out bool enabled)
        {
            enabled = true;
            if (string.IsNullOrWhiteSpace(line)) return false;

            if (Regex.IsMatch(line, @"\bDisabled\b", RegexOptions.IgnoreCase) ||
                line.Contains("已禁用") || line.Contains("禁用") || line.Contains("未启用"))
            {
                enabled = false;
                return true;
            }

            if (Regex.IsMatch(line, @"\bEnabled\b", RegexOptions.IgnoreCase) ||
                line.Contains("已启用") || line.Contains("启用"))
            {
                enabled = true;
                return true;
            }
            return false;
        }

        private static void AddIssuesFromError(HotspotDiagnosis diagnosis, string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            if (message.IndexOf("WlanRadioOff", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.Contains("无线射频关闭") ||
                message.Contains("飞行模式") ||
                message.IndexOf("airplane mode", StringComparison.OrdinalIgnoreCase) >= 0)
                AddIssue(diagnosis, "无线射频被关闭", "启动结果明确返回 WlanRadioOff 或飞行模式相关错误。",
                    "设置 → 网络和 Internet → Wi-Fi → 打开 Wi-Fi；同时关闭飞行模式和笔记本无线开关。", "ms-settings:network-wifi");

            if (message.IndexOf("InternetConnectionUnavailable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.Contains("没有可用的互联网连接"))
                AddIssue(diagnosis, "没有可共享的互联网连接", "启动结果明确返回 InternetConnectionUnavailable。",
                    "设置 → 网络和 Internet → 状态 → 先让电脑连接可上网的网络。", "ms-settings:network-status");

            if (message.IndexOf("DeviceNotSupported", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.Contains("设备或驱动不支持") ||
                message.Contains("不支持承载网络"))
                AddIssue(diagnosis, "设备或驱动不支持移动热点", "启动结果明确返回 DeviceNotSupported 或 Hosted Network 不支持。",
                    "设备管理器 → 网络适配器 → 更新无线网卡驱动；或 Windows 更新 → 可选更新。", "ms-settings:windowsupdate");

            if (message.Contains("组或资源的状态不是执行请求操作的正确状态") ||
                message.IndexOf("group or resource is not in the correct state", StringComparison.OrdinalIgnoreCase) >= 0)
                AddIssue(diagnosis, "承载网络状态冲突", "Windows 承载网络处于不可启动状态，程序已尝试关闭系统移动热点并完整重置承载网络，但仍失败。",
                    "设置 → 网络和 Internet → 移动热点 → 关闭系统移动热点；退出其他热点/虚拟网卡软件；设备管理器中禁用再启用无线网卡后重试。", "ms-settings:network-mobilehotspot");
        }

        public HotspotStatus GetStatus(out string detail)
        {
            detail = string.Empty;

            string mobileDetail;
            MobileHotspotState mobileState = MobileHotspotManager.GetState(out mobileDetail);
            if (mobileState == MobileHotspotState.On)
            {
                detail = "移动热点运行中";
                return HotspotStatus.Running;
            }
            if (mobileState == MobileHotspotState.InTransition)
            {
                detail = mobileDetail;
                return HotspotStatus.Error;
            }

            var r = RunCommand("netsh", "wlan show hostednetwork");
            if (!r.Success)
            {
                detail = r.Message;
                return HotspotStatus.Error;
            }

            string output = r.Message;
            if (Regex.IsMatch(output, @"Status\s*:\s*Started", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(output, @"状态\s*[:：]\s*已启动"))
            {
                detail = "热点运行中";
                return HotspotStatus.Running;
            }

            if (Regex.IsMatch(output, @"Status\s*:\s*Not\s*Started", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(output, @"状态\s*[:：]\s*未启动"))
            {
                detail = "热点已停止";
                return HotspotStatus.Stopped;
            }

            detail = "状态未知\n" + output;
            return HotspotStatus.Error;
        }

        public HotspotResult Configure(string ssid, string password)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                return new HotspotResult { Success = false, Message = "SSID 不能为空" };
            if (ssid.Length > 32)
                return new HotspotResult { Success = false, Message = "SSID 最长 32 个字符" };
            if (string.IsNullOrEmpty(password) || password.Length < 8)
                return new HotspotResult { Success = false, Message = "密码长度必须至少 8 位" };
            if (password.Length > 63)
                return new HotspotResult { Success = false, Message = "密码最长 63 个字符" };

            string args = string.Format("wlan set hostednetwork mode=allow ssid=\"{0}\" key=\"{1}\"",
                EscapeArgument(ssid), EscapeArgument(password));
            var r = RunCommand("netsh", args);
            if (r.Success)
                r.Message = "热点配置已保存";
            return r;
        }

        private static bool IsWlanServiceRunning(string output)
        {
            return Regex.IsMatch(output, @"STATE\s*:\s*4\s+RUNNING", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(output, @"状态\s*[:：]\s*4\s*正在运行") ||
                   output.Contains("正在运行");
        }

        private static bool IsHostedNetworkStateError(string message)
        {
            return message.Contains("未能启动承载网络") ||
                   message.Contains("无法启动承载网络") ||
                   message.Contains("Hosted network couldn't be started") ||
                   message.Contains("组或资源的状态不是执行请求操作的正确状态") ||
                   message.Contains("The group or resource is not in the correct state");
        }

        private static bool ReleaseSystemMobileHotspot(out string detail)
        {
            detail = string.Empty;
            if (!MobileHotspotManager.IsAvailable)
            {
                detail = "系统移动热点 API 不可用，跳过释放";
                return true;
            }

            string stateDetail;
            MobileHotspotState state = MobileHotspotManager.GetState(out stateDetail);
            if (state == MobileHotspotState.Off)
            {
                detail = "系统移动热点已关闭";
                return true;
            }
            if (state == MobileHotspotState.Unavailable)
            {
                detail = "系统移动热点无可用配置文件";
                return true;
            }

            var stop = MobileHotspotManager.Stop();
            var messages = new List<string>();
            messages.Add("系统移动热点关闭: " + stop.Message);

            for (int i = 0; i < 20; i++)
            {
                state = MobileHotspotManager.GetState(out stateDetail);
                if (state == MobileHotspotState.Off || state == MobileHotspotState.Unavailable)
                {
                    messages.Add(stateDetail);
                    detail = string.Join("\n", messages.ToArray());
                    return true;
                }
                if (state == MobileHotspotState.Error && !stop.Success)
                    break;
                Thread.Sleep(300);
            }

            messages.Add("最终状态: " + stateDetail);
            detail = string.Join("\n", messages.ToArray());
            return state != MobileHotspotState.On && state != MobileHotspotState.InTransition;
        }

        private static HostedNetworkState GetHostedNetworkState(out string output)
        {
            var r = RunCommand("netsh", "wlan show hostednetwork");
            output = r.Message;
            if (!r.Success) return HostedNetworkState.Unknown;

            if (Regex.IsMatch(output, @"Status\s*:\s*Started", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(output, @"状态\s*[:：]\s*已启动"))
                return HostedNetworkState.Started;

            if (Regex.IsMatch(output, @"Status\s*:\s*Not\s*Started", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(output, @"状态\s*[:：]\s*未启动"))
                return HostedNetworkState.Stopped;

            return HostedNetworkState.Unknown;
        }

        private static bool WaitHostedNetworkStopped(int timeoutMs, out string detail)
        {
            string output = string.Empty;
            int deadline = Environment.TickCount + timeoutMs;
            while (true)
            {
                HostedNetworkState state = GetHostedNetworkState(out output);
                if (state == HostedNetworkState.Stopped)
                {
                    detail = "承载网络已停止";
                    return true;
                }
                if (state == HostedNetworkState.Started)
                    RunCommand("netsh", "wlan stop hostednetwork");

                if (Environment.TickCount - deadline >= 0 || timeoutMs <= 0)
                {
                    detail = "等待承载网络停止超时\n" + output;
                    return false;
                }
                Thread.Sleep(300);
            }
        }

        private HotspotResult ResetHostedNetworkState(string ssid, string password)
        {
            var messages = new List<string>();

            var disallow = RunCommand("netsh", "wlan set hostednetwork mode=disallow");
            messages.Add("承载网络 mode=disallow: " + disallow.Message);
            RunCommand("netsh", "wlan stop hostednetwork");

            string waitDetail;
            if (!WaitHostedNetworkStopped(6000, out waitDetail))
                messages.Add(waitDetail);

            var cfg = Configure(ssid, password);
            messages.Add("承载网络重新配置: " + cfg.Message);
            if (!cfg.Success)
                return new HotspotResult { Success = false, Message = string.Join("\n", messages.ToArray()) };

            Thread.Sleep(300);
            return new HotspotResult { Success = true, Message = string.Join("\n", messages.ToArray()) };
        }

        private HotspotResult PrepareHostedNetwork(IcsManager.NetworkAdapterInfo wirelessAdapter = null)
        {
            var query = RunCommand("sc", "query WlanSvc");
            if (!query.Success || !IsWlanServiceRunning(query.Message))
            {
                var startService = RunCommand("sc", "start WlanSvc");
                if (!startService.Success &&
                    !startService.Message.Contains("1056") &&
                    !startService.Message.Contains("already running") &&
                    !startService.Message.Contains("已经启动"))
                {
                    return new HotspotResult { Success = false, Message = "无法启动 WLAN AutoConfig 服务 (WlanSvc):\n" + startService.Message };
                }
            }

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) continue;
                if (wirelessAdapter != null &&
                    !string.Equals(nic.Id, wirelessAdapter.Id, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(nic.Name, wirelessAdapter.Name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsInterfaceAdminEnabled(nic.Name, nic.OperationalStatus)) continue;
                RunCommand("netsh", string.Format("interface set interface \"{0}\" admin=ENABLED", EscapeArgument(nic.Name)));
            }

            RunCommand("netsh", "wlan stop hostednetwork");
            string waitDetail;
            WaitHostedNetworkStopped(2000, out waitDetail);
            return new HotspotResult { Success = true, Message = "无线环境已准备" };
        }

        public HotspotResult Start(IcsManager.NetworkAdapterInfo internetAdapter, string ssid, string password)
        {
            string modernError = string.Empty;
            if (MobileHotspotManager.IsAvailable)
            {
                var modern = MobileHotspotManager.Start(ssid, password);
                if (modern.Success)
                    return modern;
                modernError = "Windows 移动热点: " + modern.Message;
            }

            var legacy = StartLegacy(internetAdapter, ssid, password);
            if (legacy.Success)
                return legacy;

            if (!string.IsNullOrEmpty(modernError))
                return new HotspotResult { Success = false, Message = modernError + "\n\n传统承载网络: " + legacy.Message };
            return legacy;
        }

        private HotspotResult EnsureWiredUpstreamDns(IcsManager.NetworkAdapterInfo adapter)
        {
            _dnsAdapter = null;
            _dnsRestore = null;
            var state = IcsManager.GetDnsState(adapter);
            var servers = new List<string>();
            try
            {
                string configured = ProxyConfigManager.Load().DnsUpstreamServers;
                foreach (string part in (configured ?? string.Empty).Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    System.Net.IPAddress ip;
                    if (!System.Net.IPAddress.TryParse(part.Trim(), out ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                    string value = ip.ToString();
                    if (!servers.Contains(value)) servers.Add(value);
                    if (servers.Count == 3) break;
                }
            }
            catch
            {
            }
            if (servers.Count == 0) servers.AddRange(state.Servers);
            if (servers.Count == 0) servers.Add("1.1.1.1");

            bool unchanged = state.Servers.Count == servers.Count;
            if (unchanged)
            {
                for (int i = 0; i < servers.Count; i++)
                {
                    if (!string.Equals(state.Servers[i], servers[i], StringComparison.OrdinalIgnoreCase))
                    {
                        unchanged = false;
                        break;
                    }
                }
            }
            if (unchanged)
                return new HotspotResult { Success = true, Message = "有线入口 DNS 就绪: " + string.Join(", ", servers.ToArray()) };

            var result = IcsManager.SetDnsServers(adapter, servers);
            if (result.Success)
            {
                _dnsAdapter = adapter;
                _dnsRestore = state;
                result.Message = "已将上游 DNS 下发到有线入口 " + adapter.Name + ": " + string.Join(", ", servers.ToArray()) + "；停止热点后自动恢复。";
            }
            return result;
        }

        private void RestoreWiredUpstreamDns()
        {
            if (_dnsAdapter != null && _dnsRestore != null)
                IcsManager.RestoreDnsServers(_dnsAdapter, _dnsRestore);
            _dnsAdapter = null;
            _dnsRestore = null;
        }

        public HotspotResult StartWiredWifi(IcsManager.NetworkAdapterInfo wiredAdapter, IcsManager.NetworkAdapterInfo wirelessAdapter, string ssid, string password)
        {
            if (wiredAdapter == null)
                return new HotspotResult { Success = false, Message = "有线入口模式未选择上游网卡" };
            if (wirelessAdapter == null)
                return new HotspotResult { Success = false, Message = "有线入口模式未选择无线热点网卡" };
            if (wiredAdapter.InterfaceType != NetworkInterfaceType.Ethernet)
                return new HotspotResult { Success = false, Message = "上游网卡不是有线以太网: " + wiredAdapter.Name };
            if (wirelessAdapter.InterfaceType != NetworkInterfaceType.Wireless80211)
                return new HotspotResult { Success = false, Message = "热点网卡不是无线网卡: " + wirelessAdapter.Name };
            if (string.Equals(wiredAdapter.Id, wirelessAdapter.Id, StringComparison.OrdinalIgnoreCase))
                return new HotspotResult { Success = false, Message = "上游网卡与热点网卡不能相同" };

            var dns = EnsureWiredUpstreamDns(wiredAdapter);
            if (!dns.Success)
                return new HotspotResult { Success = false, Message = "上游 DNS 下发失败:\n" + dns.Message };

            var legacy = StartLegacy(wiredAdapter, ssid, password, wirelessAdapter, true);
            if (!legacy.Success)
            {
                RestoreWiredUpstreamDns();
                return new HotspotResult { Success = false, Message = "有线入口/无线热点启动失败:\n" + legacy.Message };
            }
            legacy.Message = "有线入口 → 无线热点 已启动。\n上行: " + wiredAdapter.Name +
                             "\n热点: " + wirelessAdapter.Name + "\n" + dns.Message + "\n" + legacy.Message;
            return legacy;
        }

        private HotspotResult StartLegacy(IcsManager.NetworkAdapterInfo internetAdapter, string ssid, string password, IcsManager.NetworkAdapterInfo wirelessAdapter = null, bool strictIcs = false)
        {
            string releaseDetail;
            if (!ReleaseSystemMobileHotspot(out releaseDetail))
                return new HotspotResult { Success = false, Message = "无法关闭系统移动热点，传统承载网络不能与它同时占用无线网卡。\n" + releaseDetail };

            var prepare = PrepareHostedNetwork(wirelessAdapter);
            if (!prepare.Success)
                return prepare;

            var cfg = Configure(ssid, password);
            if (!cfg.Success)
                return cfg;

            var startResult = RunCommand("netsh", "wlan start hostednetwork");
            if (!startResult.Success && IsHostedNetworkStateError(startResult.Message))
            {
                var reset = ResetHostedNetworkState(ssid, password);
                if (!reset.Success)
                    return new HotspotResult { Success = false, Message = "承载网络状态冲突，软件已自动关闭系统移动热点并完整重置，但仍无法启动。\n\n重置信息:\n" + reset.Message + "\n\n原始信息:\n" + startResult.Message };
                startResult = RunCommand("netsh", "wlan start hostednetwork");
            }

            if (!startResult.Success)
            {
                string m = startResult.Message;
                if (IsHostedNetworkStateError(m))
                    return new HotspotResult { Success = false, Message = "无法启动热点。软件已自动尝试启动 WLAN 服务、关闭系统移动热点、启用无线网卡并重置承载网络，诊断结果将在窗口中显示。\n\n原始信息:\n" + m };
                return startResult;
            }

            var virtualAdapter = GetHostedNetworkAdapter();
            if (virtualAdapter == null)
            {
                if (strictIcs) RunCommand("netsh", "wlan stop hostednetwork");
                return new HotspotResult
                {
                    Success = !strictIcs,
                    Message = strictIcs
                        ? "无法找到承载网络虚拟网卡，无法为客户端下发 DNS/网关；请检查 Hosted Network 驱动后重试。"
                        : "热点已启动，但未找到虚拟网卡，ICS 共享需手动设置。"
                };
            }

            if (internetAdapter != null)
            {
                var icsResult = _ics.EnableSharing(internetAdapter.Id, virtualAdapter.Id);
                if (icsResult.Success)
                {
                    _activeUpstreamId = internetAdapter.Id;
                    _activePrivateId = virtualAdapter.Id;
                }
                if (!icsResult.Success)
                {
                    if (strictIcs) RunCommand("netsh", "wlan stop hostednetwork");
                    return new HotspotResult
                    {
                        Success = !strictIcs,
                        Message = "ICS 共享设置失败: " + icsResult.Message +
                                  "\nICS 负责给手机下发网关与 DNS；未成功时客户端不会收到 DNS。" +
                                  "\n请到 控制面板 → 网络连接 → 右键当前上网网卡 → 属性 → 共享 中手动勾选共享。"
                    };
                }
            }

            return new HotspotResult { Success = true, Message = "热点已启动并开启网络共享。" };
        }

        public HotspotResult Stop()
        {
            var messages = new List<string>();
            bool stopped = false;

            if (MobileHotspotManager.IsAvailable)
            {
                var modern = MobileHotspotManager.Stop();
                messages.Add("Windows 移动热点: " + modern.Message);
                stopped = stopped || modern.Success;
            }

            var legacy = StopLegacy();
            messages.Add("传统承载网络: " + legacy.Message);
            stopped = stopped || legacy.Success;
            return new HotspotResult { Success = stopped, Message = string.Join("\n", messages) };
        }

        private HotspotResult StopLegacy()
        {
            if (!string.IsNullOrEmpty(_activeUpstreamId))
                _ics.DisableSharing(_activeUpstreamId);
            else
            {
                var adapters = _ics.GetSharingAdapters();
                foreach (var id in adapters)
                    _ics.DisableSharing(id);
            }
            _activeUpstreamId = null;
            _activePrivateId = null;
            RestoreWiredUpstreamDns();

            var r = RunCommand("netsh", "wlan stop hostednetwork");
            if (r.Success)
                r.Message = "热点已停止，网络共享已关闭";
            return r;
        }

        public List<IcsManager.NetworkAdapterInfo> GetInternetAdapters()
        {
            return _ics.GetNetworkAdapters();
        }

        public IcsManager.NetworkAdapterInfo GetHostedNetworkAdapter()
        {
            foreach (var a in _ics.GetNetworkAdapters())
            {
                if (a.Description.IndexOf("Hosted Network", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    a.Description.IndexOf("承载网络", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    a.Name.StartsWith("Local Area Connection*", StringComparison.OrdinalIgnoreCase) ||
                    a.Name.StartsWith("本地连接*", StringComparison.OrdinalIgnoreCase))
                {
                    return a;
                }
            }
            return null;
        }

        public List<string> GetConnectedClients()
        {
            var clients = new List<string>();
            var r = RunCommand("arp", "-a");
            if (!r.Success) return clients;

            foreach (string line in r.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var m = Regex.Match(line, @"^\s*(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\s+([0-9a-fA-F-]{17})");
                if (m.Success)
                {
                    string ip = m.Groups[1].Value;
                    if (ip.StartsWith("192.168.137.") || ip.StartsWith("192.168.173."))
                        clients.Add(ip + "  " + m.Groups[2].Value);
                }
            }
            return clients;
        }

        private static string EscapeArgument(string s)
        {
            return s.Replace("\"", "\\\"");
        }
    }
}
