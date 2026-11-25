# WiFi 热点共享工具 (Windows x86 / OpenWrt)

基于 C# / WinForms (.NET Framework 4.8) 的 Windows 10/11 x86 热点共享程序（原型验证），最终移植目标为 OpenWrt。

- Windows 原型：创建 WiFi 热点 + ICS 共享
- OpenWrt 目标：TTL/Hop Limit 出口归一化 → 详见 [`openwrt/README.md`](openwrt/README.md)
- 透明代理 + DeviceForge → 详见 [`proxy/README.md`](proxy/README.md)

## Windows 与 OpenWrt 功能差异

| 能力 | Windows x86 | OpenWrt |
|------|-------------|---------|
| WinForms 热点/ICS 原型 | ✅ | ❌ |
| HTTP Header 画像 | ✅（forward 代理） | ✅（透明代理） |
| HTTPS CONNECT 盲隧道 | ✅ | ✅ |
| Captive HTTP 代答/Drop | ✅ | ✅ |
| REDIRECT / TPROXY / TUN 接管 | ✅（Wintun 全局 TUN） | ✅ |
| TCP/IP 出口指纹统一 | ✅（Wintun TUN / WinDivert） | ✅（网关重建连接） |
| UDP 全量接管 | ✅（Wintun TUN） | ✅（TUN/TPROXY） |
| DNS / NTP / mDNS / DHCP 归一化 | ✅（Wintun TUN） | ✅ |
| Captive DNS 代答/Drop | ✅（Wintun TUN） | ✅ |
| TTL / Hop Limit 归一化 | ✅（Wintun IPv4 TUN / WinDivert） | ✅ |
| TLS JA3/JA4 统一 | ❌ | ❌ |

## 功能

- 一键创建 WiFi 热点（优先 Windows 移动热点 API，失败回退 `netsh wlan hostednetwork`）
- 自动诊断热点失败原因并给出可执行修改路径与设置入口
- GUI 内置“代理功能”开关窗口，可编辑 HTTP Header/Captive/UDP/DNS/NTP/mDNS/DHCP 开关、设备指纹和随热点启停代理；“UA 预设”下拉框提供内置 UA 库（Chrome/Edge/Firefox/Safari/curl），无需手输，UA/Client Hints/平台版本由同一画像生成，Firefox/Safari 不带 Chromium Hints；强制 TTL 随设备指纹自动填入推荐值（Windows 画像 128，Android/macOS/iOS/Linux 画像 64），手动改偏会提示；Wintun 模式强制从下拉列表选择真实物理出口网卡，过滤 VMware/Hyper-V/WSL 等虚拟网卡；未实现项在窗口内明确标注
- MTU/MSS 钳制自动降级：WinDivert SYN MSS 与 Wintun 合成 SYN-ACK MSS 均可钳制到 1460/1360/1280，便于排查网站卡在 TLS 握手的问题
- 探针日志面板实时滚动显示 Captive 拦截、DNS 规范化、未知 UDP 丢弃事件；一键连通性测试通过本地回显探针验证 HTTP Header 改写，并检查 TUN 默认路由与 IPv6 裸露风险
- 代理自动诊断：启动失败时自动检查 `config.json`、端口格式/占用/Windows 保留端口范围、退出码、进程输出与 `probe.log`，给出端口/权限/文件/TUN 出口网卡等具体原因；代理功能窗口提供“自动诊断”按钮；一键测试会比对配置生成号与代理 `/status`，发现旧进程时自动重启并显示版本/路径；`probe.log` 被代理占用时日志面板与诊断改为共享读
- 网络层指纹归一化：基于 WinDivert 在 Windows 网卡出口强制重写 IPv4 TTL/IPID、IPv6 Hop Limit 与 traffic class/Flow Label 清零、TCP SYN 选项；Flow Label 清零只用于指纹收敛，不代表 IPv6 隐私安全；已随包附带官方 WinDivert 2.2.2 x86 `WinDivert.dll`、`WinDivert32.sys`、`WinDivert64.sys`，需管理员权限
- Wintun 全局 TUN 接管：创建 `OneNetShare Tunnel` 虚拟网卡，只接管 IPv4 默认路由/DNS，在用户态重建 UDP/DNS/NTP 会话并统一出口包指纹，TCP 80 流同样应用 HTTP Header 画像；需要官方 x86 `wintun.dll` 和管理员权限
- IPv6 隐私保护：默认在 WinDivert `NETWORK_FORWARD` 层丢弃全部转发 IPv6，防止手机取得 GUA 后绕过 IPv4 NAT/代理裸奔；可选仅允许 ULA/Link-local 或显式允许 IPv6（不安全），热点启动即生效，与 TUN/网络层归一化互斥关系无关；自动诊断和一键测试会报告热点网卡上的全局 IPv6 地址
- 自动配置 ICS（Internet Connection Sharing）网络共享
- 可自定义 SSID、密码（8-63 位 WPA2）
- 选择用于共享上网的物理网卡
- 实时显示热点状态与已连接设备
- 设置自动保存到 `%AppData%\WifiHotspot\config.ini`

