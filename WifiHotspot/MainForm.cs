using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WifiHotspot
{
    public partial class MainForm : Form
    {
        private readonly HotspotManager _manager = new HotspotManager();
        private List<IcsManager.NetworkAdapterInfo> _adapters;
        private string _configPath;

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
            CheckSupportAndStatus();
            timerStatus.Start();
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(_configPath)) return;
                foreach (string line in File.ReadAllLines(_configPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string[] parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length != 2) continue;

                    switch (parts[0].Trim())
                    {
                        case "SSID": txtSsid.Text = parts[1]; break;
                        case "Password": txtPassword.Text = parts[1]; break;
                        case "AdapterId": _savedAdapterId = parts[1]; break;
                    }
                }
            }
            catch
            {
                // ignore config load errors
            }
        }

        private string _savedAdapterId;

        private void SaveConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string selectedId = string.Empty;
                if (cmbAdapter.SelectedIndex >= 0 && cmbAdapter.SelectedIndex < _adapters.Count)
                    selectedId = _adapters[cmbAdapter.SelectedIndex].Id;

                var lines = new[]
                {
                    "# WifiHotspot 配置文件",
                    "SSID=" + txtSsid.Text,
                    "Password=" + txtPassword.Text,
                    "AdapterId=" + selectedId
                };
                File.WriteAllLines(_configPath, lines);
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
            cmbAdapter.Items.Clear();

            int selectedIndex = -1;
            for (int i = 0; i < _adapters.Count; i++)
            {
                var a = _adapters[i];
                cmbAdapter.Items.Add(a.ToString());
                if (!string.IsNullOrEmpty(_savedAdapterId) && a.Id == _savedAdapterId)
                    selectedIndex = i;
            }

            if (selectedIndex >= 0)
                cmbAdapter.SelectedIndex = selectedIndex;
            else if (cmbAdapter.Items.Count > 0)
                cmbAdapter.SelectedIndex = 0;
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
            lblStatus.Text = string.Format("热点状态: {0}\n支持检测: {1}", statusText, support.Message);
        }

        private void RefreshClients()
        {
            lstClients.Items.Clear();
            var clients = _manager.GetConnectedClients();
            if (clients.Count == 0)
                lstClients.Items.Add("暂无已连接设备");
            else
                foreach (var c in clients)
                    lstClients.Items.Add(c);
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
            if (cmbAdapter.SelectedIndex >= 0 && cmbAdapter.SelectedIndex < _adapters.Count)
                selected = _adapters[cmbAdapter.SelectedIndex];

            btnStart.Enabled = false;
            lblStatus.ForeColor = Color.Blue;
            lblStatus.Text = "正在启动热点，请稍候...";
            Application.DoEvents();

            var result = _manager.Start(selected, txtSsid.Text, txtPassword.Text);
            MessageBox.Show(result.Message, result.Success ? "启动结果" : "启动失败",
                MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);

            CheckSupportAndStatus();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            btnStop.Enabled = false;
            lblStatus.ForeColor = Color.Blue;
            lblStatus.Text = "正在停止热点...";
            Application.DoEvents();

            var result = _manager.Stop();
            MessageBox.Show(result.Message, "停止结果", MessageBoxButtons.OK, MessageBoxIcon.Information);

            lstClients.Items.Clear();
            CheckSupportAndStatus();
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            RefreshAdapters();
            CheckSupportAndStatus();
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

        private void timerStatus_Tick(object sender, EventArgs e)
        {
            string detail;
            HotspotStatus status = _manager.GetStatus(out detail);
            bool isAdmin = HotspotManager.IsAdministrator();

            if (status == HotspotStatus.Running)
            {
                lblStatus.ForeColor = Color.Green;
                lblStatus.Text = "热点状态: " + detail + "\nSSID: " + txtSsid.Text;
                btnStart.Enabled = false;
                btnStop.Enabled = isAdmin;
                RefreshClients();
            }
            else if (status == HotspotStatus.Stopped)
            {
                lblStatus.ForeColor = Color.DarkOrange;
                lblStatus.Text = "热点状态: " + detail;
                btnStart.Enabled = isAdmin;
                btnStop.Enabled = false;
                lstClients.Items.Clear();
            }
            else
            {
                lblStatus.ForeColor = Color.Red;
                lblStatus.Text = "热点状态: " + detail + "\n状态检测失败，可尝试启动/停止";
                btnStart.Enabled = isAdmin;
                btnStop.Enabled = isAdmin;
                lstClients.Items.Clear();
            }
        }
    }
}
