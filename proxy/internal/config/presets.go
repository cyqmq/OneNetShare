package config

import (
	"fmt"
	"regexp"
	"strings"
)

type Preset struct {
	Browser  string
	Version  string
	Platform string
	Mobile   bool
	Locale   string
}

var (
	chromeVersionRE  = regexp.MustCompile(`Chrome/([0-9]+(?:\.[0-9]+)*)`)
	edgeVersionRE    = regexp.MustCompile(`Edg(?:e|A|iOS)?/([0-9]+(?:\.[0-9]+)*)`)
	firefoxVersionRE = regexp.MustCompile(`Firefox/([0-9]+(?:\.[0-9]+)*)`)
	safariVersionRE  = regexp.MustCompile(`Version/([0-9]+(?:\.[0-9]+)*)`)
	curlVersionRE    = regexp.MustCompile(`curl/([0-9]+(?:\.[0-9]+)*)`)
)

func ParseUserAgent(ua string) (Preset, bool) {
	p := Preset{Locale: "zh-CN,zh;q=0.9,en;q=0.8"}
	lower := strings.ToLower(ua)
	switch {
	case strings.Contains(lower, "windows nt 10.0"):
		p.Platform = "Windows"
	case strings.Contains(lower, "android"):
		p.Platform = "Android"
	case strings.Contains(lower, "iphone") || strings.Contains(lower, "ipad"):
		p.Platform = "iOS"
	case strings.Contains(lower, "mac os x"):
		p.Platform = "macOS"
	case strings.Contains(lower, "linux"):
		p.Platform = "Linux"
	}
	p.Mobile = p.Platform == "Android" || p.Platform == "iOS" || strings.Contains(lower, "mobile")

	switch {
	case edgeVersionRE.MatchString(ua):
		p.Browser = "edge"
		p.Version = edgeVersionRE.FindStringSubmatch(ua)[1]
	case chromeVersionRE.MatchString(ua) && !strings.Contains(lower, "edg/"):
		p.Browser = "chrome"
		p.Version = chromeVersionRE.FindStringSubmatch(ua)[1]
	case firefoxVersionRE.MatchString(ua):
		p.Browser = "firefox"
		p.Version = firefoxVersionRE.FindStringSubmatch(ua)[1]
	case safariVersionRE.MatchString(ua) && strings.Contains(lower, "safari"):
		p.Browser = "safari"
		p.Version = safariVersionRE.FindStringSubmatch(ua)[1]
	case curlVersionRE.MatchString(ua):
		p.Browser = "curl"
		p.Version = curlVersionRE.FindStringSubmatch(ua)[1]
	default:
		return Preset{}, false
	}
	return p, true
}

