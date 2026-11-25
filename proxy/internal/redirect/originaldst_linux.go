//go:build linux

package redirect

import (
	"fmt"
	"net"
	"syscall"
	"unsafe"

	"golang.org/x/sys/unix"
)

type sockaddrIn struct {
	Family uint16
	Port   uint16
	Addr   [4]byte
	Zero   [8]byte
}

const (
	soOriginalDst = 80
	ipTransparent = 19
)

func ntohs(x uint16) uint16 { return x<<8 | x>>8 }

// OriginalDst reads SO_ORIGINAL_DST (REDIRECT and TPROXY compatible).
func OriginalDst(conn *net.TCPConn) (string, error) {
	rc, err := conn.SyscallConn()
	if err != nil {
		return "", err
	}
	var sa sockaddrIn
	salen := uint32(unsafe.Sizeof(sa))
	var sockErr error
	err = rc.Control(func(fd uintptr) {
		_, _, errno := unix.Syscall6(unix.SYS_GETSOCKOPT, fd,
			uintptr(unix.SOL_IP), uintptr(soOriginalDst),
			uintptr(unsafe.Pointer(&sa)), uintptr(unsafe.Pointer(&salen)), 0)
		if errno != 0 {
			sockErr = errno
		}
	})
	if err != nil {
		return "", err
	}
	if sockErr != nil {
		return "", fmt.Errorf("SO_ORIGINAL_DST: %w", sockErr)
	}
	return net.JoinHostPort(net.IP(sa.Addr[:]).String(), fmt.Sprintf("%d", ntohs(sa.Port))), nil
}

// ListenTransparent creates an IP_TRANSPARENT listener for TPROXY mode.
// On TPROXY, the listener can bind to non-local original destinations.
func ListenTransparent(addr string) (net.Listener, error) {
	lc := net.ListenConfig{
		Control: func(network, address string, c syscall.RawConn) error {
			var opErr error
			if err := c.Control(func(fd uintptr) {
				opErr = unix.SetsockoptInt(int(fd), unix.SOL_IP, ipTransparent, 1)
			}); err != nil {
				return err
			}
			return opErr
		},
	}
	return lc.Listen(nil, "tcp", addr)
}
