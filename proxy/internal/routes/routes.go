package routes

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"log"
	"net"
	"os"
	"path/filepath"
	"strings"
	"sync"
)

type Policy struct {
	AllowDefaultRoute bool `json:"allow_default_route"`
}

type Interface struct {
	Name        string `json:"name"`
	AdapterGUID string `json:"adapter_guid"`
	Index       int    `json:"index"`
	Subnet      string `json:"subnet"`
}

type Source struct {
	Interface string `json:"interface"`
	Subnet    string `json:"subnet"`
}

type Route struct {
	ID                string `json:"id"`
	Enabled           *bool  `json:"enabled"`
	Source            Source `json:"source"`
	Destination       string `json:"destination"`
	Via               string `json:"via"`
	Interface         string `json:"interface"`
	Gateway           string `json:"gateway"`
	NextHop           string `json:"next_hop"`
	Metric            int    `json:"metric"`
	Layer             string `json:"layer"`
	AllowDefaultRoute bool   `json:"allow_default_route"`
}

type File struct {
	Version    int                 `json:"version"`
	Enabled    *bool               `json:"enabled"`
	Policy     Policy              `json:"policy"`
	Interfaces map[string]Interface `json:"interfaces"`
	Routes     []Route             `json:"routes"`
}

type InstalledRoute struct {
	ID          string `json:"id"`
	Prefix      string `json:"prefix"`
	Interface   string `json:"interface"`
	AdapterGUID string `json:"adapter_guid,omitempty"`
	Index       int    `json:"index,omitempty"`
	Gateway     string `json:"gateway"`
	Metric      int    `json:"metric"`
}

type normalizedRoute struct {
	ID          string
	Prefix      string
	Interface   string
	AdapterGUID string
	Index       int
	Gateway     string
	Metric      int
	Layer       string
}

type Manager struct {
	path       string
	statePath  string
	logger     *log.Logger
	mu         sync.Mutex
	hash       string
	current    *File
	normalized []normalizedRoute
	installed  []InstalledRoute
}

func New(path string, logger *log.Logger) *Manager {
	if path == "" {
		path = "managedroutes.json"
	}
	return &Manager{
		path:      path,
		statePath: strings.TrimSuffix(path, filepath.Ext(path)) + ".state.json",
		logger:    logger,
	}
}

func DefaultPath(configPath string) string {
	if configPath == "" {
		return "managedroutes.json"
	}
	dir := filepath.Dir(configPath)
	if dir == "." {
		return "managedroutes.json"
	}
	return filepath.Join(dir, "managedroutes.json")
}

func (m *Manager) Start() error {
	m.mu.Lock()
	defer m.mu.Unlock()
	if err := m.loadLocked(); err != nil {
		return err
	}
	if err := m.cleanupLocked(); err != nil {
		return err
	}
	if !m.enabledLocked() {
		return nil
	}
	return m.applyLocked()
}

func (m *Manager) Cleanup() error {
	m.mu.Lock()
	defer m.mu.Unlock()
	return m.cleanupLocked()
}

func (m *Manager) ReloadIfChanged() (bool, error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	raw, err := os.ReadFile(m.path)
	if err != nil {
		if !errors.Is(err, os.ErrNotExist) {
			return false, err
		}
		raw = nil
	}
	nextHash := hashBytes(raw)
	if nextHash == m.hash {
		return false, nil
	}
	if err := m.cleanupLocked(); err != nil {
		return false, err
	}
	m.current = nil
	m.normalized = nil
	m.hash = nextHash
	if err := m.loadLocked(); err != nil {
		return false, err
	}
	if !m.enabledLocked() {
		return true, nil
	}
	if err := m.applyLocked(); err != nil {
		return true, err
	}
	return true, nil
}

func (m *Manager) Status() string {
	m.mu.Lock()
	defer m.mu.Unlock()
	if m.current == nil {
		return "managed_routes=absent applied=0"
	}
	return fmt.Sprintf("managed_routes=%v configured=%d applied=%d path=%s",
		m.enabledLocked(), len(m.normalized), len(m.installed), m.path)
}

func (m *Manager) loadLocked() error {
	raw, err := os.ReadFile(m.path)
	if err != nil {
		if !errors.Is(err, os.ErrNotExist) {
			return err
		}
		m.current = nil
		m.normalized = nil
		m.hash = hashBytes(nil)
		return m.readStateLocked()
	}
	var file File
	if err := json.Unmarshal(raw, &file); err != nil {
		return fmt.Errorf("解析 %s 失败: %w", m.path, err)
	}
	normalized, err := normalize(&file)
	if err != nil {
		return err
	}
	m.current = &file
	m.normalized = normalized
	m.hash = hashBytes(raw)
	return m.readStateLocked()
}

func (m *Manager) readStateLocked() error {
	raw, err := os.ReadFile(m.statePath)
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			m.installed = nil
			return nil
		}
		return err
	}
	var installed []InstalledRoute
	if err := json.Unmarshal(raw, &installed); err != nil {
		m.logger.Printf("忽略损坏的路由账本 %s: %v", m.statePath, err)
		m.installed = nil
		return nil
	}
	m.installed = installed
	return nil
}

func (m *Manager) enabledLocked() bool {
	return m.current != nil && (m.current.Enabled == nil || *m.current.Enabled) && len(m.normalized) > 0
}

