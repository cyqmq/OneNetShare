//go:build linux

package netutil

import (
	"syscall"

	"golang.org/x/sys/unix"
)

func control(network, address string, c syscall.RawConn) error {
	var opErr error
	err := c.Control(func(fd uintptr) {
		opErr = unix.SetsockoptInt(int(fd), unix.SOL_SOCKET, unix.SO_MARK, 2)
	})
	if err != nil {
		return err
	}
	return opErr
}
