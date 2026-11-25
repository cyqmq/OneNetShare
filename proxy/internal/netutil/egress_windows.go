//go:build windows

package netutil

import "syscall"

const (
	ipProtoIP   = 0
	ipUnicastIf = 31
)

func control(network, address string, c syscall.RawConn) error {
	_, index, _ := Interface()
	if index <= 0 {
		return nil
	}
	var opErr error
	err := c.Control(func(fd uintptr) {
		opErr = syscall.SetsockoptInt(syscall.Handle(fd), ipProtoIP, ipUnicastIf, int(htonl(uint32(index))))
	})
	if err != nil {
		return err
	}
	return opErr
}

func htonl(v uint32) uint32 {
	return (v&0xff)<<24 | (v&0xff00)<<8 | (v>>8)&0xff00 | (v>>24)&0xff
}
