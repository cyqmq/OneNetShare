package headers

import (
	"bufio"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"hotspot-proxy/internal/config"
)

func TestRewriteFallsBackWhenProfileMissing(t *testing.T) {
	path := filepath.Join(t.TempDir(), "config.json")
	data := `{"enabled":true,"mode":"forward","default_profile":"legacy-profile","rules":[],"deviceforge":{"enabled":true,"headers":true},"profiles":{"custom":{"description":"custom","headers":{"Accept":"*/*"}}}}`
	if err := os.WriteFile(path, []byte(data), 0o600); err != nil {
		t.Fatal(err)
	}
	loader, err := config.NewLoader(path, nil)
	if err != nil {
		t.Fatal(err)
	}
	engine := NewEngine(loader)
	req := &Request{
		Method: "GET",
		URI:    "http://example.com/probe",
		Proto:  "HTTP/1.1",
		Host:   "example.com",
		Path:   "/probe",
		Headers: []Header{
			{Key: "User-Agent", Value: "OneNetShare-Unprobed/1.0"},
			{Key: "Via", Value: "1.1 unprobed"},
		},
	}
	_, rewritten, err := engine.Rewrite(req, "")
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(rewritten, "OneNetShare-Unprobed") {
		t.Fatalf("UA 未被改写: %s", rewritten)
	}
	if !strings.Contains(rewritten, "User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64)") {
		t.Fatalf("未回退到内置 Chrome 画像: %s", rewritten)
	}
	if strings.Contains(rewritten, "Via:") {
		t.Fatalf("Via 未清除: %s", rewritten)
	}
}

func TestRewriteForcesUAWhenProfileHasNone(t *testing.T) {
	path := filepath.Join(t.TempDir(), "config.json")
	data := `{"enabled":true,"mode":"forward","default_profile":"custom","rules":[{"name":"all","enabled":true,"domains":["*"],"profile":"custom"}],"deviceforge":{"enabled":true,"headers":true},"profiles":{"custom":{"description":"custom","headers":{"Accept":"*/*"}}}}`
	if err := os.WriteFile(path, []byte(data), 0o600); err != nil {
		t.Fatal(err)
	}
	loader, err := config.NewLoader(path, nil)
	if err != nil {
		t.Fatal(err)
	}
	engine := NewEngine(loader)
	req := &Request{
		Method:  "GET",
		URI:     "http://example.com/probe",
		Proto:   "HTTP/1.1",
		Host:    "example.com",
		Path:    "/probe",
		Headers: []Header{{Key: "User-Agent", Value: "OneNetShare-Unprobed/1.0"}},
	}
	_, rewritten, err := engine.Rewrite(req, "")
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(rewritten, "OneNetShare-Unprobed") {
		t.Fatalf("UA 未被强制改写: %s", rewritten)
	}
	if !strings.Contains(rewritten, "User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64)") {
		t.Fatalf("UA 未回退到内置画像: %s", rewritten)
	}
}

func TestBuiltinMobileAliases(t *testing.T) {
	profiles := config.BuiltinProfiles()
	if profiles["mobile-android"].Headers["User-Agent"] != profiles["chrome-android"].Headers["User-Agent"] {
		t.Fatal("mobile-android 别名未生效")
	}
	if profiles["mobile-ios"].Headers["User-Agent"] != profiles["safari-ios"].Headers["User-Agent"] {
		t.Fatal("mobile-ios 别名未生效")
	}
}

