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
