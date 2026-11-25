//go:build !linux

package tun

import (
	"fmt"
	"log"
	"net"
)

type UDPHandler func(srcIP net.IP, srcPort uint16, dstIP net.IP, dstPort uint16, payload []byte)

type Device struct{}

func New(tunName string, tunIP string, logger *log.Logger) (*Device, error) {
	return nil, fmt.Errorf("TUN 模式仅支持 Linux")
}

func (d *Device) Name() string    { return "" }
func (d *Device) LocalIP() net.IP { return nil }
func (d *Device) SendUDP(srcIP net.IP, srcPort uint16, dstIP net.IP, dstPort uint16, payload []byte) error {
	return fmt.Errorf("TUN 模式仅支持 Linux")
}
func (d *Device) SetUDPHandler(UDPHandler) {}
func (d *Device) Close()                   {}
func (d *Device) Run() error               { return fmt.Errorf("TUN 模式仅支持 Linux") }
func (d *Device) StartCleanup()            {}
func SetupTUN(name, ip, cidr string) error { return fmt.Errorf("TUN 模式仅支持 Linux") }