func BuildHeaders(p Preset) map[string]string {
	locale := p.Locale
	if locale == "" {
		locale = "zh-CN,zh;q=0.9,en;q=0.8"
	}
	version := p.Version
	if version == "" {
		version = "158.0.0.0"
	}
	major := strings.SplitN(version, ".", 2)[0]
	headers := map[string]string{
		"Accept":                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/jxl,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7",
		"Accept-Language":           locale,
		"Accept-Encoding":           "gzip, deflate, br, zstd",
		"Cache-Control":             "max-age=0",
		"Sec-Fetch-Dest":            "document",
		"Sec-Fetch-Mode":            "navigate",
		"Sec-Fetch-Site":            "none",
		"Sec-Fetch-User":            "?1",
		"Upgrade-Insecure-Requests": "1",
	}
	switch p.Browser {
	case "chrome", "edge":
		brand := "Google Chrome"
		if p.Browser == "edge" {
			brand = "Microsoft Edge"
		}
		platform := p.Platform
		if platform == "" {
			platform = "Windows"
		}
		headers["User-Agent"] = buildChromiumUA(brand, version, platform, p.Mobile)
		headers["sec-ch-ua"] = fmt.Sprintf(`"Chromium";v="%s", "Not.A/Brand";v="24", "%s";v="%s"`, major, brand, major)
		headers["sec-ch-ua-mobile"] = boolHint(p.Mobile)
		headers["sec-ch-ua-platform"] = fmt.Sprintf("%q", platform)
		headers["sec-ch-ua-full-version-list"] = fmt.Sprintf(`"Chromium";v="%s", "Not.A/Brand";v="24", "%s";v="%s"`, version, brand, version)
		headers["sec-ch-ua-platform-version"] = platformVersion(platform, version)
		headers["sec-ch-ua-arch"] = "x86"
		headers["sec-ch-ua-bitness"] = "64"
		headers["sec-ch-ua-wow64"] = "?0"
		if p.Mobile {
			headers["sec-ch-ua-model"] = "Pixel 8"
		}
	case "firefox":
		headers["User-Agent"] = buildFirefoxUA(version, p.Platform)
		headers["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8"
	case "safari":
		headers["User-Agent"] = buildSafariUA(version, p.Platform, p.Mobile)
		headers["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
	case "curl":
		headers["User-Agent"] = "curl/" + version
		headers["Accept"] = "*/*"
	default:
		headers["User-Agent"] = "Mozilla/5.0"
	}
	return headers
}

func ApplyClientHints(headers map[string]string, p Preset) {
	RemoveClientHints(headers)
	if p.Browser != "chrome" && p.Browser != "edge" {
		return
	}
	generated := BuildHeaders(p)
	for key, value := range generated {
		if strings.HasPrefix(strings.ToLower(key), "sec-ch-ua") {
			headers[key] = value
		}
	}
}

func RemoveClientHints(headers map[string]string) {
	for key := range headers {
		if strings.HasPrefix(strings.ToLower(key), "sec-ch-ua") {
			delete(headers, key)
		}
	}
}

func buildChromiumUA(brand, version, platform string, mobile bool) string {
	switch platform {
	case "Android":
		if mobile {
			return fmt.Sprintf("Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/%s Mobile Safari/537.36", version)
		}
		return fmt.Sprintf("Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/%s Safari/537.36", version)
	case "macOS":
		return fmt.Sprintf("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/%s Safari/537.36", version)
	case "Linux":
		return fmt.Sprintf("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/%s Safari/537.36", version)
	case "iOS":
		return fmt.Sprintf("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/%s Mobile/15E148 Safari/604.1", version)
	default:
		if brand == "Microsoft Edge" {
			return fmt.Sprintf("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/%s Safari/537.36 Edg/%s", version, version)
		}
		return fmt.Sprintf("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/%s Safari/537.36", version)
	}
}

func buildFirefoxUA(version, platform string) string {
	major := strings.SplitN(version, ".", 2)[0]
	switch platform {
	case "Android":
		return fmt.Sprintf("Mozilla/5.0 (Android 14; Mobile; rv:%s.0) Gecko/%s.0 Firefox/%s.0", major, major, major)
	case "macOS":
		return fmt.Sprintf("Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:%s.0) Gecko/20100101 Firefox/%s.0", major, major)
	case "Linux":
		return fmt.Sprintf("Mozilla/5.0 (X11; Linux x86_64; rv:%s.0) Gecko/20100101 Firefox/%s.0", major, major)
	default:
		return fmt.Sprintf("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:%s.0) Gecko/20100101 Firefox/%s.0", major, major)
	}
}

func buildSafariUA(version, platform string, mobile bool) string {
	major := strings.SplitN(version, ".", 2)[0]
	if mobile || platform == "iOS" {
		return fmt.Sprintf("Mozilla/5.0 (iPhone; CPU iPhone OS %s_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/%s.0 Mobile/15E148 Safari/604.1", strings.ReplaceAll(major, ".", "_"), major)
	}
	return fmt.Sprintf("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/%s.0 Safari/605.1.15", major)
}

func boolHint(value bool) string {
	if value {
		return "?1"
	}
	return "?0"
}

func platformVersion(platform, version string) string {
	switch platform {
	case "Windows":
		return "19.0.0"
	case "Android":
		return "14.0.0"
	case "macOS":
		return "14.0.0"
	case "iOS":
		return "18.0.0"
	default:
		return ""
	}
}
