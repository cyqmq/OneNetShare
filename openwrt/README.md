# OpenWrt TTL / Hop Limit 归一化

## 问题背景

不同操作系统发出的 IP 包初始 TTL 不同（Linux/Android 为 64，Windows 为 128）。每经过一台路由器 TTL 减 1，因此从热点网关出口观察，会同时出现 **63** 和 **127** 两种值。这种"跳变"可被用于识别背后有多台设备/NAT。

方案在 **出口（POSTROUTING）统一重置为固定 TTL**，IPv4 用 `--ttl-set`，IPv6 用 `--hl-set` / `hoplimit set`，消除 64/63、128/127 差异。

## 文件说明

| 文件 | 用途 |
|------|------|
| `ttl-normalize.sh` | 主脚本，自动检测 fw3(iptables) / fw4(nftables)，支持 start/stop/restart/status |
| `ttl-normalize.nft` | 独立 nftables 规则集（`nft -f` 直接加载，不依赖 fw4） |
| `etc/nftables.d/50-ttl-normalize.nft` | fw4 静态 include 片段（持久化，随 fw4 reload 生效） |
| `etc/uci-defaults/99-ttl-normalize` | 首次启动写入 UCI 默认配置（TTL=64，禁用为 0） |

## 部署方式

### 方式 A：主脚本 + UCI 配置（推荐，TTL 可动态配置）

1. 上传脚本：
   ```sh
   ssh root@192.168.1.1 "mkdir -p /usr/bin"
   scp ttl-normalize.sh root@192.168.1.1:/usr/bin/ttl-normalize.sh
   ssh root@192.168.1.1 "chmod +x /usr/bin/ttl-normalize.sh"
   ```

2. 写入 UCI 配置（TTL 值、WAN 接口均可改）：
   ```sh
   uci -q batch <<EOF
   delete ttl-normalize
   add ttl-normalize settings
   set ttl-normalize.@settings[0].enabled='1'
   set ttl-normalize.@settings[0].ttl='65'
   set ttl-normalize.@settings[0].wan_iface='wwan0'
   commit ttl-normalize
   EOF
   ```
   > `wan_iface` 留空表示作用于所有出口；自动检测顺序：UCI → `network.wan.device` → 默认路由接口。

3. 启动并验证：
   ```sh
   /usr/bin/ttl-normalize.sh start
   /usr/bin/ttl-normalize.sh status
   /usr/bin/ttl-normalize.sh restart 65 wwan0
   ```

4. 持久化（放入热插拔或开机脚本）：
   ```sh
   # 方法1: 加入 /etc/rc.local（exit 0 之前）
   echo '/usr/bin/ttl-normalize.sh start' >> /etc/rc.local
   # 方法2: WAN 口 up 时自动执行
   mkdir -p /etc/hotplug.d/iface
   cat > /etc/hotplug.d/iface/95-ttl-normalize <<'EOF'
   [ "$ACTION" = "ifup" ] && /usr/bin/ttl-normalize.sh start
   EOF
   ```

### 方式 B：fw4 静态片段（OpenWrt 22.03+）

1. 修改 `etc/nftables.d/50-ttl-normalize.nft` 中的 TTL（默认 64）与 WAN 接口。
2. 上传：
   ```sh
   scp etc/nftables.d/50-ttl-normalize.nft root@192.168.1.1:/etc/nftables.d/
   ```
3. 加载：
   ```sh
   fw4 check && fw4 reload
   ```
4. 验证：
   ```sh
   nft list table inet fw4 | grep -i ttl
   ```

### 方式 C：独立 nftables（不经 fw4）

```sh
scp ttl-normalize.nft root@192.168.1.1:/etc/ttl-normalize.nft
ssh root@192.168.1.1 "nft -f /etc/ttl-normalize.nft"
```

### 方式 D：fw3（旧版 OpenWrt，iptables）

主脚本会自动处理。等价的手动规则：

```sh
# IPv4
iptables -t mangle -A POSTROUTING -o wwan0 -j TTL --ttl-set 65
# IPv6
ip6tables -t mangle -A POSTROUTING -o wwan0 -j HL --hl-set 65
```

持久化可写入 `/etc/firewall.user`，并在 `/etc/config/firewall` 添加：
```
config include
    option enabled '1'
    option type 'script'
    option path '/etc/firewall.user'
    option fw4_compatible '1'
```

## 验证

在网关出口抓包，确认所有包 TTL 一致：

```sh
tcpdump -ni wwan0 'icmp or ip' -vv | grep -i ttl
```

客户端连续 ping，观察出口 TTL 是否恒定为设定值（如 65）：

```sh
ping -c 3 1.1.1.1
```

## TTL 取值建议

| 场景 | 建议值 | 说明 |
|------|--------|------|
| 通用归一化 | 64 | Linux/现代设备常见值 |
| 移动网络共享（规避检测） | 65 | 经一跳后对端看到 64；运营商常见套路 |
| 保持 Windows 特征 | 128 | 经一跳后对端看到 127 |

> 注意：修改报文头部可能违反部分网络/运营商条款，请仅在你有管理权限的网络中使用。

## 排错

- `fw4 check` 报错：检查 `.nft` 片段语法，确认分号与空格。
- 脚本提示"未检测到 nftables 或 iptables"：确认已安装 `nft` 或 `iptables` 且内核模块加载。
- 规则生效但客户端仍异常：确认 WAN 接口名正确（`ip link` 查看），或尝试留空作用于全部出口。
