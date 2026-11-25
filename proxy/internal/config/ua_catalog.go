package config

type UAPreset struct {
	Name   string
	Label  string
	Preset Preset
}

var uaPresets = []UAPreset{
	{"chrome-158-windows", "Chrome 158 / Windows 11", Preset{Browser: "chrome", Version: "158.0.0.0", Platform: "Windows", Locale: "zh-CN,zh;q=0.9,en;q=0.8"}},
	{"chrome-158-macos", "Chrome 158 / macOS", Preset{Browser: "chrome", Version: "158.0.0.0", Platform: "macOS", Locale: "zh-CN,zh;q=0.9,en;q=0.8"}},
	{"chrome-158-linux", "Chrome 158 / Linux", Preset{Browser: "chrome", Version: "158.0.0.0", Platform: "Linux", Locale: "zh-CN,zh;q=0.9,en;q=0.8"}},
	{"chrome-158-android", "Chrome 158 / Android", Preset{Browser: "chrome", Version: "158.0.0.0", Platform: "Android", Mobile: true, Locale: "zh-CN,zh;q=0.9,en;q=0.8"}},
	{"chrome-158-ios", "Chrome 158 / iOS", Preset{Browser: "chrome", Version: "158.0.0.0", Platform: "iOS", Mobile: true, Locale: "zh-CN,zh;q=0.9,en;q=0.8"}},
	{"edge-158-windows", "Edge 158 / Windows 11", Preset{Browser: "edge", Version: "158.0.0.0", Platform: "Windows", Locale: "zh-CN,zh;q=0.9,en;q=0.8"}},
	{"firefox-148-windows", "Firefox 148 / Windows 11", Preset{Browser: "firefox", Version: "148.0", Platform: "Windows", Locale: "zh-CN,zh;q=0.9,en;q=0.8"}},
	{"firefox-148-macos", "Firefox 148 / macOS", Preset{Browser: "firefox", Version: "148.0", Platform: "macOS", Locale: "zh-CN,zh;q=0.9,en;q=0.8"}},
	{"firefox-148-linux", "Firefox 148 / Linux", Preset{Browser: "firefox", Version: "148.0", Platform: "Linux", Locale: "zh-CN,zh;q=0.9,en;q=0.8"}},
	{"firefox-148-android", "Firefox 148 / Android", Preset{Browser: "firefox", Version: "148.0", Platform: "Android", Mobile: true, Locale: "zh-CN,zh;q=0.9,en;q=0.8"}},
	{"safari-18-macos", "Safari 18 / macOS", Preset{Browser: "safari", Version: "18.0", Platform: "macOS", Locale: "zh-CN,zh;q=0.9,en;q=0.8"}},
	{"safari-18-ios", "Safari 18 / iOS", Preset{Browser: "safari", Version: "18.0", Platform: "iOS", Mobile: true, Locale: "zh-CN,zh;q=0.9"}},
	{"curl-8.5-linux", "curl 8.5 / Linux", Preset{Browser: "curl", Version: "8.5.0", Platform: "Linux", Locale: "*/*"}},
}

func UAPresets() []UAPreset {
	return append([]UAPreset(nil), uaPresets...)
}

func LookupUAPreset(name string) (UAPreset, bool) {
	for _, item := range uaPresets {
		if item.Name == name {
			return item, true
		}
	}
	return UAPreset{}, false
}
