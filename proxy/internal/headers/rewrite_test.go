package headers

import (
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

func TestBuiltinMobileAliases(t *testing.T) {
	profiles := config.BuiltinProfiles()
	if profiles["mobile-android"].Headers["User-Agent"] != profiles["chrome-android"].Headers["User-Agent"] {
		t.Fatal("mobile-android 别名未生效")
	}
	if profiles["mobile-ios"].Headers["User-Agent"] != profiles["safari-ios"].Headers["User-Agent"] {
		t.Fatal("mobile-ios 别名未生效")
	}
}
