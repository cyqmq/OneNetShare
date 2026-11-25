package config

import "strings"

var defaultCaptiveDomains = []string{
	"captive.apple.com",
	"www.apple.com",
	"apple.com",
	"www.msftconnecttest.com",
	"msftconnecttest.com",
	"ipv6.msftconnecttest.com",
	"clients3.google.com",
	"clients4.google.com",
	"connectivitycheck.gstatic.com",
	"captive.googleapis.com",
	"detectportal.firefox.com",
	"nmcheck.gnome.org",
	"network-test.debian.org",
	"www.google.com",
}

var defaultCaptiveDNSDomains = []string{
	"captive.apple.com",
	"www.msftconnecttest.com",
	"msftconnecttest.com",
	"ipv6.msftconnecttest.com",
	"clients3.google.com",
	"clients4.google.com",
	"connectivitycheck.gstatic.com",
	"captive.googleapis.com",
	"detectportal.firefox.com",
	"nmcheck.gnome.org",
	"network-test.debian.org",
}

var defaultCaptivePaths = []string{
	"/generate_204",
	"/gen_204",
	"/connecttest.txt",
	"/hotspot-detect.html",
	"/success.txt",
	"/nmcheck",
	"/check.txt",
	"/portal-compat-check.html",
}

func applyCaptiveDefaults(c *CaptiveConfig) {
	if c.Mode == "" {
		c.Mode = "reply"
	}
	if len(c.Domains) == 0 {
		c.Domains = append([]string(nil), defaultCaptiveDomains...)
	}
	if len(c.DNSDomains) == 0 {
		c.DNSDomains = append([]string(nil), defaultCaptiveDNSDomains...)
	}
	if len(c.Paths) == 0 {
		c.Paths = append([]string(nil), defaultCaptivePaths...)
	}
	if c.TTL == 0 {
		c.TTL = 60
	}
	if c.HTTPCode == 0 {
		c.HTTPCode = 204
	}
}

func CaptiveDomainMatch(c CaptiveConfig, host string) bool {
	host = strings.ToLower(strings.TrimSuffix(strings.TrimSpace(host), "."))
	for _, pattern := range c.Domains {
		if MatchDomain(pattern, host) {
			return true
		}
	}
	return false
}

func CaptiveDNSMatch(c CaptiveConfig, host string) bool {
	host = strings.ToLower(strings.TrimSuffix(strings.TrimSpace(host), "."))
	for _, pattern := range c.DNSDomains {
		if MatchDomain(pattern, host) {
			return true
		}
	}
	return false
}

func CaptivePathMatch(c CaptiveConfig, path string) bool {
	if idx := strings.IndexByte(path, '?'); idx >= 0 {
		path = path[:idx]
	}
	if path == "" {
		path = "/"
	}
	for _, prefix := range c.Paths {
		if prefix == "" {
			continue
		}
		if strings.HasPrefix(path, prefix) {
			return true
		}
	}
	return false
}
func CaptiveHostMatch(c CaptiveConfig, host string) bool {
	if !c.Enabled {
		return false
	}
	return CaptiveDomainMatch(c, host)
}

func CaptiveHTTPMatch(c CaptiveConfig, host, path string) bool {
	if !c.Enabled {
		return false
	}
	return CaptiveDomainMatch(c, host) && CaptivePathMatch(c, path)
}
