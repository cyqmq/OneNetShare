#!/bin/sh

PORTAL_FILTER_CHAIN="HOTSPOT_PORTAL"

portal_ips() {
    primary="${PORTAL_PRIMARY_IP:-$(uci -q get hotspot-proxy.portal.primary_ip 2>/dev/null)}"
    secondary="${PORTAL_SECONDARY_IP:-$(uci -q get hotspot-proxy.portal.secondary_ip 2>/dev/null)}"
    extra="${PORTAL_IPS:-$(uci -q get hotspot-proxy.portal.ips 2>/dev/null)}"

    result=""
    for ip in $primary $secondary $extra; do
        ip="$(echo "$ip" | tr ',' ' ')"
        for part in $ip; do
            case "$part" in
                ''|*[!0-9.]*) continue ;;
            esac
            case " $result " in
                *" $part "*) continue ;;
            esac
            result="${result:+$result }$part"
        done
    done
    echo "$result"
}


portal_nat_returns() {
    for ip in $(portal_ips); do
        iptables -t nat -A HOTSPOT_PROXY -d "$ip" -j RETURN
    done
}

portal_mangle_returns() {
    for ip in $(portal_ips); do
        iptables -t mangle -A HOTSPOT_TPROXY -d "$ip" -j RETURN
    done
}

install_portal_rules() {
    ips="$(portal_ips)"
    iptables -t filter -N "$PORTAL_FILTER_CHAIN" 2>/dev/null
    iptables -t filter -F "$PORTAL_FILTER_CHAIN"
    for ip in $ips; do
        iptables -t filter -A "$PORTAL_FILTER_CHAIN" -d "$ip" -j DROP
        iptables -t filter -A "$PORTAL_FILTER_CHAIN" -s "$ip" -j DROP
    done
    iptables -t filter -C FORWARD -j "$PORTAL_FILTER_CHAIN" 2>/dev/null || \
        iptables -t filter -I FORWARD 1 -j "$PORTAL_FILTER_CHAIN"
    echo "portal 封堵已安装 (IP=${ips:-未解析})"
}

remove_portal_rules() {
    iptables -t filter -D FORWARD -j "$PORTAL_FILTER_CHAIN" 2>/dev/null
    iptables -t filter -F "$PORTAL_FILTER_CHAIN" 2>/dev/null
    iptables -t filter -X "$PORTAL_FILTER_CHAIN" 2>/dev/null
    echo "portal 封堵已移除"
}

portal_block_status() {
    echo "portal IP: $(portal_ips)"
    iptables -t filter -L "$PORTAL_FILTER_CHAIN" -n -v 2>/dev/null || echo "portal 封堵规则未安装"
}

if [ -z "$PORTAL_BLOCK_NO_AUTORUN" ]; then
    case "$1" in
        install) install_portal_rules ;;
        uninstall) remove_portal_rules ;;
        status) portal_block_status ;;
        *) echo "用法: $0 {install|uninstall|status}" ;;
    esac
fi
