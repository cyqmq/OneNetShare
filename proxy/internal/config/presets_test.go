package config

import (
	"strings"
	"testing"
)

func TestParseUserAgentAndClientHintsStayCoherent(t *testing.T) {
	ua := "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/158.0.0.0 Safari/537.36"
	preset, ok := ParseUserAgent(ua)
	if !ok {
		t.Fatal("UA 解析失败")
	}
	if preset.Browser != "chrome" || preset.Version != "158.0.0.0" || preset.Platform != "Windows" {
		t.Fatalf("画像解析错误: %+v", preset)
	}
	headers := BuildHeaders(preset)
	if headers["User-Agent"] != ua {
		t.Fatalf("UA 不一致: %s", headers["User-Agent"])
	}
	if !strings.Contains(headers["sec-ch-ua"], `;v="158"`) {
		t.Fatalf("sec-ch-ua 版本不一致: %s", headers["sec-ch-ua"])
	}
	if !strings.Contains(headers["sec-ch-ua-full-version-list"], `v="158.0.0.0"`) {
		t.Fatalf("full-version-list 不一致: %s", headers["sec-ch-ua-full-version-list"])
	}
}

func TestParseUserAgentOverrideRebuildsClientHints(t *testing.T) {
	ua := "Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Mobile Safari/537.36"
	preset, ok := ParseUserAgent(ua)
	if !ok {
		t.Fatal("UA 解析失败")
	}
	headers := map[string]string{"sec-ch-ua": "stale", "sec-ch-ua-full-version-list": "stale"}
	ApplyClientHints(headers, preset)
	if !strings.Contains(headers["sec-ch-ua"], `;v="129"`) {
		t.Fatalf("Client Hints 未跟随 UA: %s", headers["sec-ch-ua"])
	}
	if headers["sec-ch-ua-mobile"] != "?1" || headers["sec-ch-ua-platform"] != `"Android"` {
		t.Fatalf("移动端画像不一致: %+v", headers)
	}
}

func TestUAPresetCatalogIsCoherent(t *testing.T) {
	presets := UAPresets()
	if len(presets) < 10 {
		t.Fatalf("UA 预设数量不足: %d", len(presets))
	}
	seen := map[string]bool{}
	for _, item := range presets {
		if item.Name == "" || item.Label == "" {
			t.Fatalf("预设缺少名称或标签: %+v", item)
		}
		if seen[item.Name] {
			t.Fatalf("预设名称重复: %s", item.Name)
		}
		seen[item.Name] = true
		headers := BuildHeaders(item.Preset)
		if headers["User-Agent"] == "" {
			t.Fatalf("预设没有 UA: %s", item.Name)
		}
		if item.Preset.Browser == "chrome" || item.Preset.Browser == "edge" {
			major := strings.SplitN(item.Preset.Version, ".", 2)[0]
			uaToken := "Chrome/" + major
			if item.Preset.Platform == "iOS" {
				uaToken = "CriOS/" + major
			}
			if !strings.Contains(headers["User-Agent"], uaToken) {
				t.Fatalf("UA 与版本不一致: %s", item.Name)
			}
			if !strings.Contains(headers["sec-ch-ua"], `;v="`+major+`"`) {
				t.Fatalf("Client Hints 与版本不一致: %s", item.Name)
			}
		} else {
			for key := range headers {
				if strings.HasPrefix(strings.ToLower(key), "sec-ch-ua") {
					t.Fatalf("非 Chromium 预设不应有 Client Hints: %s", item.Name)
				}
			}
		}
	}
	if _, ok := LookupUAPreset("chrome-158-windows"); !ok {
		t.Fatal("默认 UA 预设不存在")
	}
}

func TestNonChromiumUARemovesClientHints(t *testing.T) {
	ua := "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:148.0) Gecko/20100101 Firefox/148.0"
	preset, ok := ParseUserAgent(ua)
	if !ok {
		t.Fatal("UA 解析失败")
	}
	headers := map[string]string{"sec-ch-ua": "stale", "sec-ch-ua-platform": `"Windows"`}
	ApplyClientHints(headers, preset)
	for key := range headers {
		if strings.HasPrefix(strings.ToLower(key), "sec-ch-ua") {
			t.Fatalf("Firefox 不应携带 Client Hints: %s", key)
		}
	}
}
