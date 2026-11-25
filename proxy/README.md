# 透明代理 + Header 归一化 (hotspot-proxy)

Go 单文件二进制，双端部署：OpenWrt 网关透明劫持（REDIRECT 模式）+ Windows 本地正向代理（forward 模式）。

## 核心能力

- **HTTP（80）明文改写**：识别请求后按规则重写 `User-Agent` / `Accept-*` / `Referer` / `Origin` / `sec-ch-ua*` / `Sec-Fetch-*` 等
- **HTTPS（443）盲转发**：不解密，由网关协议栈重新发起 TCP 连接，WAN 口看到的 TCP 指纹（ISN/窗口/MSS/时间戳）统一
- **画像系统**：8 个内置预设（Chrome/Edge/Firefox/Safari × Win11/Win10/Android/iOS/macOS）+ 完全自定义
- **规则引擎**：按域名（精确/通配/后缀）+ 路径前缀匹配，支持开关、设置/删除/添加 Header
- **热重载**：配置文件变更自动重载；SIGHUP、`/reload` HTTP 端点均可触发
- **状态端点**：`http://127.0.0.1:18089/status`
- **DeviceForge**：总开关式终端特征收敛，统一 Header / UDP / DNS / NTP / mDNS / DHCP 指纹
- **Captive 探测处置**：由路由器代答或 Drop，避免 Apple/Windows/Android/Firefox 探测外泄

## 底层指纹统一原理（重要）

代理**不是**简单转发，而是作为真正的客户端用 `net.Dial` 从网关内核重新发起 TCP 连接（`internal/proxy/server.go:113`）。因此 WAN 口看到的：

| 参数 | 来源 | 是否统一 |
|------|------|----------|
| IPID 递增 | 网关内核 | ✅ |
| TCP ISN | 网关内核 | ✅ |
| 窗口大小 / 缩放 | 网关内核 | ✅ |
| MSS / TCP options | 网关内核 | ✅ |
| 时间戳 | 网关内核 | ✅ |
| 源 IP | 网关 | ✅ |
| TLS ClientHello (JA3/JA4) | 客户端原样透传 | ❌ |

**关键区别**：若用 netfilter 纯 DNAT/FORWARD（不新建 socket），下游设备的底层参数原样透传；我们的代理层每次都 `net.Dial` 新建连接，所以 TCP/IP 层天然是网关指纹。

TLS 指纹统一需 MITM 解密后用 uTLS 重构 ClientHello，本版本按需求保留盲转发。

## DeviceForge

DeviceForge 是终端特征收敛功能名，目标是把 LAN 侧不同终端收敛为同一 OS/设备画像。

| 子模块 | 开关 | 行为 |
|--------|------|------|
| `headers` | `deviceforge.headers` | HTTP Header 套用统一画像，关闭后保留客户端原始 Header |
| `udp` | `deviceforge.udp.enabled` | UDP 由网关重新创建 socket；全端口需 TUN/TPROXY |
| `dns` | `deviceforge.dns.enabled` | 去 EDNS、清 AD/CD、TC 重试、TTL 钳制 |
| `ntp` | `deviceforge.ntp.enabled` | 固定版本/模式/poll，统一 RefID/Stratum，可改上游 NTP |
| `mdns` | `deviceforge.mdns.enabled` | 本地伪造统一 hostname/service 的 PTR/SRV/TXT/A 响应 |
| `dhcp` | `deviceforge.dhcp.enabled` | 重写 Option 55/60/61，统一 Vendor Class/参数列表/Client ID |
| `captive` | `deviceforge.captive.enabled` | DNS/HTTP 探测由网关代答或直接 Drop，模式 `reply` / `drop` |

总开关为 `deviceforge.enabled`；关闭时所有子模块停止。TLS JA3/JA4 不在本版本内，仍为客户端原始指纹。

DNS/NTP/QUIC/游戏等 UDP 由 `net.DialUDP` 从网关新建 socket，WAN 侧 UDP 源 IP、端口行为、超时行为均来自网关协议栈。REDIRECT 模式仅能按显式端口接管；要真正接管全部 UDP，用 TUN 或 TPROXY。

