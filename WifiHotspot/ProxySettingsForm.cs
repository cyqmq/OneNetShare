using System;
using System.Diagnostics;
using System.Drawing;
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
        private readonly CheckBox chkNtp;
        private readonly CheckBox chkMdns;
        private readonly CheckBox chkDhcp;
        private readonly CheckBox chkCaptive;
        private readonly CheckBox chkAutoStart;
        private readonly CheckBox chkNetwork;
        private readonly CheckBox chkIPID;
        private readonly CheckBox chkTcpOptions;
        private readonly CheckBox chkForward;
        private readonly CheckBox chkTun;
        private readonly CheckBox chkTunRoute;
        private readonly NumericUpDown numTTL;
        private readonly Label lblTtlHint;
        private bool _updatingTtlFromProfile;
        private readonly NumericUpDown numPrefix;
        private readonly TextBox txtTunAdapter;
        private readonly TextBox txtTunIP;
        private readonly TextBox txtTunGateway;
        private readonly TextBox txtTunUpstream;
        private readonly ComboBox cmbProfile;
        private readonly Label lblResult;

        public ProxySettingsForm()
        {
            _configPath = ProxyConfigManager.DefaultConfigPath;
            ProxySettings settings = ProxyConfigManager.Load(_configPath);

            Text = "代理功能开关";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(680, 690);
            Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);

            var implemented = new GroupBox { Text = "已实现功能", Location = new Point(12, 12), Size = new Size(640, 415) };
            var missing = new GroupBox { Text = "未实现功能", Location = new Point(12, 427), Size = new Size(640, 130) };
            var note = new Label { Location = new Point(15, 370), Size = new Size(600, 38), ForeColor = Color.Gray };
            note.Text = "Wintun 全局 TUN 接管开启后，UDP/DNS/NTP 经虚拟网卡进入代理；HTTP Header 仍需代理转发。TUN 与 WinDivert 网络层归一化互斥。TTL 会随设备指纹自动填入推荐值。";

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
            chkNtp = NewCheck("NTP 规范化 (需开启 Wintun)", settings.Ntp, 15, 152);
            chkMdns = NewCheck("mDNS 伪造 (需开启 Wintun)", settings.Mdns, 330, 152);
            chkDhcp = NewCheck("DHCP 伪造 (需开启 Wintun)", settings.Dhcp, 15, 182);
            chkAutoStart = NewCheck("随热点自动启动/停止代理", settings.AutoStartProxy, 330, 182);
            chkNetwork = NewCheck("网络层指纹归一化 (WinDivert)", settings.NetworkEnabled, 15, 215);
            var lblTTL = new Label { Location = new Point(300, 218), AutoSize = true, Text = "强制 TTL:" };
            numTTL = new NumericUpDown { Location = new Point(360, 215), Size = new Size(65, 23), Minimum = 1, Maximum = 255, Value = initialTTL };
            lblTtlHint = new Label { Location = new Point(435, 212), Size = new Size(190, 32), ForeColor = Color.DarkGreen };
            chkIPID = NewCheck("IPID 归一化", settings.NetworkIPID, 15, 245);
            chkTcpOptions = NewCheck("TCP SYN 选项归一化", settings.NetworkTCPOptions, 200, 245);
            chkForward = NewCheck("同时接管转发流量", settings.NetworkForward, 430, 245);
            chkTun = NewCheck("全局 TUN 接管 (Wintun)", settings.TunEnabled, 15, 275);
            chkTunRoute = NewCheck("接管默认路由与 DNS", settings.TunRoute, 200, 275);
            var lblAdapter = new Label { Location = new Point(15, 307), AutoSize = true, Text = "虚拟网卡:" };
            txtTunAdapter = NewText(settings.TunAdapter, 95, 304, 205);
            var lblUpstream = new Label { Location = new Point(315, 307), AutoSize = true, Text = "出口网卡:" };
            txtTunUpstream = NewText(settings.TunUpstreamInterface, 385, 304, 235);
            var lblTunIP = new Label { Location = new Point(15, 337), AutoSize = true, Text = "TUN IP:" };
            txtTunIP = NewText(settings.TunIP, 95, 334, 105);
            var lblGateway = new Label { Location = new Point(220, 337), AutoSize = true, Text = "网关:" };
            txtTunGateway = NewText(settings.TunGatewayIP, 270, 334, 105);
            var lblPrefix = new Label { Location = new Point(395, 337), AutoSize = true, Text = "前缀:" };
            numPrefix = new NumericUpDown { Location = new Point(440, 334), Size = new Size(55, 23), Minimum = 8, Maximum = 30, Value = settings.TunPrefixLen <= 0 ? 24 : settings.TunPrefixLen };

            chkTun.CheckedChanged += delegate { chkNetwork.Enabled = !chkTun.Checked; };
            chkNetwork.Enabled = !settings.TunEnabled;
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
            implemented.Controls.Add(chkTun);
            implemented.Controls.Add(chkTunRoute);
            implemented.Controls.Add(lblAdapter);
            implemented.Controls.Add(txtTunAdapter);
            implemented.Controls.Add(lblUpstream);
            implemented.Controls.Add(txtTunUpstream);
            implemented.Controls.Add(lblTunIP);
            implemented.Controls.Add(txtTunIP);
            implemented.Controls.Add(lblGateway);
            implemented.Controls.Add(txtTunGateway);
            implemented.Controls.Add(lblPrefix);
            implemented.Controls.Add(numPrefix);
            implemented.Controls.Add(note);

            var chkWebRtc = NewCheck("WebRTC 本地 IP 防泄漏（未实现）", false, 15, 30);
            var chkStun = NewCheck("STUN MESSAGE-INTEGRITY 重算（未实现）", false, 330, 30);
            var chkTls = NewCheck("TLS JA3/JA4 统一（未实现）", false, 15, 60);
            chkWebRtc.Enabled = false;
            chkStun.Enabled = false;
            chkTls.Enabled = false;
            var missingNote = new Label { Location = new Point(15, 95), Size = new Size(600, 30), ForeColor = Color.Gray };
            missingNote.Text = "HTTPS 当前是 TCP 透传，不做 MITM，无法改写 SDP 或 ClientHello；STUN 目前只转发 UDP，不解析/重算 MESSAGE-INTEGRITY。";
            missing.Controls.Add(chkWebRtc);
            missing.Controls.Add(chkStun);
            missing.Controls.Add(chkTls);
            missing.Controls.Add(missingNote);

            lblResult = new Label { Location = new Point(12, 565), Size = new Size(640, 45), ForeColor = Color.DarkGreen };
            lblResult.Text = (ProxyProcessManager.IsRunning() ? "代理运行" : "代理未运行") +
                             "；Wintun.dll " + (HotspotManager.WintunAvailable() ? "存在" : "缺失") +
                             "；网络层归一化 " + (NetworkNormalizer.Shared.IsRunning ? "运行" : "未运行");

            var panel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 8, 8, 8) };
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

            Controls.Add(implemented);
            Controls.Add(missing);
            Controls.Add(lblResult);
            Controls.Add(panel);
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

        private static Button NewButton(string caption)
        {
            return new Button { Text = caption, AutoSize = true, MinimumSize = new Size(120, 30), UseVisualStyleBackColor = true };
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
                TunEnabled = tun,
                TunAdapter = txtTunAdapter.Text.Trim(),
                TunIP = txtTunIP.Text.Trim(),
                TunGatewayIP = txtTunGateway.Text.Trim(),
                TunPrefixLen = (int)numPrefix.Value,
                TunRoute = chkTunRoute.Checked,
                TunUpstreamInterface = txtTunUpstream.Text.Trim(),
                Mode = "forward",
                Profile = cmbProfile.SelectedItem == null ? "chrome-win11" : cmbProfile.SelectedItem.ToString()
            };
        }

        private string TuneWarning(ProxySettings settings)
        {
            if (!settings.TunEnabled) return string.Empty;
            if (!HotspotManager.WintunAvailable()) return "；未找到 wintun.dll，请放到程序目录";
            if (!HotspotManager.IsAdministrator()) return "；全局 TUN 接管需要管理员权限";
            return string.Empty;
        }

        private void SaveOnly(object sender, EventArgs e)
        {
            ProxySettings settings = Collect();
            ProxyConfigManager.Save(settings, _configPath);
            lblResult.Text = "配置已保存。" + ApplyNetwork(settings) + TuneWarning(settings);
        }

        private void SaveAndRestart(object sender, EventArgs e)
        {
            ProxySettings settings = Collect();
            ProxyConfigManager.Save(settings, _configPath);
            lblResult.Text = (ProxyProcessManager.IsRunning() ? ProxyProcessManager.Stop() + "；" + ProxyProcessManager.Start(_configPath) : ProxyProcessManager.Start(_configPath)) +
                             "；" + ApplyNetwork(settings) + TuneWarning(settings);
        }

        private void ToggleProxy(object sender, EventArgs e)
        {
            lblResult.Text = ProxyProcessManager.IsRunning() ? ProxyProcessManager.Stop() : ProxyProcessManager.Start(_configPath) + TuneWarning(Collect());
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

        private static string ApplyNetwork(ProxySettings settings)
        {
            return settings.TunEnabled ? string.Empty : (settings.NetworkEnabled ? NetworkNormalizer.Shared.Start(settings).Message : NetworkNormalizer.Shared.Stop().Message);
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
