package config

import (
	"fmt"
	"strings"
)

type IdentityIssue struct {
	Code   string
	Detail string
}

type IdentityCheck struct {
	Status   string
	Profile  string
	Browser  string
	Platform string
	TargetOS string
	TTL      int
	Issues   []IdentityIssue
}

func (c IdentityCheck) OK() bool { return len(c.Issues) == 0 }

func (c IdentityCheck) IssueLine(i int) string {
	if i < 0 || i >= len(c.Issues) {
		return ""
	}
	issue := c.Issues[i]
	detail := strings.Join(strings.Fields(issue.Detail), " ")
	return issue.Code + ": " + detail
}

func CheckIdentity(cfg *Config) IdentityCheck {
	result := IdentityCheck{Status: "ok", Profile: cfg.DefaultProfile, TargetOS: cfg.DeviceForge.TargetOS, TTL: cfg.TUN.TTL}
	profile, ok := cfg.Profiles[cfg.DefaultProfile]
	if !ok {
		result.add("profile-missing", "默认画像 "+cfg.DefaultProfile+" 不存在")
		return result
	}
	ua := headerValue(profile.Headers, "User-Agent")
	if ua == "" {
		result.add("ua-missing", "默认画像没有 User-Agent")
		return result
	}
	preset, parsed := ParseUserAgent(ua)
	if !parsed {
		result.add("ua-unknown", "无法识别默认画像的 User-Agent")
		return result
	}
	result.Browser = preset.Browser
	result.Platform = preset.Platform

	result.checkClientHints(profile.Headers, preset)
	result.checkTargetOS(preset)
	if cfg.TUN.Enabled {
		expected := RecommendedTTL(cfg.DefaultProfile)
		if family(preset.Platform) == "windows" {
			expected = 128
		} else {
			expected = 64
		}
		if cfg.TUN.TTL != expected {
			result.add("ttl-mismatch", fmt.Sprintf("画像平台 %s 的推荐 TTL 是 %d，当前为 %d", preset.Platform, expected, cfg.TUN.TTL))
		}
	}
	if cfg.DeviceForge.DHCP.Enabled {
		vendor := strings.ToLower(cfg.DeviceForge.DHCP.VendorClass)
		if family(preset.Platform) == "windows" && !strings.Contains(vendor, "msft") {
			result.add("dhcp-vendor-mismatch", "Windows 画像的 DHCP Vendor Class 应包含 MSFT，当前为 "+cfg.DeviceForge.DHCP.VendorClass)
		}
		if family(preset.Platform) != "windows" && strings.Contains(vendor, "msft") {
			result.add("dhcp-vendor-mismatch", "非 Windows 画像仍在使用 MSFT DHCP Vendor Class: "+cfg.DeviceForge.DHCP.VendorClass)
		}
	}
	return result
}

func (c *IdentityCheck) add(code, detail string) {
	c.Issues = append(c.Issues, IdentityIssue{Code: code, Detail: detail})
	c.Status = "warn"
}

func (c *IdentityCheck) checkClientHints(headers map[string]string, preset Preset) {
	chromium := preset.Browser == "chrome" || preset.Browser == "edge"
	for key := range headers {
		if strings.HasPrefix(strings.ToLower(key), "sec-ch-ua") && !chromium {
			c.add("client-hints-conflict", "非 Chromium 画像不应携带 "+key)
			break
		}
	}
	if !chromium {
		return
	}
	platform := headerValue(headers, "sec-ch-ua-platform")
	expectedPlatform := fmt.Sprintf("%q", preset.Platform)
	if platform != expectedPlatform {
		c.add("client-hints-conflict", "sec-ch-ua-platform 应为 "+expectedPlatform+"，当前为 "+platform)
	}
	mobile := headerValue(headers, "sec-ch-ua-mobile")
	expectedMobile := "?0"
	if preset.Mobile {
		expectedMobile = "?1"
	}
	if mobile != expectedMobile {
		c.add("client-hints-conflict", "sec-ch-ua-mobile 应为 "+expectedMobile+"，当前为 "+mobile)
	}
	fullVersion := headerValue(headers, "sec-ch-ua-full-version-list")
	if fullVersion != "" && !strings.Contains(fullVersion, preset.Version) {
		c.add("client-hints-conflict", "sec-ch-ua-full-version-list 与 UA 版本 "+preset.Version+" 不一致")
	}
}

func (c *IdentityCheck) checkTargetOS(preset Preset) {
	if strings.TrimSpace(c.TargetOS) == "" {
		return
	}
	if family(c.TargetOS) != family(preset.Platform) {
		c.add("target-os-mismatch", "deviceforge.target_os="+c.TargetOS+" 与 UA 平台 "+preset.Platform+" 不一致")
	}
}

func headerValue(headers map[string]string, key string) string {
	for k, v := range headers {
		if strings.EqualFold(k, key) {
			return v
		}
	}
	return ""
}

func family(value string) string {
	v := strings.ToLower(value)
	switch {
	case strings.Contains(v, "windows") || strings.Contains(v, "win"):
		return "windows"
	case strings.Contains(v, "android"):
		return "android"
	case strings.Contains(v, "iphone") || strings.Contains(v, "ipad") || strings.Contains(v, "ios"):
		return "ios"
	case strings.Contains(v, "mac"):
		return "macos"
	case strings.Contains(v, "linux"):
		return "linux"
	default:
		return ""
	}
}
