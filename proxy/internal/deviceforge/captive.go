package deviceforge

import (
	"encoding/binary"
	"net"
	"strings"

	"hotspot-proxy/internal/config"
)

const dnsTypeAAAA = 28

func CaptiveDNSName(query []byte) string {
	if len(query) < 13 {
		return ""
	}
	name, _, err := decodeDNSName(query, 12)
	if err != nil {
		return ""
	}
	return name
}

func CaptiveProbeLabel(name string) string {
	lower := strings.ToLower(name)
	switch {
	case strings.Contains(lower, "apple"):
		return "苹果 Captive 探针"
	case strings.Contains(lower, "msft") || strings.Contains(lower, "microsoft"):
		return "微软 Captive 探针"
	case strings.Contains(lower, "android") || strings.Contains(lower, "google") || strings.Contains(lower, "gstatic"):
		return "安卓 Captive 探针"
	case strings.Contains(lower, "firefox") || strings.Contains(lower, "mozill"):
		return "Firefox Captive 探针"
	case name == "":
		return "Captive 探针"
	default:
		return name
	}
}

func CaptiveDNSReply(query []byte, cfg config.CaptiveConfig, localIP net.IP) ([]byte, bool) {
	if !cfg.Enabled || query == nil || len(query) < 17 {
		return nil, false
	}
	qd := int(binary.BigEndian.Uint16(query[4:6]))
	if qd == 0 {
		return nil, false
	}

	name, off, err := decodeDNSName(query, 12)
	if err != nil || off+4 > len(query) {
		return nil, false
	}
	qtype := binary.BigEndian.Uint16(query[off : off+2])
	if !config.CaptiveDNSMatch(cfg, name) {
		return nil, false
	}

	if cfg.Mode == "drop" {
		return nil, true
	}
	if cfg.Mode != "reply" {
		return nil, false
	}

	question := append(encodeDNSName(name), query[off:off+4]...)
	resp := make([]byte, 12)
	copy(resp[0:2], query[0:2])
	binary.BigEndian.PutUint16(resp[2:4], 0x8180)
	binary.BigEndian.PutUint16(resp[4:6], 1)
	resp = append(resp, question...)

	replyIP := net.ParseIP(cfg.ReplyIP)
	if replyIP == nil {
		replyIP = localIP
	}
	if replyIP == nil {
		replyIP = net.IPv4(127, 0, 0, 1)
	}

	var answer []byte
	if qtype == dnsTypeA && replyIP.To4() != nil {
		answer = mdnsRR(name, dnsTypeA, dnsClassIN, cfg.TTL, replyIP.To4())
	} else if qtype == dnsTypeAAAA && replyIP.To4() == nil && replyIP.To16() != nil {
		answer = mdnsRR(name, dnsTypeAAAA, dnsClassIN, cfg.TTL, replyIP.To16())
	}
	if len(answer) > 0 {
		binary.BigEndian.PutUint16(resp[6:8], 1)
		resp = append(resp, answer...)
	}
	return resp, true
}