### Captive 探测

`deviceforge.captive.mode`：

- `reply`：DNS 查询匹配 `dns_domains` 时返回 `reply_ip`（默认网关 IP）的 A 记录；HTTP 请求匹配 `domains` + `paths` 时由网关直接返回 `http_code` / `http_body`。
- `drop`：DNS 查询静默丢弃；HTTP 探测直接断开；HTTPS CONNECT 按 Host 匹配时直接断开。

HTTPS Captive 探测无法在不解密的情况下伪造 HTTP 响应；本版本对匹配 Host 的 CONNECT 采用断开处理，避免探测到达 WAN。

## 三种接管方式

| 模式 | 机制 | 特点 |
|------|------|------|
| `redirect`（默认） | iptables nat REDIRECT + SO_ORIGINAL_DST | 最成熟，出口 TCP 已由网关内核发起 |
| `tproxy` | iptables mangle TPROXY + IP_TRANSPARENT | 可保留/伪造源 IP，策略路由更灵活 |
| `tun` | 用户态 TUN + 精简 TCP 状态机 | 跨平台性最好，IPID/窗口等由我们生成（模拟网关栈） |

TUN 模式通过 `water` 库创建 `/dev/net/tun`，自己处理 SYN/SYN-ACK/ACK/数据/FIN，出站连接仍由网关 `net.Dial` 发起。TUN 模式会对 TCP 80 的 HTTP 请求按消息边界解析并应用 Header 画像，移除 XFF/Via 等泄露头；同时启动 `listen_http` 端口供显式 HTTP 代理测试使用。HTTPS 仍不解密。

## 目录结构

```
proxy/
├── go.mod / go.sum
├── cmd/hotspot-proxy/main.go       # 入口
├── internal/
│   ├── config/                     # 配置模型 / 预设 / 加载器
│   ├── headers/rewrite.go          # Header 重写引擎
│   ├── redirect/                   # SO_ORIGINAL_DST / IP_TRANSPARENT (Linux)
│   ├── deviceforge/                # UDP / DNS / NTP / mDNS / DHCP 指纹收敛
│   ├── proxy/server.go             # 透明代理 / 正向代理 / 盲隧道
│   ├── tun/                        # TUN + TCP/UDP 状态机
│   └── version/
├── configs/config.json             # 规则配置示例
├── scripts/
│   ├── setup-openwrt.sh
│   └── run-windows.bat
└── build/                          # 交叉编译产物
```

## 构建

```sh
export PATH=/path/to/go/bin:$PATH
cd proxy
# 当前平台
go build -o hotspot-proxy ./cmd/hotspot-proxy
# OpenWrt x86
GOOS=linux GOARCH=386 CGO_ENABLED=0 go build -o build/hotspot-proxy-openwrt-x86 ./cmd/hotspot-proxy
# OpenWrt MIPSLE (MT7621 等)
GOOS=linux GOARCH=mipsle CGO_ENABLED=0 go build -o build/hotspot-proxy-openwrt-mipsle ./cmd/hotspot-proxy
# Windows x86
GOOS=windows GOARCH=386 CGO_ENABLED=0 go build -o build/hotspot-proxy-win-x86.exe ./cmd/hotspot-proxy
```

## 配置说明

