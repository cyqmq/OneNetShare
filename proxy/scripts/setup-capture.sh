#!/bin/sh
# OpenWrt 接管方式安装脚本
# 用法: sh setup-capture.sh {tun|tproxy|redirect} [install|uninstall]

MODE="${1:-tun}"
ACTION="${2:-install}"
BIN="/usr/bin/hotspot-proxy"
CONF="/etc/hotspot-proxy/config.json"
TUN_NAME="tun0"
TUN_IP="10.66.0.1"
HTTP_PORT="18080"
HTTPS_PORT="18443"
LAN_NET="$(uci -q get network.lan.ipaddr 2>/dev/null | cut -d. -f1-3).0/24"
[ -z "$LAN_NET" ] && LAN_NET="192.168.1.0/24"

install_tun() {
    # 加载 tun 模块
    modprobe tun 2>/dev/null
    [ -e /dev/net/tun ] || { echo "缺少 /dev/net/tun，请先 modprobe tun"; return 1; }

    # 生成 TUN 配置
    cp "$CONF" "${CONF}.bak" 2>/dev/null
    python3 - <<'PYEOF' 2>/dev/null || sed -i 's/"enabled": false/"enabled": true/' "$CONF"
import json
c = json.load(open('/etc/hotspot-proxy/config.json'))
c['tun'] = {'enabled': True, 'name': 'tun0', 'ip': '10.66.0.1', 'route': True}
c['deviceforge'] = {
    'enabled': True, 'target_os': 'windows-11', 'headers': True,
    'udp': {'enabled': True, 'listen': '0.0.0.0:18500', 'all_ports': True,
            'target_ports': [53, 123, 443, 5353],
            'listen_ports': {'53': 18053, '123': 18123, '443': 18443, '5353': 23353}},
    'dns': {'enabled': True, 'strip_edns': True, 'force_tcp': False, 'min_ttl': 60, 'max_ttl': 86400},
    'ntp': {'enabled': True, 'server': 'pool.ntp.org', 'rewrite_refid': True},
    'mdns': {'enabled': True, 'hostname': 'gateway', 'service': '_workstation._tcp.local'},
    'dhcp': {'enabled': True, 'vendor_class': 'MSFT 5.0', 'client_id': ''},
    'captive': {
        'enabled': True, 'mode': 'reply',
        'dns_domains': ['captive.apple.com', 'www.msftconnecttest.com', 'connectivitycheck.gstatic.com', 'detectportal.firefox.com', 'nmcheck.gnome.org'],
        'domains': ['captive.apple.com', 'www.msftconnecttest.com', 'connectivitycheck.gstatic.com', 'detectportal.firefox.com', 'www.google.com'],
        'paths': ['/generate_204', '/connecttest.txt', '/hotspot-detect.html', '/success.txt', '/nmcheck'],
        'reply_ip': '', 'ttl': 60, 'http_code': 204, 'http_body': ''
    }
}
json.dump(c, open('/etc/hotspot-proxy/config.json','w'), indent=2)
PYEOF

    cat > /etc/init.d/hotspot-proxy <<'EOF'
#!/bin/sh /etc/rc.common
START=96
STOP=10
start() {
    modprobe tun 2>/dev/null
    /usr/bin/hotspot-proxy --config /etc/hotspot-proxy/config.json >/dev/null 2>&1 &
}
stop() { killall hotspot-proxy 2>/dev/null; }
EOF
    ip rule add fwmark 0x2 lookup main pref 100 2>/dev/null
    ip route add default dev tun0 table 101 2>/dev/null
    ip rule add pref 200 lookup 101 2>/dev/null

    chmod +x /etc/init.d/hotspot-proxy
    /etc/init.d/hotspot-proxy enable
    /etc/init.d/hotspot-proxy start
    echo "TUN 模式已安装 (tun0: $TUN_IP)"
}

install_tproxy() {
    # mangle TPROXY 规则
    iptables -t mangle -N HOTSPOT_TPROXY 2>/dev/null
    iptables -t mangle -F HOTSPOT_TPROXY

    iptables -t mangle -A HOTSPOT_TPROXY -d "$LAN_NET" -j RETURN
    iptables -t mangle -A HOTSPOT_TPROXY -p tcp --dport 80 -j TPROXY --on-port $HTTP_PORT --tproxy-mark 0x1
    iptables -t mangle -A HOTSPOT_TPROXY -p tcp --dport 443 -j TPROXY --on-port $HTTPS_PORT --tproxy-mark 0x1
    iptables -t mangle -A HOTSPOT_TPROXY -p udp -j TPROXY --on-port 18500 --tproxy-mark 0x1
    iptables -t mangle -I PREROUTING 1 -j HOTSPOT_TPROXY

    # 策略路由
    ip rule add fwmark 0x1 lookup 100 2>/dev/null
    ip route add local 0.0.0.0/0 dev lo table 100 2>/dev/null

    echo "TPROXY 规则已安装"
}

install_redirect() {
    sh "$(dirname "$0")/setup-openwrt.sh" install
    iptables -t nat -A HOTSPOT_PROXY -p udp --dport 53 -j REDIRECT --to-ports 18053 2>/dev/null
    iptables -t nat -A HOTSPOT_PROXY -p udp --dport 123 -j REDIRECT --to-ports 18123 2>/dev/null
    iptables -t nat -A HOTSPOT_PROXY -p udp --dport 443 -j REDIRECT --to-ports 18443 2>/dev/null
    iptables -t nat -A HOTSPOT_PROXY -p udp --dport 5353 -j REDIRECT --to-ports 23353 2>/dev/null
    echo "REDIRECT UDP 规则已安装 (53/123/443/5353)"
}

uninstall() {
    /etc/init.d/hotspot-proxy stop 2>/dev/null
    /etc/init.d/hotspot-proxy disable 2>/dev/null
    iptables -t mangle -D PREROUTING -j HOTSPOT_TPROXY 2>/dev/null
    iptables -t mangle -F HOTSPOT_TPROXY 2>/dev/null
    iptables -t mangle -X HOTSPOT_TPROXY 2>/dev/null
    ip rule del fwmark 0x1 lookup 100 2>/dev/null
    ip route del local 0.0.0.0/0 dev lo table 100 2>/dev/null
    ip rule del pref 200 lookup 101 2>/dev/null
    ip route del default dev tun0 table 101 2>/dev/null
    ip rule del fwmark 0x2 lookup main pref 100 2>/dev/null
    sh "$(dirname "$0")/setup-openwrt.sh" uninstall
    echo "已卸载"
}

case "$ACTION" in
    install)
        case "$MODE" in
            tun)      install_tun ;;
            tproxy)   install_tproxy ;;
            redirect) install_redirect ;;
            *) echo "未知模式: $MODE"; exit 1 ;;
        esac
        ;;
    uninstall) uninstall ;;
    *) echo "用法: $0 {tun|tproxy|redirect} {install|uninstall}" ;;
esac
