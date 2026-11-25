//go:build !linux

package deviceforge

import (
	"fmt"
	"net"
)

func startUDPListeners(m *Manager, mode string) error {
	return fmt.Errorf("DeviceForge UDP 透明接管仅支持 Linux")
}

func markOutboundConn(conn *net.UDPConn) error { return nil }
