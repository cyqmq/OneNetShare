package monitor

import "strings"

type uaKind string

const (
	kindPhone   uaKind = "phone"
	kindDesktop uaKind = "desktop"
	kindUnknown uaKind = "unknown"
)

func classifyUserAgent(ua string) uaKind {
	lower := strings.ToLower(strings.TrimSpace(ua))
	if lower == "" {
		return kindUnknown
	}
	for _, token := range []string{"android", "iphone", "ipad", "ipod", "windows phone", "mobile safari", "mobile/", "blackberry", "opera mini"} {
		if strings.Contains(lower, token) {
			return kindPhone
		}
	}
	for _, token := range []string{"windows nt", "macintosh", "mac os x", "x11; linux", "x11; ubuntu", "cros"} {
		if strings.Contains(lower, token) {
			return kindDesktop
		}
	}
	if strings.Contains(lower, "linux") {
		return kindDesktop
	}
	return kindUnknown
}
