using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
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

    public class HotspotManager
    {
        private readonly IcsManager _ics = new IcsManager();

        public static bool IsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public static string DecodeText(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;

            var candidates = new List<Encoding>();
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                candidates.Add(new UTF8Encoding(false, false));
            candidates.Add(new UTF8Encoding(false, true));

            foreach (int codePage in new[] { 936, 54936, System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage, Encoding.Default.CodePage })
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
                    string decoded = encoding.GetString(bytes);
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

        private static int ScoreText(string text)
        {
            int score = 0;
            foreach (char c in text)
            {
                if (c == '\uFFFD') score -= 100;
                else if (c >= 0x4E00 && c <= 0x9FFF) score += 10;
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

        public HotspotStatus GetStatus(out string detail)
        {
            detail = string.Empty;
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

        public HotspotResult Start(IcsManager.NetworkAdapterInfo internetAdapter, string ssid, string password)
        {
            var cfg = Configure(ssid, password);
            if (!cfg.Success)
                return cfg;

            var startResult = RunCommand("netsh", "wlan start hostednetwork");
            if (!startResult.Success)
            {
                string m = startResult.Message;
                if (m.Contains("Hosted network couldn't be started") || m.Contains("无法启动承载网络"))
                    return new HotspotResult { Success = false, Message = "无法启动热点。可能原因:\n" +
                        "1. 无线网卡被占用或已断开\n" +
                        "2. 系统不支持承载网络\n" +
                        "3. 需要以管理员身份运行\n\n原始信息:\n" + m };
                return startResult;
            }

            var virtualAdapter = GetHostedNetworkAdapter();
            if (virtualAdapter == null)
                return new HotspotResult { Success = true, Message = "热点已启动，但未找到虚拟网卡，ICS 共享需手动设置。" };

            if (internetAdapter != null)
            {
                var icsResult = _ics.EnableSharing(internetAdapter.Id, virtualAdapter.Id);
                if (!icsResult.Success)
                    return new HotspotResult { Success = true, Message =
                        "热点已启动，但 ICS 共享设置失败: " + icsResult.Message +
                        "\n请到 控制面板 → 网络连接 → 右键当前上网网卡 → 属性 → 共享 中手动勾选共享。" };
            }

            return new HotspotResult { Success = true, Message = "热点已启动并开启网络共享。" };
        }

        public HotspotResult Stop()
        {
            var adapters = _ics.GetSharingAdapters();
            foreach (var id in adapters)
                _ics.DisableSharing(id);

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