```json
{
  "enabled": true,              // 全局开关
  "mode": "redirect",           // redirect (OpenWrt) / forward (Windows)
  "listen_http": "0.0.0.0:18080",
  "listen_https": "0.0.0.0:18443",
  "network": {
    "enabled": false,
    "ttl": 128,
    "mtu_clamp_enabled": false,
    "mss_clamp": 1460
  },
  "tun": {
    "enabled": true,
    "name": "OneNetShare Tunnel",
    "ip": "10.66.0.1",
    "gateway_ip": "10.66.0.2",
    "prefix_len": 24,
    "route": true,
    "upstream_interface": "",
    "ttl": 128
  },
    "default_profile": "chrome-win11",
    "ua_preset": "chrome-158-windows",
    "user_agent_override": "",
    "deviceforge": {
     "enabled": true,
     "target_os": "windows-11",
     "headers": true,
     "udp": {"enabled": true, "listen": "0.0.0.0:18500", "all_ports": true, "target_ports": [53, 123, 443, 5353]},
     "dns": {"enabled": true, "strip_edns": true, "force_tcp": false, "min_ttl": 60, "max_ttl": 86400},
     "ntp": {"enabled": true, "server": "pool.ntp.org", "rewrite_refid": true},
     "mdns": {"enabled": true, "hostname": "gateway", "service": "_workstation._tcp.local"},
     "dhcp": {"enabled": true, "vendor_class": "MSFT 5.0", "client_id": ""},
     "captive": {
       "enabled": true,
       "mode": "reply",
       "dns_domains": ["captive.apple.com", "www.msftconnecttest.com", "connectivitycheck.gstatic.com"],
       "domains": ["captive.apple.com", "www.msftconnecttest.com", "www.google.com"],
       "paths": ["/generate_204", "/connecttest.txt", "/hotspot-detect.html"],
       "reply_ip": "",
       "ttl": 60,
       "http_code": 204,
       "http_body": ""
     }
   },
   "profiles": {
    "my-profile": {             // 自定义画像
      "description": "...",
      "headers": { "User-Agent": "...", "Accept-Language": "..." }
    }
  },
  "rules": [
    {
      "enabled": true,          // 规则开关
      "domains": ["*.example.com", "example.com"],
      "path_prefix": "/api",    // 可选
      "profile": "my-profile",  // 套用画像
      "set_headers": {},        // 覆盖（存在则替换，不存在则新增）
      "remove_headers": [],     // 删除
      "add_headers": {}         // 追加
    }
  ]
}
```

内置画像按“单一画像源”生成：UA、`sec-ch-ua`、`sec-ch-ua-full-version-list`、`sec-ch-ua-mobile` 与平台版本都来自同一浏览器版本/平台；Firefox 与 Safari 不携带 Chromium Client Hints。`ua_preset` 从内置 UA 库选择预设并自动套用完整 Header，不需要手输 UA；`user_agent_override` 仅保留给旧配置兼容。规则的 `profile: "default"` 会跟随 `default_profile`。

Windows 下 `tun.enabled=true` 使用 Wintun 全局接管：需把官方 x86 `wintun.dll` 放在 `hotspot-proxy.exe` 同目录并以管理员运行。GUI 会列出真实物理网卡供用户选择出口网卡；`upstream_interface` 留空时自动读取 IPv4 默认路由对应的物理网卡，代理上游 TCP/UDP 会绑定到该网卡，避免流量重新进入 TUN。退出时通过 `GET /shutdown` 或直接信号恢复路由。`tun.ttl` 与 GUI 的“强制 TTL”共用：Windows 画像推荐 128，Android/macOS/iOS/Linux 画像推荐 64，GUI 切换画像时会自动填入并显示防呆提示。`network.mtu_clamp_enabled=true` 时，WinDivert SYN MSS 与 Wintun 合成 SYN-ACK MSS 都会被钳制到 `mss_clamp`；遇到 TLS 握手卡住时可将 1460 降到 1360/1280。经 GUI 启动的代理会把 `[probe]` 事件写入程序目录 `probe.log`，供代理功能窗口滚动显示 Captive/DNS/未知 UDP 事件。

Windows 网络层归一化使用 WinDivert 2.x：Windows 发行包已附带官方 2.2.2 A 版 x86 `WinDivert.dll`、32 位驱动 `WinDivert32.sys` 与 64 位驱动 `WinDivert64.sys`，三者需与 `WifiHotspot.exe` 放在同一目录，并以管理员权限运行。WinDivert 2.x 驱动文件名不是 `WinDivert.sys`；32 位程序在 64 位 Windows 上需要同时保留两个 `.sys` 文件。

