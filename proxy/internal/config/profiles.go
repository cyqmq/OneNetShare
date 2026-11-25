package config

func BuiltinProfiles() map[string]Profile {
	profiles := map[string]Profile{
		"chrome-win11": {
			Description: "Chrome 131 / Windows 11 x64",
			Headers: map[string]string{
				"User-Agent":                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
				"Accept":                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7",
				"Accept-Language":           "zh-CN,zh;q=0.9,en;q=0.8,en-US;q=0.7",
				"Accept-Encoding":           "gzip, deflate, br, zstd",
				"Cache-Control":             "max-age=0",
				"sec-ch-ua":                 "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"",
				"sec-ch-ua-mobile":          "?0",
				"sec-ch-ua-platform":        "\"Windows\"",
				"Sec-Fetch-Dest":            "document",
				"Sec-Fetch-Mode":            "navigate",
				"Sec-Fetch-Site":            "none",
				"Sec-Fetch-User":            "?1",
				"Upgrade-Insecure-Requests": "1",
			},
		},
		"chrome-win10": {
			Description: "Chrome 120 / Windows 10 x64",
			Headers: map[string]string{
				"User-Agent":                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
				"Accept":                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8",
				"Accept-Language":           "zh-CN,zh;q=0.9,en;q=0.8",
				"Accept-Encoding":           "gzip, deflate, br",
				"sec-ch-ua":                 "\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"120\", \"Google Chrome\";v=\"120\"",
				"sec-ch-ua-mobile":          "?0",
				"sec-ch-ua-platform":        "\"Windows\"",
				"Sec-Fetch-Dest":            "document",
				"Sec-Fetch-Mode":            "navigate",
				"Sec-Fetch-Site":            "none",
				"Upgrade-Insecure-Requests": "1",
			},
		},
		"edge-win11": {
			Description: "Edge 131 / Windows 11 x64",
			Headers: map[string]string{
				"User-Agent":                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0",
				"Accept":                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8",
				"Accept-Language":           "zh-CN,zh;q=0.9,en;q=0.8",
				"Accept-Encoding":           "gzip, deflate, br, zstd",
				"sec-ch-ua":                 "\"Microsoft Edge\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"",
				"sec-ch-ua-mobile":          "?0",
				"sec-ch-ua-platform":        "\"Windows\"",
				"Sec-Fetch-Dest":            "document",
				"Sec-Fetch-Mode":            "navigate",
				"Sec-Fetch-Site":            "none",
				"Upgrade-Insecure-Requests": "1",
			},
		},
		"chrome-android": {
			Description: "Chrome 131 / Android 14",
			Headers: map[string]string{
				"User-Agent":                "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36",
				"Accept":                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8",
				"Accept-Language":           "zh-CN,zh;q=0.9,en;q=0.8",
				"Accept-Encoding":           "gzip, deflate, br",
				"sec-ch-ua":                 "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"",
				"sec-ch-ua-mobile":          "?1",
				"sec-ch-ua-platform":        "\"Android\"",
				"Sec-Fetch-Dest":            "document",
				"Sec-Fetch-Mode":            "navigate",
				"Sec-Fetch-Site":            "none",
				"Upgrade-Insecure-Requests": "1",
			},
		},
		"safari-ios": {
			Description: "Safari 17 / iOS 17",
			Headers: map[string]string{
				"User-Agent":      "Mozilla/5.0 (iPhone; CPU iPhone OS 17_2 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Mobile/15E148 Safari/604.1",
				"Accept":          "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
				"Accept-Language": "zh-CN,zh;q=0.9",
				"Accept-Encoding": "gzip, deflate, br",
			},
		},
		"safari-macos": {
			Description: "Safari 17 / macOS 14",
			Headers: map[string]string{
				"User-Agent":      "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_2) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Safari/605.1.15",
				"Accept":          "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
				"Accept-Language": "zh-CN,zh;q=0.9,en;q=0.8",
				"Accept-Encoding": "gzip, deflate, br",
			},
		},
		"firefox-win11": {
			Description: "Firefox 133 / Windows 11 x64",
			Headers: map[string]string{
				"User-Agent":                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0",
				"Accept":                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8",
				"Accept-Language":           "zh-CN,zh;q=0.9,en;q=0.8",
				"Accept-Encoding":           "gzip, deflate, br",
				"Sec-Fetch-Dest":            "document",
				"Sec-Fetch-Mode":            "navigate",
				"Sec-Fetch-Site":            "none",
				"Upgrade-Insecure-Requests": "1",
			},
		},
		"curl-linux": {
			Description: "curl 8.x / Linux x64",
			Headers: map[string]string{
				"User-Agent": "curl/8.5.0",
				"Accept":     "*/*",
			},
		},
	}
	profiles["mobile-android"] = profiles["chrome-android"]
	profiles["mobile-ios"] = profiles["safari-ios"]
	return profiles
}
