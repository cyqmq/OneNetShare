namespace WifiHotspot
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.sidebar = new System.Windows.Forms.Panel();
            this.lblBrand = new System.Windows.Forms.Label();
            this.lblBrandSub = new System.Windows.Forms.Label();
            this.navDashboard = new System.Windows.Forms.Button();
            this.navConfig = new System.Windows.Forms.Button();
            this.navFeatures = new System.Windows.Forms.Button();
            this.navMonitor = new System.Windows.Forms.Button();
            this.lblSidebarStatus = new System.Windows.Forms.Label();
            this.btnSideDiagnosis = new System.Windows.Forms.Button();
            this.header = new System.Windows.Forms.Panel();
            this.lblHeaderTitle = new System.Windows.Forms.Label();
            this.lblHeaderSubtitle = new System.Windows.Forms.Label();
            this.btnQuickStart = new System.Windows.Forms.Button();
            this.btnQuickStop = new System.Windows.Forms.Button();
            this.contentHost = new System.Windows.Forms.Panel();
            this.pageDashboard = new System.Windows.Forms.Panel();
            this.pageConfig = new System.Windows.Forms.Panel();
            this.pageFeatures = new System.Windows.Forms.Panel();
            this.pageMonitor = new System.Windows.Forms.Panel();
            this.lblHeroTitle = new System.Windows.Forms.Label();
            this.lblHeroStatus = new System.Windows.Forms.Label();
            this.lblMetricHotspot = new System.Windows.Forms.Label();
            this.lblMetricProxy = new System.Windows.Forms.Label();
            this.lblMetricMonitor = new System.Windows.Forms.Label();
            this.lblMetricClients = new System.Windows.Forms.Label();
            this.lblMetricIssues = new System.Windows.Forms.Label();
            this.lblAlerts = new System.Windows.Forms.Label();
            this.lblDashProxy = new System.Windows.Forms.Label();
            this.lblDashNetwork = new System.Windows.Forms.Label();
            this.lblDashMonitor = new System.Windows.Forms.Label();
            this.lblDashGuards = new System.Windows.Forms.Label();
            this.lblDashHeader = new System.Windows.Forms.Label();
            this.lblDashTun = new System.Windows.Forms.Label();
            this.btnDashboardStart = new System.Windows.Forms.Button();
            this.btnDashboardStop = new System.Windows.Forms.Button();
            this.btnDashboardAdvanced = new System.Windows.Forms.Button();
            this.btnDashboardRefresh = new System.Windows.Forms.Button();
            this.grpSettings = new System.Windows.Forms.Panel();
            this.lblTopology = new System.Windows.Forms.Label();
            this.cmbTopology = new System.Windows.Forms.ComboBox();
            this.lblSsid = new System.Windows.Forms.Label();
            this.txtSsid = new System.Windows.Forms.TextBox();
            this.lblPassword = new System.Windows.Forms.Label();
            this.txtPassword = new System.Windows.Forms.TextBox();
            this.chkShowPassword = new System.Windows.Forms.CheckBox();
            this.lblAdapter = new System.Windows.Forms.Label();
            this.cmbAdapter = new System.Windows.Forms.ComboBox();
            this.lblWirelessAdapter = new System.Windows.Forms.Label();
            this.cmbWirelessAdapter = new System.Windows.Forms.ComboBox();
            this.lblDirection = new System.Windows.Forms.Label();
            this.btnProxy = new System.Windows.Forms.Button();
            this.btnStart = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.btnRefresh = new System.Windows.Forms.Button();
            this.btnSave = new System.Windows.Forms.Button();
            this.lblConfigHint = new System.Windows.Forms.Label();
            this.chkProxyEnabled = new System.Windows.Forms.CheckBox();
            this.chkHeaders = new System.Windows.Forms.CheckBox();
            this.chkDeviceForge = new System.Windows.Forms.CheckBox();
            this.chkNetworkNormalizer = new System.Windows.Forms.CheckBox();
            this.chkTun = new System.Windows.Forms.CheckBox();
            this.chkIPv6Guard = new System.Windows.Forms.CheckBox();
            this.chkPortalBlock = new System.Windows.Forms.CheckBox();
            this.chkOutboundGuard = new System.Windows.Forms.CheckBox();
            this.chkMonitorTtl = new System.Windows.Forms.CheckBox();
            this.chkMonitorDns = new System.Windows.Forms.CheckBox();
            this.lblProxyState = new System.Windows.Forms.Label();
            this.lblHeaderState = new System.Windows.Forms.Label();
            this.lblDeviceForgeState = new System.Windows.Forms.Label();
            this.lblNetworkState = new System.Windows.Forms.Label();
            this.lblTunState = new System.Windows.Forms.Label();
            this.lblIPv6State = new System.Windows.Forms.Label();
            this.lblPortalState = new System.Windows.Forms.Label();
            this.lblOutboundState = new System.Windows.Forms.Label();
            this.lblMonitorTtlState = new System.Windows.Forms.Label();
            this.lblMonitorDnsState = new System.Windows.Forms.Label();
            this.btnFeatureSave = new System.Windows.Forms.Button();
            this.btnFeatureAdvanced = new System.Windows.Forms.Button();
            this.lblFeatureHint = new System.Windows.Forms.Label();
            this.grpClients = new System.Windows.Forms.Panel();
            this.lstClients = new System.Windows.Forms.ListBox();
            this.btnRefreshClients = new System.Windows.Forms.Button();
            this.txtMonitorLog = new System.Windows.Forms.TextBox();
            this.lblStatus = new System.Windows.Forms.Label();
            this.lblFooter = new System.Windows.Forms.Label();
            this.timerStatus = new System.Windows.Forms.Timer(this.components);
            this.timerMonitor = new System.Windows.Forms.Timer(this.components);
            this.SuspendLayout();

            this.sidebar.BackColor = System.Drawing.Color.FromArgb(15, 23, 42);
            this.sidebar.Controls.Add(this.lblBrand);
            this.sidebar.Controls.Add(this.lblBrandSub);
            this.sidebar.Controls.Add(this.navDashboard);
            this.sidebar.Controls.Add(this.navConfig);
            this.sidebar.Controls.Add(this.navFeatures);
            this.sidebar.Controls.Add(this.navMonitor);
            this.sidebar.Controls.Add(this.lblSidebarStatus);
            this.sidebar.Controls.Add(this.btnSideDiagnosis);
            this.sidebar.Dock = System.Windows.Forms.DockStyle.Left;
            this.sidebar.Location = new System.Drawing.Point(0, 0);
            this.sidebar.Name = "sidebar";
            this.sidebar.Size = new System.Drawing.Size(210, 800);
            this.sidebar.TabIndex = 0;

            this.lblBrand.AutoSize = true;
            this.lblBrand.Font = new System.Drawing.Font("Microsoft YaHei", 16F, System.Drawing.FontStyle.Bold);
            this.lblBrand.ForeColor = System.Drawing.Color.White;
            this.lblBrand.Location = new System.Drawing.Point(18, 22);
            this.lblBrand.Name = "lblBrand";
            this.lblBrand.Size = new System.Drawing.Size(160, 34);
            this.lblBrand.TabIndex = 0;
            this.lblBrand.Text = "OneNetShare";

            this.lblBrandSub.AutoSize = true;
            this.lblBrandSub.ForeColor = System.Drawing.Color.FromArgb(148, 163, 184);
            this.lblBrandSub.Location = new System.Drawing.Point(19, 58);
            this.lblBrandSub.Name = "lblBrandSub";
            this.lblBrandSub.Size = new System.Drawing.Size(150, 17);
            this.lblBrandSub.TabIndex = 1;
            this.lblBrandSub.Text = "热点 / 代理控制台";

            this.navDashboard = NewSideButton("仪表盘", 110, new System.EventHandler(this.navDashboard_Click));
            this.navConfig = NewSideButton("热点配置", 156, new System.EventHandler(this.navConfig_Click));
            this.navFeatures = NewSideButton("功能开关", 202, new System.EventHandler(this.navFeatures_Click));
            this.navMonitor = NewSideButton("运行监控", 248, new System.EventHandler(this.navMonitor_Click));

            this.lblSidebarStatus.BackColor = System.Drawing.Color.FromArgb(30, 41, 59);
            this.lblSidebarStatus.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblSidebarStatus.ForeColor = System.Drawing.Color.FromArgb(134, 239, 172);
            this.lblSidebarStatus.Location = new System.Drawing.Point(14, 686);
            this.lblSidebarStatus.Name = "lblSidebarStatus";
            this.lblSidebarStatus.Padding = new System.Windows.Forms.Padding(10, 10, 10, 10);
            this.lblSidebarStatus.Size = new System.Drawing.Size(182, 54);
            this.lblSidebarStatus.TabIndex = 5;
            this.lblSidebarStatus.Text = "系统状态\n正在检测...";

            this.btnSideDiagnosis = NewSecondaryButton("打开诊断报告", 14, 746, 182, 36);
            this.btnSideDiagnosis.Click += new System.EventHandler(this.btnSideDiagnosis_Click);

            this.header.BackColor = System.Drawing.Color.White;
            this.header.Controls.Add(this.lblHeaderTitle);
            this.header.Controls.Add(this.lblHeaderSubtitle);
            this.header.Controls.Add(this.btnQuickStart);
            this.header.Controls.Add(this.btnQuickStop);
            this.header.Dock = System.Windows.Forms.DockStyle.Top;
            this.header.Location = new System.Drawing.Point(210, 0);
            this.header.Name = "header";
            this.header.Size = new System.Drawing.Size(1110, 74);
            this.header.TabIndex = 1;

            this.lblHeaderTitle.AutoSize = true;
            this.lblHeaderTitle.Font = new System.Drawing.Font("Microsoft YaHei", 16F, System.Drawing.FontStyle.Bold);
            this.lblHeaderTitle.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            this.lblHeaderTitle.Location = new System.Drawing.Point(24, 12);
            this.lblHeaderTitle.Name = "lblHeaderTitle";
            this.lblHeaderTitle.Size = new System.Drawing.Size(100, 32);
            this.lblHeaderTitle.TabIndex = 0;
            this.lblHeaderTitle.Text = "仪表盘";

            this.lblHeaderSubtitle.AutoSize = true;
            this.lblHeaderSubtitle.ForeColor = System.Drawing.Color.FromArgb(100, 116, 139);
            this.lblHeaderSubtitle.Location = new System.Drawing.Point(26, 44);
            this.lblHeaderSubtitle.Name = "lblHeaderSubtitle";
            this.lblHeaderSubtitle.Size = new System.Drawing.Size(400, 17);
            this.lblHeaderSubtitle.TabIndex = 1;
            this.lblHeaderSubtitle.Text = "一眼查看热点、代理、监控和异常";

            this.btnQuickStart = NewPrimaryButton("启动热点", 830, 18, 126, 38);
            this.btnQuickStart.Click += new System.EventHandler(this.btnStart_Click);
            this.btnQuickStop = NewDangerButton("停止热点", 966, 18, 126, 38);
            this.btnQuickStop.Click += new System.EventHandler(this.btnStop_Click);

            this.contentHost.BackColor = System.Drawing.Color.FromArgb(245, 247, 251);
            this.contentHost.Controls.Add(this.pageMonitor);
            this.contentHost.Controls.Add(this.pageFeatures);
            this.contentHost.Controls.Add(this.pageConfig);
            this.contentHost.Controls.Add(this.pageDashboard);
            this.contentHost.Dock = System.Windows.Forms.DockStyle.Fill;
            this.contentHost.Location = new System.Drawing.Point(210, 74);
            this.contentHost.Name = "contentHost";
            this.contentHost.Padding = new System.Windows.Forms.Padding(20, 18, 20, 18);
            this.contentHost.TabIndex = 2;

            ConfigurePage(this.pageDashboard, "pageDashboard");
            ConfigurePage(this.pageConfig, "pageConfig");
            this.pageConfig.Visible = false;
            ConfigurePage(this.pageFeatures, "pageFeatures");
            this.pageFeatures.Visible = false;
            ConfigurePage(this.pageMonitor, "pageMonitor");
            this.pageMonitor.Visible = false;

            this.lblHeroTitle.AutoSize = true;
            this.lblHeroTitle.Font = new System.Drawing.Font("Microsoft YaHei", 20F, System.Drawing.FontStyle.Bold);
            this.lblHeroTitle.ForeColor = System.Drawing.Color.FromArgb(30, 64, 175);
            this.lblHeroTitle.Location = new System.Drawing.Point(4, 2);
            this.lblHeroTitle.Name = "lblHeroTitle";
            this.lblHeroTitle.Size = new System.Drawing.Size(250, 38);
            this.lblHeroTitle.TabIndex = 0;
            this.lblHeroTitle.Text = "热点状态检测中";
            this.pageDashboard.Controls.Add(this.lblHeroTitle);

            this.lblHeroStatus.Font = new System.Drawing.Font("Microsoft YaHei", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblHeroStatus.ForeColor = System.Drawing.Color.FromArgb(71, 85, 105);
            this.lblHeroStatus.Location = new System.Drawing.Point(6, 44);
            this.lblHeroStatus.Name = "lblHeroStatus";
            this.lblHeroStatus.Size = new System.Drawing.Size(1030, 42);
            this.lblHeroStatus.TabIndex = 1;
            this.lblHeroStatus.Text = "正在检测热点、代理和监控状态...";
            this.pageDashboard.Controls.Add(this.lblHeroStatus);

            AddMetricCard(this.pageDashboard, 0, 100, "热点", out this.lblMetricHotspot, "启动 / 停止状态");
            AddMetricCard(this.pageDashboard, 210, 100, "代理", out this.lblMetricProxy, "hotspot-proxy");
            AddMetricCard(this.pageDashboard, 420, 100, "监控", out this.lblMetricMonitor, "TTL / DNS");
            AddMetricCard(this.pageDashboard, 630, 100, "设备", out this.lblMetricClients, "已连接客户端");
            AddMetricCard(this.pageDashboard, 840, 100, "异常", out this.lblMetricIssues, "需要关注的项目");

            System.Windows.Forms.Panel alertCard = NewCard("需要处理", 0, 222, 680, 180);
            this.lblAlerts.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblAlerts.ForeColor = System.Drawing.Color.FromArgb(51, 65, 85);
            this.lblAlerts.Location = new System.Drawing.Point(16, 42);
            this.lblAlerts.Name = "lblAlerts";
            this.lblAlerts.Size = new System.Drawing.Size(648, 122);
            this.lblAlerts.TabIndex = 0;
            this.lblAlerts.Text = "正在检查...";
            alertCard.Controls.Add(this.lblAlerts);
            this.pageDashboard.Controls.Add(alertCard);

            System.Windows.Forms.Panel actionCard = NewCard("快捷操作", 692, 222, 346, 180);
            this.btnDashboardStart = NewPrimaryButton("启动热点", 16, 44, 150, 38);
            this.btnDashboardStart.Click += new System.EventHandler(this.btnStart_Click);
            this.btnDashboardStop = NewDangerButton("停止热点", 178, 44, 150, 38);
            this.btnDashboardStop.Click += new System.EventHandler(this.btnStop_Click);
            this.btnDashboardAdvanced = NewSecondaryButton("高级代理设置", 16, 92, 312, 36);
            this.btnDashboardAdvanced.Click += new System.EventHandler(this.btnProxy_Click);
            this.btnDashboardRefresh = NewSecondaryButton("立即刷新状态", 16, 136, 312, 36);
            this.btnDashboardRefresh.Click += new System.EventHandler(this.btnDashboardRefresh_Click);
            actionCard.Controls.Add(this.btnDashboardStart);
            actionCard.Controls.Add(this.btnDashboardStop);
            actionCard.Controls.Add(this.btnDashboardAdvanced);
            actionCard.Controls.Add(this.btnDashboardRefresh);
            this.pageDashboard.Controls.Add(actionCard);

            System.Windows.Forms.Panel stateCard = NewCard("功能状态总览", 0, 418, 1038, 250);
            this.lblDashProxy = NewStateLabel(18, 48);
            this.lblDashNetwork = NewStateLabel(18, 82);
            this.lblDashMonitor = NewStateLabel(18, 116);
            this.lblDashGuards = NewStateLabel(18, 150);
            this.lblDashHeader = NewStateLabel(18, 184);
            this.lblDashTun = NewStateLabel(540, 48);
            stateCard.Controls.Add(this.lblDashProxy);
            stateCard.Controls.Add(this.lblDashNetwork);
            stateCard.Controls.Add(this.lblDashMonitor);
            stateCard.Controls.Add(this.lblDashGuards);
            stateCard.Controls.Add(this.lblDashHeader);
            stateCard.Controls.Add(this.lblDashTun);
            this.pageDashboard.Controls.Add(stateCard);

            this.lblFooter.AutoSize = true;
            this.lblFooter.ForeColor = System.Drawing.Color.FromArgb(148, 163, 184);
            this.lblFooter.Location = new System.Drawing.Point(6, 680);
            this.lblFooter.Name = "lblFooter";
            this.lblFooter.Size = new System.Drawing.Size(700, 17);
            this.lblFooter.TabIndex = 0;
            this.lblFooter.Text = "需要管理员权限运行 · Windows 10/11 x86/x64 · 版本 1.0";
            this.pageDashboard.Controls.Add(this.lblFooter);

            this.grpSettings.BackColor = System.Drawing.Color.White;
            this.grpSettings.Controls.Add(this.lblTopology);
            this.grpSettings.Controls.Add(this.cmbTopology);
            this.grpSettings.Controls.Add(this.lblSsid);
            this.grpSettings.Controls.Add(this.txtSsid);
            this.grpSettings.Controls.Add(this.lblPassword);
            this.grpSettings.Controls.Add(this.txtPassword);
            this.grpSettings.Controls.Add(this.chkShowPassword);
            this.grpSettings.Controls.Add(this.lblAdapter);
            this.grpSettings.Controls.Add(this.cmbAdapter);
            this.grpSettings.Controls.Add(this.lblWirelessAdapter);
            this.grpSettings.Controls.Add(this.cmbWirelessAdapter);
            this.grpSettings.Controls.Add(this.lblDirection);
            this.grpSettings.Controls.Add(this.btnProxy);
            this.grpSettings.Location = new System.Drawing.Point(0, 0);
            this.grpSettings.Name = "grpSettings";
            this.grpSettings.Size = new System.Drawing.Size(720, 260);
            this.grpSettings.TabIndex = 0;
            this.pageConfig.Controls.Add(this.grpSettings);

            this.lblTopology.AutoSize = true;
            this.lblTopology.Location = new System.Drawing.Point(18, 24);
            this.lblTopology.Name = "lblTopology";
            this.lblTopology.Size = new System.Drawing.Size(90, 17);
            this.lblTopology.TabIndex = 0;
            this.lblTopology.Text = "网络拓扑:";

            this.cmbTopology.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbTopology.FormattingEnabled = true;
            this.cmbTopology.Items.AddRange(new object[] { "自动（当前行为）", "有线入口 → 无线热点" });
            this.cmbTopology.Location = new System.Drawing.Point(120, 21);
            this.cmbTopology.Name = "cmbTopology";
            this.cmbTopology.Size = new System.Drawing.Size(250, 23);
            this.cmbTopology.TabIndex = 1;
            this.cmbTopology.SelectedIndex = 0;
            this.cmbTopology.SelectedIndexChanged += new System.EventHandler(this.cmbTopology_SelectedIndexChanged);

            this.lblSsid.AutoSize = true;
            this.lblSsid.Location = new System.Drawing.Point(18, 64);
            this.lblSsid.Name = "lblSsid";
            this.lblSsid.Size = new System.Drawing.Size(100, 17);
            this.lblSsid.TabIndex = 2;
            this.lblSsid.Text = "网络名称 (SSID):";

            this.txtSsid.Location = new System.Drawing.Point(120, 61);
            this.txtSsid.MaxLength = 32;
            this.txtSsid.Name = "txtSsid";
            this.txtSsid.Size = new System.Drawing.Size(360, 23);
            this.txtSsid.TabIndex = 3;

            this.lblPassword.AutoSize = true;
            this.lblPassword.Location = new System.Drawing.Point(18, 99);
            this.lblPassword.Name = "lblPassword";
            this.lblPassword.Size = new System.Drawing.Size(44, 17);
            this.lblPassword.TabIndex = 4;
            this.lblPassword.Text = "密码:";

            this.txtPassword.Location = new System.Drawing.Point(120, 96);
            this.txtPassword.MaxLength = 63;
            this.txtPassword.Name = "txtPassword";
            this.txtPassword.PasswordChar = '*';
            this.txtPassword.Size = new System.Drawing.Size(300, 23);
            this.txtPassword.TabIndex = 5;

            this.chkShowPassword.AutoSize = true;
            this.chkShowPassword.Location = new System.Drawing.Point(430, 98);
            this.chkShowPassword.Name = "chkShowPassword";
            this.chkShowPassword.Size = new System.Drawing.Size(60, 21);
            this.chkShowPassword.TabIndex = 6;
            this.chkShowPassword.Text = "显示";
            this.chkShowPassword.UseVisualStyleBackColor = true;
            this.chkShowPassword.CheckedChanged += new System.EventHandler(this.chkShowPassword_CheckedChanged);

            this.lblAdapter.AutoSize = true;
            this.lblAdapter.Location = new System.Drawing.Point(18, 134);
            this.lblAdapter.Name = "lblAdapter";
            this.lblAdapter.Size = new System.Drawing.Size(80, 17);
            this.lblAdapter.TabIndex = 7;
            this.lblAdapter.Text = "共享网络:";

            this.cmbAdapter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbAdapter.FormattingEnabled = true;
            this.cmbAdapter.Location = new System.Drawing.Point(120, 131);
            this.cmbAdapter.Name = "cmbAdapter";
            this.cmbAdapter.Size = new System.Drawing.Size(250, 23);
            this.cmbAdapter.TabIndex = 8;

            this.lblWirelessAdapter.AutoSize = true;
            this.lblWirelessAdapter.Location = new System.Drawing.Point(18, 169);
            this.lblWirelessAdapter.Name = "lblWirelessAdapter";
            this.lblWirelessAdapter.Size = new System.Drawing.Size(90, 17);
            this.lblWirelessAdapter.TabIndex = 9;
            this.lblWirelessAdapter.Text = "无线热点网卡:";
            this.lblWirelessAdapter.Visible = false;

            this.cmbWirelessAdapter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbWirelessAdapter.FormattingEnabled = true;
            this.cmbWirelessAdapter.Location = new System.Drawing.Point(120, 166);
            this.cmbWirelessAdapter.Name = "cmbWirelessAdapter";
            this.cmbWirelessAdapter.Size = new System.Drawing.Size(250, 23);
            this.cmbWirelessAdapter.TabIndex = 10;
            this.cmbWirelessAdapter.Visible = false;

            this.lblDirection.AutoSize = false;
            this.lblDirection.ForeColor = System.Drawing.Color.FromArgb(37, 99, 235);
            this.lblDirection.Location = new System.Drawing.Point(18, 199);
            this.lblDirection.Name = "lblDirection";
            this.lblDirection.Size = new System.Drawing.Size(660, 24);
            this.lblDirection.TabIndex = 11;
            this.lblDirection.Visible = false;

            this.btnProxy = NewSecondaryButton("高级代理设置", 500, 131, 200, 30);
            this.btnProxy.Click += new System.EventHandler(this.btnProxy_Click);
            this.grpSettings.Controls.Add(this.btnProxy);

            this.btnStart = NewPrimaryButton("启动热点", 0, 282, 150, 42);
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
            this.btnStop = NewDangerButton("停止热点", 160, 282, 150, 42);
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);
            this.btnRefresh = NewSecondaryButton("刷新网卡", 320, 282, 150, 42);
            this.btnRefresh.Click += new System.EventHandler(this.btnRefresh_Click);
            this.btnSave = NewSecondaryButton("保存设置", 480, 282, 150, 42);
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);
            this.pageConfig.Controls.Add(this.btnStart);
            this.pageConfig.Controls.Add(this.btnStop);
            this.pageConfig.Controls.Add(this.btnRefresh);
            this.pageConfig.Controls.Add(this.btnSave);

            this.lblConfigHint.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblConfigHint.ForeColor = System.Drawing.Color.FromArgb(100, 116, 139);
            this.lblConfigHint.Location = new System.Drawing.Point(4, 342);
            this.lblConfigHint.Name = "lblConfigHint";
            this.lblConfigHint.Size = new System.Drawing.Size(900, 60);
            this.lblConfigHint.TabIndex = 0;
            this.lblConfigHint.Text = "启动热点会自动拉起代理及已启用的防护模块；停止或关闭软件会同步清理代理进程和 WinDivert 句柄。";
            this.pageConfig.Controls.Add(this.lblConfigHint);

            System.Windows.Forms.Panel featureCard = NewCard("功能开关", 0, 0, 1038, 500);
            AddFeatureRow(featureCard, "代理服务（随热点启动）", "启动热点时自动运行 hotspot-proxy.exe", 42, this.chkProxyEnabled, this.lblProxyState);
            AddFeatureRow(featureCard, "HTTP Header 画像", "统一 UA / Client Hints / Accept，清理探针泄露头", 86, this.chkHeaders, this.lblHeaderState);
            AddFeatureRow(featureCard, "DeviceForge", "UDP / DNS / NTP / mDNS / DHCP 指纹收敛", 130, this.chkDeviceForge, this.lblDeviceForgeState);
            AddFeatureRow(featureCard, "网络层归一化", "WinDivert 强制 TTL / IPID / TCP 选项", 174, this.chkNetworkNormalizer, this.lblNetworkState);
            AddFeatureRow(featureCard, "全局 TUN 接管", "Wintun 接管 IPv4 默认路由与 DNS", 218, this.chkTun, this.lblTunState);
            AddFeatureRow(featureCard, "IPv6 转发封禁", "检测到公网 IPv6 时自动启用", 262, this.chkIPv6Guard, this.lblIPv6State);
            AddFeatureRow(featureCard, "Portal 封堵", "丢弃到认证门户主 / 备 IP 的转发包", 306, this.chkPortalBlock, this.lblPortalState);
            AddFeatureRow(featureCard, "出站封堵", "QUIC / DoT / LLMNR / WS-D / 广播组播", 350, this.chkOutboundGuard, this.lblOutboundState);
            AddFeatureRow(featureCard, "TTL 监控", "只读关联同流 TTL 差异", 394, this.chkMonitorTtl, this.lblMonitorTtlState);
            AddFeatureRow(featureCard, "DNS 监控", "只读 sniff UDP:53 判定代理链路", 438, this.chkMonitorDns, this.lblMonitorDnsState);
            this.pageFeatures.Controls.Add(featureCard);

            this.btnFeatureSave = NewPrimaryButton("保存并应用", 0, 516, 160, 40);
            this.btnFeatureSave.Click += new System.EventHandler(this.btnFeatureSave_Click);
            this.btnFeatureAdvanced = NewSecondaryButton("更多代理参数", 172, 516, 160, 40);
            this.btnFeatureAdvanced.Click += new System.EventHandler(this.btnProxy_Click);
            this.lblFeatureHint.ForeColor = System.Drawing.Color.FromArgb(100, 116, 139);
            this.lblFeatureHint.Location = new System.Drawing.Point(344, 522);
            this.lblFeatureHint.Name = "lblFeatureHint";
            this.lblFeatureHint.Size = new System.Drawing.Size(680, 30);
            this.lblFeatureHint.TabIndex = 0;
            this.lblFeatureHint.Text = "开关会写入 config.json；热点运行时会立即应用可热切换项，其余项在下次启动热点时生效。";
            this.pageFeatures.Controls.Add(this.btnFeatureSave);
            this.pageFeatures.Controls.Add(this.btnFeatureAdvanced);
            this.pageFeatures.Controls.Add(this.lblFeatureHint);

            System.Windows.Forms.Panel clientCard = NewCard("已连接设备", 0, 0, 680, 520);
            this.lstClients.FormattingEnabled = true;
            this.lstClients.Location = new System.Drawing.Point(16, 44);
            this.lstClients.Name = "lstClients";
            this.lstClients.Size = new System.Drawing.Size(460, 440);
            this.lstClients.TabIndex = 0;
            this.btnRefreshClients = NewSecondaryButton("刷新列表", 490, 44, 174, 36);
            this.btnRefreshClients.Click += new System.EventHandler(this.btnRefreshClients_Click);
            clientCard.Controls.Add(this.lstClients);
            clientCard.Controls.Add(this.btnRefreshClients);
            this.pageMonitor.Controls.Add(clientCard);

            System.Windows.Forms.Panel logCard = NewCard("探针日志", 692, 0, 346, 520);
            this.txtMonitorLog.Location = new System.Drawing.Point(16, 44);
            this.txtMonitorLog.Multiline = true;
            this.txtMonitorLog.Name = "txtMonitorLog";
            this.txtMonitorLog.ReadOnly = true;
            this.txtMonitorLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtMonitorLog.Size = new System.Drawing.Size(314, 440);
            this.txtMonitorLog.TabIndex = 0;
            this.txtMonitorLog.BackColor = System.Drawing.Color.White;
            this.txtMonitorLog.Font = new System.Drawing.Font("Consolas", 9F);
            logCard.Controls.Add(this.txtMonitorLog);
            this.pageMonitor.Controls.Add(logCard);

            System.Windows.Forms.Panel detailCard = NewCard("运行状态明细", 0, 532, 1038, 140);
            this.lblStatus.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblStatus.ForeColor = System.Drawing.Color.FromArgb(51, 65, 85);
            this.lblStatus.Location = new System.Drawing.Point(16, 42);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(1000, 80);
            this.lblStatus.TabIndex = 0;
            this.lblStatus.Text = "正在检测...";
            detailCard.Controls.Add(this.lblStatus);
            this.pageMonitor.Controls.Add(detailCard);

            this.timerStatus.Interval = 5000;
            this.timerStatus.Tick += new System.EventHandler(this.timerStatus_Tick);
            this.timerMonitor.Interval = 2000;
            this.timerMonitor.Tick += new System.EventHandler(this.timerMonitor_Tick);

            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1320, 800);
            this.Controls.Add(this.contentHost);
            this.Controls.Add(this.header);
            this.Controls.Add(this.sidebar);
            this.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.MinimumSize = new System.Drawing.Size(1180, 700);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "OneNetShare 控制台";
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.ResumeLayout(false);
        }

        private static void ConfigurePage(System.Windows.Forms.Panel page, string name)
        {
            page.AutoScroll = true;
            page.AutoScrollMinSize = new System.Drawing.Size(1040, 700);
            page.BackColor = System.Drawing.Color.FromArgb(245, 247, 251);
            page.Dock = System.Windows.Forms.DockStyle.Fill;
            page.Location = new System.Drawing.Point(20, 18);
            page.Name = name;
            page.Size = new System.Drawing.Size(1070, 708);
        }

        private static System.Windows.Forms.Button NewSideButton(string text, int y, System.EventHandler click)
        {
            var button = new System.Windows.Forms.Button();
            button.BackColor = System.Drawing.Color.FromArgb(30, 41, 59);
            button.FlatAppearance.BorderSize = 0;
            button.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            button.ForeColor = System.Drawing.Color.FromArgb(226, 232, 240);
            button.Location = new System.Drawing.Point(14, y);
            button.Name = "nav" + text;
            button.Padding = new System.Windows.Forms.Padding(16, 0, 0, 0);
            button.Size = new System.Drawing.Size(182, 38);
            button.TabIndex = 2;
            button.Text = text;
            button.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            button.UseVisualStyleBackColor = false;
            button.Click += click;
            return button;
        }

        private static System.Windows.Forms.Panel NewCard(string title, int x, int y, int width, int height)
        {
            var card = new System.Windows.Forms.Panel();
            card.BackColor = System.Drawing.Color.White;
            card.Location = new System.Drawing.Point(x, y);
            card.Name = "card" + title;
            card.Size = new System.Drawing.Size(width, height);
            var label = new System.Windows.Forms.Label();
            label.AutoSize = true;
            label.Font = new System.Drawing.Font("Microsoft YaHei", 11F, System.Drawing.FontStyle.Bold);
            label.ForeColor = System.Drawing.Color.FromArgb(30, 41, 59);
            label.Location = new System.Drawing.Point(16, 12);
            label.Text = title;
            card.Controls.Add(label);
            return card;
        }

        private static void AddMetricCard(System.Windows.Forms.Control parent, int x, int y, string title, out System.Windows.Forms.Label value, string hint)
        {
            var card = NewCard(title, x, y, 198, 105);
            value = new System.Windows.Forms.Label();
            value.AutoSize = true;
            value.Font = new System.Drawing.Font("Microsoft YaHei", 18F, System.Drawing.FontStyle.Bold);
            value.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            value.Location = new System.Drawing.Point(16, 40);
            value.Text = "检测中";
            var hintLabel = new System.Windows.Forms.Label();
            hintLabel.AutoSize = true;
            hintLabel.ForeColor = System.Drawing.Color.FromArgb(100, 116, 139);
            hintLabel.Location = new System.Drawing.Point(16, 76);
            hintLabel.Text = hint;
            card.Controls.Add(value);
            card.Controls.Add(hintLabel);
            parent.Controls.Add(card);
        }

        private static System.Windows.Forms.Label NewStateLabel(int x, int y)
        {
            var label = new System.Windows.Forms.Label();
            label.Font = new System.Drawing.Font("Microsoft YaHei", 10F, System.Drawing.FontStyle.Bold);
            label.Location = new System.Drawing.Point(x, y);
            label.Name = "state" + y;
            label.Size = new System.Drawing.Size(470, 26);
            label.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            label.Text = "检测中";
            return label;
        }

        private static void AddFeatureRow(System.Windows.Forms.Control parent, string title, string description, int y, System.Windows.Forms.CheckBox toggle, System.Windows.Forms.Label state)
        {
            toggle.AutoSize = false;
            toggle.Location = new System.Drawing.Point(20, y + 12);
            toggle.Name = "chk" + title;
            toggle.Size = new System.Drawing.Size(20, 20);
            toggle.UseVisualStyleBackColor = true;
            var titleLabel = new System.Windows.Forms.Label();
            titleLabel.AutoSize = true;
            titleLabel.Font = new System.Drawing.Font("Microsoft YaHei", 10F, System.Drawing.FontStyle.Bold);
            titleLabel.ForeColor = System.Drawing.Color.FromArgb(30, 41, 59);
            titleLabel.Location = new System.Drawing.Point(52, y + 4);
            titleLabel.Text = title;
            var detail = new System.Windows.Forms.Label();
            detail.AutoSize = true;
            detail.ForeColor = System.Drawing.Color.FromArgb(100, 116, 139);
            detail.Location = new System.Drawing.Point(52, y + 26);
            detail.Text = description;
            state.Font = new System.Drawing.Font("Microsoft YaHei", 10F, System.Drawing.FontStyle.Bold);
            state.Location = new System.Drawing.Point(720, y + 12);
            state.Size = new System.Drawing.Size(300, 24);
            state.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            state.Text = "检测中";
            parent.Controls.Add(toggle);
            parent.Controls.Add(titleLabel);
            parent.Controls.Add(detail);
            parent.Controls.Add(state);
        }

        private static System.Windows.Forms.Button NewPrimaryButton(string text, int x, int y, int width, int height)
        {
            var button = new System.Windows.Forms.Button();
            button.BackColor = System.Drawing.Color.FromArgb(37, 99, 235);
            button.FlatAppearance.BorderSize = 0;
            button.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            button.ForeColor = System.Drawing.Color.White;
            button.Location = new System.Drawing.Point(x, y);
            button.Size = new System.Drawing.Size(width, height);
            button.TabIndex = 3;
            button.Text = text;
            button.UseVisualStyleBackColor = false;
            return button;
        }

        private static System.Windows.Forms.Button NewDangerButton(string text, int x, int y, int width, int height)
        {
            var button = new System.Windows.Forms.Button();
            button.BackColor = System.Drawing.Color.FromArgb(220, 53, 69);
            button.FlatAppearance.BorderSize = 0;
            button.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            button.ForeColor = System.Drawing.Color.White;
            button.Location = new System.Drawing.Point(x, y);
            button.Size = new System.Drawing.Size(width, height);
            button.TabIndex = 4;
            button.Text = text;
            button.UseVisualStyleBackColor = false;
            return button;
        }

        private static System.Windows.Forms.Button NewSecondaryButton(string text, int x, int y, int width, int height)
        {
            var button = new System.Windows.Forms.Button();
            button.BackColor = System.Drawing.Color.White;
            button.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(203, 213, 225);
            button.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            button.ForeColor = System.Drawing.Color.FromArgb(51, 65, 85);
            button.Location = new System.Drawing.Point(x, y);
            button.Size = new System.Drawing.Size(width, height);
            button.TabIndex = 5;
            button.Text = text;
            button.UseVisualStyleBackColor = false;
            return button;
        }

        #endregion

        private System.Windows.Forms.Panel sidebar;
        private System.Windows.Forms.Label lblBrand;
        private System.Windows.Forms.Label lblBrandSub;
        private System.Windows.Forms.Button navDashboard;
        private System.Windows.Forms.Button navConfig;
        private System.Windows.Forms.Button navFeatures;
        private System.Windows.Forms.Button navMonitor;
        private System.Windows.Forms.Label lblSidebarStatus;
        private System.Windows.Forms.Button btnSideDiagnosis;
        private System.Windows.Forms.Panel header;
        private System.Windows.Forms.Label lblHeaderTitle;
        private System.Windows.Forms.Label lblHeaderSubtitle;
        private System.Windows.Forms.Button btnQuickStart;
        private System.Windows.Forms.Button btnQuickStop;
        private System.Windows.Forms.Panel contentHost;
        private System.Windows.Forms.Panel pageDashboard;
        private System.Windows.Forms.Panel pageConfig;
        private System.Windows.Forms.Panel pageFeatures;
        private System.Windows.Forms.Panel pageMonitor;
        private System.Windows.Forms.Label lblHeroTitle;
        private System.Windows.Forms.Label lblHeroStatus;
        private System.Windows.Forms.Label lblMetricHotspot;
        private System.Windows.Forms.Label lblMetricProxy;
        private System.Windows.Forms.Label lblMetricMonitor;
        private System.Windows.Forms.Label lblMetricClients;
        private System.Windows.Forms.Label lblMetricIssues;
        private System.Windows.Forms.Label lblAlerts;
        private System.Windows.Forms.Label lblDashProxy;
        private System.Windows.Forms.Label lblDashNetwork;
        private System.Windows.Forms.Label lblDashMonitor;
        private System.Windows.Forms.Label lblDashGuards;
        private System.Windows.Forms.Label lblDashHeader;
        private System.Windows.Forms.Label lblDashTun;
        private System.Windows.Forms.Button btnDashboardStart;
        private System.Windows.Forms.Button btnDashboardStop;
        private System.Windows.Forms.Button btnDashboardAdvanced;
        private System.Windows.Forms.Button btnDashboardRefresh;
        private System.Windows.Forms.Panel grpSettings;
        private System.Windows.Forms.Label lblTopology;
        private System.Windows.Forms.ComboBox cmbTopology;
        private System.Windows.Forms.Label lblSsid;
        private System.Windows.Forms.TextBox txtSsid;
        private System.Windows.Forms.Label lblPassword;
        private System.Windows.Forms.TextBox txtPassword;
        private System.Windows.Forms.CheckBox chkShowPassword;
        private System.Windows.Forms.Label lblAdapter;
        private System.Windows.Forms.ComboBox cmbAdapter;
        private System.Windows.Forms.Label lblWirelessAdapter;
        private System.Windows.Forms.ComboBox cmbWirelessAdapter;
        private System.Windows.Forms.Label lblDirection;
        private System.Windows.Forms.Button btnProxy;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.Button btnRefresh;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Label lblConfigHint;
        private System.Windows.Forms.CheckBox chkProxyEnabled;
        private System.Windows.Forms.CheckBox chkHeaders;
        private System.Windows.Forms.CheckBox chkDeviceForge;
        private System.Windows.Forms.CheckBox chkNetworkNormalizer;
        private System.Windows.Forms.CheckBox chkTun;
        private System.Windows.Forms.CheckBox chkIPv6Guard;
        private System.Windows.Forms.CheckBox chkPortalBlock;
        private System.Windows.Forms.CheckBox chkOutboundGuard;
        private System.Windows.Forms.CheckBox chkMonitorTtl;
        private System.Windows.Forms.CheckBox chkMonitorDns;
        private System.Windows.Forms.Label lblProxyState;
        private System.Windows.Forms.Label lblHeaderState;
        private System.Windows.Forms.Label lblDeviceForgeState;
        private System.Windows.Forms.Label lblNetworkState;
        private System.Windows.Forms.Label lblTunState;
        private System.Windows.Forms.Label lblIPv6State;
        private System.Windows.Forms.Label lblPortalState;
        private System.Windows.Forms.Label lblOutboundState;
        private System.Windows.Forms.Label lblMonitorTtlState;
        private System.Windows.Forms.Label lblMonitorDnsState;
        private System.Windows.Forms.Button btnFeatureSave;
        private System.Windows.Forms.Button btnFeatureAdvanced;
        private System.Windows.Forms.Label lblFeatureHint;
        private System.Windows.Forms.Panel grpClients;
        private System.Windows.Forms.ListBox lstClients;
        private System.Windows.Forms.Button btnRefreshClients;
        private System.Windows.Forms.TextBox txtMonitorLog;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Label lblFooter;
        private System.Windows.Forms.Timer timerStatus;
        private System.Windows.Forms.Timer timerMonitor;
    }
}