## 目录结构

```
winx86-hotspot/
├── WifiHotspot.sln              # Visual Studio 解决方案
├── WifiHotspot/
│   ├── WifiHotspot.csproj       # 工程文件 (x86, .NET Framework 4.8)
│   ├── Program.cs               # 程序入口
│   ├── MainForm.cs              # 主窗体逻辑
│   ├── MainForm.Designer.cs     # 窗体布局
│   ├── MainForm.resx            # 窗体资源
│   ├── HotspotManager.cs        # 热点核心逻辑 (netsh)
│   ├── IcsManager.cs            # ICS COM 接口 (HNetCfg.HNetShare)
│   ├── MobileHotspotManager.cs  # Windows 移动热点 API (WinRT)
│   ├── DiagnosisForm.cs         # 失败诊断与修复路径弹窗
│   ├── ProxySettingsForm.cs     # 代理功能开关窗口
│   ├── ProxyConfigManager.cs    # proxy/config.json 读写
│   ├── ProxyProcessManager.cs   # hotspot-proxy.exe 启停
│   ├── NetworkNormalizer.cs     # WinDivert 网络层 TTL/IPID/TCP 归一化
│   ├── app.config               # .NET 运行时配置
│   ├── app.manifest             # 管理员权限清单
│   └── Properties/AssemblyInfo.cs
├── openwrt/                     # OpenWrt 目标方案 (TTL 归一化)
│   ├── README.md                # 部署文档
│   ├── ttl-normalize.sh         # 自适应脚本 (fw3/fw4)
│   ├── ttl-normalize.nft        # 独立 nftables 规则集
│   └── etc/
│       ├── nftables.d/50-ttl-normalize.nft
│       └── uci-defaults/99-ttl-normalize
└── proxy/                       # Go 透明代理 + DeviceForge
    ├── cmd/hotspot-proxy/
    ├── internal/
    ├── configs/config.json
    ├── scripts/
    └── README.md
```

## 构建

### 环境要求
- Windows 10/11（x86 或 x64 均可开发，编译目标为 x86）
- Visual Studio 2019 或更高版本（Community 版即可），需包含 ".NET 桌面开发" 工作负载
- .NET Framework 4.8 Target Pack（VS 安装器默认包含）

### 步骤
1. 用 Visual Studio 打开 `WifiHotspot.sln`
2. 确认平台为 `x86`（工具栏下拉框显示 Debug / x86）
3. 按 `F5` 调试运行，或菜单 **生成 → 生成解决方案**
4. 输出文件位于 `WifiHotspot\bin\Release\WifiHotspot.exe`（Release 模式）

### 命令行构建（可选）
在 "Developer Command Prompt for VS" 中执行：
```
msbuild WifiHotspot.sln /p:Configuration=Release /p:Platform=x86
```

## 使用

1. **以管理员身份运行** `WifiHotspot.exe`（app.manifest 会自动请求 UAC 提权）
2. 程序启动时自动检测无线网卡是否支持承载网络（Hosted Network）
3. 填写 SSID 和密码，选择已经能上网的网卡（如以太网或正在连接的 WiFi）
4. 点击 **启动热点**，手机/其他设备即可搜索到该 WiFi 并连接
5. 点击 **停止热点** 关闭热点并自动关闭 ICS 共享

### 常见问题

**Q: 程序提示"当前无线网卡不支持承载网络"**
- Windows 10 2004+ / Windows 11 的部分无线网卡驱动已移除 Hosted Network 支持
- 解决方案：更新无线网卡驱动；或使用系统自带的"移动热点"功能；或使用支持 AP 模式的 USB 无线网卡

**Q: 热点启动但设备连接后无法上网**
- 检查"共享网络"下拉框是否选择了正确的、 currently 有网络的网卡
- ICS 设置可能因系统策略失败，程序会提示手动设置：
  控制面板 → 网络连接 → 右键正在上网的网卡 → 属性 → 共享 → 勾选"允许其他网络用户通过此计算机的 Internet 连接来连接"

**Q: 32 位程序在 64 位系统运行**
- 项目目标平台为 x86，在 x64 Windows 上以 WOW64 模式运行
- ICS COM 调用在 Win10 1607 (Anniversary Update) 的 x64 系统上，32 位程序调用会触发访问冲突；如需在 x64 系统使用，建议将 Platform 改为 x64 编译

## 技术说明

- **热点创建**：`netsh wlan set hostednetwork mode=allow ssid=... key=...` + `netsh wlan start hostednetwork`
- **网络共享**：通过 `HNetCfg.HNetShare.1` COM 对象 (`INetSharingManager`) 启用 ICS：
  - 对上网网卡调用 `EnableSharing(ICSSHARINGTYPE_PUBLIC)`
  - 对虚拟网卡调用 `EnableSharing(ICSSHARINGTYPE_PRIVATE)`
- **客户端列表**：解析 `arp -a` 输出，筛选 `192.168.137.x` / `192.168.173.x` 网段

## 许可证

仅供学习交流使用。
