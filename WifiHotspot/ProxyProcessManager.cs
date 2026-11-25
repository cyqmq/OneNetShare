using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;

namespace WifiHotspot
{
    public static class ProxyProcessManager
    {
        public static string ProbeLogPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "probe.log"); }
        }

        public static bool IsRunning()
        {
            Process[] processes = Process.GetProcessesByName("hotspot-proxy");
            foreach (Process process in processes) process.Dispose();
            return processes.Length > 0;
        }

        public static string Start(string configPath)
        {
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hotspot-proxy.exe");
            if (!File.Exists(exe)) return "未找到 hotspot-proxy.exe，代理未启动";
            if (IsRunning()) return "代理已在运行";

            try
            {
                var psi = new ProcessStartInfo(exe, "--config \"" + configPath + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };
                psi.EnvironmentVariables["HOTSPOT_PROXY_LOG_FILE"] = ProbeLogPath;
                Process process = Process.Start(psi);
                if (process != null) process.Dispose();
                Thread.Sleep(600);
                return IsRunning() ? "代理已启动" : "代理进程启动后立即退出，请查看 config.json 和端口占用";
            }
            catch (Exception ex)
            {
                return "代理启动失败: " + ex.Message;
            }
        }

        public static string Stop()
        {
            bool graceful = false;
            try
            {
                using (var client = new WebClient())
                    client.DownloadString("http://127.0.0.1:18089/shutdown");
                graceful = true;
                Thread.Sleep(800);
            }
            catch { }

            int count = 0;
            foreach (Process process in Process.GetProcessesByName("hotspot-proxy"))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(3000);
                    count++;
                }
                catch { }
                finally { process.Dispose(); }
            }
            if (count == 0)
                return graceful ? "代理已停止，路由已恢复" : "代理未运行";
            return graceful ? "代理已停止，已清理残留进程 (" + count + ")" : "代理已强制停止 (" + count + ")";
        }
    }
}
