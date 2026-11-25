package deviceforge

import (
	"encoding/hex"
	"strings"

	"hotspot-proxy/internal/config"
)

func NormalizeDHCP(payload []byte, cfg config.DHCPForgeConfig, targetOS string, request bool) []byte {
	if len(payload) < 240 {
		return payload
	}
	if payload[236] != 99 || payload[237] != 130 || payload[238] != 83 || payload[239] != 99 {
		return payload
	}

	out := append([]byte(nil), payload[:240]...)
	for off := 240; off < len(payload); {
		code := payload[off]
		if code == 255 {
			break
		}
		if code == 0 {
			off++
			continue
		}
		if off+1 >= len(payload) {
			break
		}
		l := int(payload[off+1])
		end := off + 2 + l
		if end > len(payload) {
			break
		}
		if code != 55 && code != 60 && code != 61 {
			out = append(out, payload[off:end]...)
		}
		off = end
	}

	if request {
		vendor := cfg.VendorClass
		if vendor == "" {
			vendor = dhcpVendorClass(targetOS)
		}
		out = appendDHCPOption(out, 55, dhcpPRL(targetOS))
		if vendor != "" {
			out = appendDHCPOption(out, 60, []byte(vendor))
		}
		if cfg.ClientID != "" {
			out = appendDHCPOption(out, 61, parseDHCPValue(cfg.ClientID))
		}
	} else if cfg.VendorClass != "" {
		out = appendDHCPOption(out, 60, []byte(cfg.VendorClass))
	}
	return append(out, 255)
}

func appendDHCPOption(msg []byte, code byte, value []byte) []byte {
	if len(value) > 255 {
		value = value[:255]
	}
	msg = append(msg, code, byte(len(value)))
	return append(msg, value...)
}

func parseDHCPValue(s string) []byte {
	if strings.HasPrefix(s, "hex:") {
		if b, err := hex.DecodeString(strings.TrimPrefix(s, "hex:")); err == nil {
			return b
		}
	}
	return []byte(s)
}

func dhcpVendorClass(targetOS string) string {
	switch strings.ToLower(targetOS) {
	case "android", "chrome-android":
		return "android-dhcp-13"
	case "ios", "safari-ios":
		return "dhcpcd-8.1.2"
	case "macos", "safari-macos":
		return "dhcpcd-9.0.0"
	case "linux", "curl-linux":
		return "dhcpcd-9.0.0"
	default:
		return "MSFT 5.0"
	}
}

func dhcpPRL(targetOS string) []byte {
	switch strings.ToLower(targetOS) {
	case "android", "chrome-android":
		return []byte{1, 3, 4, 6, 15, 26, 28, 31, 33, 43, 44, 46, 47, 119, 121, 249, 252}
	case "ios", "safari-ios":
		return []byte{1, 3, 5, 6, 11, 15, 23, 26, 28, 29, 30, 31, 119, 121, 249, 252}
	case "macos", "safari-macos":
		return []byte{1, 3, 5, 6, 11, 15, 23, 26, 28, 29, 30, 31, 119, 121, 249, 252}
	case "linux", "curl-linux":
		return []byte{1, 3, 6, 15, 26, 28, 31, 33, 43, 44, 47, 119, 121, 249, 252}
	default:
		return []byte{1, 3, 6, 15, 31, 33, 43, 44, 46, 47, 119, 121, 249, 252}
	}
}
