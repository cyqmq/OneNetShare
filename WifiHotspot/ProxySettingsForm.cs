using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WifiHotspot
{
    public class ProxySettingsForm : Form
    {
        private readonly string _configPath;
        private readonly CheckBox chkEnabled;
        private readonly CheckBox chkDeviceForge;
        private readonly CheckBox chkHeaders;
        private readonly CheckBox chkUdp;
        private readonly CheckBox chkDns;
        private readonly CheckBox chkDnsEmbedded;
        private readonly Button btnDetectDns;
        private readonly TextBox txtDnsServers;
        private readonly CheckBox chkDnsStripEdns;
        private readonly CheckBox chkDnsForceTcp;
        private readonly NumericUpDown numDnsMinTtl;
        private readonly NumericUpDown numDnsMaxTtl;
        private readonly CheckBox chkNtp;
        private readonly CheckBox chkMdns;
        private readonly CheckBox chkDhcp;
        private readonly CheckBox chkCaptive;
        private readonly CheckBox chkAutoStart;
        private readonly CheckBox chkNetwork;
        private readonly CheckBox chkIPID;
        private readonly CheckBox chkTcpOptions;
        private readonly CheckBox chkForward;
        private readonly CheckBox chkMtuClamp;
        private readonly NumericUpDown numMssClamp;
        private readonly CheckBox chkTun;
        private readonly CheckBox chkTunRoute;
        private readonly NumericUpDown numTTL;
        private readonly Label lblTtlHint;
        private bool _updatingTtlFromProfile;
        private readonly NumericUpDown numPrefix;
        private readonly TextBox txtTunAdapter;
        private readonly TextBox txtTunIP;
        private readonly TextBox txtTunGateway;
        private readonly ComboBox cmbTunUpstream;
        private readonly ComboBox cmbProfile;
        private readonly ComboBox cmbUaPreset;
        private readonly ComboBox cmbIpv6Policy;
        private readonly CheckBox chkIpv6Guard;
        private readonly CheckBox chkPortalBlock;
        private readonly TextBox txtPortalPrimary;
        private readonly TextBox txtPortalSecondary;
        private readonly CheckBox chkMonitorEnabled;
        private readonly CheckBox chkMonitorTtl;
        private readonly CheckBox chkMonitorUa;
        private readonly CheckBox chkMonitorTls;
        private readonly CheckBox chkMonitorDns;
        private readonly Label lblMonitorStatus;
        private readonly TextBox txtProbeLog;
        private readonly System.Windows.Forms.Timer probeTimer;
        private long _probeLogOffset;
        private readonly Label lblResult;

        public ProxySettingsForm()
        {
            _configPath = ProxyConfigManager.DefaultConfigPath;
            ProxySettings settings = ProxyConfigManager.Load(_configPath);

            Text = "代理功能开关";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(680, 760);
            Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);

            var content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, AutoScrollMinSize = new Size(0, 1120) };
            var implemented = new GroupBox { Text = "已实现功能", Location = new Point(12, 12), Size = new Size(640, 545) };
            var monitorGroup = new GroupBox { Text = "核心监控（只读，不改包）", Location = new Point(12, 560), Size = new Size(640, 125) };
            var missing = new GroupBox { Text = "未实现功能", Location = new Point(12, 695), Size = new Size(640, 100) };
            var note = new Label { Location = new Point(15, 505), Size = new Size(600, 30), ForeColor = Color.Gray };
            note.Text = "内嵌 DNS 在全局 TUN / Linux redirect 的 UDP:53 拦截路径生效，先查本地缓存，未命中时经出口网卡 DNS 上游查询；ICS 原生模式下 53 由 Windows ICS 占用。WinDivert 只读监控 UDP:53 验证“手机是否发出”和“电脑是否转发”。TUN 与 WinDivert 网络层归一化互斥。";

            var lblMode = new Label { Location = new Point(15, 28), AutoSize = true, Text = "运行模式: forward (Windows)" };
            var lblProfile = new Label { Location = new Point(160, 28), AutoSize = true, Text = "默认设备指纹:" };
            cmbProfile = new ComboBox { Location = new Point(260, 25), Size = new Size(220, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (string profile in ProxyConfigManager.GetProfiles(_configPath)) cmbProfile.Items.Add(profile);
            if (!cmbProfile.Items.Contains(settings.Profile)) cmbProfile.Items.Add(settings.Profile);
            cmbProfile.SelectedItem = settings.Profile;
            int initialTTL = ProxyConfigManager.RecommendedTTL(settings.Profile);

            chkEnabled = NewCheck("HTTP 代理与 Header 改写总开关", settings.Enabled, 15, 62);
            chkDeviceForge = NewCheck("设备伪装模块总开关", settings.DeviceForgeEnabled, 330, 62);
            chkHeaders = NewCheck("HTTP Header 指纹统一", settings.Headers, 15, 92);
            chkCaptive = NewCheck("Captive 门户检测处理", settings.Captive, 330, 92);
            chkUdp = NewCheck("UDP 会话重建 (需开启 Wintun)", settings.Udp, 15, 122);
            chkDns = NewCheck("DNS 规范化 (需开启 Wintun)", settings.Dns, 330, 122);
            var lblDnsServers = new Label { Location = new Point(330, 150), AutoSize = true, Text = "上游DNS:" };
            txtDnsServers = NewText(settings.DnsUpstreamServers, 395, 147, 225);
            chkDnsStripEdns = NewCheck("去EDNS", settings.DnsStripEdns, 330, 174);
            chkDnsForceTcp = NewCheck("强制TCP", settings.DnsForceTcp, 410, 174);
            var lblDnsMin = new Label { Location = new Point(490, 177), AutoSize = true, Text = "TTL:" };
            numDnsMinTtl = new NumericUpDown { Location = new Point(522, 174), Size = new Size(45, 23), Minimum = 1, Maximum = 86400, Value = Math.Max(1, Math.Min(86400, settings.DnsMinTtl <= 0 ? 60 : settings.DnsMinTtl)) };
            var lblDnsMax = new Label { Location = new Point(569, 177), AutoSize = true, Text = "-" };
            numDnsMaxTtl = new NumericUpDown { Location = new Point(579, 174), Size = new Size(56, 23), Minimum = 1, Maximum = 86400, Value = Math.Max(1, Math.Min(86400, settings.DnsMaxTtl <= 0 ? 86400 : settings.DnsMaxTtl)) };
            chkNtp = NewCheck("NTP 规范化 (需开启 Wintun)", settings.Ntp, 15, 152);
            chkMdns = NewCheck("mDNS 伪造 (需开启 Wintun)", settings.Mdns, 330, 202);
            chkDhcp = NewCheck("DHCP 伪造 (需开启 Wintun)", settings.Dhcp, 15, 182);
            chkAutoStart = NewCheck("随热点自动启动/停止代理", settings.AutoStartProxy, 15, 202);
            chkNetwork = NewCheck("网络层指纹归一化 (WinDivert)", settings.NetworkEnabled, 15, 215);
            var lblTTL = new Label { Location = new Point(300, 218), AutoSize = true, Text = "强制 TTL:" };
            numTTL = new NumericUpDown { Location = new Point(360, 215), Size = new Size(65, 23), Minimum = 1, Maximum = 255, Value = initialTTL };
            lblTtlHint = new Label { Location = new Point(435, 212), Size = new Size(190, 32), ForeColor = Color.DarkGreen };
            chkIPID = NewCheck("IPID 归一化", settings.NetworkIPID, 15, 245);
            chkTcpOptions = NewCheck("TCP SYN 选项归一化", settings.NetworkTCPOptions, 200, 245);
            chkForward = NewCheck("同时接管转发流量", settings.NetworkForward, 430, 245);
            chkMtuClamp = NewCheck("MTU/MSS 钳制 (自动降级)", settings.MtuClampEnabled, 15, 275);
            var lblMss = new Label { Location = new Point(190, 278), AutoSize = true, Text = "MSS:" };
            numMssClamp = new NumericUpDown { Location = new Point(230, 275), Size = new Size(65, 23), Minimum = 576, Maximum = 1460, Value = settings.MssClamp > 0 ? settings.MssClamp : 1460, Enabled = settings.MtuClampEnabled };
            var lblMssHint = new Label { Location = new Point(305, 274), Size = new Size(320, 30), ForeColor = Color.Gray };
            lblMssHint.Text = "网站握手异常时开启；1460 偏大，建议先试 1360 或 1280。";
            chkTun = NewCheck("全局 TUN 接管 (Wintun)", settings.TunEnabled, 15, 305);
            chkTunRoute = NewCheck("接管默认路由与 DNS", settings.TunRoute, 200, 305);
            var lblAdapter = new Label { Location = new Point(15, 337), AutoSize = true, Text = "虚拟网卡:" };
            txtTunAdapter = NewText(settings.TunAdapter, 95, 334, 205);
            var lblUpstream = new Label { Location = new Point(315, 337), AutoSize = true, Text = "出口网卡:" };
            cmbTunUpstream = new ComboBox { Location = new Point(385, 334), Size = new Size(235, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            LoadUpstreamInterfaces(settings.TunUpstreamInterface);
            var lblTunIP = new Label { Location = new Point(15, 367), AutoSize = true, Text = "TUN IP:" };
            txtTunIP = NewText(settings.TunIP, 95, 364, 105);
            var lblGateway = new Label { Location = new Point(220, 367), AutoSize = true, Text = "网关:" };
            txtTunGateway = NewText(settings.TunGatewayIP, 270, 364, 105);
            var lblPrefix = new Label { Location = new Point(395, 367), AutoSize = true, Text = "前缀:" };
            numPrefix = new NumericUpDown { Location = new Point(440, 364), Size = new Size(55, 23), Minimum = 8, Maximum = 30, Value = settings.TunPrefixLen <= 0 ? 24 : settings.TunPrefixLen };
            var lblUserAgent = new Label { Location = new Point(15, 395), AutoSize = true, Text = "UA 预设:" };
            cmbUaPreset = new ComboBox { Location = new Point(95, 392), Size = new Size(450, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            var uaPresets = ProxyConfigManager.GetUAPresets();
            var uaPresetList = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, string> item in uaPresets) uaPresetList.Add(item);
            cmbUaPreset.DataSource = uaPresetList;
            cmbUaPreset.DisplayMember = "Value";
            cmbUaPreset.ValueMember = "Key";
            cmbUaPreset.SelectedValue = uaPresets.ContainsKey(settings.UAPreset) ? settings.UAPreset : "chrome-158-windows";
            var lblUserAgentHint = new Label { Location = new Point(550, 390), Size = new Size(85, 32), ForeColor = Color.Gray, Text = "内置 UA 库，自动对齐 Client Hints" };
            chkIpv6Guard = NewCheck("启用 IPv6 转发封禁", settings.IPv6GuardEnabled, 15, 423);
            var lblIPv6Policy = new Label { Location = new Point(170, 426), AutoSize = true, Text = "策略:" };
            cmbIpv6Policy = new ComboBox { Location = new Point(210, 423), Size = new Size(335, 23), DropDownStyle = ComboBoxStyle.DropDownList, Enabled = settings.IPv6GuardEnabled };
            var ipv6Policies = ProxyConfigManager.GetIPv6Policies();
            var ipv6PolicyList = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, string> item in ipv6Policies) ipv6PolicyList.Add(item);
            cmbIpv6Policy.DataSource = ipv6PolicyList;
            cmbIpv6Policy.DisplayMember = "Value";
            cmbIpv6Policy.ValueMember = "Key";
            cmbIpv6Policy.SelectedValue = ipv6Policies.ContainsKey(settings.IPv6Policy) ? settings.IPv6Policy : "block";
            var lblIPv6Hint = new Label { Location = new Point(550, 420), Size = new Size(85, 32), ForeColor = Color.DarkRed, Text = "有 GUA 时建议封禁" };
            chkPortalBlock = NewCheck("封堵认证门户主/备 IP（禁手机后台弹窗）", settings.PortalBlockEnabled, 15, 450);
            var lblPortalPrimary = new Label { Location = new Point(300, 453), AutoSize = true, Text = "主IP:" };
            txtPortalPrimary = NewText(settings.PortalPrimaryIP, 340, 450, 110);
            var lblPortalSecondary = new Label { Location = new Point(460, 453), AutoSize = true, Text = "备IP:" };
            txtPortalSecondary = NewText(settings.PortalSecondaryIP, 500, 450, 120);
            chkDnsEmbedded = NewCheck("内嵌 DNS 服务+缓存（手机查询→缓存→物理网卡 DNS）", settings.DnsEmbedded, 15, 478);
            btnDetectDns = NewButton("读取出口 DNS", 130);
            btnDetectDns.Location = new Point(340, 475);
            btnDetectDns.Click += DetectDnsServers;

            chkMonitorEnabled = NewCheck("核心监控总开关", settings.MonitorEnabled, 15, 24);
            chkMonitorTtl = NewCheck("TTL 跳数变化 (WinDivert sniff)", settings.MonitorTtl, 155, 24);
            chkMonitorUa = NewCheck("HTTP UA 记录/分析", settings.MonitorUa, 370, 24);
            chkMonitorTls = NewCheck("TLS SNI/JA3 记录", settings.MonitorTls, 510, 24);
            chkMonitorDns = NewCheck("UDP:53 到达/转发验证", settings.MonitorDns, 15, 58);
            var btnToggleMonitor = NewButton("启动/停止监控", 108);
            btnToggleMonitor.Location = new Point(510, 58);
            btnToggleMonitor.Click += ToggleMonitor;
            lblMonitorStatus = new Label { Location = new Point(155, 58), Size = new Size(340, 52), ForeColor = Color.DarkBlue };
            Action updateMonitorControls = delegate
            {
                bool enabled = chkMonitorEnabled.Checked;
                chkMonitorTtl.Enabled = enabled;
                chkMonitorUa.Enabled = enabled;
                chkMonitorTls.Enabled = enabled;
                chkMonitorDns.Enabled = enabled;
                btnToggleMonitor.Enabled = enabled;
            };
            chkMonitorEnabled.CheckedChanged += delegate { updateMonitorControls(); };
            updateMonitorControls();

            chkTun.CheckedChanged += delegate { chkNetwork.Enabled = !chkTun.Checked; };
            chkNetwork.Enabled = !settings.TunEnabled;
            chkIpv6Guard.CheckedChanged += delegate { cmbIpv6Policy.Enabled = chkIpv6Guard.Checked; };
            cmbIpv6Policy.Enabled = settings.IPv6GuardEnabled;
            chkMtuClamp.CheckedChanged += delegate { numMssClamp.Enabled = chkMtuClamp.Checked; };
            cmbProfile.SelectedIndexChanged += delegate { ApplyProfileTTL(); };
            numTTL.ValueChanged += delegate { if (!_updatingTtlFromProfile) UpdateTtlHint(); };
            UpdateTtlHint();

            implemented.Controls.Add(lblMode);
            implemented.Controls.Add(lblProfile);
            implemented.Controls.Add(cmbProfile);
            implemented.Controls.Add(chkEnabled);
            implemented.Controls.Add(chkDeviceForge);
            implemented.Controls.Add(chkHeaders);
            implemented.Controls.Add(chkCaptive);
            implemented.Controls.Add(chkUdp);
            implemented.Controls.Add(chkDns);
            implemented.Controls.Add(lblDnsServers);
            implemented.Controls.Add(txtDnsServers);
            implemented.Controls.Add(chkDnsStripEdns);
            implemented.Controls.Add(chkDnsForceTcp);
            implemented.Controls.Add(lblDnsMin);
            implemented.Controls.Add(numDnsMinTtl);
            implemented.Controls.Add(lblDnsMax);
            implemented.Controls.Add(numDnsMaxTtl);
            implemented.Controls.Add(chkNtp);
            implemented.Controls.Add(chkMdns);
            implemented.Controls.Add(chkDhcp);
            implemented.Controls.Add(chkAutoStart);
            implemented.Controls.Add(chkNetwork);
            implemented.Controls.Add(lblTTL);
            implemented.Controls.Add(numTTL);
            implemented.Controls.Add(lblTtlHint);
            implemented.Controls.Add(chkIPID);
            implemented.Controls.Add(chkTcpOptions);
            implemented.Controls.Add(chkForward);
            implemented.Controls.Add(chkMtuClamp);
            implemented.Controls.Add(lblMss);
            implemented.Controls.Add(numMssClamp);
            implemented.Controls.Add(lblMssHint);
            implemented.Controls.Add(chkTun);
            implemented.Controls.Add(chkTunRoute);
            implemented.Controls.Add(lblAdapter);
            implemented.Controls.Add(txtTunAdapter);
            implemented.Controls.Add(lblUpstream);
            implemented.Controls.Add(cmbTunUpstream);
            implemented.Controls.Add(lblTunIP);
            implemented.Controls.Add(txtTunIP);
            implemented.Controls.Add(lblGateway);
            implemented.Controls.Add(txtTunGateway);
            implemented.Controls.Add(lblPrefix);
            implemented.Controls.Add(numPrefix);
            implemented.Controls.Add(lblUserAgent);
            implemented.Controls.Add(cmbUaPreset);
            implemented.Controls.Add(lblUserAgentHint);
            implemented.Controls.Add(chkIpv6Guard);
            implemented.Controls.Add(lblIPv6Policy);
            implemented.Controls.Add(cmbIpv6Policy);
            implemented.Controls.Add(lblIPv6Hint);
            implemented.Controls.Add(chkPortalBlock);
            implemented.Controls.Add(lblPortalPrimary);
            implemented.Controls.Add(txtPortalPrimary);
            implemented.Controls.Add(lblPortalSecondary);
            implemented.Controls.Add(txtPortalSecondary);
            implemented.Controls.Add(chkDnsEmbedded);
            implemented.Controls.Add(btnDetectDns);
            implemented.Controls.Add(note);

            var chkWebRtc = NewCheck("WebRTC 本地 IP 防泄漏（未实现）", false, 15, 18);
            var chkStun = NewCheck("STUN MESSAGE-INTEGRITY 重算（未实现）", false, 330, 18);
            var chkTls = NewCheck("TLS JA3/JA4 统一（未实现）", false, 15, 48);
            chkWebRtc.Enabled = false;
            chkStun.Enabled = false;
            chkTls.Enabled = false;
            var missingNote = new Label { Location = new Point(15, 72), Size = new Size(600, 26), ForeColor = Color.Gray };
            missingNote.Text = "HTTPS 当前是 TCP 透传，不做 MITM；核心监控只被动记录 ClientHello SNI/JA3，不改写 SDP/ClientHello。STUN 仅转发 UDP。";
            missing.Controls.Add(chkWebRtc);
            missing.Controls.Add(chkStun);
            missing.Controls.Add(chkTls);
            missing.Controls.Add(missingNote);

            monitorGroup.Controls.Add(chkMonitorEnabled);
            monitorGroup.Controls.Add(chkMonitorTtl);
            monitorGroup.Controls.Add(chkMonitorUa);
            monitorGroup.Controls.Add(chkMonitorTls);
            monitorGroup.Controls.Add(chkMonitorDns);
            monitorGroup.Controls.Add(lblMonitorStatus);
            monitorGroup.Controls.Add(btnToggleMonitor);

            var grpProbe = new GroupBox { Text = "探针日志 / 连通性测试", Location = new Point(12, 805), Size = new Size(640, 250) };
            txtProbeLog = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, Font = new Font("Consolas", 9F) };
            var probePanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6, 6, 6, 6) };
            var btnTest = NewButton("一键连通性测试", 150);
            btnTest.Click += RunConnectivityTest;
            var btnDiagnose = NewButton("自动诊断", 108);
            btnDiagnose.Click += RunDiagnosis;
            var btnClearLog = NewButton("清空日志", 108);
            btnClearLog.Click += delegate { txtProbeLog.Clear(); ResetProbeLogOffset(); };
            probePanel.Controls.Add(btnClearLog);
            probePanel.Controls.Add(btnDiagnose);
            probePanel.Controls.Add(btnTest);
            grpProbe.Controls.Add(txtProbeLog);
            grpProbe.Controls.Add(probePanel);

            lblResult = new Label { Location = new Point(12, 1065), Size = new Size(640, 30), ForeColor = Color.DarkGreen };
            lblResult.Text = (ProxyProcessManager.IsRunning() ? "代理运行" : "代理未运行") +
                             "；Wintun.dll " + (HotspotManager.WintunAvailable() ? "存在" : "缺失") +
                             "；网络层归一化 " + (NetworkNormalizer.Shared.IsRunning ? "运行" : "未运行（" + NetworkNormalizer.Shared.LastMessage + "）") +
                             "；IPv6 封禁 " + (!settings.IPv6GuardEnabled ? "已关闭" : (IPv6Guard.IsRunning ? "运行" : "未运行")) +
                             "；Portal " + (PortalGuard.IsRunning ? "运行" : "未运行") +
                              "；TTL 监控 " + TrafficMonitor.Shared.StatusText +
                              "；DNS 监控 " + DnsMonitor.Shared.StatusText;
            lblMonitorStatus.Text = MonitorStatusText();

            var panel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(8, 8, 8, 8) };
            var btnClose = NewButton("关闭");
            btnClose.Click += delegate { Close(); };
            var btnOpen = NewButton("打开配置文件");
            btnOpen.Click += OpenConfig;
            var btnToggle = NewButton("启动/停止代理");
            btnToggle.Click += ToggleProxy;
            var btnNetwork = NewButton("启动/停止网络层");
            btnNetwork.Click += ToggleNetwork;
            var btnSaveOnly = NewButton("仅保存");
            btnSaveOnly.Click += SaveOnly;
            var btnSaveRestart = NewButton("保存并重启代理");
            btnSaveRestart.Click += SaveAndRestart;
            panel.Controls.Add(btnClose);
            panel.Controls.Add(btnOpen);
            panel.Controls.Add(btnNetwork);
            panel.Controls.Add(btnToggle);
            panel.Controls.Add(btnSaveOnly);
            panel.Controls.Add(btnSaveRestart);

            content.Controls.Add(implemented);
            content.Controls.Add(monitorGroup);
            content.Controls.Add(missing);
            content.Controls.Add(grpProbe);
            content.Controls.Add(lblResult);
            Controls.Add(content);
            Controls.Add(panel);

            probeTimer = new System.Windows.Forms.Timer { Interval = 800 };
            probeTimer.Tick += delegate { RefreshProbeLog(false); };
            RefreshProbeLog(true);
            probeTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && probeTimer != null) probeTimer.Stop();
            base.Dispose(disposing);
        }

        private void LoadUpstreamInterfaces(string selected)
        {
            var adapters = new IcsManager().GetNetworkAdapters();
            foreach (IcsManager.NetworkAdapterInfo adapter in adapters)
            {
                if (adapter.IsActive && IsUsableUpstream(adapter) && !cmbTunUpstream.Items.Contains(adapter.Name))
                    cmbTunUpstream.Items.Add(adapter.Name);
            }
            if (cmbTunUpstream.Items.Count == 0)
            {
                foreach (IcsManager.NetworkAdapterInfo adapter in adapters)
                {
                    if (IsUsableUpstream(adapter) && !cmbTunUpstream.Items.Contains(adapter.Name))
                        cmbTunUpstream.Items.Add(adapter.Name);
                }
            }
            if (!string.IsNullOrEmpty(selected) && !cmbTunUpstream.Items.Contains(selected))
                cmbTunUpstream.Items.Insert(0, selected);
            if (cmbTunUpstream.Items.Count == 0)
                cmbTunUpstream.Items.Add("未检测到可用网卡");
            string target = string.IsNullOrEmpty(selected) ? cmbTunUpstream.Items[0].ToString() : selected;
            cmbTunUpstream.SelectedItem = target;
        }

        private void DetectDnsServers(object sender, EventArgs e)
        {
            string selected = cmbTunUpstream.SelectedItem == null ? string.Empty : cmbTunUpstream.SelectedItem.ToString();
            var servers = new List<string>();
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                bool match = !string.IsNullOrEmpty(selected) &&
                             selected != "未检测到可用网卡" &&
                             string.Equals(nic.Name, selected, StringComparison.OrdinalIgnoreCase);
                if (!match) continue;
                foreach (IPAddress address in nic.GetIPProperties().DnsAddresses)
                {
                    if (address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string text = address.ToString();
                    if (!servers.Contains(text)) servers.Add(text);
                    if (servers.Count == 3) break;
                }
                break;
            }
            if (servers.Count == 0)
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    var properties = nic.GetIPProperties();
                    if (properties.GatewayAddresses.Count == 0 || properties.DnsAddresses.Count == 0) continue;
                    string text = (nic.Name + " " + nic.Description).ToLowerInvariant();
                    if (text.Contains("virtual") || text.Contains("vmware") || text.Contains("hyper-v") ||
                        text.Contains("wsl") || text.Contains("docker") || text.Contains("onenetshare")) continue;
                    foreach (IPAddress address in properties.DnsAddresses)
                    {
                        if (address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string value = address.ToString();
                        if (!servers.Contains(value)) servers.Add(value);
                        if (servers.Count == 3) break;
                    }
                    if (servers.Count > 0) break;
                }
            }
            if (servers.Count == 0)
            {
                lblResult.Text = "未从出口网卡读取到 IPv4 DNS，请手动填写上游 DNS";
                return;
            }
            txtDnsServers.Text = string.Join(",", servers.ToArray());
            lblResult.Text = "已从出口网卡读取上游 DNS: " + txtDnsServers.Text;
        }

        private static bool IsUsableUpstream(IcsManager.NetworkAdapterInfo adapter)
        {
            string text = (adapter.Name + " " + adapter.Description).ToLowerInvariant();
            return !text.Contains("virtual") && !text.Contains("vmware") && !text.Contains("virtualbox") &&
                   !text.Contains("hyper-v") && !text.Contains("vswitch") && !text.Contains("wsl") &&
                   !text.Contains("docker") && !text.Contains("hosted network") && !text.Contains("loopback") &&
                   !text.Contains("wan miniport") && !text.Contains("onenetshare") && !text.Contains("tap") &&
                   !text.Contains("tun");
        }

        private void ApplyProfileTTL()
        {
            string profile = cmbProfile.SelectedItem == null ? "chrome-win11" : cmbProfile.SelectedItem.ToString();
            _updatingTtlFromProfile = true;
            numTTL.Value = ProxyConfigManager.RecommendedTTL(profile);
            _updatingTtlFromProfile = false;
            UpdateTtlHint();
        }

        private void UpdateTtlHint()
        {
            string profile = cmbProfile.SelectedItem == null ? "chrome-win11" : cmbProfile.SelectedItem.ToString();
            int recommended = ProxyConfigManager.RecommendedTTL(profile);
            if ((int)numTTL.Value == recommended)
            {
                lblTtlHint.ForeColor = Color.DarkGreen;
                lblTtlHint.Text = profile + " 推荐 TTL=" + recommended + "，已自动填入";
            }
            else
            {
                lblTtlHint.ForeColor = Color.DarkOrange;
                lblTtlHint.Text = "注意：与 " + profile + " 推荐 TTL=" + recommended + " 不一致，可能暴露系统指纹";
            }
        }

        private void ResetProbeLogOffset()
        {
            string path = ProxyProcessManager.ProbeLogPath;
            if (!File.Exists(path))
            {
                _probeLogOffset = 0;
                return;
            }
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    _probeLogOffset = stream.Length;
            }
            catch
            {
                _probeLogOffset = 0;
            }
        }

        private void RefreshProbeLog(bool reset)
        {
            string path = ProxyProcessManager.ProbeLogPath;
            if (!File.Exists(path))
            {
                if (reset)
                {
                    txtProbeLog.Clear();
                    _probeLogOffset = 0;
                }
                return;
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (reset || stream.Length < _probeLogOffset)
                    {
                        txtProbeLog.Clear();
                        _probeLogOffset = 0;
                    }
                    if (stream.Length <= _probeLogOffset) return;
                    stream.Seek(_probeLogOffset, SeekOrigin.Begin);
                    byte[] buffer = new byte[stream.Length - _probeLogOffset];
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int count = stream.Read(buffer, read, buffer.Length - read);
                        if (count == 0) break;
                        read += count;
                    }
                    _probeLogOffset += read;
                    string text = Encoding.UTF8.GetString(buffer, 0, read);
                    foreach (string line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int marker = line.IndexOf("[probe]", StringComparison.OrdinalIgnoreCase);
                        if (marker < 0) continue;
                        string message = line.Substring(marker).Replace("[probe]", string.Empty).Trim();
                        string clock = DateTime.Now.ToString("HH:mm:ss");
                        if (line.Length >= 19)
                        {
                            DateTime parsed;
                            if (DateTime.TryParse(line.Substring(0, 19), out parsed)) clock = parsed.ToString("HH:mm:ss");
                        }
                        AppendProbeLine("[" + clock + "] " + message);
                    }
                }
            }
            catch
            {
                ResetProbeLogOffset();
            }
        }

        private void AppendProbeLine(string line)
        {
            if (txtProbeLog.Lines.Length > 300)
            {
                string[] lines = txtProbeLog.Lines;
                var keep = new List<string>();
                for (int i = lines.Length - 250; i < lines.Length; i++) keep.Add(lines[i]);
                txtProbeLog.Lines = keep.ToArray();
            }
            txtProbeLog.AppendText(line + Environment.NewLine);
            txtProbeLog.SelectionStart = txtProbeLog.TextLength;
            txtProbeLog.ScrollToCaret();
        }

        private void RunConnectivityTest(object sender, EventArgs e)
        {
            lblResult.Text = "连通性测试中...";
            var thread = new Thread(new ThreadStart(delegate
            {
                string result = PerformConnectivityTest();
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        lblResult.Text = result;
                        AppendProbeLine("[测试] " + result);
                    }));
                }
                catch { }
            }));
            thread.IsBackground = true;
            thread.Start();
        }

        private string PerformConnectivityTest()
        {
            string http = TestHttpRewrite();
            string tun = TestTunRoute();
            string ipv6 = ProxyDiagnostics.CheckIPv6Safety();
            bool httpOK = http.StartsWith("HTTP 改写生效", StringComparison.Ordinal);
            bool tunOK = tun.StartsWith("TUN 路由正常", StringComparison.Ordinal) || tun.StartsWith("TUN 未开启", StringComparison.Ordinal);
            bool ipv6OK = ipv6.StartsWith("IPv6 安全", StringComparison.Ordinal);
            return httpOK && tunOK && ipv6OK
                ? "全副武装，请放心冲浪；" + http + "；" + tun + "；" + ipv6
                : "仍需检查；" + http + "；" + tun + "；" + ipv6;
        }

        private static Dictionary<string, string> GetProxyStatus()
        {
            var status = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:18089/status");
                request.Method = "GET";
                request.Timeout = 1200;
                request.ReadWriteTimeout = 1200;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        ParseStatusLine(status, line);
                    }
                }
            }
            catch { }
            return status;
        }

        private static void ParseStatusLine(Dictionary<string, string> status, string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            int firstEq = line.IndexOf('=');
            if (firstEq <= 0) return;
            if (line.IndexOf('=', firstEq + 1) < 0)
            {
                status[line.Substring(0, firstEq).Trim()] = line.Substring(firstEq + 1).Trim();
                return;
            }

            string key = null;
            var value = new StringBuilder();
            foreach (string token in line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = token.IndexOf('=');
                if (eq <= 0)
                {
                    if (key != null)
                    {
                        if (value.Length > 0) value.Append(' ');
                        value.Append(token);
                    }
                    continue;
                }
                if (key != null) status[key] = value.ToString();
                key = token.Substring(0, eq).Trim();
                value.Length = 0;
                value.Append(token.Substring(eq + 1).Trim());
            }
            if (key != null) status[key] = value.ToString();
        }

        private string EnsureProxyUsesCurrentConfig()
        {
            string expected = ProxyConfigManager.GetGeneration(_configPath);
            if (string.IsNullOrEmpty(expected)) return null;
            Dictionary<string, string> status = GetProxyStatus();
            string actual = status.ContainsKey("generation") ? status["generation"] : "";
            if (actual == expected) return null;

            string stop = ProxyProcessManager.Stop();
            string start = ProxyProcessManager.Start(_configPath);
            for (int i = 0; i < 12; i++)
            {
                status = GetProxyStatus();
                actual = status.ContainsKey("generation") ? status["generation"] : "";
                if (actual == expected) return null;
                if (status.Count > 0 && StatusValue(status, "version") != "未知") break;
                Thread.Sleep(250);
            }
            status = GetProxyStatus();
            actual = status.ContainsKey("generation") ? status["generation"] : "";
            if (actual != expected)
                return "HTTP 改写失败（代理进程不是当前配置：停止=" + stop + "，启动=" + start + "，进程配置=" + Short(actual, 16) + "，期望配置=" + Short(expected, 16) + "，进程=" + StatusValue(status, "version") + " " + StatusValue(status, "exe") + "）";
            return null;
        }

        private static string StatusValue(Dictionary<string, string> status, string key)
        {
            string value;
            return status.TryGetValue(key, out value) ? value : "未知";
        }

        private string TestHttpRewrite()
        {
            string stale = EnsureProxyUsesCurrentConfig();
            if (stale != null) return stale;
            if (!ProxyProcessManager.IsRunning()) return "HTTP 改写失败（代理未运行）";
            ProxySettings settings = ProxyConfigManager.Load(_configPath);
            if (!settings.Enabled || !settings.DeviceForgeEnabled || !settings.Headers)
                return "HTTP 改写失败（总开关/设备伪装/Header 有未勾选项，请保存并重启代理）";
            string observed = null;
            Exception failure = null;
            HttpListener listener = null;
            Thread listenerThread = null;
            var done = new ManualResetEvent(false);
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add("http://127.0.0.1:18099/");
                listener.Start();
                listenerThread = new Thread(new ThreadStart(delegate
                {
                    try
                    {
                        HttpListenerContext context = listener.GetContext();
                        observed = context.Request.Headers.ToString();
                        context.Response.StatusCode = 204;
                        context.Response.Close();
                    }
                    catch { }
                    finally { done.Set(); }
                }));
                listenerThread.IsBackground = true;
                listenerThread.Start();

                SendRawProxyProbe();
                done.WaitOne(3000);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                if (listener != null) listener.Stop();
                if (listenerThread != null && listenerThread.IsAlive) listenerThread.Join(500);
                done.Close();
            }
            if (failure != null) return "HTTP 改写失败（" + failure.Message + "）";
            if (string.IsNullOrEmpty(observed)) return "HTTP 改写失败（本地探针未收到请求）";
            string ua = GetHeaderValue(observed, "User-Agent");
            string xff = GetHeaderValue(observed, "X-Forwarded-For");
            string via = GetHeaderValue(observed, "Via");
            bool unrewritten = ua.IndexOf("OneNetShare-Unprobed", StringComparison.OrdinalIgnoreCase) >= 0;
            var leaks = new List<string>();
            if (!string.IsNullOrEmpty(xff)) leaks.Add("X-Forwarded-For=" + xff);
            if (!string.IsNullOrEmpty(via)) leaks.Add("Via=" + via);
            Dictionary<string, string> status = GetProxyStatus();
            string diag = "UA预设=" + StatusValue(status, "ua_preset") + "，UA覆盖=" + Short(StatusValue(status, "user_agent_override"), 30) + "，规则数=" + StatusValue(status, "rules") + "，画像=" + StatusValue(status, "profile") + "，进程=" + StatusValue(status, "version") + " " + Short(StatusValue(status, "exe"), 40);
            if (unrewritten)
                return "HTTP 改写失败（UA 仍是探针值；请从“UA 预设”选择内置画像，并点“保存并重启代理”。当前 UA=" + Short(ua, 60) + (leaks.Count > 0 ? "；泄露头仍在=" + string.Join("，", leaks.ToArray()) : "") + "；" + diag + "）";
            if (leaks.Count > 0)
                return "HTTP 改写失败（泄露头未清除：" + string.Join("，", leaks.ToArray()) + "；" + diag + "）";
            return "HTTP 改写生效（UA=" + Short(ua, 60) + "，XFF/Via 已清除）";
        }

        private static void SendRawProxyProbe()
        {
            string request =
                "GET http://127.0.0.1:18099/probe HTTP/1.1\r\n" +
                "Host: 127.0.0.1:18099\r\n" +
                "User-Agent: OneNetShare-Unprobed/1.0\r\n" +
                "X-Forwarded-For: 203.0.113.9\r\n" +
                "Via: 1.1 unprobed\r\n" +
                "Connection: close\r\n\r\n";
            byte[] payload = Encoding.ASCII.GetBytes(request);
            using (var client = new TcpClient())
            {
                client.Connect("127.0.0.1", 18080);
                client.SendTimeout = 5000;
                client.ReceiveTimeout = 5000;
                NetworkStream stream = client.GetStream();
                stream.Write(payload, 0, payload.Length);
                var buffer = new byte[4096];
                while (stream.Read(buffer, 0, buffer.Length) > 0) { }
            }
        }

        private static string GetHeaderValue(string headers, string name)
        {
            foreach (string line in headers.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                if (line.Substring(0, colon).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(colon + 1).Trim();
            }
            return string.Empty;
        }

        private static string Short(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "(空)";
            return value.Length <= max ? value : value.Substring(0, max) + "...";
        }

        private static string TestTunRoute()
        {
            ProxySettings settings = ProxyConfigManager.Load();
            if (!settings.TunEnabled) return "TUN 未开启（跳过）";
            string output = RunNetsh("interface ipv4 show route");
            if (output.IndexOf("10.66.0.2", StringComparison.Ordinal) < 0)
                return "TUN 路由异常（未发现经 10.66.0.2 的默认路由）";

            bool adapterFound = false;
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (UnicastIPAddressInformation address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.ToString() == "10.66.0.1") adapterFound = true;
                }
            }
            return adapterFound ? "TUN 路由正常（默认路由经 10.66.0.2）" : "TUN 路由异常（未发现虚拟网卡地址 10.66.0.1）";
        }

        private static string RunNetsh(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", arguments)
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

        private static CheckBox NewCheck(string text, bool checkedValue, int x, int y)
        {
            return new CheckBox
            {
                Text = text,
                Checked = checkedValue,
                AutoSize = true,
                Location = new Point(x, y),
                UseVisualStyleBackColor = true
            };
        }

        private static TextBox NewText(string value, int x, int y, int width)
        {
            return new TextBox { Text = value ?? string.Empty, Location = new Point(x, y), Size = new Size(width, 23) };
        }

        private static Button NewButton(string caption, int width = 108)
        {
            return new Button { Text = caption, AutoSize = false, Size = new Size(width, 30), UseVisualStyleBackColor = true };
        }

        private ProxySettings Collect()
        {
            bool tun = chkTun.Checked;
            return new ProxySettings
            {
                Enabled = chkEnabled.Checked,
                DeviceForgeEnabled = tun || chkDeviceForge.Checked,
                Headers = chkHeaders.Checked,
                Udp = tun || chkUdp.Checked,
                Dns = chkDns.Checked,
                DnsEmbedded = chkDnsEmbedded.Checked,
                DnsUpstreamServers = txtDnsServers.Text.Trim(),
                DnsStripEdns = chkDnsStripEdns.Checked,
                DnsForceTcp = chkDnsForceTcp.Checked,
                DnsMinTtl = (int)numDnsMinTtl.Value,
                DnsMaxTtl = (int)numDnsMaxTtl.Value,
                Ntp = chkNtp.Checked,
                Mdns = chkMdns.Checked,
                Dhcp = chkDhcp.Checked,
                Captive = chkCaptive.Checked,
                AutoStartProxy = chkAutoStart.Checked,
                NetworkEnabled = tun ? false : chkNetwork.Checked,
                NetworkTTL = (int)numTTL.Value,
                NetworkIPID = chkIPID.Checked,
                NetworkTCPOptions = chkTcpOptions.Checked,
                NetworkForward = chkForward.Checked,
                MtuClampEnabled = chkMtuClamp.Checked,
                MssClamp = (int)numMssClamp.Value,
                TunEnabled = tun,
                TunAdapter = txtTunAdapter.Text.Trim(),
                TunIP = txtTunIP.Text.Trim(),
                TunGatewayIP = txtTunGateway.Text.Trim(),
                TunPrefixLen = (int)numPrefix.Value,
                TunRoute = chkTunRoute.Checked,
                TunUpstreamInterface = cmbTunUpstream.SelectedItem == null ? string.Empty : cmbTunUpstream.SelectedItem.ToString(),
                Mode = "forward",
                Profile = cmbProfile.SelectedItem == null ? "chrome-win11" : cmbProfile.SelectedItem.ToString(),
                UAPreset = cmbUaPreset.SelectedValue == null ? "" : cmbUaPreset.SelectedValue.ToString(),
                UserAgentOverride = "",
                IPv6Policy = cmbIpv6Policy.SelectedValue == null ? "block" : cmbIpv6Policy.SelectedValue.ToString(),
                IPv6GuardEnabled = chkIpv6Guard.Checked,
                PortalBlockEnabled = chkPortalBlock.Checked,
                PortalPrimaryIP = txtPortalPrimary.Text.Trim(),
                PortalSecondaryIP = txtPortalSecondary.Text.Trim(),
                MonitorEnabled = chkMonitorEnabled.Checked,
                MonitorTtl = chkMonitorTtl.Checked,
                MonitorUa = chkMonitorUa.Checked,
                MonitorTls = chkMonitorTls.Checked,
                MonitorDns = chkMonitorDns.Checked
            };
        }

        private string TuneWarning(ProxySettings settings)
        {
            if (!settings.TunEnabled) return string.Empty;
            if (!HotspotManager.WintunAvailable()) return "；未找到 wintun.dll，请放到程序目录";
            if (!HotspotManager.IsAdministrator()) return "；全局 TUN 接管需要管理员权限";
            if (string.IsNullOrEmpty(settings.TunUpstreamInterface) || settings.TunUpstreamInterface == "未检测到可用网卡") return "；请先选择实际出口网卡";
            return string.Empty;
        }

        private void SaveOnly(object sender, EventArgs e)
        {
            ProxySettings settings = Collect();
            ProxyConfigManager.Save(settings, _configPath);
            lblResult.Text = "配置已保存。" + ApplyNetwork(settings) + "；" + ApplyIPv6Guard(settings) + "；" + ApplyMonitor(settings) + TuneWarning(settings);
            lblMonitorStatus.Text = MonitorStatusText();
        }

        private void SaveAndRestart(object sender, EventArgs e)
        {
            ProxySettings settings = Collect();
            ProxyConfigManager.Save(settings, _configPath);
            string result = ProxyProcessManager.IsRunning() ? ProxyProcessManager.Stop() + "；" + ProxyProcessManager.Start(_configPath) : ProxyProcessManager.Start(_configPath);
            lblResult.Text = result + "；" + ApplyNetwork(settings) + "；" + ApplyIPv6Guard(settings) + "；" + ApplyMonitor(settings) + TuneWarning(settings);
            lblMonitorStatus.Text = MonitorStatusText();
            ShowProxyDiagnosticIfNeeded();
        }

        private void ToggleProxy(object sender, EventArgs e)
        {
            string result;
            if (ProxyProcessManager.IsRunning())
            {
                result = ProxyProcessManager.Stop();
            }
            else
            {
                result = ProxyProcessManager.Start(_configPath) + TuneWarning(Collect());
                ShowProxyDiagnosticIfNeeded();
            }
            lblResult.Text = result;
        }

        private void RunDiagnosis(object sender, EventArgs e)
        {
            string text = ProxyDiagnostics.Analyze(_configPath, ProxyProcessManager.GetProcessOutput(), null);
            ProxyProcessManager.LastDiagnostic = text;
            lblResult.Text = "自动诊断已生成，详见弹窗。";
            MessageBox.Show(this, text, "代理自动诊断", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowProxyDiagnosticIfNeeded()
        {
            if (string.IsNullOrEmpty(ProxyProcessManager.LastDiagnostic)) return;
            MessageBox.Show(this, ProxyProcessManager.LastDiagnostic, "代理自动诊断", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ToggleNetwork(object sender, EventArgs e)
        {
            if (chkTun.Checked)
            {
                lblResult.Text = "全局 TUN 接管已启用，WinDivert 网络层归一化已禁用。";
                return;
            }
            lblResult.Text = NetworkNormalizer.Shared.IsRunning ? NetworkNormalizer.Shared.Stop().Message : ApplyNetwork(Collect());
        }

        private void ToggleMonitor(object sender, EventArgs e)
        {
            lblResult.Text = TrafficMonitor.Shared.IsRunning || DnsMonitor.Shared.IsRunning
                ? TrafficMonitor.Shared.Stop().Message + "；" + DnsMonitor.Shared.Stop().Message
                : ApplyMonitor(Collect());
            lblMonitorStatus.Text = MonitorStatusText();
        }

        private static string ApplyNetwork(ProxySettings settings)
        {
            return settings.TunEnabled ? string.Empty : (settings.NetworkEnabled ? NetworkNormalizer.Shared.Start(settings).Message : NetworkNormalizer.Shared.Stop().Message);
        }

        private static string ApplyIPv6Guard(ProxySettings settings)
        {
            if (!settings.IPv6GuardEnabled) return IPv6Guard.Stop().Message;
            if (ProxyDiagnostics.FindHotspotGlobalIPv6().Count == 0) return "IPv6 封禁已启用（当前无公网 IPv6，不启动）";
            return IPv6Guard.Start(settings.IPv6Policy).Message;
        }

        private static string ApplyMonitor(ProxySettings settings)
        {
            string ttl = !settings.MonitorEnabled || !settings.MonitorTtl
                ? TrafficMonitor.Shared.Stop().Message
                : TrafficMonitor.Shared.Start(settings).Message;
            string dns = !settings.MonitorEnabled || !settings.MonitorDns
                ? DnsMonitor.Shared.Stop().Message
                : DnsMonitor.Shared.Start(settings).Message;
            return ttl + "；" + dns;
        }

        private static string MonitorStatusText()
        {
            return "代理侧 HTTP UA / TLS SNI+JA3 随代理进程启用；" + TrafficMonitor.Shared.StatusText +
                   "；" + DnsMonitor.Shared.StatusText;
        }

        private void OpenConfig(object sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + _configPath + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开配置文件: " + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
