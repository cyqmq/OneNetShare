#!/bin/sh

PORTAL_FILTER_CHAIN="HOTSPOT_PORTAL"

iptables_insert_unique() {
    table="$1"; chain="$2"; shift 2
    iptables -t "$table" -C "$chain" "$@" 2>/dev/null || iptables -t "$table" -I "$chain" 1 "$@"
}

iptables_append_unique() {
    table="$1"; chain="$2"; shift 2
    iptables -t "$table" -C "$chain" "$@" 2>/dev/null || iptables -t "$table" -A "$chain" "$@"
}

hnat_status() {
    found=""
    [ -d /sys/module/mtk_hnat ] && found="mtk_hnat"
    [ -e /proc/net/hnat ] && found="${found:+$found }/proc/net/hnat"
    if iptables -t nat -S 2>/dev/null | grep -qi hnat; then
        found="${found:+$found }nat-hnat-target"
    fi
    if [ -n "$found" ]; then
        echo "HNAT/PPE 检测: 疑似启用 ($found)，规则计数为 0 时请检查硬件转发绕过"
    else
        echo "HNAT/PPE 检测: 未发现明显迹象"
    fi
}

capture_counter_status() {
    for spec in "nat HOTSPOT_PROXY" "mangle HOTSPOT_TPROXY" "filter HOTSPOT_PORTAL"; do
        set -- $spec
        echo "iptables $1 $2 计数器:"
        iptables -t "$1" -L "$2" -n -v -x 2>/dev/null || echo "未安装"
    done
    hnat_status
}

reset_capture_counters() {
    iptables -t nat -Z HOTSPOT_PROXY 2>/dev/null
    iptables -t mangle -Z HOTSPOT_TPROXY 2>/dev/null
    iptables -t filter -Z HOTSPOT_PORTAL 2>/dev/null
    echo "捕获计数器已清零"
}

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
        iptables_append_unique nat HOTSPOT_PROXY -d "$ip" -j RETURN
    done
}

portal_mangle_returns() {
    for ip in $(portal_ips); do
        iptables_append_unique mangle HOTSPOT_TPROXY -d "$ip" -j RETURN
    done
}

install_portal_rules() {
    ips="$(portal_ips)"
    iptables -t filter -N "$PORTAL_FILTER_CHAIN" 2>/dev/null
    iptables -t filter -F "$PORTAL_FILTER_CHAIN"
    for ip in $ips; do
        iptables_append_unique filter "$PORTAL_FILTER_CHAIN" -d "$ip" -j DROP
        iptables_append_unique filter "$PORTAL_FILTER_CHAIN" -s "$ip" -j DROP
    done
    iptables_insert_unique filter FORWARD -j "$PORTAL_FILTER_CHAIN"
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
        status) portal_block_status; capture_counter_status ;;
        reset-counters) reset_capture_counters ;;
        *) echo "用法: $0 {install|uninstall|status|reset-counters}" ;;
    esac
fi
