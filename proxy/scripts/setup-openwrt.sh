#!/bin/sh
# OpenWrt 透明代理部署脚本 (REDIRECT 模式)
# 用法: sh setup-openwrt.sh [install|uninstall|status]

CONF_DIR="/etc/hotspot-proxy"
BIN="/usr/bin/hotspot-proxy"
HTTP_PORT="18080"
HTTPS_PORT="18443"
LAN_NET="$(uci -q get network.lan.ipaddr 2>/dev/null | cut -d. -f1-3).0/24"
[ -z "$LAN_NET" ] && LAN_NET="192.168.1.0/24"

install_rules() {
    iptables -t nat -N HOTSPOT_PROXY 2>/dev/null
    iptables -t nat -F HOTSPOT_PROXY

    iptables -t nat -A HOTSPOT_PROXY -p tcp --dport 80 -j REDIRECT --to-ports $HTTP_PORT
    iptables -t nat -A HOTSPOT_PROXY -p tcp --dport 443 -j REDIRECT --to-ports $HTTPS_PORT
    # bypass proxy's own outbound connections and loopback
    iptables -t nat -A HOTSPOT_PROXY -d 127.0.0.0/8 -j RETURN
    iptables -t nat -I PREROUTING 1 -s "$LAN_NET" -p tcp -j HOTSPOT_PROXY

    echo "iptables REDIRECT 规则已安装 (LAN=$LAN_NET)"
}

remove_rules() {
    iptables -t nat -D PREROUTING -s "$LAN_NET" -p tcp -j HOTSPOT_PROXY 2>/dev/null
    iptables -t nat -F HOTSPOT_PROXY 2>/dev/null
    iptables -t nat -X HOTSPOT_PROXY 2>/dev/null
    echo "iptables REDIRECT 规则已移除"
}

install_service() {
    mkdir -p "$CONF_DIR"
    cp ./config.json "$CONF_DIR/config.json"
    cp ./hotspot-proxy "$BIN"
    chmod +x "$BIN"
    cat > /etc/init.d/hotspot-proxy <<'EOF'
#!/bin/sh /etc/rc.common
START=96
STOP=10
start() {
    /usr/bin/hotspot-proxy --config /etc/hotspot-proxy/config.json >/dev/null 2>&1 &
}
stop() {
    killall hotspot-proxy 2>/dev/null
}
EOF
    chmod +x /etc/init.d/hotspot-proxy
    /etc/init.d/hotspot-proxy enable
    echo "服务已安装并启用"
}

case "$1" in
    install)
        install_service
        install_rules
        /etc/init.d/hotspot-proxy start
        ;;
    uninstall)
        /etc/init.d/hotspot-proxy stop 2>/dev/null
        /etc/init.d/hotspot-proxy disable 2>/dev/null
        rm -f /etc/init.d/hotspot-proxy "$BIN"
        rm -rf "$CONF_DIR"
        remove_rules
        ;;
    rules-only)
        install_rules
        ;;
    status)
        ps | grep '[h]otspot-proxy' || echo "进程未运行"
        iptables -t nat -L HOTSPOT_PROXY -n -v 2>/dev/null || echo "规则未安装"
        ;;
    *)
        echo "用法: $0 {install|uninstall|rules-only|status}"
        ;;
esac