内置画像名：`chrome-win11`, `chrome-win10`, `edge-win11`, `chrome-android`, `safari-ios`, `safari-macos`, `firefox-win11`, `curl-linux`；兼容别名：`mobile-android`（等同 `chrome-android`）、`mobile-ios`（等同 `safari-ios`）。

用 `hotspot-proxy --dump-config` 输出完整模板。

## 部署

### OpenWrt

```sh
scp build/hotspot-proxy-openwrt-x86 root@192.168.1.1:/usr/bin/hotspot-proxy
scp scripts/setup-openwrt.sh scripts/setup-capture.sh configs/config.json root@192.168.1.1:/root/
ssh root@192.168.1.1 'sh /root/setup-openwrt.sh install'
ssh root@192.168.1.1 'sh /root/setup-capture.sh tun install'
```

脚本会：
1. 安装二进制到 `/usr/bin/hotspot-proxy`
2. 复制配置到 `/etc/hotspot-proxy/config.json`
3. 创建 `/etc/init.d/hotspot-proxy` 并 enable
4. 安装 iptables REDIRECT 规则（LAN 网段自动检测）

验证：
```sh
/etc/init.d/hotspot-proxy status
curl http://127.0.0.1:18089/status
iptables -t nat -L HOTSPOT_PROXY -n -v
```

`/status` 会输出 `version`、`revision`、`pid`、`exe`、`generation` 以及当前 `ua_preset`；GUI 用 `generation` 判断 18080 上的进程是否仍是当前配置，不匹配会自动重启该进程。

### Windows 原型

将 `build/hotspot-proxy-win-x86.exe`、`config.json`（mode 改为 `forward`）、`scripts/run-windows.bat` 放同一目录，双击 bat。浏览器设置代理：
- HTTP: `127.0.0.1:18080`
- HTTPS: `127.0.0.1:18443`

## 热重载

```sh
kill -HUP $(pidof hotspot-proxy)      # OpenWrt
curl http://127.0.0.1:18089/reload    # 两者
```

## 验证记录

以下功能已在本地实测通过：
- HTTP Header 重写：`User-Agent`/`Accept-Language`/`sec-ch-ua` 等被画像值覆盖，`X-Forwarded-For` 被清除
- 全局开关 `enabled=false`：原始请求 Header 完整透传
- HTTPS CONNECT 盲隧道：返回 `200 Connection Established`，双向数据流正常
- DeviceForge 单元测试：DNS 去 EDNS / TC 重试 / TTL 钳制、NTP、DHCP、mDNS、Captive DNS/HTTP 匹配均通过
- 交叉编译：Windows x86 / OpenWrt x86 / OpenWrt MIPSLE 均构建成功

## 已知边界

- TLS ClientHello / JA3 / JA4 不在 DeviceForge 当前版本内，仍为客户端原始指纹。
- DHCP 广播在 REDIRECT/TPROXY 下通常由网关 dnsmasq 本地处理；Option 55/60/61 重写主要在 TUN 路径生效。
- REDIRECT 模式不支持全端口 UDP；全端口 UDP 请使用 TUN 或 TPROXY。
- mDNS 启用后由网关直接返回统一服务响应，真实局域网设备的 mDNS 响应会被抑制。
- Captive HTTPS 探测无法在不解密时代答 HTTP 内容，只能按 Host 断开。

## 排错

- 透明模式无法上网：确认 iptables REDIRECT 规则已安装、LAN 网段正确；检查 `log.file` 日志
- Header 未改写：确认 `enabled=true`、规则 `domains` 匹配；注意 HTTPS 请求无法改写（盲转发设计如此）
- 端口冲突：修改 `listen_http` / `listen_https`。Windows GUI 启动代理失败时会自动生成诊断报告，涵盖配置解析、HTTP/HTTPS/状态/UDP 端口占用、Windows 保留端口范围、退出码、进程输出与 `probe.log`；也可在“代理功能 → 探针日志 / 连通性测试”点击“自动诊断”。
