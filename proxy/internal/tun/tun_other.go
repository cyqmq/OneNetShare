//go:build !linux && !windows

package tun

import (
	"fmt"
	"log"
)

func newAdapter(name string, ip string, logger *log.Logger) (packetAdapter, error) {
	return nil, fmt.Errorf("TUN 模式仅支持 Linux 与 Windows")
}

func SetupTUN(opts Options, logger *log.Logger) (func(), error) {
	return func() {}, fmt.Errorf("TUN 模式仅支持 Linux 与 Windows")
}
