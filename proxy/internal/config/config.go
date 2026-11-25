package config

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"sync"
)

type Config struct {
	Enabled        bool               `json:"enabled"`
	Mode           string             `json:"mode"`
	ListenHTTP     string             `json:"listen_http"`
	ListenHTTPS    string             `json:"listen_https"`
	DefaultProfile string             `json:"default_profile"`
	Profiles       map[string]Profile `json:"profiles"`
	Rules          []Rule             `json:"rules"`
	Log            LogConfig          `json:"log"`
	AutoReload     bool               `json:"auto_reload"`
	TUN            TUNConfig          `json:"tun"`
	DeviceForge    DeviceForgeConfig  `json:"deviceforge"`
}

type TUNConfig struct {
	Enabled           bool   `json:"enabled"`
	Name              string `json:"name"`
	IP                string `json:"ip"`
	GatewayIP         string `json:"gateway_ip"`
	UpstreamInterface string `json:"upstream_interface"`
	PrefixLen         int    `json:"prefix_len"`
	Route             *bool  `json:"route"`
	TTL               int    `json:"ttl"`
}

type DeviceForgeConfig struct {
	Enabled  bool            `json:"enabled"`
	TargetOS string          `json:"target_os"`
	UDP      UDPForgeConfig  `json:"udp"`
	DNS      DNSForgeConfig  `json:"dns"`
	NTP      NTPForgeConfig  `json:"ntp"`
	MDNS     MDNSForgeConfig `json:"mdns"`
	DHCP     DHCPForgeConfig `json:"dhcp"`
	Captive  CaptiveConfig   `json:"captive"`
	Headers  bool            `json:"headers"`
}

type UDPForgeConfig struct {
	Enabled     bool           `json:"enabled"`
	Listen      string         `json:"listen"`
	AllPorts    bool           `json:"all_ports"`
	TargetPorts []int          `json:"target_ports"`
	ListenPorts map[string]int `json:"listen_ports"`
}

type DNSForgeConfig struct {
	Enabled   bool   `json:"enabled"`
	StripEDNS bool   `json:"strip_edns"`
	ForceTCP  bool   `json:"force_tcp"`
	MinTTL    uint32 `json:"min_ttl"`
	MaxTTL    uint32 `json:"max_ttl"`
}

type NTPForgeConfig struct {
	Enabled      bool   `json:"enabled"`
	Server       string `json:"server"`
	RewriteRefID bool   `json:"rewrite_refid"`
}

type MDNSForgeConfig struct {
	Enabled  bool   `json:"enabled"`
	Hostname string `json:"hostname"`
	Service  string `json:"service"`
}

type DHCPForgeConfig struct {
	Enabled     bool   `json:"enabled"`
	VendorClass string `json:"vendor_class"`
	ClientID    string `json:"client_id"`
}

type CaptiveConfig struct {
	Enabled    bool     `json:"enabled"`
	Mode       string   `json:"mode"`
	Domains    []string `json:"domains"`
	DNSDomains []string `json:"dns_domains"`
	Paths      []string `json:"paths"`
	ReplyIP    string   `json:"reply_ip"`
	TTL        uint32   `json:"ttl"`
	HTTPCode   int      `json:"http_code"`
	HTTPBody   string   `json:"http_body"`
}

type Profile struct {
	Description string            `json:"description"`
	Headers     map[string]string `json:"headers"`
}

type Rule struct {
	Name          string            `json:"name"`
	Enabled       bool              `json:"enabled"`
	Domains       []string          `json:"domains"`
	PathPrefix    string            `json:"path_prefix,omitempty"`
	Profile       string            `json:"profile,omitempty"`
	SetHeaders    map[string]string `json:"set_headers,omitempty"`
	RemoveHeaders []string          `json:"remove_headers,omitempty"`
	AddHeaders    map[string]string `json:"add_headers,omitempty"`
}

type LogConfig struct {
	Level string `json:"level"`
	File  string `json:"file"`
}

type Loader struct {
	mu       sync.RWMutex
	path     string
	cfg      *Config
	modTime  int64
	onChange func(*Config)
}

func NewLoader(path string, onChange func(*Config)) (*Loader, error) {
	l := &Loader{path: path, onChange: onChange}
	if err := l.Reload(); err != nil {
		return nil, err
	}
	return l, nil
}

