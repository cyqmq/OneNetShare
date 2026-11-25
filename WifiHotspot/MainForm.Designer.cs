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
            this.grpSettings = new System.Windows.Forms.GroupBox();
            this.lblSsid = new System.Windows.Forms.Label();
            this.txtSsid = new System.Windows.Forms.TextBox();
            this.lblPassword = new System.Windows.Forms.Label();
            this.txtPassword = new System.Windows.Forms.TextBox();
            this.chkShowPassword = new System.Windows.Forms.CheckBox();
            this.lblAdapter = new System.Windows.Forms.Label();
            this.cmbAdapter = new System.Windows.Forms.ComboBox();
            this.btnStart = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.btnRefresh = new System.Windows.Forms.Button();
            this.btnSave = new System.Windows.Forms.Button();
            this.grpStatus = new System.Windows.Forms.GroupBox();
            this.lblStatus = new System.Windows.Forms.Label();
            this.grpClients = new System.Windows.Forms.GroupBox();
            this.lstClients = new System.Windows.Forms.ListBox();
            this.btnRefreshClients = new System.Windows.Forms.Button();
            this.lblFooter = new System.Windows.Forms.Label();
            this.timerStatus = new System.Windows.Forms.Timer(this.components);
            this.grpSettings.SuspendLayout();
            this.grpStatus.SuspendLayout();
            this.grpClients.SuspendLayout();
            this.SuspendLayout();

            // grpSettings
            this.grpSettings.Controls.Add(this.lblSsid);
            this.grpSettings.Controls.Add(this.txtSsid);
            this.grpSettings.Controls.Add(this.lblPassword);
            this.grpSettings.Controls.Add(this.txtPassword);
            this.grpSettings.Controls.Add(this.chkShowPassword);
            this.grpSettings.Controls.Add(this.lblAdapter);
            this.grpSettings.Controls.Add(this.cmbAdapter);
            this.grpSettings.Location = new System.Drawing.Point(12, 12);
            this.grpSettings.Name = "grpSettings";
            this.grpSettings.Size = new System.Drawing.Size(460, 145);
            this.grpSettings.TabIndex = 0;
            this.grpSettings.TabStop = false;
            this.grpSettings.Text = "热点设置";

            // lblSsid
            this.lblSsid.AutoSize = true;
            this.lblSsid.Location = new System.Drawing.Point(15, 28);
            this.lblSsid.Name = "lblSsid";
            this.lblSsid.Size = new System.Drawing.Size(95, 17);
            this.lblSsid.TabIndex = 0;
            this.lblSsid.Text = "网络名称 (SSID):";

            // txtSsid
            this.txtSsid.Location = new System.Drawing.Point(120, 25);
            this.txtSsid.MaxLength = 32;
            this.txtSsid.Name = "txtSsid";
            this.txtSsid.Size = new System.Drawing.Size(320, 23);
            this.txtSsid.TabIndex = 1;

            // lblPassword
            this.lblPassword.AutoSize = true;
            this.lblPassword.Location = new System.Drawing.Point(15, 63);
            this.lblPassword.Name = "lblPassword";
            this.lblPassword.Size = new System.Drawing.Size(40, 17);
            this.lblPassword.TabIndex = 2;
            this.lblPassword.Text = "密码:";

            // txtPassword
            this.txtPassword.Location = new System.Drawing.Point(120, 60);
            this.txtPassword.MaxLength = 63;
            this.txtPassword.Name = "txtPassword";
            this.txtPassword.PasswordChar = '*';
            this.txtPassword.Size = new System.Drawing.Size(265, 23);
            this.txtPassword.TabIndex = 3;

            // chkShowPassword
            this.chkShowPassword.AutoSize = true;
            this.chkShowPassword.Location = new System.Drawing.Point(392, 62);
            this.chkShowPassword.Name = "chkShowPassword";
            this.chkShowPassword.Size = new System.Drawing.Size(60, 21);
            this.chkShowPassword.TabIndex = 4;
            this.chkShowPassword.Text = "显示";
            this.chkShowPassword.UseVisualStyleBackColor = true;
            this.chkShowPassword.CheckedChanged += new System.EventHandler(this.chkShowPassword_CheckedChanged);

            // lblAdapter
            this.lblAdapter.AutoSize = true;
            this.lblAdapter.Location = new System.Drawing.Point(15, 100);
            this.lblAdapter.Name = "lblAdapter";
            this.lblAdapter.Size = new System.Drawing.Size(68, 17);
            this.lblAdapter.TabIndex = 5;
            this.lblAdapter.Text = "共享网络:";

            // cmbAdapter
            this.cmbAdapter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbAdapter.FormattingEnabled = true;
            this.cmbAdapter.Location = new System.Drawing.Point(120, 97);
            this.cmbAdapter.Name = "cmbAdapter";
            this.cmbAdapter.Size = new System.Drawing.Size(320, 23);
            this.cmbAdapter.TabIndex = 6;

            // btnStart
            this.btnStart.Location = new System.Drawing.Point(12, 163);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(105, 35);
            this.btnStart.TabIndex = 7;
            this.btnStart.Text = "启动热点";
            this.btnStart.UseVisualStyleBackColor = true;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);

            // btnStop
            this.btnStop.Location = new System.Drawing.Point(123, 163);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(105, 35);
            this.btnStop.TabIndex = 8;
            this.btnStop.Text = "停止热点";
            this.btnStop.UseVisualStyleBackColor = true;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);

            // btnRefresh
            this.btnRefresh.Location = new System.Drawing.Point(234, 163);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Size = new System.Drawing.Size(105, 35);
            this.btnRefresh.TabIndex = 9;
            this.btnRefresh.Text = "刷新网卡";
            this.btnRefresh.UseVisualStyleBackColor = true;
            this.btnRefresh.Click += new System.EventHandler(this.btnRefresh_Click);

            // btnSave
            this.btnSave.Location = new System.Drawing.Point(345, 163);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(127, 35);
            this.btnSave.TabIndex = 10;
            this.btnSave.Text = "保存设置";
            this.btnSave.UseVisualStyleBackColor = true;
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);

            // grpStatus
            this.grpStatus.Controls.Add(this.lblStatus);
            this.grpStatus.Location = new System.Drawing.Point(12, 204);
            this.grpStatus.Name = "grpStatus";
            this.grpStatus.Size = new System.Drawing.Size(460, 80);
            this.grpStatus.TabIndex = 11;
            this.grpStatus.TabStop = false;
            this.grpStatus.Text = "运行状态";

            // lblStatus
            this.lblStatus.Location = new System.Drawing.Point(15, 22);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(430, 48);
            this.lblStatus.TabIndex = 0;
            this.lblStatus.Text = "正在检测...";
            this.lblStatus.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));

            // grpClients
            this.grpClients.Controls.Add(this.lstClients);
            this.grpClients.Controls.Add(this.btnRefreshClients);
            this.grpClients.Location = new System.Drawing.Point(12, 290);
            this.grpClients.Name = "grpClients";
            this.grpClients.Size = new System.Drawing.Size(460, 160);
            this.grpClients.TabIndex = 12;
            this.grpClients.TabStop = false;
            this.grpClients.Text = "已连接设备";

            // lstClients
            this.lstClients.FormattingEnabled = true;
            this.lstClients.Location = new System.Drawing.Point(15, 22);
            this.lstClients.Name = "lstClients";
            this.lstClients.Size = new System.Drawing.Size(330, 122);
            this.lstClients.TabIndex = 0;

            // btnRefreshClients
            this.btnRefreshClients.Location = new System.Drawing.Point(353, 22);
            this.btnRefreshClients.Name = "btnRefreshClients";
            this.btnRefreshClients.Size = new System.Drawing.Size(92, 35);
            this.btnRefreshClients.TabIndex = 1;
            this.btnRefreshClients.Text = "刷新列表";
            this.btnRefreshClients.UseVisualStyleBackColor = true;
            this.btnRefreshClients.Click += new System.EventHandler(this.btnRefreshClients_Click);

            // lblFooter
            this.lblFooter.AutoSize = true;
            this.lblFooter.ForeColor = System.Drawing.Color.Gray;
            this.lblFooter.Location = new System.Drawing.Point(15, 455);
            this.lblFooter.Name = "lblFooter";
            this.lblFooter.Size = new System.Drawing.Size(440, 17);
            this.lblFooter.TabIndex = 13;
            this.lblFooter.Text = "需要管理员权限运行 · Windows 10/11 x86 · 版本 1.0";

            // timerStatus
            this.timerStatus.Interval = 5000;
            this.timerStatus.Tick += new System.EventHandler(this.timerStatus_Tick);

            // MainForm
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(484, 480);
            this.Controls.Add(this.grpSettings);
            this.Controls.Add(this.btnStart);
            this.Controls.Add(this.btnStop);
            this.Controls.Add(this.btnRefresh);
            this.Controls.Add(this.btnSave);
            this.Controls.Add(this.grpStatus);
            this.Controls.Add(this.grpClients);
            this.Controls.Add(this.lblFooter);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.Text = "WiFi 热点共享工具";
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.grpSettings.ResumeLayout(false);
            this.grpSettings.PerformLayout();
            this.grpStatus.ResumeLayout(false);
            this.grpClients.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.GroupBox grpSettings;
        private System.Windows.Forms.Label lblSsid;
        private System.Windows.Forms.TextBox txtSsid;
        private System.Windows.Forms.Label lblPassword;
        private System.Windows.Forms.TextBox txtPassword;
        private System.Windows.Forms.CheckBox chkShowPassword;
        private System.Windows.Forms.Label lblAdapter;
        private System.Windows.Forms.ComboBox cmbAdapter;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.Button btnRefresh;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.GroupBox grpStatus;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.GroupBox grpClients;
        private System.Windows.Forms.ListBox lstClients;
        private System.Windows.Forms.Button btnRefreshClients;
        private System.Windows.Forms.Label lblFooter;
        private System.Windows.Forms.Timer timerStatus;
    }
}
