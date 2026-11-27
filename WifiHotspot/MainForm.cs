using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WifiHotspot
{
    public partial class MainForm : Form
    {
        private readonly HotspotManager _manager = new HotspotManager();
        private List<IcsManager.NetworkAdapterInfo> _adapters;
        private List<IcsManager.NetworkAdapterInfo> _adapterSource;
        private List<IcsManager.NetworkAdapterInfo> _wiredAdapters;
        private List<IcsManager.NetworkAdapterInfo> _wirelessAdapters;
        private string _configPath;
        private string _savedAdapterId;
        private string _savedTopologyMode = "auto";
        private string _savedWirelessAdapterId;
        private string _lastClientsKey;
        private int _statusBusy;
        private int _clientsBusy;
        private bool _shuttingDown;

        private class StatusSnapshot
        {
            public HotspotStatus Status;
            public string Text;
            public Color Color;
            public bool StartEnabled;
            public bool StopEnabled;
            public bool Running;
            public bool ClearClients;
            public List<string> Clients;
        }

        public MainForm()
        {
            InitializeComponent();
            _configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WifiHotspot", "config.ini");
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            if (!HotspotManager.IsAdministrator())
            {
                lblStatus.ForeColor = Color.Red;
                lblStatus.Text = "警告：当前不是管理员权限，热点功能无法正常工作。\n请右键程序 → 以管理员身份运行。";
                btnStart.Enabled = false;
                btnStop.Enabled = false;
            }

            LoadConfig();
            RefreshAdapters();
            StartNetworkNormalizer();
            StartTrafficMonitorIfNeeded();
            CheckSupportAndStatus();
            timerStatus.Start();
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(_configPath)) return;
                string content = HotspotManager.DecodeText(File.ReadAllBytes(_configPath));
                foreach (string line in content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string[] parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length != 2) continue;

                    switch (parts[0].Trim())
                    {
                        case "SSID": txtSsid.Text = parts[1]; break;
                        case "Password": txtPassword.Text = parts[1]; break;
                        case "AdapterId": _savedAdapterId = parts[1]; break;
                        case "TopologyMode": _savedTopologyMode = parts[1]; break;
                        case "WirelessAdapterId": _savedWirelessAdapterId = parts[1]; break;
                    }
                }
            }
            catch
            {
                // ignore config load errors
            }
        }

        private void SaveConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string upstreamId = string.Empty;
                if (cmbAdapter.SelectedIndex >= 0 && cmbAdapter.SelectedIndex < _adapterSource.Count)
                    upstreamId = _adapterSource[cmbAdapter.SelectedIndex].Id;
                string wirelessId = string.Empty;
                if (cmbWirelessAdapter.SelectedIndex >= 0 && cmbWirelessAdapter.SelectedIndex < _wirelessAdapters.Count)
                    wirelessId = _wirelessAdapters[cmbWirelessAdapter.SelectedIndex].Id;

                var lines = new[]
                {
                    "# WifiHotspot 配置文件",
                    "SSID=" + txtSsid.Text,
                    "Password=" + txtPassword.Text,
                    "TopologyMode=" + TopologyMode(),
                    "AdapterId=" + upstreamId,
                    "WirelessAdapterId=" + wirelessId
                };
                File.WriteAllLines(_configPath, lines, new UTF8Encoding(true));
                MessageBox.Show("设置已保存。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存设置失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshAdapters()
        {
            _adapters = _manager.GetInternetAdapters();
            _wiredAdapters = new List<IcsManager.NetworkAdapterInfo>();
            _wirelessAdapters = new List<IcsManager.NetworkAdapterInfo>();
            foreach (var adapter in _adapters)
            {
                if (adapter.InterfaceType == NetworkInterfaceType.Ethernet)
                    _wiredAdapters.Add(adapter);
                else if (adapter.InterfaceType == NetworkInterfaceType.Wireless80211)
                    _wirelessAdapters.Add(adapter);
            }

            if (_wiredAdapters.Count == 0)
                _wiredAdapters.Add(new IcsManager.NetworkAdapterInfo { Name = "未检测到有线网卡", StatusText = "请先连接有线网络" });
            if (_wirelessAdapters.Count == 0)
                _wirelessAdapters.Add(new IcsManager.NetworkAdapterInfo { Name = "未检测到无线网卡", StatusText = "请启用无线网卡" });

            SetTopologyCombo(_savedTopologyMode);
            _adapterSource = IsWiredWifiMode() ? _wiredAdapters : _adapters;
            FillAdapterCombo(cmbAdapter, _adapterSource, _savedAdapterId);
            FillAdapterCombo(cmbWirelessAdapter, _wirelessAdapters, _savedWirelessAdapterId);
            UpdateTopologyUi();
        }

        private static void FillAdapterCombo(ComboBox combo, List<IcsManager.NetworkAdapterInfo> adapters, string savedId)
        {
            combo.Items.Clear();
            int selectedIndex = -1;
            for (int i = 0; i < adapters.Count; i++)
            {
                var adapter = adapters[i];
                combo.Items.Add(adapter.ToString());
                if (!string.IsNullOrEmpty(savedId) &&
                    string.Equals(adapter.Id, savedId, StringComparison.OrdinalIgnoreCase))
                    selectedIndex = i;
            }
            combo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : (combo.Items.Count > 0 ? 0 : -1);
        }

        private bool IsWiredWifiMode()
        {
            return TopologyMode() == "wired-wifi";
        }

        private string TopologyText()
        {
            return IsWiredWifiMode() ? "有线入口 → 无线热点" : "自动（当前行为）";
        }

        private string TopologyMode()
        {
            string text = cmbTopology != null && cmbTopology.SelectedIndex >= 0
                ? cmbTopology.SelectedItem.ToString()
                : string.Empty;
            return text.Contains("有线") ? "wired-wifi" : "auto";
        }

        private void SetTopologyCombo(string mode)
        {
            string wanted = string.Equals(mode, "wired-wifi", StringComparison.OrdinalIgnoreCase)
                ? "有线入口 → 无线热点"
                : "自动（当前行为）";
            for (int i = 0; i < cmbTopology.Items.Count; i++)
            {
                if (string.Equals(cmbTopology.Items[i].ToString(), wanted, StringComparison.Ordinal))
                {
                    cmbTopology.SelectedIndex = i;
                    return;
                }
            }
            if (cmbTopology.Items.Count > 0) cmbTopology.SelectedIndex = 0;
        }

        private void UpdateTopologyUi()
        {
            bool wired = IsWiredWifiMode();
            lblAdapter.Text = wired ? "有线入口网卡:" : "上行网卡:";
            lblWirelessAdapter.Visible = wired;
            cmbWirelessAdapter.Visible = wired;
            lblDirection.Visible = wired;
            lblDirection.Text = wired ? "流量方向: 有线入口 → 本机 ICS/NAT → 无线热点客户端" : string.Empty;
        }

        private void cmbTopology_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_adapters == null) return;
            _savedTopologyMode = TopologyMode();
            RefreshAdapters();
        }

        private int ResolveHotspotInterfaceIndex()
        {
            var hotspot = _manager.GetHostedNetworkAdapter();
            if (hotspot == null) return 0;
            return IcsManager.GetInterfaceIndex(hotspot.Id, hotspot.Name, hotspot.Description);
        }

        private void CheckSupportAndStatus()
        {
            var support = _manager.CheckSupport();
            string statusText;
            Color color;

            HotspotStatus status = _manager.GetStatus(out statusText);
            switch (status)
            {
                case HotspotStatus.Running:
                    color = Color.Green;
                    btnStart.Enabled = false;
                    btnStop.Enabled = true;
                    timerStatus.Enabled = true;
                    RefreshClients();
                    break;
                case HotspotStatus.Stopped:
                    color = Color.DarkOrange;
                    btnStart.Enabled = true;
                    btnStop.Enabled = false;
                    break;
                case HotspotStatus.Unsupported:
                case HotspotStatus.Error:
                default:
                    color = Color.Red;
                    btnStart.Enabled = true;
                    btnStop.Enabled = true;
                    break;
            }

            if (!HotspotManager.IsAdministrator())
            {
                color = Color.Red;
                btnStart.Enabled = false;
                btnStop.Enabled = false;
            }

            lblStatus.ForeColor = color;
            lblStatus.Text = string.Format("热点状态: {0}\n支持检测: {1}\n代理/网络层/TTL/DNS: {2}", statusText, support.Message, ModuleStatusText());
        }

        private void ShowHotspotDiagnosis(string message, IcsManager.NetworkAdapterInfo adapter, IcsManager.NetworkAdapterInfo wirelessAdapter = null)
        {
            var diagnosis = _manager.Diagnose(message, adapter, txtSsid.Text, txtPassword.Text, wirelessAdapter);
            using (var form = new DiagnosisForm(diagnosis))
                form.ShowDialog(this);
        }

        private void ShowProxyDiagnosis(string message)
        {
            MessageBox.Show(this, message, "代理自动诊断", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void RefreshClients()
        {
            if (_shuttingDown) return;
            if (Interlocked.Exchange(ref _clientsBusy, 1) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                List<string> clients = new List<string>();
                try
                {
                    clients = _manager.GetConnectedClients();
                }
                catch
                {
                    clients = new List<string>();
                }
                try
                {
                    BeginInvoke(new Action(() => ApplyClients(clients, true)));
                }
                catch { }
                finally
                {
                    Interlocked.Exchange(ref _clientsBusy, 0);
                }
            });
        }

        private void ApplyClients(List<string> clients, bool running)
        {
            if (_shuttingDown || IsDisposed) return;
            if (!running)
            {
                _lastClientsKey = null;
                if (lstClients.Items.Count > 0) lstClients.Items.Clear();
                return;
            }
            if (clients == null) return;
            string key = clients.Count == 0 ? "empty" : string.Join("\n", clients.ToArray());
            if (key == _lastClientsKey) return;
            _lastClientsKey = key;
            lstClients.BeginUpdate();
            try
            {
                lstClients.Items.Clear();
                if (clients.Count == 0)
                    lstClients.Items.Add("暂无已连接设备");
                else
                    foreach (var client in clients)
                        lstClients.Items.Add(client);
            }
            finally
            {
                lstClients.EndUpdate();
            }
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtSsid.Text))
            {
                MessageBox.Show("请输入网络名称 (SSID)。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtSsid.Focus();
                return;
            }
            if (txtPassword.Text.Length < 8)
            {
                MessageBox.Show("密码长度必须至少 8 位。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtPassword.Focus();
                return;
            }

            IcsManager.NetworkAdapterInfo selected = null;
            if (cmbAdapter.SelectedIndex >= 0 && cmbAdapter.SelectedIndex < _adapterSource.Count)
                selected = _adapterSource[cmbAdapter.SelectedIndex];
            IcsManager.NetworkAdapterInfo selectedWireless = null;
            if (cmbWirelessAdapter.SelectedIndex >= 0 && cmbWirelessAdapter.SelectedIndex < _wirelessAdapters.Count)
                selectedWireless = _wirelessAdapters[cmbWirelessAdapter.SelectedIndex];

            btnStart.Enabled = false;
            lblStatus.ForeColor = Color.Blue;
            lblStatus.Text = "正在启动热点，请稍候...";
            lblStatus.Refresh();

            HotspotTiming.BeginStart();
            HotspotResult result = IsWiredWifiMode()
                ? _manager.StartWiredWifi(selected, selectedWireless, txtSsid.Text, txtPassword.Text)
                : _manager.Start(selected, txtSsid.Text, txtPassword.Text);
            if (result.Success)
            {
                string proxyMessage = string.Empty;
                try
                {
                    ProxySettings settings = ProxyConfigManager.Load();
                    int hotspotIndex = IsWiredWifiMode() ? ResolveHotspotInterfaceIndex() : 0;
                    if (settings.AutoStartProxy)
                    {
                        proxyMessage = "\n" + ProxyProcessManager.Start(ProxyConfigManager.DefaultConfigPath);
                        if (!ProxyProcessManager.IsRunning() && !string.IsNullOrEmpty(ProxyProcessManager.LastDiagnostic))
                            ShowProxyDiagnosis("热点启动后代理未就绪。\n" + ProxyProcessManager.LastDiagnostic);
                        else if (settings.TunEnabled && !ProxyProcessManager.IsRunning())
                            ShowHotspotDiagnosis("全局 TUN 代理启动失败：\n" + proxyMessage, selected);
                    }
                    List<string> globalIPv6 = ProxyDiagnostics.FindHotspotGlobalIPv6();
                    bool needIPv6Guard = settings.IPv6GuardEnabled && globalIPv6.Count > 0;
                    if (needIPv6Guard)
                    {
                        string policy = "block";
                        string guardScope = hotspotIndex > 0 ? "热点接口转发封禁" : "全局转发封禁";
                        proxyMessage += "\n检测到热点网卡公网 IPv6 " + string.Join(", ", globalIPv6.ToArray()) + "，自动启用" + guardScope + "。";
                        var ipv6Result = IPv6Guard.Start(policy, hotspotIndex);
                        proxyMessage += "\n" + ipv6Result.Message;
                        if (!ipv6Result.Success)
                        {
                            string hint = ipv6Result.Message.IndexOf("1450") >= 0
                                ? "\n\n错误码 1450 (ERROR_NO_SYSTEM_RESOURCES): 可能是系统内核资源不足，或 WinDivert 驱动状态异常。建议：重启电脑后重试；确认无其他 WinDivert/Clash/TUN 工具占用同一驱动；必要时以管理员运行 `sc stop WinDivert`、`sc delete WinDivert` 后重启。"
                                : "";
                            ShowHotspotDiagnosis("IPv6 转发封禁启动失败：\n" + ipv6Result.Message + hint, selected);
                        }
                    }
                    else
                    {
                        proxyMessage += !settings.IPv6GuardEnabled
                            ? "\nIPv6 封禁开关已关闭，未启用 IPv6 转发封禁。"
                            : "\n未检测到热点网卡公网 IPv6（仅 ULA/Link-local），未启用 IPv6 封禁。";
                    }
                    if (settings.PortalBlockEnabled)
                    {
                        var portalResult = PortalGuard.Start(settings.PortalPrimaryIP, settings.PortalSecondaryIP, hotspotIndex);
                        proxyMessage += "\n" + portalResult.Message;
                        if (!portalResult.Success)
                            ShowHotspotDiagnosis("Portal 封堵启动失败：\n" + portalResult.Message, selected);
                    }
                    if (settings.OutboundGuardEnabled)
                    {
                        if (settings.TunEnabled)
                        {
                            proxyMessage += "\n全局 TUN 模式由代理层执行出站封堵，跳过 WinDivert 出站封堵。";
                        }
                        else
                        {
                            var guardResult = OutboundGuard.Start(settings, hotspotIndex);
                            proxyMessage += "\n" + guardResult.Message;
                            if (!guardResult.Success)
                                ShowHotspotDiagnosis("出站封堵启动失败：\n" + guardResult.Message, selected);
                        }
                    }
                    if (settings.MonitorEnabled && (settings.MonitorTtl || settings.MonitorDns))
                    {
                        var monitorResult = TrafficMonitor.Shared.Start(settings);
                        proxyMessage += "\n" + monitorResult.Message;
                        if (!monitorResult.Success)
                            ShowHotspotDiagnosis("核心监控启动失败：\n" + monitorResult.Message, selected);
                    }
                }
                catch (Exception ex)
                {
                    proxyMessage = "\n代理自动启动失败: " + ex.Message;
                }
                HotspotTiming.EndStart(true);
                MessageBox.Show(result.Message + proxyMessage + "\n" + HotspotTiming.StatusText, "启动结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                HotspotTiming.EndStart(false);
                ShowHotspotDiagnosis(result.Message, selected, selectedWireless);
            }

            CheckSupportAndStatus();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            btnStop.Enabled = false;
            lblStatus.ForeColor = Color.Blue;
            lblStatus.Text = "正在停止热点...";
            lblStatus.Refresh();

            string cleanup = StopRuntimeComponents(true);
            HotspotTiming.EndSession();
            if (cleanup.IndexOf("失败", StringComparison.OrdinalIgnoreCase) < 0)
                MessageBox.Show(cleanup + "\n" + HotspotTiming.StatusText, "停止结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                ShowHotspotDiagnosis(cleanup, null);

            lstClients.Items.Clear();
            CheckSupportAndStatus();
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            RefreshAdapters();
            CheckSupportAndStatus();
        }

        private void btnProxy_Click(object sender, EventArgs e)
        {
            using (var form = new ProxySettingsForm())
                form.ShowDialog(this);
            CheckSupportAndStatus();
        }

        private void StartNetworkNormalizer()
        {
            try
            {
                if (IsWiredWifiMode())
                {
                    NetworkNormalizer.Shared.Stop();
                    return;
                }
                ProxySettings settings = ProxyConfigManager.Load();
                if (settings.TunEnabled || !settings.NetworkEnabled) return;
                var result = NetworkNormalizer.Shared.Start(settings);
                if (!result.Success) ShowHotspotDiagnosis("网络层归一化启动失败:\n" + result.Message, null);
            }
            catch (Exception ex)
            {
                ShowHotspotDiagnosis("网络层归一化启动失败:\n" + ex.Message, null);
            }
        }

        private void StartTrafficMonitorIfNeeded()
        {
            try
            {
                string detail;
                if (_manager.GetStatus(out detail) != HotspotStatus.Running) return;
                ProxySettings settings = ProxyConfigManager.Load();
                if (settings.MonitorEnabled && (settings.MonitorTtl || settings.MonitorDns))
                {
                    var result = TrafficMonitor.Shared.Start(settings);
                    if (!result.Success) ShowHotspotDiagnosis("核心监控启动失败:\n" + result.Message, null);
                }
            }
            catch (Exception ex)
            {
                ShowHotspotDiagnosis("核心监控启动失败:\n" + ex.Message, null);
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            timerStatus.Stop();
            for (int i = 0; i < 100 && (Volatile.Read(ref _statusBusy) != 0 || Volatile.Read(ref _clientsBusy) != 0); i++)
                Thread.Sleep(20);
            StopRuntimeComponents(true);
            HotspotTiming.EndSession();
        }

        private string StopRuntimeComponents(bool stopHotspot)
        {
            var messages = new List<string>();
            if (stopHotspot)
            {
                try
                {
                    var result = _manager.Stop();
                    messages.Add(result.Success ? result.Message : "热点停止失败: " + result.Message);
                }
                catch (Exception ex)
                {
                    messages.Add("热点停止失败: " + ex.Message);
                }
            }
            try { messages.Add(ProxyProcessManager.Stop()); }
            catch (Exception ex) { messages.Add("代理停止失败: " + ex.Message); }
            try { messages.Add(IPv6Guard.Stop().Message); }
            catch (Exception ex) { messages.Add("IPv6 封禁停止失败: " + ex.Message); }
            try { messages.Add(PortalGuard.Stop().Message); }
            catch (Exception ex) { messages.Add("Portal 封堵停止失败: " + ex.Message); }
            try { messages.Add(OutboundGuard.Stop().Message); }
            catch (Exception ex) { messages.Add("出站封堵停止失败: " + ex.Message); }
            try { messages.Add(TrafficMonitor.Shared.Stop().Message); }
            catch (Exception ex) { messages.Add("核心监控停止失败: " + ex.Message); }
            try { messages.Add(DnsMonitor.Shared.Stop().Message); }
            catch (Exception ex) { messages.Add("DNS 监控停止失败: " + ex.Message); }
            try { messages.Add(NetworkNormalizer.Shared.Stop().Message); }
            catch (Exception ex) { messages.Add("网络层归一化停止失败: " + ex.Message); }
            return string.Join("\n", messages.ToArray());
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            SaveConfig();
        }

        private void btnRefreshClients_Click(object sender, EventArgs e)
        {
            RefreshClients();
        }

        private void chkShowPassword_CheckedChanged(object sender, EventArgs e)
        {
            txtPassword.PasswordChar = chkShowPassword.Checked ? '\0' : '*';
        }

        private static string ModuleStatusText()
        {
            string proxy = ProxyProcessManager.IsRunning() ? "运行" : "未运行";
            string network;
            if (NetworkNormalizer.Shared.IsRunning)
            {
                network = "运行";
            }
            else
            {
                string message = NetworkNormalizer.Shared.LastMessage ?? string.Empty;
                if (message.Contains("未启用")) network = "未运行(未启用)";
                else if (message.Contains("WinDivert")) network = "未运行(缺 WinDivert 文件)";
                else if (message.Contains("管理员")) network = "未运行(需管理员)";
                else if (message.Contains("初始化失败")) network = "未运行(初始化失败)";
                else network = "未运行";
            }
            string monitor = TrafficMonitor.Shared.IsRunning ? "TTL监控运行" : "TTL监控未运行";
            string dns = DnsMonitor.Shared.IsRunning ? "DNS监控运行" : "DNS监控未运行";
            return proxy + " / " + network + " / " + monitor + " / " + dns;
        }

        private void timerStatus_Tick(object sender, EventArgs e)
        {
            if (_shuttingDown) return;
            if (Interlocked.Exchange(ref _statusBusy, 1) != 0) return;
            string ssid = txtSsid.Text;
            string topology = TopologyText();
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                var snapshot = new StatusSnapshot();
                try
                {
                    string detail;
                    snapshot.Status = _manager.GetStatus(out detail);
                    bool isAdmin = HotspotManager.IsAdministrator();
                    string moduleState = ModuleStatusText();
                    snapshot.Running = snapshot.Status == HotspotStatus.Running;
                    snapshot.ClearClients = !snapshot.Running;
                    snapshot.Color = snapshot.Status == HotspotStatus.Running ? Color.Green
                        : snapshot.Status == HotspotStatus.Stopped ? Color.DarkOrange : Color.Red;
                    string failure = snapshot.Status == HotspotStatus.Running || snapshot.Status == HotspotStatus.Stopped
                        ? string.Empty : "\n状态检测失败，可尝试启动/停止";
                    string ssidLine = snapshot.Status == HotspotStatus.Running ? "\nSSID: " + ssid : string.Empty;
                    snapshot.Text = "热点状态: " + detail + ssidLine + "\n拓扑: " + topology + failure + "\n代理/网络层/TTL/DNS: " + moduleState;
                    snapshot.StartEnabled = isAdmin && snapshot.Status != HotspotStatus.Running;
                    snapshot.StopEnabled = isAdmin && snapshot.Status != HotspotStatus.Stopped;
                    if (snapshot.Running) snapshot.Clients = _manager.GetConnectedClients();
                }
                catch
                {
                    snapshot.Status = HotspotStatus.Error;
                    snapshot.Color = Color.Red;
                    snapshot.Text = "热点状态: 状态检测失败，可尝试启动/停止";
                    snapshot.StartEnabled = true;
                    snapshot.StopEnabled = true;
                    snapshot.ClearClients = true;
                }
                try
                {
                    BeginInvoke(new Action(() => ApplyStatusSnapshot(snapshot)));
                }
                catch { }
                finally
                {
                    Interlocked.Exchange(ref _statusBusy, 0);
                }
            });
        }

        private void ApplyStatusSnapshot(StatusSnapshot snapshot)
        {
            if (_shuttingDown || snapshot == null || IsDisposed) return;
            lblStatus.ForeColor = snapshot.Color;
            if (lblStatus.Text != snapshot.Text) lblStatus.Text = snapshot.Text;
            btnStart.Enabled = snapshot.StartEnabled;
            btnStop.Enabled = snapshot.StopEnabled;
            ApplyClients(snapshot.Clients, snapshot.Running);
            if (snapshot.ClearClients) ApplyClients(null, false);
        }
    }
}
