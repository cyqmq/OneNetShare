#!/bin/sh
# TTL / Hop Limit 出口归一化 (OpenWrt)
# 在 WAN 出口统一重置 TTL/Hoplimit，消除 64/63、128/127 跳变。

SCRIPT="ttl-normalize"
UCI_CFG="ttl-normalize"
NFT_TABLE="inet ttl_normalize"
IPT_CHAIN="TTL_NORMALIZE"
IPT_CHAIN6="TTL6_NORMALIZE"

log() { logger -t "$SCRIPT" "$1"; }

uci_get() {
    uci -q get "${UCI_CFG}.@settings[0].$1" 2>/dev/null
}

cfg_ttl="$(uci_get ttl)"
cfg_wan="$(uci_get wan_iface)"
cfg_enabled="$(uci_get enabled)"

TTL="${2:-${cfg_ttl:-64}}"
WAN="${3:-${cfg_wan:-}}"

case "$TTL" in
    ''|*[!0-9]*) echo "错误: TTL 必须是 1-255 的整数" >&2; exit 1 ;;
esac
if [ "$TTL" -lt 1 ] || [ "$TTL" -gt 255 ]; then
    echo "错误: TTL 必须是 1-255 的整数" >&2; exit 1
fi

detect_fw() {
    command -v nft >/dev/null 2>&1 && nft list tables 2>/dev/null | grep -q 'inet fw4' && echo "nftables" && return
    command -v iptables >/dev/null 2>&1 && echo "iptables" && return
    echo "unknown"
}

detect_wan() {
    [ -n "$WAN" ] && { echo "$WAN"; return; }
    local w
    w="$(uci -q get network.wan.device 2>/dev/null || uci -q get network.wan.ifname 2>/dev/null)"
    [ -z "$w" ] && w="$(ip -4 route 2>/dev/null | awk '/^default/ {print $5; exit}')"
    echo "$w"
}

# ---------- nftables (fw4) ----------
nft_start() {
    local wan ttl
    wan="$(detect_wan)"; ttl="$TTL"
    nft delete table $NFT_TABLE 2>/dev/null
    nft add table $NFT_TABLE
    nft add chain $NFT_TABLE postrouting '{ type filter hook postrouting priority mangle ; policy accept ; }'
    if [ -n "$wan" ]; then
        nft add rule $NFT_TABLE postrouting oifname "$wan" ip ttl set "$ttl"
        nft add rule $NFT_TABLE postrouting oifname "$wan" ip6 hoplimit set "$ttl"
    else
        nft add rule $NFT_TABLE postrouting ip ttl set "$ttl"
        nft add rule $NFT_TABLE postrouting ip6 hoplimit set "$ttl"
    fi
    log "nftables 规则已加载: TTL=$ttl WAN=${wan:-all}"
}

nft_stop() {
    nft delete table $NFT_TABLE 2>/dev/null
    log "nftables 规则已清除"
}

nft_status() {
    nft list table $NFT_TABLE 2>/dev/null
}

# ---------- iptables (fw3) ----------
ipt_start() {
    local wan ttl
    wan="$(detect_wan)"; ttl="$TTL"

    iptables -t mangle -N "$IPT_CHAIN" 2>/dev/null
    ip6tables -t mangle -N "$IPT_CHAIN6" 2>/dev/null
    iptables -t mangle -F "$IPT_CHAIN"
    ip6tables -t mangle -F "$IPT_CHAIN6"

    if [ -n "$wan" ]; then
        iptables -t mangle -A "$IPT_CHAIN" -o "$wan" -j TTL --ttl-set "$ttl"
        ip6tables -t mangle -A "$IPT_CHAIN6" -o "$wan" -j HL --hl-set "$ttl"
    else
        iptables -t mangle -A "$IPT_CHAIN" -j TTL --ttl-set "$ttl"
        ip6tables -t mangle -A "$IPT_CHAIN6" -j HL --hl-set "$ttl"
    fi

    iptables -t mangle -C POSTROUTING -j "$IPT_CHAIN" 2>/dev/null || \
        iptables -t mangle -I POSTROUTING 1 -j "$IPT_CHAIN"
    ip6tables -t mangle -C POSTROUTING -j "$IPT_CHAIN6" 2>/dev/null || \
        ip6tables -t mangle -I POSTROUTING 1 -j "$IPT_CHAIN6"

    log "iptables 规则已加载: TTL=$ttl WAN=${wan:-all}"
}

ipt_stop() {
    iptables -t mangle -D POSTROUTING -j "$IPT_CHAIN" 2>/dev/null
    ip6tables -t mangle -D POSTROUTING -j "$IPT_CHAIN6" 2>/dev/null
    iptables -t mangle -F "$IPT_CHAIN" 2>/dev/null
    ip6tables -t mangle -F "$IPT_CHAIN6" 2>/dev/null
    iptables -t mangle -X "$IPT_CHAIN" 2>/dev/null
    ip6tables -t mangle -X "$IPT_CHAIN6" 2>/dev/null
    log "iptables 规则已清除"
}

ipt_status() {
    iptables -t mangle -L "$IPT_CHAIN" -n -v 2>/dev/null
    ip6tables -t mangle -L "$IPT_CHAIN6" -n -v 2>/dev/null
}

# ---------- dispatch ----------
FW="$(detect_fw)"
ACTION="${1:-start}"

case "$ACTION" in
    start)
        [ "$cfg_enabled" = "0" ] && { echo "已在 UCI 中禁用"; exit 0; }
        case "$FW" in
            nftables) nft_start ;;
            iptables)  ipt_start ;;
            *) echo "错误: 未检测到 nftables 或 iptables" >&2; exit 1 ;;
        esac
        ;;
    stop)
        case "$FW" in
            nftables) nft_stop ;;
            iptables)  ipt_stop ;;
        esac
        ;;
    restart)
        "$0" stop "$TTL" "$WAN" >/dev/null 2>&1
        "$0" start "$TTL" "$WAN"
        ;;
    status)
        echo "防火墙: $FW  TTL: $TTL  WAN: $(detect_wan)"
        case "$FW" in
            nftables) nft_status ;;
            iptables)  ipt_status ;;
        esac
        ;;
    *)
        echo "用法: $0 {start|stop|restart|status} [TTL] [WAN接口]" >&2
        echo "  TTL 默认 64（也可通过 UCI ttl-normalize.@settings[0].ttl 配置）" >&2
        exit 1
        ;;
esac