func TestRewriteNormalizesFingerprintHeaders(t *testing.T) {
	path := filepath.Join(t.TempDir(), "config.json")
	data := `{"enabled":true,"mode":"forward","default_profile":"chrome-win11","rules":[{"name":"all","enabled":true,"domains":["*"],"profile":"chrome-win11"}],"deviceforge":{"enabled":true,"headers":true},"profiles":{}}`
	if err := os.WriteFile(path, []byte(data), 0o600); err != nil {
		t.Fatal(err)
	}
	loader, err := config.NewLoader(path, nil)
	if err != nil {
		t.Fatal(err)
	}
	engine := NewEngine(loader)
	req := &Request{
		Method: "GET", URI: "http://example.com/", Proto: "HTTP/1.1", Host: "example.com", Path: "/",
		Headers: []Header{
			{Key: "User-Agent", Value: "Mozilla/5.0 Android"},
			{Key: "Accept-Language", Value: "en-US,en;q=0.5"},
			{Key: "Accept-Encoding", Value: "gzip"},
		},
	}
	_, rewritten, err := engine.Rewrite(req, "")
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(rewritten, "Accept-Language: zh-CN,zh;q=0.9,en;q=0.8") {
		t.Fatalf("Accept-Language 未归一: %s", rewritten)
	}
	if !strings.Contains(rewritten, "Accept-Encoding: gzip, deflate, br, zstd") {
		t.Fatalf("Accept-Encoding 未归一: %s", rewritten)
	}
}

func TestRewriteStripsRefererOnCaptiveProbe(t *testing.T) {
	path := filepath.Join(t.TempDir(), "config.json")
	data := `{"enabled":true,"mode":"forward","default_profile":"chrome-win11","rules":[{"name":"all","enabled":true,"domains":["*"],"profile":"chrome-win11"}],"deviceforge":{"enabled":true,"headers":true,"captive":{"enabled":true,"domains":["captive.apple.com"],"dns_domains":["captive.apple.com"],"paths":["/hotspot-detect.html"]}},"profiles":{}}`
	if err := os.WriteFile(path, []byte(data), 0o600); err != nil {
		t.Fatal(err)
	}
	loader, err := config.NewLoader(path, nil)
	if err != nil {
		t.Fatal(err)
	}
	engine := NewEngine(loader)
	req := &Request{
		Method: "GET", URI: "http://captive.apple.com/hotspot-detect.html", Proto: "HTTP/1.1", Host: "captive.apple.com", Path: "/hotspot-detect.html",
		Headers: []Header{{Key: "Referer", Value: "http://captive.apple.com/"}, {Key: "Origin", Value: "http://captive.apple.com"}},
	}
	_, rewritten, err := engine.Rewrite(req, "")
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(strings.ToLower(rewritten), "\r\nreferer:") || strings.Contains(strings.ToLower(rewritten), "\r\norigin:") {
		t.Fatalf("captive probe Referer/Origin 未清除: %s", rewritten)
	}
}

func TestRewriteWithClientExactPreservesHeaderLength(t *testing.T) {
	path := filepath.Join(t.TempDir(), "config.json")
	data := `{"enabled":true,"mode":"tun","default_profile":"chrome-win11","rules":[],"deviceforge":{"enabled":true,"headers":true},"profiles":{}}`
	if err := os.WriteFile(path, []byte(data), 0o600); err != nil {
		t.Fatal(err)
	}
	loader, err := config.NewLoader(path, nil)
	if err != nil {
		t.Fatal(err)
	}
	engine := NewEngine(loader)
	oldUA := strings.Repeat("A", 180)
	raw := "GET / HTTP/1.1\r\nHost: example.com\r\nUser-Agent: " + oldUA + "\r\nAccept: */*\r\n\r\n"
	req, err := ParseRequest(bufio.NewReader(strings.NewReader(raw)))
	if err != nil {
		t.Fatal(err)
	}
	_, rewritten, err := engine.RewriteWithClientExact(req, "", "192.168.137.2")
	if err != nil {
		t.Fatal(err)
	}
	parsed, err := ParseRequest(bufio.NewReader(strings.NewReader(rewritten)))
	if err != nil {
		t.Fatal(err)
	}
	if len(parsed.RawHeader) != len(req.RawHeader) {
		t.Fatalf("Header 长度变化: old=%d new=%d\n%s", len(req.RawHeader), len(parsed.RawHeader), rewritten)
	}
	if strings.Contains(rewritten, oldUA) {
		t.Fatalf("UA 未被改写: %s", rewritten)
	}
}
