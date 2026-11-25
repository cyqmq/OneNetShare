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
        private readonly NumericUpDown numTTL;
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

            vargrp = new GroupBox { Text = "已实现功能", Location = new Point(12, 12), Size = new Size(640, 365) };
            var missing = new GroupBox { Text = "未实现功能", Location = new Point(12, 383), Size = new Size(640, 150) };
            var note = new Label { Location = new Point(15, 285), Size = new Size(600, 65), ForeColor = Color.Gray };
            note.Text = "Windows 当前为 forward 代理模式：HTTP Header 改写有效；UDP/DNS/NTP/mDNS/DHCP 需要 TUN/TPROXY 接管，Windows 下开关会写入配置但不生效。网络层归一化通过 WinDivert 在网卡出口改写 TTL/IPID/TCP SYN 选项，需要 WinDivert.dll 与管理员权限。";

            var lblMode = new Label { Location = new Point(15, 28), AutoSize = true, Text = "运行模式: forward (Windows)" };
            var lblProfile = new Label { Location = new Point(160, 28), AutoSize = true, Text = "默认设备指纹:" };
            cmbProfile = new ComboBox { Location = new Point(260, 25), Size = new Size(220, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (string profile in ProxyConfigManager.GetProfiles(_configPath)) cmbProfile.Items.Add(profile);
            if (!cmbProfile.Items.Contains(settings.Profile)) cmbProfile.Items.Add(settings.Profile);
            cmbProfile.SelectedItem = settings.Profile;

            chkEnabled = NewCheck("HTTP 代理与 Header 改写总开关", settings.Enabled, 15, 62);
            chkDeviceForge = NewCheck("设备伪装模块总开关", settings.DeviceForgeEnabled, 330, 62);
            chkHeaders = NewCheck("HTTP Header 指纹统一", settings.Headers, 15, 92);
            chkCaptive = NewCheck("Captive 门户检测处理", settings.Captive, 330, 92);
            chkUdp = NewCheck("UDP 会话重建 (Windows 下不生效)", settings.Udp, 15, 122);
            chkDns = NewCheck("DNS 规范化 (Windows 下不生效)", settings.Dns, 330, 122);
            chkNtp = NewCheck("NTP 规范化 (Windows 下不生效)", settings.Ntp, 15, 152);
            chkMdns = NewCheck("mDNS 伪造 (Windows 下不生效)", settings.Mdns, 330, 152);
            chkDhcp = NewCheck("DHCP 伪造 (Windows 下不生效)", settings.Dhcp, 15, 182);
            chkAutoStart = NewCheck("随热点自动启动/停止代理", settings.AutoStartProxy, 330, 182);
            chkNetwork = NewCheck("网络层指纹归一化 (WinDivert)", settings.NetworkEnabled, 15, 215);
            var lblTTL = new Label { Location = new Point(300, 218), AutoSize = true, Text = "强制 TTL:" };
            numTTL = new NumericUpDown { Location = new Point(360, 215), Size = new Size(65, 23), Minimum = 1, Maximum = 255, Value = settings.NetworkTTL };
            chkIPID = NewCheck("IPID 归一化", settings.NetworkIPID, 15, 245);
            chkTcpOptions = NewCheck("TCP SYN 选项归一化", settings.NetworkTCPOptions, 200, 245);
            chkForward = NewCheck("同时接管转发流量", settings.NetworkForward, 430, 245);

            vargrp.Controls.Add(lblMode);
            vargrp.Controls.Add(lblProfile);
            vargrp.Controls.Add(cmbProfile);
            vargrp.Controls.Add(chkEnabled);
            vargrp.Controls.Add(chkDeviceForge);
            vargrp.Controls.Add(chkHeaders);
            vargrp.Controls.Add(chkCaptive);
            vargrp.Controls.Add(chkUdp);
            vargrp.Controls.Add(chkDns);
            vargrp.Controls.Add(chkNtp);
            vargrp.Controls.Add(chkMdns);
            vargrp.Controls.Add(chkDhcp);
            vargrp.Controls.Add(chkAutoStart);
            vargrp.Controls.Add(chkNetwork);
            vargrp.Controls.Add(lblTTL);
            vargrp.Controls.Add(numTTL);
            vargrp.Controls.Add(chkIPID);
            vargrp.Controls.Add(chkTcpOptions);
            vargrp.Controls.Add(chkForward);
            vargrp.Controls.Add(note);

            var chkWebRtc = NewCheck("WebRTC 本地 IP 防泄漏（未实现）", false, 15, 30);
            var chkStun = NewCheck("STUN MESSAGE-INTEGRITY 重算（未实现）", false, 330, 30);
            var chkTls = NewCheck("TLS JA3/JA4 统一（未实现）", false, 15, 60);
            chkWebRtc.Enabled = false;
            chkStun.Enabled = false;
            chkTls.Enabled = false;
            var missingNote = new Label { Location = new Point(15, 95), Size = new Size(600, 45), ForeColor = Color.Gray };
            missingNote.Text = "HTTPS 当前是 TCP 透传，不做 MITM，无法改写 SDP 或 ClientHello；STUN 目前只转发 UDP，不解析/重算 MESSAGE-INTEGRITY。";
            missing.Controls.Add(chkWebRtc);
            missing.Controls.Add(chkStun);
            missing.Controls.Add(chkTls);
            missing.Controls.Add(missingNote);

            lblResult = new Label { Location = new Point(12, 540), Size = new Size(640, 45), ForeColor = Color.DarkGreen };
            lblResult.Text = (ProxyProcessManager.IsRunning() ? "代理运行" : "代理未运行") + "；" +
                             (NetworkNormalizer.Shared.IsRunning ? "网络层归一化运行" : "网络层归一化未运行");

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

            Controls.Add(vargrp);
            Controls.Add(missing);
            Controls.Add(lblResult);
            Controls.Add(panel);
        }

        private GroupBox vargrp;

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

        private static Button NewButton(string caption)
        {
            return new Button { Text = caption, AutoSize = true, MinimumSize = new Size(120, 30), UseVisualStyleBackColor = true };
        }

        private ProxySettings Collect()
        {
            return new ProxySettings
            {
                Enabled = chkEnabled.Checked,
                DeviceForgeEnabled = chkDeviceForge.Checked,
                Headers = chkHeaders.Checked,
                Udp = chkUdp.Checked,
                Dns = chkDns.Checked,
                Ntp = chkNtp.Checked,
                Mdns = chkMdns.Checked,
                Dhcp = chkDhcp.Checked,
                Captive = chkCaptive.Checked,
                AutoStartProxy = chkAutoStart.Checked,
                NetworkEnabled = chkNetwork.Checked,
                NetworkTTL = (int)numTTL.Value,
                NetworkIPID = chkIPID.Checked,
                NetworkTCPOptions = chkTcpOptions.Checked,
                NetworkForward = chkForward.Checked,
                Mode = "forward",
                Profile = cmbProfile.SelectedItem == null ? "chrome-win11" : cmbProfile.SelectedItem.ToString()
            };
        }

        private void SaveOnly(object sender, EventArgs e)
        {
            ProxySettings settings = Collect();
            ProxyConfigManager.Save(settings, _configPath);
            lblResult.Text = "配置已保存。" + ApplyNetwork(settings);
        }

        private void SaveAndRestart(object sender, EventArgs e)
        {
            ProxySettings settings = Collect();
            ProxyConfigManager.Save(settings, _configPath);
            lblResult.Text = (ProxyProcessManager.IsRunning() ? ProxyProcessManager.Stop() + "；" + ProxyProcessManager.Start(_configPath) : ProxyProcessManager.Start(_configPath)) + "；" + ApplyNetwork(settings);
        }

        private void ToggleProxy(object sender, EventArgs e)
        {
            lblResult.Text = ProxyProcessManager.IsRunning() ? ProxyProcessManager.Stop() : ProxyProcessManager.Start(_configPath);
        }

        private void ToggleNetwork(object sender, EventArgs e)
        {
            lblResult.Text = NetworkNormalizer.Shared.IsRunning ? NetworkNormalizer.Shared.Stop().Message : ApplyNetwork(Collect());
        }

        private static string ApplyNetwork(ProxySettings settings)
        {
            return settings.NetworkEnabled ? NetworkNormalizer.Shared.Start(settings).Message : NetworkNormalizer.Shared.Stop().Message;
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
