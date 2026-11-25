package headers

import (
	"os"
	"path/filepath"
	"strings"
	"testing"

	"hotspot-proxy/internal/config"
)

func TestRewriteUsesUAPresetCatalog(t *testing.T) {
	path := filepath.Join(t.TempDir(), "config.json")
	data := `{"enabled":true,"mode":"forward","default_profile":"chrome-win11","ua_preset":"chrome-158-android","rules":[{"name":"default-all","enabled":true,"domains":["*"],"profile":"default"}],"deviceforge":{"enabled":true,"headers":true}}`
	if err := os.WriteFile(path, []byte(data), 0o600); err != nil {
		t.Fatal(err)
	}
	loader, err := config.NewLoader(path, nil)
	if err != nil {
		t.Fatal(err)
	}
	req := &Request{
		Method:  "GET",
		URI:     "http://example.com/probe",
		Proto:   "HTTP/1.1",
		Host:    "example.com",
		Path:    "/probe",
		Headers: []Header{{Key: "User-Agent", Value: "OneNetShare-Unprobed/1.0"}},
	}
	_, rewritten, err := NewEngine(loader).Rewrite(req, "")
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(rewritten, "User-Agent: Mozilla/5.0 (Linux; Android 10; K)") {
		t.Fatalf("UA 预设未生效: %s", rewritten)
	}
	if !strings.Contains(rewritten, `sec-ch-ua-mobile: ?1`) {
		t.Fatalf("移动端 Client Hints 未生效: %s", rewritten)
	}
}

func TestRewriteUsesDefaultProfileAndUAOverride(t *testing.T) {
	ua := "Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Mobile Safari/537.36"
	path := filepath.Join(t.TempDir(), "config.json")
	data := `{"enabled":true,"mode":"forward","default_profile":"chrome-android","user_agent_override":"` + ua + `","rules":[{"name":"default-all","enabled":true,"domains":["*"],"profile":"default"}],"deviceforge":{"enabled":true,"headers":true}}`
	if err := os.WriteFile(path, []byte(data), 0o600); err != nil {
		t.Fatal(err)
	}
	loader, err := config.NewLoader(path, nil)
	if err != nil {
		t.Fatal(err)
	}
	cfg := loader.Get()
	if cfg.Rules[0].Profile != "chrome-android" {
		t.Fatalf("默认规则未跟随画像: %s", cfg.Rules[0].Profile)
	}
	req := &Request{
		Method:  "GET",
		URI:     "http://example.com/probe",
		Proto:   "HTTP/1.1",
		Host:    "example.com",
		Path:    "/probe",
		Headers: []Header{{Key: "User-Agent", Value: "OneNetShare-Unprobed/1.0"}},
	}
	_, rewritten, err := NewEngine(loader).Rewrite(req, "")
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(rewritten, "User-Agent: "+ua) {
		t.Fatalf("UA 覆盖未生效: %s", rewritten)
	}
	if !strings.Contains(rewritten, `sec-ch-ua: "Chromium";v="129"`) {
		t.Fatalf("Client Hints 未跟随 UA 覆盖: %s", rewritten)
	}
}
