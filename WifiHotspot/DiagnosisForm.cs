using System;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace WifiHotspot
{
    public class DiagnosisForm : Form
    {
        public DiagnosisForm(HotspotDiagnosis diagnosis)
        {
            Text = "热点问题诊断";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(680, 460);
            Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);

            var text = new TextBox
            {
                ReadOnly = true,
                Multiline = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Font = Font,
                Text = BuildText(diagnosis)
            };

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 8, 8, 8)
            };

            var btnClose = NewButton("关闭");
            btnClose.Click += delegate { Close(); };
            var btnServices = NewButton("打开服务");
            btnServices.Click += delegate { OpenTarget("services.msc"); };
            var btnWifi = NewButton("打开 Wi-Fi 设置");
            btnWifi.Click += delegate { OpenTarget("ms-settings:network-wifi"); };
            var btnHotspot = NewButton("打开移动热点设置");
            btnHotspot.Click += delegate { OpenTarget("ms-settings:network-mobilehotspot"); };

            panel.Controls.Add(btnClose);
            panel.Controls.Add(btnServices);
            panel.Controls.Add(btnWifi);
            panel.Controls.Add(btnHotspot);

            Controls.Add(text);
            Controls.Add(panel);
        }

        private static Button NewButton(string caption)
        {
            return new Button
            {
                Text = caption,
                AutoSize = true,
                MinimumSize = new Size(120, 30),
                UseVisualStyleBackColor = true
            };
        }

        private static string BuildText(HotspotDiagnosis diagnosis)
        {
            var sb = new StringBuilder();
            if (diagnosis == null) return string.Empty;

            if (diagnosis.Issues.Count == 0)
                sb.AppendLine("软件未检测到明确问题，请根据下方原始信息处理。").AppendLine();
            else
                sb.AppendLine("软件检测到以下问题，请按路径修改后重试：").AppendLine();

            for (int i = 0; i < diagnosis.Issues.Count; i++)
            {
                HotspotIssue issue = diagnosis.Issues[i];
                sb.AppendLine(string.Format("{0}. {1}", i + 1, issue.Title));
                if (!string.IsNullOrEmpty(issue.Detail))
                    sb.AppendLine("   检测结果: " + issue.Detail);
                if (!string.IsNullOrEmpty(issue.FixPath))
                    sb.AppendLine("   修改路径: " + issue.FixPath);
                sb.AppendLine();
            }

            sb.AppendLine("原始信息:");
            sb.AppendLine(diagnosis.RawMessage ?? string.Empty);
            return sb.ToString();
        }

        private static void OpenTarget(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开 " + target + ":\n" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
