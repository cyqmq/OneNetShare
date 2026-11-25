//go:build linux

package tun

import (
	"fmt"
	"log"
	"os/exec"

	"github.com/songgao/water"
)

func newAdapter(name string, ip string, logger *log.Logger) (packetAdapter, error) {
	cfg := water.Config{DeviceType: water.TUN}
	cfg.Name = name
	iface, err := water.New(cfg)
	if err != nil {
		return nil, fmt.Errorf("创建 TUN 设备失败: %w", err)
	}
	return iface, nil
}

func SetupTUN(opts Options, logger *log.Logger) (func(), error) {
	_ = opts.DNSServers
	if err := runCommand("ip", "link", "set", opts.Name, "up"); err != nil {
		return func() {}, err
	}
	prefix := opts.PrefixLen
	if prefix <= 0 || prefix > 32 {
		prefix = 24
	}
	if err := runCommand("ip", "addr", "add", fmt.Sprintf("%s/%d", opts.IP, prefix), "dev", opts.Name); err != nil {
		logger.Printf("TUN 地址配置: %v", err)
	}
	return func() {}, nil
}

func runCommand(name string, args ...string) error {
	cmd := exec.Command(name, args...)
	out, err := cmd.CombinedOutput()
	if err != nil {
		return fmt.Errorf("%s %v: %w: %s", name, args, err, string(out))
	}
	return nil
}
