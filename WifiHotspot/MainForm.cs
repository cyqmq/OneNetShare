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
        private int _adaptersBusy;
        private bool _applyingAdapters;
        private bool _shuttingDown;
        private StatusSnapshot _latestSnapshot;

        private enum FeatureHealth
        {
            Ok,
            Warning,
            Error,
            Disabled,
            Info
        }

        private class AdapterSnapshot
        {
            public List<IcsManager.NetworkAdapterInfo> Adapters;
            public List<IcsManager.NetworkAdapterInfo> WiredAdapters;
            public List<IcsManager.NetworkAdapterInfo> WirelessAdapters;
        }

        private class StatusSnapshot
        {
            public HotspotStatus Status;
            public string Detail;
            public string Support;
            public string Text;
            public Color Color;
            public bool StartEnabled;
            public bool StopEnabled;
            public bool Running;
            public bool ClearClients;
            public bool Admin;
            public bool ProxyRunning;
            public bool NetworkRunning;
            public bool MonitorRunning;
            public bool DnsRunning;
            public bool IPv6Running;
            public bool PortalRunning;
            public bool OutboundRunning;
            public string NetworkMessage;
            public ProxySettings Settings;
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
                SetHotspotButtons(false, false);
            }

            LoadConfig();
            RequestAdaptersRefresh();
            LoadFeatureSwitches();
            StartNetworkNormalizer();
            StartTrafficMonitorIfNeeded();
            CheckSupportAndStatus();
            RefreshMonitorLog();
            ShowPage(pageDashboard, navDashboard, "仪表盘", "一眼查看热点、代理、监控和异常");
            timerStatus.Start();
            timerMonitor.Start();
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

        private void RequestAdaptersRefresh()
        {
            if (_shuttingDown || IsDisposed) return;
            if (Interlocked.Exchange(ref _adaptersBusy, 1) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                AdapterSnapshot snapshot = null;
                try
                {
                    var adapters = _manager.GetInternetAdapters() ?? new List<IcsManager.NetworkAdapterInfo>();
                    var wired = new List<IcsManager.NetworkAdapterInfo>();
                    var wireless = new List<IcsManager.NetworkAdapterInfo>();
                    foreach (var adapter in adapters)
                    {
                        if (adapter.InterfaceType == NetworkInterfaceType.Ethernet)
                            wired.Add(adapter);
                        else if (adapter.InterfaceType == NetworkInterfaceType.Wireless80211)
                            wireless.Add(adapter);
                    }
                    if (wired.Count == 0)
                        wired.Add(new IcsManager.NetworkAdapterInfo { Name = "未检测到有线网卡", StatusText = "请先连接有线网络" });
                    if (wireless.Count == 0)
                        wireless.Add(new IcsManager.NetworkAdapterInfo { Name = "未检测到无线网卡", StatusText = "请启用无线网卡" });
                    snapshot = new AdapterSnapshot
                    {
                        Adapters = adapters,
                        WiredAdapters = wired,
                        WirelessAdapters = wireless
                    };
                }
                catch
                {
                    snapshot = new AdapterSnapshot
                    {
                        Adapters = new List<IcsManager.NetworkAdapterInfo>(),
                        WiredAdapters = new List<IcsManager.NetworkAdapterInfo> { new IcsManager.NetworkAdapterInfo { Name = "未检测到有线网卡", StatusText = "检测失败" } },
                        WirelessAdapters = new List<IcsManager.NetworkAdapterInfo> { new IcsManager.NetworkAdapterInfo { Name = "未检测到无线网卡", StatusText = "检测失败" } }
                    };
                }
                try
                {
                    BeginInvoke(new Action(() => ApplyAdapters(snapshot)));
                }
                catch { }
                finally
                {
                    Interlocked.Exchange(ref _adaptersBusy, 0);
                }
            });
        }

        private void ApplyAdapters(AdapterSnapshot snapshot)
        {
            if (_shuttingDown || snapshot == null || IsDisposed) return;
            _applyingAdapters = true;
            try
            {
                _adapters = snapshot.Adapters;
                _wiredAdapters = snapshot.WiredAdapters;
                _wirelessAdapters = snapshot.WirelessAdapters;
                SetTopologyCombo(_savedTopologyMode);
                _adapterSource = IsWiredWifiMode() ? _wiredAdapters : _adapters;
                FillAdapterCombo(cmbAdapter, _adapterSource, _savedAdapterId);
                FillAdapterCombo(cmbWirelessAdapter, _wirelessAdapters, _savedWirelessAdapterId);
                UpdateTopologyUi();
            }
            finally
            {
                _applyingAdapters = false;
            }
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
            if (_applyingAdapters || _adapters == null) return;
            _savedTopologyMode = TopologyMode();
            RequestAdaptersRefresh();
        }

        private int ResolveHotspotInterfaceIndex()
        {
            var hotspot = _manager.GetHostedNetworkAdapter();
            if (hotspot == null) return 0;
            return IcsManager.GetInterfaceIndex(hotspot.Id, hotspot.Name, hotspot.Description);
        }

        private void CheckSupportAndStatus()
        {
            RequestStatusRefresh();
        }

        private void SetHotspotButtons(bool startEnabled, bool stopEnabled)
        {
            btnStart.Enabled = startEnabled;
            btnQuickStart.Enabled = startEnabled;
            btnStop.Enabled = stopEnabled;
            btnQuickStop.Enabled = stopEnabled;
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

            SetHotspotButtons(false, false);
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
            SetHotspotButtons(false, false);
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
            RequestAdaptersRefresh();
            CheckSupportAndStatus();
        }

        private void btnProxy_Click(object sender, EventArgs e)
        {
            using (var form = new ProxySettingsForm())
                form.ShowDialog(this);
            LoadFeatureSwitches();
            CheckSupportAndStatus();
        }

        private void navDashboard_Click(object sender, EventArgs e)
        {
            ShowPage(pageDashboard, navDashboard, "仪表盘", "一眼查看热点、代理、监控和异常");
            RenderDashboard();
        }

        private void navConfig_Click(object sender, EventArgs e)
        {
            ShowPage(pageConfig, navConfig, "热点配置", "设置 SSID、密码、拓扑和上行网卡");
        }

        private void navFeatures_Click(object sender, EventArgs e)
        {
            ShowPage(pageFeatures, navFeatures, "功能开关", "查看并切换代理、防护和监控功能");
            LoadFeatureSwitches();
        }

        private void navMonitor_Click(object sender, EventArgs e)
        {
            ShowPage(pageMonitor, navMonitor, "运行监控", "连接设备、探针事件与详细运行状态");
            RefreshMonitorLog();
        }

        private void btnSideProxy_Click(object sender, EventArgs e)
        {
            btnProxy_Click(sender, e);
        }

        private void btnSideDiagnosis_Click(object sender, EventArgs e)
        {
            var diagnosis = _manager.Diagnose(ModuleStatusText(), null, txtSsid.Text, txtPassword.Text);
            using (var form = new DiagnosisForm(diagnosis))
                form.ShowDialog(this);
        }

        private void btnFeatureAdvanced_Click(object sender, EventArgs e)
        {
            btnProxy_Click(sender, e);
        }

        private void ShowPage(Panel page, Button active, string title, string subtitle)
        {
            pageDashboard.Visible = page == pageDashboard;
            pageConfig.Visible = page == pageConfig;
            pageFeatures.Visible = page == pageFeatures;
            pageMonitor.Visible = page == pageMonitor;
            page.BringToFront();
            lblHeaderTitle.Text = title;
            lblHeaderSubtitle.Text = subtitle;
            Button[] navButtons = { navDashboard, navConfig, navFeatures, navMonitor, btnSideProxy };
            foreach (Button button in navButtons)
            {
                bool selected = button == active;
                button.BackColor = selected ? Color.FromArgb(37, 99, 235) : Color.FromArgb(30, 41, 59);
                button.ForeColor = selected ? Color.White : Color.FromArgb(226, 232, 240);
            }
        }

        private void LoadFeatureSwitches()
        {
            ProxySettings settings = ProxyConfigManager.Load();
            chkProxyEnabled.Checked = settings.Enabled && settings.AutoStartProxy;
            chkHeaders.Checked = settings.Headers;
            chkDeviceForge.Checked = settings.DeviceForgeEnabled;
            chkNetworkNormalizer.Checked = settings.NetworkEnabled;
            chkTun.Checked = settings.TunEnabled;
            chkIPv6Guard.Checked = settings.IPv6GuardEnabled;
            chkPortalBlock.Checked = settings.PortalBlockEnabled;
            chkOutboundGuard.Checked = settings.OutboundGuardEnabled;
            chkMonitorTtl.Checked = settings.MonitorTtl;
            chkMonitorDns.Checked = settings.MonitorDns;
            RenderDashboard();
        }

        private void btnFeatureSave_Click(object sender, EventArgs e)
        {
            ProxySettings before = ProxyConfigManager.Load();
            ProxySettings settings = ProxyConfigManager.Load();
            settings.Enabled = chkProxyEnabled.Checked;
            settings.AutoStartProxy = chkProxyEnabled.Checked;
            settings.Headers = chkHeaders.Checked;
            settings.DeviceForgeEnabled = chkDeviceForge.Checked;
            settings.NetworkEnabled = chkNetworkNormalizer.Checked;
            settings.TunEnabled = chkTun.Checked;
            if (settings.TunEnabled) settings.NetworkEnabled = false;
            settings.IPv6GuardEnabled = chkIPv6Guard.Checked;
            settings.PortalBlockEnabled = chkPortalBlock.Checked;
            settings.OutboundGuardEnabled = chkOutboundGuard.Checked;
            settings.MonitorTtl = chkMonitorTtl.Checked;
            settings.MonitorDns = chkMonitorDns.Checked;
            settings.MonitorEnabled = settings.MonitorTtl || settings.MonitorDns;
            ProxyConfigManager.Save(settings);
            string result = ApplyFeatureRuntime(settings, before);
            LoadFeatureSwitches();
            CheckSupportAndStatus();
            MessageBox.Show(this, "功能开关已保存。\n" + result, "功能开关", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private string ApplyFeatureRuntime(ProxySettings settings, ProxySettings before)
        {
            var messages = new List<string>();
            string detail;
            HotspotStatus hotspotStatus = _manager.GetStatus(out detail);
            bool hotspotRunning = hotspotStatus == HotspotStatus.Running;
            bool proxyRunning = ProxyProcessManager.IsRunning();

            if (settings.Enabled && proxyRunning)
            {
                messages.Add(ProxyProcessManager.Stop());
                messages.Add(ProxyProcessManager.Start(ProxyConfigManager.DefaultConfigPath));
            }
            else if (!settings.Enabled && proxyRunning)
            {
                messages.Add(ProxyProcessManager.Stop());
            }

            bool wantNetwork = settings.NetworkEnabled && !settings.TunEnabled && !IsWiredWifiMode();
            if (wantNetwork && !NetworkNormalizer.Shared.IsRunning)
                messages.Add(NetworkNormalizer.Shared.Start(settings).Message);
            else if (!wantNetwork && NetworkNormalizer.Shared.IsRunning)
                messages.Add(NetworkNormalizer.Shared.Stop().Message);

            bool wantMonitor = settings.MonitorEnabled && (settings.MonitorTtl || settings.MonitorDns);
            if (!wantMonitor && (TrafficMonitor.Shared.IsRunning || DnsMonitor.Shared.IsRunning))
                messages.Add(TrafficMonitor.Shared.Stop().Message);
            else if (wantMonitor && hotspotRunning && !TrafficMonitor.Shared.IsRunning)
                messages.Add(TrafficMonitor.Shared.Start(settings).Message);

            if (!settings.IPv6GuardEnabled && IPv6Guard.IsRunning)
                messages.Add(IPv6Guard.Stop().Message);
            else if (settings.IPv6GuardEnabled && hotspotRunning && !IPv6Guard.IsRunning)
            {
                if (ProxyDiagnostics.FindHotspotGlobalIPv6().Count > 0)
                    messages.Add(IPv6Guard.Start(settings.IPv6Policy, ResolveHotspotInterfaceIndex()).Message);
                else
                    messages.Add("未检测到公网 IPv6，IPv6 封禁按设计暂不启用。");
            }

            if (!settings.PortalBlockEnabled && PortalGuard.IsRunning)
                messages.Add(PortalGuard.Stop().Message);
            else if (settings.PortalBlockEnabled && hotspotRunning && !PortalGuard.IsRunning)
            {
                if (string.IsNullOrWhiteSpace(settings.PortalPrimaryIP) && string.IsNullOrWhiteSpace(settings.PortalSecondaryIP))
                    messages.Add("Portal 封堵已开启但未配置主/备 IP，请在高级代理设置中填写。");
                else
                    messages.Add(PortalGuard.Start(settings.PortalPrimaryIP, settings.PortalSecondaryIP, ResolveHotspotInterfaceIndex()).Message);
            }

            if (!settings.OutboundGuardEnabled && OutboundGuard.IsRunning)
                messages.Add(OutboundGuard.Stop().Message);
            else if (settings.OutboundGuardEnabled && hotspotRunning && !OutboundGuard.IsRunning)
            {
                if (settings.TunEnabled)
                    messages.Add("全局 TUN 模式由代理层执行出站封堵。");
                else
                    messages.Add(OutboundGuard.Start(settings, ResolveHotspotInterfaceIndex()).Message);
            }

            if (before == null) messages.Add("配置已更新。");
            return string.Join("\n", messages.ToArray());
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
            timerMonitor.Stop();
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

        private void RenderDashboard()
        {
            if (_shuttingDown || IsDisposed) return;
            StatusSnapshot snapshot = _latestSnapshot;
            if (snapshot == null || snapshot.Settings == null) return;

            ProxySettings settings = snapshot.Settings;
            HotspotStatus effective = snapshot.Status;
            bool running = snapshot.Running;
            var issues = new List<string>();
            RefreshFeatureStates(snapshot, issues);

            int clientCount = snapshot.Clients != null ? snapshot.Clients.Count : 0;
            lblHeroTitle.Text = running ? "热点运行中" : effective == HotspotStatus.Stopped ? "热点已停止" : "热点状态异常";
            lblHeroTitle.ForeColor = running ? Color.FromArgb(22, 101, 52) : effective == HotspotStatus.Stopped ? Color.FromArgb(180, 83, 9) : Color.FromArgb(153, 27, 27);
            lblHeroStatus.Text = "状态: " + snapshot.Detail + "\n会话: " + HotspotTiming.StatusText + "    管理员权限: " + (snapshot.Admin ? "已获取" : "缺失");

            SetMetric(lblMetricHotspot, running ? "运行中" : effective == HotspotStatus.Stopped ? "已停止" : "异常",
                running ? FeatureHealth.Ok : effective == HotspotStatus.Stopped ? FeatureHealth.Warning : FeatureHealth.Error);
            SetMetric(lblMetricProxy, settings.Enabled ? (snapshot.ProxyRunning ? "运行中" : "待启动") : "已关闭",
                settings.Enabled ? (snapshot.ProxyRunning ? FeatureHealth.Ok : (running ? FeatureHealth.Error : FeatureHealth.Info)) : FeatureHealth.Disabled);
            bool monitorEnabled = settings.MonitorEnabled && (settings.MonitorTtl || settings.MonitorDns);
            bool monitorRunning = snapshot.MonitorRunning || snapshot.DnsRunning;
            SetMetric(lblMetricMonitor, !monitorEnabled ? "已关闭" : monitorRunning ? "运行中" : (running ? "未运行" : "待启动"),
                !monitorEnabled ? FeatureHealth.Disabled : monitorRunning ? FeatureHealth.Ok : (running ? FeatureHealth.Error : FeatureHealth.Info));
            SetMetric(lblMetricClients, running ? clientCount.ToString() : "—", running ? FeatureHealth.Info : FeatureHealth.Disabled);
            SetMetric(lblMetricIssues, issues.Count.ToString(), issues.Count == 0 ? FeatureHealth.Ok : FeatureHealth.Warning);

            lblSessionInfo.Text = string.Format("SSID: {0}\n拓扑: {1}\n线程: {2}\n刷新: 后台缓存",
                string.IsNullOrWhiteSpace(txtSsid.Text) ? "—" : txtSsid.Text,
                TopologyText(),
                snapshot.Running ? "热点 / 代理 / 监控" : "待热点启动");

            lblAlerts.Text = issues.Count == 0
                ? "未发现异常。所有已启用的功能均在预期状态；未启用的功能不计入异常。"
                : string.Join("\n", issues.ToArray());
            lblSidebarStatus.Text = "系统状态\n" + (issues.Count == 0 ? (running ? "运行正常" : "待启动热点") : issues.Count + " 项需要关注");
            lblSidebarStatus.ForeColor = issues.Count == 0 ? Color.FromArgb(134, 239, 172) : Color.FromArgb(253, 224, 71);
        }

        private void RefreshFeatureStates(StatusSnapshot snapshot, List<string> issues)
        {
            ProxySettings settings = snapshot.Settings;
            bool admin = snapshot.Admin;
            bool hotspotRunning = snapshot.Status == HotspotStatus.Running;
            bool proxyRunning = snapshot.ProxyRunning;
            bool monitorRunning = snapshot.MonitorRunning;
            bool dnsRunning = snapshot.DnsRunning;

            if (!settings.Enabled)
                SetStatePair(lblProxyState, lblDashProxy, proxyRunning ? "运行中·开关已关" : "已关闭", proxyRunning ? FeatureHealth.Warning : FeatureHealth.Disabled);
            else if (!hotspotRunning && !proxyRunning)
                SetStatePair(lblProxyState, lblDashProxy, "待热点启动", FeatureHealth.Info);
            else if (proxyRunning)
                SetStatePair(lblProxyState, lblDashProxy, "运行中", FeatureHealth.Ok);
            else
            {
                SetStatePair(lblProxyState, lblDashProxy, "未运行", FeatureHealth.Error);
                issues.Add("代理服务已启用但未运行。");
            }

            bool headerOn = settings.Enabled && settings.DeviceForgeEnabled && settings.Headers;
            SetStatePair(lblHeaderState, lblDashHeader, !headerOn ? "已关闭" : proxyRunning ? "已启用" : "已配置·待代理运行",
                !headerOn ? FeatureHealth.Disabled : proxyRunning ? FeatureHealth.Ok : FeatureHealth.Info);

            SetStatePair(lblDeviceForgeState, null, !settings.DeviceForgeEnabled ? "已关闭" : proxyRunning ? "已启用" : "已配置·待代理运行",
                !settings.DeviceForgeEnabled ? FeatureHealth.Disabled : proxyRunning ? FeatureHealth.Ok : FeatureHealth.Info);

            if (settings.TunEnabled)
                SetStatePair(lblNetworkState, lblDashNetwork, "由 TUN 接管", FeatureHealth.Info);
            else if (IsWiredWifiMode())
                SetStatePair(lblNetworkState, lblDashNetwork, "有线模式跳过", FeatureHealth.Info);
            else if (!settings.NetworkEnabled)
                SetStatePair(lblNetworkState, lblDashNetwork, "已关闭", FeatureHealth.Disabled);
            else if (snapshot.NetworkRunning)
                SetStatePair(lblNetworkState, lblDashNetwork, "运行中", FeatureHealth.Ok);
            else if (!admin)
            {
                SetStatePair(lblNetworkState, lblDashNetwork, "缺少管理员权限", FeatureHealth.Error);
                issues.Add("网络层归一化缺少管理员权限。");
            }
            else
            {
                SetStatePair(lblNetworkState, lblDashNetwork, "未运行", FeatureHealth.Error);
                issues.Add("网络层归一化已启用但未运行。");
            }

            if (!settings.TunEnabled)
                SetStatePair(lblTunState, lblDashTun, "已关闭", FeatureHealth.Disabled);
            else if (!proxyRunning)
                SetStatePair(lblTunState, lblDashTun, "待代理运行", FeatureHealth.Info);
            else
                SetStatePair(lblTunState, lblDashTun, "已启用", FeatureHealth.Ok);

            if (!settings.IPv6GuardEnabled)
                SetStatePair(lblIPv6State, lblDashGuards, "已关闭", FeatureHealth.Disabled);
            else if (snapshot.IPv6Running)
                SetStatePair(lblIPv6State, lblDashGuards, "封禁运行中", FeatureHealth.Ok);
            else if (hotspotRunning)
                SetStatePair(lblIPv6State, lblDashGuards, "无需封禁", FeatureHealth.Info);
            else
                SetStatePair(lblIPv6State, lblDashGuards, "待热点检测", FeatureHealth.Info);

            bool portalIpMissing = string.IsNullOrWhiteSpace(settings.PortalPrimaryIP) && string.IsNullOrWhiteSpace(settings.PortalSecondaryIP);
            if (!settings.PortalBlockEnabled)
                SetStatePair(lblPortalState, null, "已关闭", FeatureHealth.Disabled);
            else if (portalIpMissing)
            {
                SetStatePair(lblPortalState, null, "缺少门户 IP", FeatureHealth.Error);
                issues.Add("Portal 封堵已开启但未配置主/备 IP。");
            }
            else if (snapshot.PortalRunning)
                SetStatePair(lblPortalState, null, "封堵运行中", FeatureHealth.Ok);
            else if (hotspotRunning)
            {
                SetStatePair(lblPortalState, null, "未运行", FeatureHealth.Error);
                issues.Add("Portal 封堵已启用但未运行。");
            }
            else
                SetStatePair(lblPortalState, null, "待热点启动", FeatureHealth.Info);

            if (!settings.OutboundGuardEnabled)
                SetStatePair(lblOutboundState, null, "已关闭", FeatureHealth.Disabled);
            else if (settings.TunEnabled)
                SetStatePair(lblOutboundState, null, "由 TUN 执行", FeatureHealth.Info);
            else if (snapshot.OutboundRunning)
                SetStatePair(lblOutboundState, null, "封堵运行中", FeatureHealth.Ok);
            else if (hotspotRunning)
            {
                SetStatePair(lblOutboundState, null, "未运行", FeatureHealth.Error);
                issues.Add("出站封堵已启用但未运行。");
            }
            else
                SetStatePair(lblOutboundState, null, "待热点启动", FeatureHealth.Info);

            if (!settings.MonitorEnabled || !settings.MonitorTtl)
                SetStatePair(lblMonitorTtlState, lblDashMonitor, "已关闭", FeatureHealth.Disabled);
            else if (monitorRunning)
                SetStatePair(lblMonitorTtlState, lblDashMonitor, "监控运行中", FeatureHealth.Ok);
            else if (hotspotRunning)
            {
                SetStatePair(lblMonitorTtlState, lblDashMonitor, "未运行", FeatureHealth.Error);
                issues.Add("TTL 监控已启用但未运行。");
            }
            else
                SetStatePair(lblMonitorTtlState, lblDashMonitor, "待热点启动", FeatureHealth.Info);

            if (!settings.MonitorEnabled || !settings.MonitorDns)
                SetStatePair(lblMonitorDnsState, null, "已关闭", FeatureHealth.Disabled);
            else if (dnsRunning)
                SetStatePair(lblMonitorDnsState, null, "监控运行中", FeatureHealth.Ok);
            else if (hotspotRunning)
            {
                SetStatePair(lblMonitorDnsState, null, "未运行", FeatureHealth.Error);
                issues.Add("DNS 监控已启用但未运行。");
            }
            else
                SetStatePair(lblMonitorDnsState, null, "待热点启动", FeatureHealth.Info);
        }

        private void SetStatePair(Label featureLabel, Label dashboardLabel, string text, FeatureHealth health)
        {
            SetState(featureLabel, text, health);
            if (dashboardLabel != null) SetState(dashboardLabel, text, health);
        }

        private void SetState(Label label, string text, FeatureHealth health)
        {
            if (label == null) return;
            label.Text = text;
            label.ForeColor = HealthColor(health);
        }

        private void SetMetric(Label label, string text, FeatureHealth health)
        {
            if (label == null) return;
            label.Text = text;
            label.ForeColor = HealthColor(health);
        }

        private static Color HealthColor(FeatureHealth health)
        {
            switch (health)
            {
                case FeatureHealth.Ok: return Color.FromArgb(22, 101, 52);
                case FeatureHealth.Warning: return Color.FromArgb(180, 83, 9);
                case FeatureHealth.Error: return Color.FromArgb(153, 27, 27);
                case FeatureHealth.Disabled: return Color.FromArgb(100, 116, 139);
                default: return Color.FromArgb(37, 99, 235);
            }
        }

        private void timerMonitor_Tick(object sender, EventArgs e)
        {
            if (_shuttingDown) return;
            RefreshMonitorLog();
        }

        private void RefreshMonitorLog()
        {
            if (_shuttingDown || IsDisposed) return;
            string path = ProxyProcessManager.ProbeLogPath;
            if (!File.Exists(path))
            {
                if (txtMonitorLog.Text.Length > 0) txtMonitorLog.Clear();
                return;
            }
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long start = Math.Max(0L, stream.Length - 12000L);
                    stream.Seek(start, SeekOrigin.Begin);
                    byte[] buffer = new byte[stream.Length - start];
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int count = stream.Read(buffer, read, buffer.Length - read);
                        if (count == 0) break;
                        read += count;
                    }
                    string text = Encoding.UTF8.GetString(buffer, 0, read);
                    string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    int from = Math.Max(0, lines.Length - 160);
                    string display = string.Join(Environment.NewLine, lines, from, lines.Length - from);
                    if (txtMonitorLog.Text == display) return;
                    bool atBottom = txtMonitorLog.SelectionStart >= txtMonitorLog.TextLength - 2;
                    txtMonitorLog.Text = display;
                    if (atBottom)
                    {
                        txtMonitorLog.SelectionStart = txtMonitorLog.TextLength;
                        txtMonitorLog.ScrollToCaret();
                    }
                }
            }
            catch { }
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
            RequestStatusRefresh();
        }

        private void RequestStatusRefresh()
        {
            if (_shuttingDown || IsDisposed) return;
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
                    snapshot.Detail = detail;
                    snapshot.Support = _manager.CheckSupport().Message;
                    snapshot.Settings = ProxyConfigManager.Load();
                    snapshot.Admin = HotspotManager.IsAdministrator();
                    snapshot.ProxyRunning = ProxyProcessManager.IsRunning();
                    snapshot.NetworkRunning = NetworkNormalizer.Shared.IsRunning;
                    snapshot.NetworkMessage = NetworkNormalizer.Shared.LastMessage ?? string.Empty;
                    snapshot.MonitorRunning = TrafficMonitor.Shared.IsRunning;
                    snapshot.DnsRunning = DnsMonitor.Shared.IsRunning;
                    snapshot.IPv6Running = IPv6Guard.IsRunning;
                    snapshot.PortalRunning = PortalGuard.IsRunning;
                    snapshot.OutboundRunning = OutboundGuard.IsRunning;
                    snapshot.Running = snapshot.Status == HotspotStatus.Running;
                    snapshot.ClearClients = !snapshot.Running;
                    snapshot.Color = snapshot.Status == HotspotStatus.Running ? Color.Green
                        : snapshot.Status == HotspotStatus.Stopped ? Color.DarkOrange : Color.Red;
                    string failure = snapshot.Status == HotspotStatus.Running || snapshot.Status == HotspotStatus.Stopped
                        ? string.Empty : "\n状态检测失败，可尝试启动/停止";
                    string ssidLine = snapshot.Status == HotspotStatus.Running ? "\nSSID: " + ssid : string.Empty;
                    string moduleState = BuildModuleStatusText(snapshot);
                    snapshot.Text = "热点状态: " + detail + ssidLine + "\n拓扑: " + topology
                        + "\n支持检测: " + snapshot.Support + failure + "\n代理/网络层/TTL/DNS: " + moduleState;
                    snapshot.StartEnabled = snapshot.Admin && snapshot.Status != HotspotStatus.Running;
                    snapshot.StopEnabled = snapshot.Admin && snapshot.Status != HotspotStatus.Stopped;
                    if (snapshot.Running) snapshot.Clients = _manager.GetConnectedClients();
                }
                catch
                {
                    snapshot.Status = HotspotStatus.Error;
                    snapshot.Detail = "状态检测失败，可尝试启动/停止";
                    snapshot.Support = "检测失败";
                    snapshot.Settings = new ProxySettings();
                    snapshot.Color = Color.Red;
                    snapshot.Text = "热点状态: " + snapshot.Detail;
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
            _latestSnapshot = snapshot;
            lblStatus.ForeColor = snapshot.Color;
            if (lblStatus.Text != snapshot.Text) lblStatus.Text = snapshot.Text;
            SetHotspotButtons(snapshot.StartEnabled, snapshot.StopEnabled);
            RenderDashboard();
            ApplyClients(snapshot.Clients, snapshot.Running);
            if (snapshot.ClearClients) ApplyClients(null, false);
        }

        private static string BuildModuleStatusText(StatusSnapshot snapshot)
        {
            string proxy = snapshot.ProxyRunning ? "运行" : "未运行";
            string network;
            if (snapshot.NetworkRunning)
            {
                network = "运行";
            }
            else
            {
                string message = snapshot.NetworkMessage ?? string.Empty;
                if (message.Contains("未启用")) network = "未运行(未启用)";
                else if (message.Contains("WinDivert")) network = "未运行(缺 WinDivert 文件)";
                else if (message.Contains("管理员")) network = "未运行(需管理员)";
                else if (message.Contains("初始化失败")) network = "未运行(初始化失败)";
                else network = "未运行";
            }
            string monitor = snapshot.MonitorRunning ? "TTL监控运行" : "TTL监控未运行";
            string dns = snapshot.DnsRunning ? "DNS监控运行" : "DNS监控未运行";
            return proxy + " / " + network + " / " + monitor + " / " + dns;
        }
    }
}
