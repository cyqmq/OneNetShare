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
- 两种网络拓扑：`自动（当前行为）` 保持原有自动选路；`有线入口 → 无线热点` 强制选择有线以太网卡作为 ICS 公共端、指定无线网卡作为热点端，绕过 WinRT 自动热点选路，明确流量方向为“有线入口 → 本机 ICS/NAT → 无线客户端”
- 有线入口模式保护：跳过全局 WinDivert 网络层归一化，IPv6/Portal 转发封禁只作用于热点虚拟网卡 `ifIdx`，避免改写或丢弃有线入口上的上游流量；只读 TTL/DNS 监控保持工作
- 有线入口 DNS 下发：启动前把代理设置中的 `upstream_servers` 临时写入所选有线网卡，作为 Windows ICS 的 DNS 上游，解决客户端拿到 IP 但 DNS 不可用；停止热点后自动恢复原 DNS/DHCP 设置。ICS 共享失败时直接报错，不再伪装成启动成功
- 自动诊断热点失败原因并给出可执行修改路径与设置入口
- GUI 内置“代理功能”开关窗口，可编辑 HTTP Header/Captive/UDP/DNS/NTP/mDNS/DHCP 开关、DNS 上游服务器/去 EDNS/强制 TCP/TTL 钳制、设备指纹和随热点启停代理；“UA 预设”下拉框提供内置 UA 库（Chrome/Edge/Firefox/Safari/curl），无需手输，UA/Client Hints/平台版本由同一画像生成，Firefox/Safari 不带 Chromium Hints；强制 TTL 随设备指纹自动填入推荐值（Windows 画像 128，Android/macOS/iOS/Linux 画像 64），手动改偏会提示；Wintun 模式强制从下拉列表选择真实物理出口网卡，过滤 VMware/Hyper-V/WSL 等虚拟网卡；未实现项在窗口内明确标注
- MTU/MSS 钳制自动降级：WinDivert SYN MSS 与 Wintun 合成 SYN-ACK MSS 均可钳制到 1460/1360/1280，便于排查网站卡在 TLS 握手的问题
- 探针日志面板实时滚动显示 Captive 拦截、DNS 规范化、未知 UDP 丢弃事件；一键连通性测试通过本地回显探针验证 HTTP Header 改写，并检查 TUN 默认路由与 IPv6 裸露风险
- 代理自动诊断：启动失败时自动检查 `config.json`、端口格式/占用/Windows 保留端口范围、退出码、进程输出与 `probe.log`，给出端口/权限/文件/TUN 出口网卡等具体原因；代理功能窗口提供“自动诊断”按钮；一键测试会比对配置生成号与代理 `/status`，发现旧进程时自动重启并显示版本/路径；`probe.log` 被代理占用时日志面板与诊断改为共享读
- 网络层指纹归一化：基于 WinDivert 在 Windows 网卡出口强制重写 IPv4 TTL/IPID、IPv6 Hop Limit 与 traffic class/Flow Label 清零、TCP SYN 选项，并统一所有出站 TCP 的 timestamp（TSval），保留 TSecr；Flow Label 清零只用于指纹收敛，不代表 IPv6 隐私安全；已随包附带官方 WinDivert 2.2.2 x86 `WinDivert.dll`、`WinDivert32.sys`、`WinDivert64.sys`，需管理员权限
- 核心监控容错：`ip or ipv6` 过滤器打开失败（如错误码 3/87）时自动降级为 DNS-only UDP:53 只读监控；错误码 3 会进一步读取 `sc qc WinDivert` 的驱动路径，发现旧路径/失效服务时自动 `sc stop WinDivert`、`sc delete WinDivert` 并重试，仍失败时输出具体路径与恢复步骤，自动诊断同步识别。
- 核心监控（只读）：WinDivert `SNIFF` 模式旁路记录手机→电脑与 NAT 后电脑→网关的同流 TTL/Hop Limit，出现 `64 → 63` 时在 `probe.log` 写入 `[TTL证据]`；代理侧记录原始/改写后 HTTP User-Agent，并被动解析 HTTPS ClientHello 的 SNI 与 JA3（不 MITM、不改包）；WinDivert 只读 sniff UDP:53，区分“手机未发出查询/电脑未收到”“电脑已收到但代理未转发”“已返回手机”；DNS 与 TTL 共用单个 WinDivert 只读句柄，避免多句柄触发错误 1450。全局 TUN 接管时 TTL 由 TUN 自身归一化，跳过 WinDivert TTL 监控，但 UDP:53 监控仍运行。若 90 秒内出现多次手机 UA 与电脑流量混杂且随后断开，会写入 `[监控证据]` 供自动诊断定位认证网关应用层指纹匹配
- 内嵌 DNS 服务：在全局 TUN / Linux redirect 的 UDP:53 拦截路径生效；手机 DNS 查询先查本地缓存（支持 TTL 递减、负缓存与容量上限），未命中时通过电脑物理网卡配置的 DNS 上游查询，按现有规则归一化 TTL/EDNS 后返回手机；GUI 可一键从出口网卡读取 IPv4 DNS，`force_tcp` 改为代理到上游使用 TCP 而不是让手机重试。ICS 原生模式下 53 由 Windows ICS 占用，需开启全局 TUN 才能让内嵌 DNS 接手
- Wintun 全局 TUN 接管：创建 `OneNetShare Tunnel` 虚拟网卡，只接管 IPv4 默认路由/DNS，在用户态重建 UDP/DNS/NTP 会话并统一出口包指纹，TCP 80 流同样应用 HTTP Header 画像；需要官方 x86 `wintun.dll` 和管理员权限
- 管理路由账本：代理读取配置目录旁的 `managedroutes.json`，启动前全量预检冲突，发现 VPN/代理/其他接口占用目标路由时拒绝启动且不删除他人路由；只添加账本中的精确 IPv4 路由，退出/重载时按 `(prefix, interface, gateway, metric)` 精确清理，不修改全局网卡跃点。HTTP `User-Agent` 在 TUN 80 流中按原始 Header 长度等长改写，避免包长变化；默认路由必须显式 `allow_default_route`，否则拒绝
- IPv6 隐私保护：可在 GUI 中一键启用/关闭 IPv6 转发封禁。启用时，若热点虚拟网卡检测到公网/全局 IPv6(GUA) 则在 WinDivert `NETWORK_FORWARD` 层丢弃转发 IPv6，防止手机绕过 IPv4 NAT/代理裸奔；仅 ULA/Link-local 时不启动封禁；可选仅允许 ULA/Link-local 或显式允许 IPv6（不安全）。关闭后不启动 WinDivert 封禁。
- 画像一致性自检：代理启动/重载时检查 UA、Client Hints、目标 OS、TUN TTL 与 DHCP Vendor Class 是否属于同一画像；`/status` 输出 `identity=ok|warn` 与具体 `identity_issue`，一键连通性测试同步显示结果
- 可选出站封堵：默认关闭；启用后在热点转发层丢弃 QUIC UDP443、DoT 853、LLMNR 5355、WS-D 3702、广播/组播等，并输出丢弃计数；全局 TUN 模式由代理层执行，ICS 模式由 WinDivert 执行。规则与验证步骤见 [`docs/校园网防共享调研与验证.md`](docs/校园网防共享调研与验证.md)
- Captive 探针 Header 清理：HTTP 探测请求清除 `Referer`、`Origin`、`Accept*`、`Sec-Fetch-*`、`Priority`、`Upgrade-Insecure-Requests`，并把本机代理探针与 LAN/TUN 转发请求分别计数；`/status` 输出 `header_plane=local=... forward=... captive_scrub=... leak_scrub=...`，GUI 一键测试显示本机/转发两平面证据
- 会话计时与运行时证据：GUI 显示最近热点启动耗时、停止时显示最近在线时长；代理 `/status` 输出 `runtime=uptime_seconds=... start_time=... config_reloads=... last_reload_age_seconds=...`
- OpenWrt 运维：init 脚本改为 procd `respawn` watchdog，安装规则幂等，脚本提供 `status`/`reset-counters`，输出 nat/mangle/filter 计数器并检测 MTK HNAT/PPE 绕过风险
- UI 响应优化：状态轮询与客户端列表查询移到后台线程，结果缓存后按变化批量刷新；探针日志按批追加并限制为 300 行，避免每次逐行解析 TextBox；设置窗口与诊断窗口构建时批量布局，减少启动/打开时的重排重绘
- 关闭/停止时统一同步清理：先停止热点与 ICS，再停止代理进程、IPv6/Portal/出站 WinDivert 模块、TTL/DNS 监控、网络层归一化和计时，确保退出后不残留 `hotspot-proxy.exe` 或 WinDivert 句柄
- 自动配置 ICS（Internet Connection Sharing）网络共享
- 可自定义 SSID、密码（8-63 位 WPA2）
- 选择用于共享上网的物理网卡；有线入口模式还可单独选择无线热点网卡
- 实时显示热点状态、拓扑与已连接设备
- 设置自动保存到 `%AppData%\WifiHotspot\config.ini`：`TopologyMode=auto|wired-wifi`、`AdapterId=...`、`WirelessAdapterId=...`；旧配置只含 `AdapterId` 时按 `auto` 处理

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
│   ├── HotspotTiming.cs         # 热点启动耗时与在线时长
│   ├── NetworkNormalizer.cs     # WinDivert 网络层 TTL/IPID/TCP 归一化
│   ├── OutboundGuard.cs         # 可选出站封堵与计数
│   ├── TrafficMonitor.cs        # WinDivert 只读 TTL 变化监控
│   ├── DnsMonitor.cs            # WinDivert 只读 UDP:53 到达/转发验证
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