func (m *Manager) applyLocked() error {
	installed, err := applyRoutes(m.current, m.normalized, m.logger)
	if err != nil {
		rollback := installed
		if cleanupErr := cleanupRoutes(rollback, m.logger); cleanupErr != nil {
			m.logger.Printf("管理路由回滚失败: %v", cleanupErr)
		}
		_ = os.Remove(m.statePath)
		m.installed = nil
		return err
	}
	m.installed = installed
	if err := m.writeStateLocked(installed); err != nil {
		if cleanupErr := cleanupRoutes(installed, m.logger); cleanupErr != nil {
			m.logger.Printf("管理路由清理失败: %v", cleanupErr)
		}
		m.installed = nil
		return err
	}
	m.logger.Printf("管理路由已应用 %d 条", len(installed))
	return nil
}

func (m *Manager) cleanupLocked() error {
	if len(m.installed) > 0 {
		if err := cleanupRoutes(m.installed, m.logger); err != nil {
			return err
		}
		m.logger.Printf("管理路由已清理 %d 条", len(m.installed))
	}
	m.installed = nil
	err := os.Remove(m.statePath)
	if errors.Is(err, os.ErrNotExist) {
		return nil
	}
	return err
}

func (m *Manager) writeStateLocked(installed []InstalledRoute) error {
	if len(installed) == 0 {
		err := os.Remove(m.statePath)
		if errors.Is(err, os.ErrNotExist) {
			return nil
		}
		return err
	}
	data, err := json.MarshalIndent(installed, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(m.statePath, data, 0o600)
}

func hashBytes(raw []byte) string {
	sum := sha256.Sum256(raw)
	return hex.EncodeToString(sum[:])
}

func normalize(file *File) ([]normalizedRoute, error) {
	if file.Version != 1 {
		return nil, fmt.Errorf("managedroutes.json version 只支持 1，当前为 %d", file.Version)
	}
	if len(file.Routes) == 0 {
		return nil, nil
	}
	seen := make(map[string]bool)
	out := make([]normalizedRoute, 0, len(file.Routes))
	for i, route := range file.Routes {
		if route.Enabled != nil && !*route.Enabled {
			continue
		}
		iface, ok := file.Interfaces[route.Via]
		if !ok {
			return nil, fmt.Errorf("路由 %d 引用了未定义的 via %q", i, route.Via)
		}
		if route.Source.Interface != "" {
			if _, ok := file.Interfaces[route.Source.Interface]; !ok {
				return nil, fmt.Errorf("路由 %d 引用了未定义的 source.interface %q", i, route.Source.Interface)
			}
		}
		if route.Source.Subnet != "" {
			if err := validateIPv4CIDR(route.Source.Subnet); err != nil {
				return nil, fmt.Errorf("路由 %d source.subnet 无效: %w", i, err)
			}
		}
		prefix, defaultRoute, err := parseDestination(route.Destination)
		if err != nil {
			return nil, fmt.Errorf("路由 %d destination 无效: %w", i, err)
		}
		if defaultRoute && !route.AllowDefaultRoute && !file.Policy.AllowDefaultRoute {
			return nil, fmt.Errorf("路由 %d 使用默认路由 %s，但未显式设置 allow_default_route", i, prefix)
		}
		gateway := route.Gateway
		if gateway == "" {
			gateway = route.NextHop
		}
		ip := net.ParseIP(gateway)
		if ip == nil || ip.To4() == nil {
			return nil, fmt.Errorf("路由 %d gateway/next_hop 必须是 IPv4 地址", i)
		}
		name := route.Interface
		if name == "" {
			name = iface.Name
		}
		if name == "" {
			return nil, fmt.Errorf("路由 %d 的 interface 为空", i)
		}
		metric := route.Metric
		if metric <= 0 {
			metric = 1
		}
		id := route.ID
		if id == "" {
			id = fmt.Sprintf("route-%d-%s-%s", i+1, prefix, name)
		}
		if seen[id] {
			return nil, fmt.Errorf("路由 id 重复: %s", id)
		}
		seen[id] = true
		out = append(out, normalizedRoute{
			ID:          id,
			Prefix:      prefix,
			Interface:   name,
			AdapterGUID: iface.AdapterGUID,
			Index:       iface.Index,
			Gateway:     ip.To4().String(),
			Metric:      metric,
			Layer:       route.Layer,
		})
	}
	return out, nil
}

func parseDestination(value string) (string, bool, error) {
	if value == "" {
		return "", false, errors.New("destination 为空")
	}
	ip, network, err := net.ParseCIDR(value)
	if err != nil {
		return "", false, err
	}
	if ip.To4() == nil {
		return "", false, errors.New("只支持 IPv4")
	}
	ones, bits := network.Mask.Size()
	if bits != 32 {
		return "", false, errors.New("只支持 IPv4")
	}
	return network.String(), ones == 0, nil
}

func validateIPv4CIDR(value string) error {
	_, _, err := parseDestination(value)
	return err
}

func toInstalled(route normalizedRoute) InstalledRoute {
	return InstalledRoute{
		ID:          route.ID,
		Prefix:      route.Prefix,
		Interface:   route.Interface,
		AdapterGUID: route.AdapterGUID,
		Index:       route.Index,
		Gateway:     route.Gateway,
		Metric:      route.Metric,
	}
}
