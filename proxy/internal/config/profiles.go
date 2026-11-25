package config

type profileSpec struct {
	name        string
	description string
	preset      Preset
}

var profileSpecs = []profileSpec{
	{"chrome-win11", "Chrome 158 / Windows 11 x64", Preset{Browser: "chrome", Version: "158.0.0.0", Platform: "Windows", Locale: "zh-CN,zh;q=0.9,en;q=0.8"}},
	{"chrome-win10", "Chrome 158 / Windows 10 x64", Preset{Browser: "chrome", Version: "158.0.0.0", Platform: "Windows", Locale: "zh-CN,zh;q=0.9,en;q=0.8"}},
	{"edge-win11", "Edge 158 / Windows 11 x64", Preset{Browser: "edge", Version: "158.0.0.0", Platform: "Windows", Locale: "zh-CN,zh;q=0.9,en;q=0.8"}},
	{"chrome-android", "Chrome 158 / Android 14", Preset{Browser: "chrome", Version: "158.0.0.0", Platform: "Android", Mobile: true, Locale: "zh-CN,zh;q=0.9,en;q=0.8"}},
	{"safari-ios", "Safari 18 / iOS 18", Preset{Browser: "safari", Version: "18.0", Platform: "iOS", Mobile: true, Locale: "zh-CN,zh;q=0.9"}},
	{"safari-macos", "Safari 18 / macOS 15", Preset{Browser: "safari", Version: "18.0", Platform: "macOS", Locale: "zh-CN,zh;q=0.9,en;q=0.8"}},
	{"firefox-win11", "Firefox 148 / Windows 11 x64", Preset{Browser: "firefox", Version: "148.0", Platform: "Windows", Locale: "zh-CN,zh;q=0.9,en;q=0.8"}},
	{"curl-linux", "curl 8.5 / Linux x64", Preset{Browser: "curl", Version: "8.5.0", Platform: "Linux", Locale: "*/*"}},
}

func BuiltinProfiles() map[string]Profile {
	profiles := make(map[string]Profile, len(profileSpecs)+2)
	for _, spec := range profileSpecs {
		profiles[spec.name] = Profile{Description: spec.description, Headers: BuildHeaders(spec.preset)}
	}
	profiles["mobile-android"] = profiles["chrome-android"]
	profiles["mobile-ios"] = profiles["safari-ios"]
	return profiles
}
