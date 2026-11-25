//go:build !linux && !windows

package netutil

import "syscall"

func control(network, address string, c syscall.RawConn) error { return nil }
