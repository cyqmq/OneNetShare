using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace WifiHotspot
{
    public static class ProxyProcessManager
    {
        private static readonly object OutputLock = new object();
        private static readonly StringBuilder ProcessOutput = new StringBuilder();
        private static Process _activeProcess;

        public static string LastDiagnostic { get; set; }

        public static string ProbeLogPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "probe.log"); }
        }

        public static bool IsRunning()
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:18089/status");
                request.Method = "GET";
                request.Timeout = 700;
                request.ReadWriteTimeout = 700;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300) return true;
                }
            }
            catch { }

            string[] names = { "hotspot-proxy", "hotspot-proxy-win-x86", "hotspot-proxy-openwrt-x86", "hotspot-proxy-openwrt-mipsle" };
            foreach (string name in names)
            {
                Process[] processes = Process.GetProcessesByName(name);
                int count = processes.Length;
                foreach (Process process in processes) process.Dispose();
                if (count > 0) return true;
            }
            return false;
        }

        public static string Start(string configPath)
        {
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hotspot-proxy.exe");
            LastDiagnostic = null;
            if (!File.Exists(exe))
            {
                LastDiagnostic = ProxyDiagnostics.Analyze(configPath, "未找到 hotspot-proxy.exe", null);
                return "未找到 hotspot-proxy.exe，代理未启动";
            }
            if (IsRunning()) return "代理已在运行";

            try
            {
                if (_activeProcess != null)
                {
                    try
                    {
                        if (_activeProcess.HasExited) DisposeActiveProcess();
                    }
                    catch { DisposeActiveProcess(); }
                }

                ClearOutput();
                var psi = new ProcessStartInfo(exe, "--config \"" + configPath + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.EnvironmentVariables["HOTSPOT_PROXY_LOG_FILE"] = ProbeLogPath;
                Process process = Process.Start(psi);
                if (process == null)
                {
                    LastDiagnostic = ProxyDiagnostics.Analyze(configPath, "Process.Start 未返回进程对象", null);
                    return "代理启动失败（进程对象为空）";
                }

                _activeProcess = process;
                AttachOutput(process);
                bool exited = process.WaitForExit(1500);
                if (exited)
                {
                    process.WaitForExit();
                    Thread.Sleep(100);
                    int code = process.ExitCode;
                    string output = GetProcessOutput();
                    LastDiagnostic = ProxyDiagnostics.Analyze(configPath, output, code);
                    DisposeActiveProcess();
                    return "代理启动失败（进程退出码 " + code + "）";
                }

                Thread.Sleep(300);
                if (!IsRunning())
                {
                    LastDiagnostic = ProxyDiagnostics.Analyze(configPath, GetProcessOutput(), null);
                    return "代理进程启动后未就绪，请查看自动诊断";
                }
                return "代理已启动";
            }
            catch (Exception ex)
            {
                LastDiagnostic = ProxyDiagnostics.Analyze(configPath, ex.Message, null);
                return "代理启动失败: " + ex.Message;
            }
        }

        public static string Stop()
        {
            bool graceful = false;
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:18089/shutdown");
                request.Method = "GET";
                request.Timeout = 1500;
                request.ReadWriteTimeout = 1500;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse()) { }
                graceful = true;
                Thread.Sleep(800);
            }
            catch { }

            int count = 0;
            string[] names = { "hotspot-proxy", "hotspot-proxy-win-x86", "hotspot-proxy-openwrt-x86", "hotspot-proxy-openwrt-mipsle" };
            foreach (string name in names)
            {
                foreach (Process process in Process.GetProcessesByName(name))
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
            }
            DisposeActiveProcess();
            if (count == 0)
                return graceful ? "代理已停止，路由已恢复" : "代理未运行";
            return graceful ? "代理已停止，已清理残留进程 (" + count + ")" : "代理已强制停止 (" + count + ")";
        }

        public static string GetProcessOutput()
        {
            lock (OutputLock) return ProcessOutput.ToString();
        }

        private static void AttachOutput(Process process)
        {
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data != null) AppendOutput(e.Data);
            };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data != null) AppendOutput(e.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        private static void AppendOutput(string line)
        {
            lock (OutputLock)
            {
                ProcessOutput.AppendLine(line);
                if (ProcessOutput.Length > 20000)
                    ProcessOutput.Remove(0, ProcessOutput.Length - 20000);
            }
        }

        private static void ClearOutput()
        {
            lock (OutputLock) ProcessOutput.Clear();
        }

        private static void DisposeActiveProcess()
        {
            Process process = _activeProcess;
            _activeProcess = null;
            if (process == null) return;
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch { }
            try { process.CancelOutputRead(); } catch { }
            try { process.CancelErrorRead(); } catch { }
            try { process.Dispose(); } catch { }
        }
    }
}
