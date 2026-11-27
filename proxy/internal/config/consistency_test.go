package config

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func loadTestConfig(t *testing.T, text string) *Config {
	t.Helper()
	path := filepath.Join(t.TempDir(), "config.json")
	if err := os.WriteFile(path, []byte(text), 0644); err != nil {
		t.Fatal(err)
	}
	loader, err := NewLoader(path, nil)
	if err != nil {
		t.Fatal(err)
	}
	return loader.Get()
}

func TestCheckIdentityDefaultIsCoherent(t *testing.T) {
	cfg := loadTestConfig(t, DefaultJSON())
	result := CheckIdentity(cfg)
	if !result.OK() {
		t.Fatalf("默认画像不应有问题: %+v", result.Issues)
	}
	if result.Platform != "Windows" || result.Browser != "chrome" {
		t.Fatalf("画像解析错误: %+v", result)
	}
}

func TestCheckIdentityDetectsCrossProfileMismatch(t *testing.T) {
	cfg := loadTestConfig(t, `{
		"default_profile":"chrome-android",
		"ua_preset":"chrome-158-android",
		"tun":{"enabled":true,"ttl":128},
		"deviceforge":{"enabled":true,"target_os":"windows-11","dhcp":{"enabled":true,"vendor_class":"MSFT 5.0"}}
	}`)
	result := CheckIdentity(cfg)
	if result.OK() {
		t.Fatal("交叉画像应被识别为不一致")
	}
	joined := ""
	for i := range result.Issues {
		joined += result.IssueLine(i) + "\n"
	}
	for _, code := range []string{"target-os-mismatch", "ttl-mismatch", "dhcp-vendor-mismatch"} {
		if !strings.Contains(joined, code) {
			t.Fatalf("缺少 %s: %s", code, joined)
		}
	}
}