func (l *Loader) Get() *Config {
	l.mu.RLock()
	defer l.mu.RUnlock()
	c := *l.cfg
	c.Profiles = make(map[string]Profile, len(l.cfg.Profiles))
	for k, v := range l.cfg.Profiles {
		c.Profiles[k] = v
	}
	c.Rules = append([]Rule(nil), l.cfg.Rules...)
	return &c
}

func (l *Loader) Path() string { return l.path }

func (l *Loader) Reload() error {
	data, err := os.ReadFile(l.path)
	if err != nil {
		return fmt.Errorf("读取配置失败 %s: %w", l.path, err)
	}
	var cfg Config
	if err := json.Unmarshal(data, &cfg); err != nil {
		return fmt.Errorf("解析配置失败 %s: %w", l.path, err)
	}
	l.applyDefaults(&cfg)

	info, _ := os.Stat(l.path)
	var mt int64
	if info != nil {
		mt = info.ModTime().UnixNano()
	}

	l.mu.Lock()
	l.cfg = &cfg
	l.modTime = mt
	l.mu.Unlock()

	if l.onChange != nil {
		l.onChange(&cfg)
	}
	return nil
}

func (l *Loader) ReloadIfChanged() (bool, error) {
	info, err := os.Stat(l.path)
	if err != nil {
		return false, err
	}
	l.mu.RLock()
	changed := info.ModTime().UnixNano() != l.modTime
	l.mu.RUnlock()
	if !changed {
		return false, nil
	}
	return true, l.Reload()
}

func (l *Loader) applyDefaults(cfg *Config) {
	if cfg.Mode == "" {
		cfg.Mode = "redirect"
	}
	if cfg.ListenHTTP == "" {
		cfg.ListenHTTP = "0.0.0.0:18080"
	}
	if cfg.ListenHTTPS == "" {
		cfg.ListenHTTPS = "0.0.0.0:18443"
	}
	if cfg.DefaultProfile == "" {
		cfg.DefaultProfile = "chrome-win11"
	}
	if cfg.Log.Level == "" {
		cfg.Log.Level = "info"
	}
	if cfg.TUN.Name == "" {
		if runtime.GOOS == "windows" {
			cfg.TUN.Name = "OneNetShare Tunnel"
		} else {
			cfg.TUN.Name = "tun0"
		}
	}
	if cfg.TUN.IP == "" {
		cfg.TUN.IP = "10.66.0.1"
	}
	if cfg.TUN.GatewayIP == "" {
		cfg.TUN.GatewayIP = "10.66.0.2"
	}
	if cfg.TUN.PrefixLen <= 0 || cfg.TUN.PrefixLen > 32 {
		cfg.TUN.PrefixLen = 24
	}
	if cfg.TUN.TTL <= 0 || cfg.TUN.TTL > 255 {
		cfg.TUN.TTL = RecommendedTTL(cfg.DefaultProfile)
	}
	if cfg.DeviceForge.TargetOS == "" {
		cfg.DeviceForge.TargetOS = "windows-11"
	}
	if cfg.DeviceForge.UDP.Listen == "" {
		cfg.DeviceForge.UDP.Listen = "0.0.0.0:18500"
	}
	if cfg.DeviceForge.UDP.ListenPorts == nil {
		cfg.DeviceForge.UDP.ListenPorts = map[string]int{}
	}
	if cfg.DeviceForge.DNS.MinTTL == 0 {
		cfg.DeviceForge.DNS.MinTTL = 60
	}
	if cfg.DeviceForge.DNS.MaxTTL == 0 {
		cfg.DeviceForge.DNS.MaxTTL = 86400
	}
	if cfg.DeviceForge.NTP.Server == "" {
		cfg.DeviceForge.NTP.Server = "pool.ntp.org"
	}
	if cfg.DeviceForge.MDNS.Hostname == "" {
		cfg.DeviceForge.MDNS.Hostname = "gateway"
	}
	if cfg.DeviceForge.MDNS.Service == "" {
		cfg.DeviceForge.MDNS.Service = "_workstation._tcp.local"
	}
	applyCaptiveDefaults(&cfg.DeviceForge.Captive)
	if len(cfg.Profiles) == 0 {
		cfg.Profiles = BuiltinProfiles()
	}
	for name, p := range BuiltinProfiles() {
		if _, ok := cfg.Profiles[name]; !ok {
			cfg.Profiles[name] = p
		}
	}
	for i := range cfg.Rules {
		if cfg.Rules[i].Name == "" {
			cfg.Rules[i].Name = fmt.Sprintf("rule-%d", i+1)
		}
		if len(cfg.Rules[i].Domains) == 0 {
			cfg.Rules[i].Domains = []string{"*"}
		}
	}
}

