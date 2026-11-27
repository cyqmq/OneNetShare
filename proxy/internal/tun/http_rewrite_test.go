package tun

import (
	"os"
	"path/filepath"
	"strings"
	"testing"

	"hotspot-proxy/internal/config"
	"hotspot-proxy/internal/headers"
)

func TestHTTPStreamRewrite(t *testing.T) {
	path := filepath.Join(t.TempDir(), "config.json")
	data := `{"enabled":true,"mode":"forward","default_profile":"chrome-win11","rules":[],"deviceforge":{"enabled":true,"headers":true}}`
	if err := os.WriteFile(path, []byte(data), 0o600); err != nil {
		t.Fatal(err)
	}
	loader, err := config.NewLoader(path, nil)
	if err != nil {
		t.Fatal(err)
	}
	rw := &httpStreamRewriter{engine: headers.NewEngine(loader)}
	req := "GET /probe HTTP/1.1\r\n" +
		"Host: 127.0.0.1:18099\r\n" +
		"User-Agent: OneNetShare-Unprobed/1.0\r\n" +
		"Via: 1.1 unprobed\r\n" +
		"X-Forwarded-For: 203.0.113.9\r\n\r\n"
	out, err := rw.Rewrite([]byte(req))
	if err != nil {
		t.Fatal(err)
	}
	text := string(out)
	if strings.Contains(text, "OneNetShare-Unprobed") {
		t.Fatalf("UA 未被改写: %s", text)
	}
	if !strings.Contains(text, "User-Agent:Mozilla/5.0") {
		t.Fatalf("UA 未替换为画像值: %s", text)
	}
	if strings.Contains(text, "Via:") || strings.Contains(text, "X-Forwarded-For:") {
		t.Fatalf("泄露头未清除: %s", text)
	}
}
