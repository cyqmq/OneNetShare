package routes

import (
	"io"
	"log"
	"os"
	"path/filepath"
	"testing"
)

func testLogger() *log.Logger {
	return log.New(io.Discard, "", 0)
}

func TestNormalizeRejectsDefaultRoute(t *testing.T) {
	file := &File{
		Version:    1,
		Interfaces: map[string]Interface{"upstream": {Name: "Ethernet", AdapterGUID: "{1}"}},
		Routes: []Route{{
			ID:          "default",
			Destination: "0.0.0.0/0",
			Via:         "upstream",
			Gateway:     "192.168.1.1",
		}},
	}
	if _, err := normalize(file); err == nil {
		t.Fatal("默认路由应被拒绝")
	}
}

func TestNormalizeAllowsExplicitDefaultWhenOptedIn(t *testing.T) {
	file := &File{
		Version:    1,
		Interfaces: map[string]Interface{"tun": {Name: "OneNetShare Tunnel"}},
		Routes: []Route{{
			ID:                "tun-all",
			Destination:       "0.0.0.0/0",
			Via:               "tun",
			NextHop:           "10.66.0.2",
			AllowDefaultRoute: true,
		}},
	}
	routes, err := normalize(file)
	if err != nil {
		t.Fatalf("显式默认路由应允许: %v", err)
	}
	if len(routes) != 1 || routes[0].Prefix != "0.0.0.0/0" || routes[0].Gateway != "10.66.0.2" {
		t.Fatalf("路由规范化错误: %+v", routes)
	}
}

func TestNormalizeRejectsUnknownVia(t *testing.T) {
	file := &File{
		Version: 1,
		Routes: []Route{{
			Destination: "198.51.100.7/32",
			Via:         "missing",
			Gateway:     "192.168.1.1",
		}},
	}
	if _, err := normalize(file); err == nil {
		t.Fatal("未定义 via 应被拒绝")
	}
}

func TestManagerReloadIfChanged(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "managedroutes.json")
	content := []byte(`{"version":1,"enabled":false,"routes":[]}`)
	if err := os.WriteFile(path, content, 0o600); err != nil {
		t.Fatal(err)
	}
	manager := New(path, testLogger())
	changed, err := manager.ReloadIfChanged()
	if err != nil || !changed {
		t.Fatalf("首次加载失败: changed=%v err=%v", changed, err)
	}
	changed, err = manager.ReloadIfChanged()
	if err != nil || changed {
		t.Fatalf("未变化不应重载: changed=%v err=%v", changed, err)
	}
}