func RecommendedTTL(profile string) int {
	name := strings.ToLower(profile)
	if strings.Contains(name, "win") || strings.Contains(name, "windows") {
		return 128
	}
	return 64
}

func MatchDomain(pattern, host string) bool {
	host = strings.ToLower(strings.TrimSuffix(host, "."))
	pattern = strings.ToLower(pattern)
	if pattern == "*" || pattern == "" {
		return true
	}
	if strings.HasPrefix(pattern, "*.") {
		suffix := pattern[2:]
		return host == suffix || strings.HasSuffix(host, "."+suffix)
	}
	if strings.HasPrefix(pattern, ".") {
		return host == pattern[1:] || strings.HasSuffix(host, pattern)
	}
	return host == pattern
}

func LoadRaw(path string) (*Config, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}
	var cfg Config
	if err := json.Unmarshal(data, &cfg); err != nil {
		return nil, err
	}
	return &cfg, nil
}

func DefaultJSON() string {
	return `{
  "enabled": true,
  "mode": "redirect",
  "listen_http": "0.0.0.0:18080",
  "listen_https": "0.0.0.0:18443",
  "default_profile": "chrome-win11",
  "auto_reload": true,
  "log": {"level": "info", "file": ""},
  "tun": {
    "enabled": false,
    "name": "OneNetShare Tunnel",
    "ip": "10.66.0.1",
    "gateway_ip": "10.66.0.2",
    "prefix_len": 24,
    "route": true,
    "upstream_interface": "",
    "ttl": 128
  },
  "deviceforge": {
    "enabled": true,
    "target_os": "windows-11",
    "headers": true,
    "udp": {
      "enabled": true,
      "listen": "0.0.0.0:18500",
      "all_ports": true,
      "target_ports": [53, 123, 443, 5353],
      "listen_ports": {"53": 18053, "123": 18123, "443": 18443, "5353": 23353}
    },
    "dns": {"enabled": true, "strip_edns": true, "force_tcp": false, "min_ttl": 60, "max_ttl": 86400},
    "ntp": {"enabled": true, "server": "pool.ntp.org", "rewrite_refid": true},
    "mdns": {"enabled": true, "hostname": "gateway", "service": "_workstation._tcp.local"},
    "dhcp": {"enabled": true, "vendor_class": "MSFT 5.0", "client_id": ""},
    "captive": {
      "enabled": true,
      "mode": "reply",
      "domains": ["captive.apple.com", "www.msftconnecttest.com", "connectivitycheck.gstatic.com", "detectportal.firefox.com", "www.google.com"],
      "dns_domains": ["captive.apple.com", "www.msftconnecttest.com", "connectivitycheck.gstatic.com", "detectportal.firefox.com", "nmcheck.gnome.org"],
      "paths": ["/generate_204", "/connecttest.txt", "/hotspot-detect.html", "/success.txt", "/nmcheck"],
      "reply_ip": "",
      "ttl": 60,
      "http_code": 204,
      "http_body": ""
    }
  },
  "profiles": {
    "my-custom": {
      "description": "自定义画像",
      "headers": {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
        "Accept-Language": "zh-CN,zh;q=0.9"
      }
    }
  },
  "rules": [
    {
      "name": "default",
      "enabled": true,
      "domains": ["*"],
      "profile": "chrome-win11",
      "set_headers": {
        "Referer": "https://www.google.com/"
      },
      "remove_headers": ["X-Forwarded-For", "Via"],
      "add_headers": {}
    }
  ]
}
`
}

func ResolveConfigPath(explicit string) string {
	if explicit != "" {
		return explicit
	}
	if p := os.Getenv("HOTSPOT_PROXY_CONFIG"); p != "" {
		return p
	}
	exe, _ := os.Executable()
	dir := filepath.Dir(exe)
	for _, candidate := range []struct {
		name         string
		preferExeDir bool
	}{
		{filepath.Join(dir, "config.json"), true},
		{"/etc/hotspot-proxy/config.json", false},
		{"./config.json", false},
	} {
		if _, err := os.Stat(candidate.name); err == nil {
			return candidate.name
		}
	}
	return filepath.Join(dir, "config.json")
}
